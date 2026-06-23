using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SecondaryAttacks;

internal static partial class ProjectileRuntimeSystem
{
    internal static bool FireOverchargedBomb(Attack attack, SecondaryAttackDefinition definition)
    {
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!TryGetProjectilePayload(attack, definition, launchData, out Projectile _))
        {
            attack.m_consumeItem = false;
            return true;
        }

        ProjectileSecondaryBehavior projectileBehavior = (ProjectileSecondaryBehavior)definition.Behavior;
        if (!OverchargedBombSystem.TryConsumeStackCost(attack, projectileBehavior))
        {
            attack.m_consumeItem = false;
            return true;
        }

        PrepareCustomProjectileBurst(attack);
        attack.GetProjectileSpawnPoint(out Vector3 spawnPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        if (attack.m_burstEffect.HasEffects())
        {
            attack.m_burstEffect.Create(spawnPoint, Quaternion.LookRotation(aimDirection));
        }

        int projectileCount = Mathf.Max(1, projectileBehavior.ProjectileCount);
        float startAngle = projectileCount > 1 ? -projectileBehavior.SpreadAngle * 0.5f : 0f;
        float angleStep = projectileCount > 1 ? projectileBehavior.SpreadAngle / (projectileCount - 1) : 0f;
        Vector3 rotationAxis = attack.m_character.transform.up;
        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            float angleOffset = startAngle + angleStep * projectileIndex;
            Vector3 direction = Quaternion.AngleAxis(angleOffset, rotationAxis) * aimDirection;
            GameObject projectileObject = SpawnProjectileObject(
                attack,
                launchData,
                spawnPoint,
                direction,
                ResolveProjectileSpeed(launchData),
                setLastProjectile: true,
                out Projectile? projectile);
            if (projectileObject == null || projectile == null)
            {
                continue;
            }

            OverchargedBombSystem.RegisterProjectile(projectile, projectileBehavior.AoeRadiusFactor);
        }

        return true;
    }
}

internal static class OverchargedBombSystem
{
    private static readonly ConditionalWeakTable<Projectile, ProjectileState> Projectiles = new();

    [ThreadStatic]
    private static List<float>? ActiveVisualScales;

    internal static bool CanStart(Humanoid humanoid, ItemDrop.ItemData weapon, ProjectileSecondaryBehavior behavior)
    {
        if (!RequiresStackCost(weapon, behavior))
        {
            return true;
        }

        Inventory inventory = humanoid.GetInventory();
        int requiredCount = GetRequiredStackConsumption(behavior);
        if (inventory != null && CountMatchingItems(inventory, weapon) >= requiredCount)
        {
            return true;
        }

        humanoid.Message(MessageHud.MessageType.Center, "$msg_outof " + weapon.m_shared.m_name);
        return false;
    }

    internal static bool TryConsumeStackCost(Attack attack, ProjectileSecondaryBehavior behavior)
    {
        ItemDrop.ItemData weapon = attack.m_weapon;
        if (!RequiresStackCost(weapon, behavior))
        {
            return true;
        }

        Inventory inventory = attack.m_character.GetInventory();
        int requiredCount = GetRequiredStackConsumption(behavior);
        if (inventory == null || CountMatchingItems(inventory, weapon) < requiredCount)
        {
            attack.m_character.Message(MessageHud.MessageType.Center, "$msg_outof " + weapon.m_shared.m_name);
            return false;
        }

        if (!attack.m_consumeItem)
        {
            attack.m_consumeItem = true;
        }

        int vanillaConsumeCount = 1;
        int extraConsumption = Mathf.Max(0, requiredCount - vanillaConsumeCount);
        RemoveMatchingItems(inventory, weapon, extraConsumption, keepWeaponStack: vanillaConsumeCount);
        return true;
    }

    internal static void RegisterProjectile(Projectile projectile, float aoeRadiusFactor)
    {
        RegisterProjectile(projectile, projectileScaleFactor: 1f, aoeRadiusFactor);
    }

    internal static void RegisterProjectile(Projectile projectile, float projectileScaleFactor, float aoeRadiusFactor)
    {
        if (projectile == null)
        {
            return;
        }

        Projectiles.Remove(projectile);
        Projectiles.Add(projectile, new ProjectileState(
            Mathf.Max(0.01f, projectileScaleFactor),
            Mathf.Max(0.01f, aoeRadiusFactor)));
        ScaleProjectileInstance(projectile, projectileScaleFactor);
    }

