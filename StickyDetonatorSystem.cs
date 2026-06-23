using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static partial class ProjectileRuntimeSystem
{
    internal static void FireStickyDetonator(Attack attack, SecondaryAttackDefinition definition)
    {
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!TryGetProjectilePayload(attack, definition, launchData, out Projectile _))
        {
            return;
        }

        ProjectileSecondaryBehavior projectileBehavior = (ProjectileSecondaryBehavior)definition.Behavior;
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

            StickyDetonatorSystem.RegisterLaunchedProjectile(
                projectile,
                launchData.ProjectilePrefab!,
                attack.m_character,
                attack.m_weapon,
                attack.m_lastUsedAmmo,
                launchData.AttackHitNoise,
                definition,
                projectileBehavior);
        }
    }
}

internal static class StickyDetonatorSystem
{
    private const float StickSurfaceOffset = 0.03f;

    private static readonly ConditionalWeakTable<Projectile, StickyChargeState> PendingProjectiles = new();
    private static readonly List<StickyChargeController> ActiveCharges = new();
    private static readonly ConditionalWeakTable<Player, StickyDetonatorInputState> InputStates = new();
    private static bool _detonating;

    internal static void RegisterLaunchedProjectile(
        Projectile projectile,
        GameObject projectilePrefab,
        Character owner,
        ItemDrop.ItemData weapon,
        ItemDrop.ItemData? ammo,
        float hitNoise,
        SecondaryAttackDefinition definition,
        ProjectileSecondaryBehavior behavior)
    {
        if (projectile == null ||
            projectilePrefab == null ||
            owner == null ||
            weapon?.m_dropPrefab == null ||
            projectile.m_originalHitData == null)
        {
            return;
        }

        TrimOwnerCharges(owner, Mathf.Max(1, behavior.MaxCharges - 1));
        SuppressProjectileItemDrops(projectile);
        PendingProjectiles.Remove(projectile);
        PendingProjectiles.Add(
            projectile,
            new StickyChargeState(
                projectilePrefab,
                owner,
                weapon,
                ammo,
                hitNoise,
                projectile.m_originalHitData.Clone(),
                definition,
                Mathf.Max(0.5f, behavior.SentinelLifetime),
                Mathf.Max(0.01f, behavior.AoeRadiusFactor)));
    }

    internal static bool TryHandleProjectileHit(Projectile projectile, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
    {
        if (_detonating ||
            projectile == null ||
            !PendingProjectiles.TryGetValue(projectile, out StickyChargeState? state))
        {
            return false;
        }

        if (water)
        {
            PendingProjectiles.Remove(projectile);
            return false;
        }

        if (collider != null && ProjectileRuntimeSystem.GetHitCharacter(collider) == state.Owner)
        {
            return true;
        }

        PendingProjectiles.Remove(projectile);
        StickProjectile(projectile, collider, hitPoint, normal, state);
        return true;
    }

    internal static void UpdateInput(Player player, ref bool blocking)
    {
        StickyDetonatorInputState inputState = InputStates.GetValue(player, _ => new StickyDetonatorInputState());
        if (player == null || player != Player.m_localPlayer || !IsStickyDetonatorInputContext(player))
        {
            inputState.WasBlocking = false;
            return;
        }

        bool blockDown = ZInput.GetButtonDown("Block") || blocking && !inputState.WasBlocking;
        inputState.WasBlocking = blocking;
        if (blocking)
        {
            blocking = false;
        }

        if (blockDown)
        {
            if (CountActiveCharges(player) > 0)
            {
                TryPlayDetonateAnimation(player, ResolveDetonateAnimation(player));
            }

            DetonateAll(player);
        }
    }

    internal static bool ShouldSuppressBlock(Humanoid humanoid)
    {
        return humanoid is Player player &&
               player == Player.m_localPlayer &&
               IsStickyDetonatorInputContext(player);
    }

    internal static bool ShouldShowDetonateBlockHint(Player? player)
    {
        return player != null &&
               player == Player.m_localPlayer &&
               IsHoldingStickyDetonator(player);
    }

    private static bool IsStickyDetonatorInputContext(Player player)
    {
        if (IsHoldingStickyDetonator(player))
        {
            return true;
        }

        return player.GetRightItem() == null && HasActiveCharges(player);
    }

    private static bool IsHoldingStickyDetonator(Player player)
    {
        ItemDrop.ItemData weapon = ((Humanoid)player).GetCurrentWeapon();
        return weapon != null &&
               SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition) &&
               definition.Behavior is ProjectileSecondaryBehavior { Preset: SecondaryAttackPreset.StickyDetonator };
    }