    internal static ProjectileHitScaleState BeginProjectileHit(Projectile projectile)
    {
        if (projectile == null ||
            !Projectiles.TryGetValue(projectile, out ProjectileState? state) ||
            Mathf.Approximately(state.AoeRadiusFactor, 1f))
        {
            return ProjectileHitScaleState.Empty;
        }

        List<AoeRadiusState> scaledAoes = new();
        List<TransformScaleState> scaledTransforms = new();
        HashSet<Aoe> seenAoes = new();
        HashSet<Transform> seenTransforms = new();
        float originalProjectileAoe = projectile.m_aoe;
        projectile.m_aoe *= state.AoeRadiusFactor;
        ScaleEffectList(projectile.m_hitEffects, state.AoeRadiusFactor, scaledTransforms, seenTransforms);
        ScaleEffectList(projectile.m_spawnOnHitEffects, state.AoeRadiusFactor, scaledTransforms, seenTransforms);
        ScaleAoePrefab(projectile.m_spawnOnHit, state.AoeRadiusFactor, scaledAoes, scaledTransforms, seenAoes, seenTransforms);
        foreach (GameObject spawnPrefab in projectile.m_randomSpawnOnHit ?? new List<GameObject>())
        {
            ScaleAoePrefab(spawnPrefab, state.AoeRadiusFactor, scaledAoes, scaledTransforms, seenAoes, seenTransforms);
        }

        PushActiveVisualScale(state.AoeRadiusFactor);
        return new ProjectileHitScaleState(
            projectile,
            originalProjectileAoe,
            scaledAoes.ToArray(),
            scaledTransforms.ToArray(),
            visualScalePushed: true);
    }

    internal static void EndProjectileHit(ProjectileHitScaleState state)
    {
        if (!state.Applies)
        {
            return;
        }

        if (state.Projectile != null)
        {
            state.Projectile.m_aoe = state.OriginalProjectileAoe;
        }

        foreach (AoeRadiusState aoeState in state.ScaledAoes)
        {
            if (aoeState.Aoe != null)
            {
                aoeState.Aoe.m_radius = aoeState.OriginalRadius;
            }
        }

        foreach (TransformScaleState transformState in state.ScaledTransforms)
        {
            if (transformState.Transform != null)
            {
                transformState.Transform.localScale = transformState.OriginalScale;
            }
        }

        if (state.VisualScalePushed)
        {
            PopActiveVisualScale();
        }
    }

    internal static void ScaleCreatedVisuals(GameObject? instance)
    {
        if (instance == null || !TryGetActiveVisualScale(out float scale))
        {
            return;
        }

        ScaleRuntimeVisuals(instance, scale);
    }

    internal static void ScaleCreatedVisuals(GameObject[]? instances)
    {
        if (instances == null || !TryGetActiveVisualScale(out float scale))
        {
            return;
        }

        foreach (GameObject instance in instances)
        {
            ScaleRuntimeVisuals(instance, scale);
        }
    }

    private static bool RequiresStackCost(ItemDrop.ItemData? weapon, ProjectileSecondaryBehavior behavior)
    {
        return weapon?.m_shared != null &&
               behavior.Preset == SecondaryAttackPreset.OverchargedBomb &&
               GetRequiredStackConsumption(behavior) > 1 &&
               string.IsNullOrWhiteSpace(weapon.m_shared.m_ammoType) &&
               weapon.m_shared.m_maxStackSize > 1;
    }

    private static int GetRequiredStackConsumption(ProjectileSecondaryBehavior behavior)
    {
        return Mathf.Max(1, behavior.AmmoConsumption);
    }

    private static int CountMatchingItems(Inventory inventory, ItemDrop.ItemData weapon)
    {
        int count = 0;
        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            if (IsMatchingItem(item, weapon))
            {
                count += Mathf.Max(0, item.m_stack);
            }
        }