    private static bool HasActiveCharges(Character owner)
    {
        return CountActiveCharges(owner) > 0;
    }

    private static int CountActiveCharges(Character owner)
    {
        int count = 0;
        for (int i = ActiveCharges.Count - 1; i >= 0; i--)
        {
            StickyChargeController controller = ActiveCharges[i];
            if (controller == null || !controller.IsValid)
            {
                ActiveCharges.RemoveAt(i);
                continue;
            }

            if (controller.Owner == owner)
            {
                count++;
            }
        }

        return count;
    }

    private static string ResolveDetonateAnimation(Player player)
    {
        ItemDrop.ItemData weapon = ((Humanoid)player).GetCurrentWeapon();
        if (weapon != null &&
            SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition heldDefinition) &&
            heldDefinition.Behavior is ProjectileSecondaryBehavior { Preset: SecondaryAttackPreset.StickyDetonator } heldBehavior &&
            !string.IsNullOrWhiteSpace(heldBehavior.DetonateAnimation))
        {
            return heldBehavior.DetonateAnimation;
        }

        for (int i = ActiveCharges.Count - 1; i >= 0; i--)
        {
            StickyChargeController controller = ActiveCharges[i];
            if (controller == null || !controller.IsValid || controller.Owner != player)
            {
                continue;
            }

            if (controller.State.Definition.Behavior is ProjectileSecondaryBehavior chargeBehavior &&
                !string.IsNullOrWhiteSpace(chargeBehavior.DetonateAnimation))
            {
                return chargeBehavior.DetonateAnimation;
            }
        }