        return count;
    }

    private static void RemoveMatchingItems(Inventory inventory, ItemDrop.ItemData weapon, int amount, int keepWeaponStack)
    {
        if (amount <= 0)
        {
            return;
        }

        List<ItemDrop.ItemData> matchingItems = inventory.GetAllItems().FindAll(item => IsMatchingItem(item, weapon));
        matchingItems.Sort((left, right) => ReferenceEquals(left, weapon).CompareTo(ReferenceEquals(right, weapon)));
        foreach (ItemDrop.ItemData item in matchingItems)
        {
            if (amount <= 0)
            {
                return;
            }

            int removableStack = Mathf.Max(0, item.m_stack - (ReferenceEquals(item, weapon) ? keepWeaponStack : 0));
            int removeCount = Mathf.Min(removableStack, amount);
            if (removeCount <= 0)
            {
                continue;
            }

            inventory.RemoveItem(item, removeCount);
            amount -= removeCount;
        }
    }

    private static bool IsMatchingItem(ItemDrop.ItemData? item, ItemDrop.ItemData weapon)
    {
        if (item?.m_shared == null || weapon?.m_shared == null)
        {
            return false;
        }

        string itemPrefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
        string weaponPrefabName = weapon.m_dropPrefab != null ? weapon.m_dropPrefab.name : "";
        if (!string.IsNullOrWhiteSpace(itemPrefabName) && !string.IsNullOrWhiteSpace(weaponPrefabName))
        {
            return itemPrefabName.Equals(weaponPrefabName, StringComparison.OrdinalIgnoreCase);
        }

        return item.m_shared.m_name.Equals(weapon.m_shared.m_name, StringComparison.OrdinalIgnoreCase);
    }

    private static void ScaleAoePrefab(
        GameObject? prefab,
        float radiusFactor,
        List<AoeRadiusState> scaledAoes,
        List<TransformScaleState> scaledTransforms,
        HashSet<Aoe> seenAoes,
        HashSet<Transform> seenTransforms)
    {
        if (prefab == null)
        {
            return;
        }

        ScalePrefabTransform(prefab, radiusFactor, scaledTransforms, seenTransforms);

        foreach (Aoe aoe in prefab.GetComponentsInChildren<Aoe>(true))
        {
            if (!seenAoes.Add(aoe))
            {
                continue;
            }

            scaledAoes.Add(new AoeRadiusState(aoe, aoe.m_radius));
            aoe.m_radius *= radiusFactor;
        }
    }

    private static void ScaleEffectList(
        EffectList? effectList,
        float scale,
        List<TransformScaleState> scaledTransforms,
        HashSet<Transform> seenTransforms)
    {
        if (effectList?.m_effectPrefabs == null)
        {
            return;
        }

        foreach (EffectList.EffectData? effectData in effectList.m_effectPrefabs)
        {
            if (effectData?.m_prefab == null)
            {
                continue;
            }

            ScalePrefabTransform(effectData.m_prefab, scale, scaledTransforms, seenTransforms);
        }
    }

    private static void ScalePrefabTransform(
        GameObject prefab,
        float scale,
        List<TransformScaleState> scaledTransforms,
        HashSet<Transform> seenTransforms)
    {
        Transform transform = prefab.transform;
        if (!seenTransforms.Add(transform))
        {
            return;
        }

        scaledTransforms.Add(new TransformScaleState(transform, transform.localScale));
        transform.localScale *= scale;
    }

    private static void PushActiveVisualScale(float scale)
    {
        ActiveVisualScales ??= new List<float>();
        ActiveVisualScales.Add(Mathf.Max(0.01f, scale));
    }

    private static void PopActiveVisualScale()
    {
        if (ActiveVisualScales == null || ActiveVisualScales.Count == 0)
        {
            return;
        }

        ActiveVisualScales.RemoveAt(ActiveVisualScales.Count - 1);
    }

    private static bool TryGetActiveVisualScale(out float scale)
    {
        if (ActiveVisualScales == null || ActiveVisualScales.Count == 0)
        {
            scale = 1f;
            return false;
        }

        scale = ActiveVisualScales[ActiveVisualScales.Count - 1];
        return !Mathf.Approximately(scale, 1f);
    }

    private static void ScaleProjectileInstance(Projectile projectile, float scale)
    {
        if (projectile == null || Mathf.Approximately(scale, 1f))
        {
            return;
        }

        GameObject projectileObject = projectile.gameObject;
        ProjectileInstanceScaleMarker marker = projectileObject.GetComponent<ProjectileInstanceScaleMarker>();
        if (marker != null)
        {
            return;
        }

        marker = projectileObject.AddComponent<ProjectileInstanceScaleMarker>();
        marker.Scale = scale;
        projectileObject.transform.localScale *= scale;
        projectile.m_rayRadius = projectile.m_rayRadius > 0f
            ? projectile.m_rayRadius * scale
            : Mathf.Max(0f, scale - 1f) * 0.1f;
    }

    private static void ScaleRuntimeVisuals(GameObject instance, float scale)
    {
        if (instance == null || Mathf.Approximately(scale, 1f))
        {
            return;
        }

        RuntimeVisualScaleMarker marker = instance.GetComponent<RuntimeVisualScaleMarker>();
        if (marker != null)
        {
            return;
        }

        marker = instance.AddComponent<RuntimeVisualScaleMarker>();
        marker.Scale = scale;

        ScaleParticleSystems(instance, scale);
        ScaleTrailRenderers(instance, scale);
        ScaleLineRenderers(instance, scale);
        ScaleLights(instance, scale);
    }

    private static void ScaleParticleSystems(GameObject instance, float scale)
    {
        foreach (ParticleSystem particleSystem in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particleSystem.main;
            if (main.startSize3D)
            {
                main.startSizeX = ScaleCurve(main.startSizeX, scale);
                main.startSizeY = ScaleCurve(main.startSizeY, scale);
                main.startSizeZ = ScaleCurve(main.startSizeZ, scale);
            }
            else
            {
                main.startSize = ScaleCurve(main.startSize, scale);
            }

            main.startSpeed = ScaleCurve(main.startSpeed, scale);

            ParticleSystem.ShapeModule shape = particleSystem.shape;
            if (shape.enabled)
            {
                shape.radius *= scale;
                shape.scale *= scale;
                shape.position *= scale;
            }

            ParticleSystem.TrailModule trails = particleSystem.trails;
            if (trails.enabled)
            {
                trails.widthOverTrail = ScaleCurve(trails.widthOverTrail, scale);
            }

            ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.lengthScale *= scale;
                renderer.velocityScale *= scale;
            }
        }
    }

    private static void ScaleTrailRenderers(GameObject instance, float scale)
    {
        foreach (TrailRenderer trailRenderer in instance.GetComponentsInChildren<TrailRenderer>(true))
        {
            trailRenderer.widthMultiplier *= scale;
        }
    }

    private static void ScaleLineRenderers(GameObject instance, float scale)
    {
        foreach (LineRenderer lineRenderer in instance.GetComponentsInChildren<LineRenderer>(true))
        {
            lineRenderer.widthMultiplier *= scale;
        }
    }

    private static void ScaleLights(GameObject instance, float scale)
    {
        foreach (Light light in instance.GetComponentsInChildren<Light>(true))
        {
            light.range *= scale;
        }
    }

    private static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float scale)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                curve.constant *= scale;
                break;
            case ParticleSystemCurveMode.TwoConstants:
                curve.constantMin *= scale;
                curve.constantMax *= scale;
                break;
            case ParticleSystemCurveMode.Curve:
            case ParticleSystemCurveMode.TwoCurves:
                curve.curveMultiplier *= scale;
                break;
        }

        return curve;
    }

    private sealed class ProjectileState
    {
        public ProjectileState(float projectileScaleFactor, float aoeRadiusFactor)
        {
            ProjectileScaleFactor = projectileScaleFactor;
            AoeRadiusFactor = aoeRadiusFactor;
        }

        public float ProjectileScaleFactor { get; }

        public float AoeRadiusFactor { get; }
    }

    private sealed class RuntimeVisualScaleMarker : MonoBehaviour
    {
        public float Scale { get; set; }
    }

    private sealed class ProjectileInstanceScaleMarker : MonoBehaviour
    {
        public float Scale { get; set; }
    }

    internal readonly struct AoeRadiusState
    {
        public AoeRadiusState(Aoe aoe, float originalRadius)
        {
            Aoe = aoe;
            OriginalRadius = originalRadius;
        }

        public Aoe Aoe { get; }

        public float OriginalRadius { get; }
    }

    internal readonly struct TransformScaleState
    {
        public TransformScaleState(Transform transform, Vector3 originalScale)
        {
            Transform = transform;
            OriginalScale = originalScale;
        }

        public Transform Transform { get; }

        public Vector3 OriginalScale { get; }
    }

    internal readonly struct ProjectileHitScaleState
    {
        internal static readonly ProjectileHitScaleState Empty = new(
            null,
            0f,
            Array.Empty<AoeRadiusState>(),
            Array.Empty<TransformScaleState>(),
            visualScalePushed: false);

        public ProjectileHitScaleState(
            Projectile? projectile,
            float originalProjectileAoe,
            AoeRadiusState[] scaledAoes,
            TransformScaleState[] scaledTransforms,
            bool visualScalePushed)
        {
            Projectile = projectile;
            OriginalProjectileAoe = originalProjectileAoe;
            ScaledAoes = scaledAoes;
            ScaledTransforms = scaledTransforms;
            VisualScalePushed = visualScalePushed;
        }

        public Projectile? Projectile { get; }

        public float OriginalProjectileAoe { get; }

        public AoeRadiusState[] ScaledAoes { get; }

        public TransformScaleState[] ScaledTransforms { get; }

        public bool VisualScalePushed { get; }

        public bool Applies => Projectile != null ||
                               ScaledAoes.Length > 0 ||
                               ScaledTransforms.Length > 0 ||
                               VisualScalePushed;
    }
}