        return "";
    }

    private static bool TryPlayDetonateAnimation(Player player, string animation)
    {
        if (player == null || string.IsNullOrWhiteSpace(animation))
        {
            return false;
        }

        string trigger = animation.Trim();
        if (player.InAttack() || player.InDodge() || player.InMinorAction() || player.IsStaggering() || !player.CanMove())
        {
            return false;
        }

        ItemDrop.ItemData weapon = ((Humanoid)player).GetCurrentWeapon();
        if (weapon == null)
        {
            player.GetZAnim()?.SetTrigger(trigger);
            return true;
        }

        Attack? previousAttack = ((Humanoid)player).m_currentAttack;
        Attack animationAttack = CreateDetonateAnimationAttack(trigger);
        ZSyncAnimation? zanim = player.GetZAnim();
        CharacterAnimEvent? animEvent = player.GetComponentInChildren<CharacterAnimEvent>();
        VisEquipment? visEquipment = player.GetComponent<VisEquipment>();
        if (zanim == null || animEvent == null || visEquipment == null ||
            !animationAttack.Start(
                player,
                player.m_body,
                zanim,
                animEvent,
                visEquipment,
                weapon,
                previousAttack,
                player.GetTimeSinceLastAttack(),
                player.GetAttackDrawPercentage()))
        {
            return false;
        }

        ((Humanoid)player).m_currentAttack = animationAttack;
        ((Humanoid)player).m_currentAttackIsSecondary = true;
        return true;
    }

    private static Attack CreateDetonateAnimationAttack(string animation)
    {
        return new Attack
        {
            m_attackType = Attack.AttackType.TriggerProjectile,
            m_attackAnimation = animation,
            m_attackStamina = 0f,
            m_attackAdrenaline = 0f,
            m_attackUseAdrenaline = 0f,
            m_attackEitr = 0f,
            m_attackHealth = 0f,
            m_attackHealthPercentage = 0f,
            m_attackStartNoise = 0f,
            m_attackHitNoise = 0f,
            m_consumeItem = false,
            m_requiresReload = false,
            m_speedFactor = 1f,
            m_speedFactorRotation = 1f
        };
    }

    private static void StickProjectile(
        Projectile projectile,
        Collider? collider,
        Vector3 hitPoint,
        Vector3 normal,
        StickyChargeState state)
    {
        Vector3 resolvedNormal = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
        Vector3 resolvedPoint = hitPoint;
        if (resolvedPoint == Vector3.zero && collider != null)
        {
            resolvedPoint = SecondaryAttackManager.ResolveSafeClosestPoint(collider, projectile.transform.position);
        }

        state.HitPoint = resolvedPoint;
        state.HitNormal = resolvedNormal;
        Vector3 incomingVelocity = projectile.GetVelocity();
        state.ImpactForward = incomingVelocity.sqrMagnitude > 0.001f
            ? incomingVelocity.normalized
            : projectile.transform.forward;
        state.ExpiresAt = Time.time + state.Lifetime;

        SuppressProjectileItemDrops(projectile);
        ProjectileAccess.SetVelocity(projectile, Vector3.zero);
        ProjectileAccess.SetDidHit(projectile, true);
        projectile.enabled = false;
        projectile.transform.position = resolvedPoint + resolvedNormal * StickSurfaceOffset;
        projectile.transform.rotation = ResolveImpactRotation(-resolvedNormal);

        foreach (Rigidbody rigidbody in projectile.GetComponentsInChildren<Rigidbody>())
        {
            rigidbody.linearVelocity = Vector3.zero;
            rigidbody.angularVelocity = Vector3.zero;
            rigidbody.isKinematic = true;
            rigidbody.detectCollisions = false;
        }

        foreach (Collider projectileCollider in projectile.GetComponentsInChildren<Collider>())
        {
            projectileCollider.enabled = false;
        }

        GameObject? hitObject = collider != null ? Projectile.FindHitObject(collider) : null;
        if (hitObject != null &&
            hitObject != projectile.gameObject &&
            !hitObject.isStatic)
        {
            projectile.transform.SetParent(hitObject.transform, worldPositionStays: true);
        }

        StickyChargeController controller = projectile.GetComponent<StickyChargeController>() ??
                                            projectile.gameObject.AddComponent<StickyChargeController>();
        controller.Initialize(projectile, state);
        ActiveCharges.Add(controller);
    }

    private static int DetonateAll(Player player)
    {
        int detonated = 0;
        for (int i = ActiveCharges.Count - 1; i >= 0; i--)
        {
            StickyChargeController controller = ActiveCharges[i];
            if (controller == null || !controller.IsValid)
            {
                ActiveCharges.RemoveAt(i);
                continue;
            }

            if (controller.Owner != player)
            {
                continue;
            }

            if (controller.Detonate())
            {
                detonated++;
            }
        }

        return detonated;
    }

    private static void TrimOwnerCharges(Character owner, int keepCount)
    {
        if (keepCount < 0)
        {
            keepCount = 0;
        }

        int count = 0;
        for (int i = ActiveCharges.Count - 1; i >= 0; i--)
        {
            StickyChargeController controller = ActiveCharges[i];
            if (controller == null || !controller.IsValid)
            {
                ActiveCharges.RemoveAt(i);
                continue;
            }

            if (controller.Owner != owner)
            {
                continue;
            }

            count++;
            if (count > keepCount)
            {
                controller.Expire();
            }
        }
    }

    private static void Detonate(StickyChargeController controller)
    {
        StickyChargeState state = controller.State;
        if (!TrySpawnAoeExplosion(state))
        {
            SpawnProjectileExplosion(state);
        }

        ProjectileRuntimeSystem.DestroyProjectileObject(controller.gameObject);
    }

    private static bool TrySpawnAoeExplosion(StickyChargeState state)
    {
        Projectile? prefabProjectile = state.ProjectilePrefab.GetComponent<Projectile>();
        GameObject? spawnOnHit = prefabProjectile?.m_spawnOnHit;
        if (spawnOnHit == null)
        {
            return false;
        }

        Aoe? prefabAoe = spawnOnHit.GetComponent<Aoe>() ?? spawnOnHit.GetComponentInChildren<Aoe>();
        IProjectile? prefabProjectileInterface = spawnOnHit.GetComponent<IProjectile>() ??
                                                  spawnOnHit.GetComponentInChildren<IProjectile>();
        if (prefabAoe == null || prefabProjectileInterface == null)
        {
            return false;
        }

        Quaternion rotation = ResolveSurfaceAlignedRotation(state.HitNormal, state.ImpactForward);
        prefabProjectile!.m_hitEffects.Create(state.HitPoint, rotation);
        prefabProjectile.m_spawnOnHitEffects.Create(state.HitPoint, rotation);

        GameObject spawned = Object.Instantiate(spawnOnHit, state.HitPoint, rotation);
        Aoe? aoe = spawned.GetComponent<Aoe>() ?? spawned.GetComponentInChildren<Aoe>();
        IProjectile? projectileInterface = spawned.GetComponent<IProjectile>();
        if (aoe == null || projectileInterface == null)
        {
            ProjectileRuntimeSystem.DestroyProjectileObject(spawned);
            return false;
        }

        aoe.m_radius *= state.AoeRadiusFactor;
        projectileInterface.Setup(
            state.Owner,
            Vector3.zero,
            state.HitNoise,
            state.HitData.Clone(),
            state.Weapon,
            state.Ammo);
        return true;
    }

    private static void SpawnProjectileExplosion(StickyChargeState state)
    {
        Quaternion rotation = ResolveSurfaceAlignedRotation(state.HitNormal, state.ImpactForward);
        GameObject projectileObject = Object.Instantiate(state.ProjectilePrefab, state.HitPoint, rotation);
        Projectile? projectile = projectileObject.GetComponent<Projectile>();
        IProjectile? projectileInterface = projectileObject.GetComponent<IProjectile>();
        if (projectile == null || projectileInterface == null)
        {
            ProjectileRuntimeSystem.DestroyProjectileObject(projectileObject);
            return;
        }

        SuppressProjectileItemDrops(projectile);
        projectile.m_aoe *= state.AoeRadiusFactor;
        projectileInterface.Setup(
            state.Owner,
            Vector3.zero,
            state.HitNoise,
            state.HitData.Clone(),
            state.Weapon,
            state.Ammo);
        projectile.m_aoe *= state.AoeRadiusFactor;

        _detonating = true;
        try
        {
            projectile.OnHit(null, state.HitPoint, false, state.HitNormal);
        }
        finally
        {
            _detonating = false;
        }
    }

    private static Quaternion ResolveImpactRotation(Vector3 normal)
    {
        Vector3 forward = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
        if (Mathf.Abs(Vector3.Dot(forward, Vector3.up)) > 0.98f)
        {
            return Quaternion.LookRotation(forward, Vector3.forward);
        }

        return Quaternion.LookRotation(forward, Vector3.up);
    }

    private static Quaternion ResolveSurfaceAlignedRotation(Vector3 normal, Vector3 preferredForward)
    {
        Vector3 up = normal.sqrMagnitude > 0.001f ? normal.normalized : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(preferredForward, up);
        if (forward.sqrMagnitude <= 0.001f)
        {
            forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        }

        if (forward.sqrMagnitude <= 0.001f)
        {
            forward = Vector3.ProjectOnPlane(Vector3.right, up);
        }

        return Quaternion.LookRotation(forward.normalized, up);
    }

    private static void SuppressProjectileItemDrops(Projectile projectile)
    {
        projectile.m_respawnItemOnHit = false;
        projectile.m_spawnItem = null;
        projectile.m_spawnOnTtl = false;
    }

    private static void Unregister(StickyChargeController controller)
    {
        ActiveCharges.Remove(controller);
    }

    private sealed class StickyChargeState
    {
        public StickyChargeState(
            GameObject projectilePrefab,
            Character owner,
            ItemDrop.ItemData weapon,
            ItemDrop.ItemData? ammo,
            float hitNoise,
            HitData hitData,
            SecondaryAttackDefinition definition,
            float lifetime,
            float aoeRadiusFactor)
        {
            ProjectilePrefab = projectilePrefab;
            Owner = owner;
            Weapon = weapon;
            Ammo = ammo;
            HitNoise = hitNoise;
            HitData = hitData;
            Definition = definition;
            Lifetime = lifetime;
            AoeRadiusFactor = aoeRadiusFactor;
        }

        public GameObject ProjectilePrefab { get; }

        public Character Owner { get; }

        public ItemDrop.ItemData Weapon { get; }

        public ItemDrop.ItemData? Ammo { get; }

        public float HitNoise { get; }

        public HitData HitData { get; }

        public SecondaryAttackDefinition Definition { get; }

        public float Lifetime { get; }

        public float AoeRadiusFactor { get; }

        public Vector3 HitPoint { get; set; }

        public Vector3 HitNormal { get; set; } = Vector3.up;

        public Vector3 ImpactForward { get; set; } = Vector3.forward;

        public float ExpiresAt { get; set; }
    }

    private sealed class StickyChargeController : MonoBehaviour
    {
        private Projectile _projectile = null!;
        private bool _finished;

        public StickyChargeState State { get; private set; } = null!;

        public Character Owner => State.Owner;

        public bool IsValid => !_finished && this != null && State != null && _projectile != null;

        public void Initialize(Projectile projectile, StickyChargeState state)
        {
            _projectile = projectile;
            State = state;
        }

        public bool Detonate()
        {
            if (_finished)
            {
                return false;
            }

            _finished = true;
            StickyDetonatorSystem.Detonate(this);
            return true;
        }

        public void Expire()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            ProjectileRuntimeSystem.DestroyProjectileObject(gameObject);
        }

        private void Update()
        {
            if (_finished)
            {
                return;
            }

            if (State.Owner == null || State.Owner.IsDead() || Time.time >= State.ExpiresAt)
            {
                Expire();
            }
        }

        private void OnDestroy()
        {
            _finished = true;
            StickyDetonatorSystem.Unregister(this);
        }
    }

    private sealed class StickyDetonatorInputState
    {
        public bool WasBlocking { get; set; }
    }
}
