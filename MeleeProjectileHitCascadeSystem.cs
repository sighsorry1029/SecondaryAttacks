using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class MeleeProjectileHitCascadeSystem
{
    private const int MaxImpactBurstColliders = 96;
    private const float MinPositiveImpactBurstDamage = 0.1f;
    private const string SpearRainPresetName = "spearRain";
    private const float SpearRainVelocityLeadFactor = 0.75f;
    private const float SpearRainMaxVelocityLeadDistance = 3f;

    private static readonly ConditionalWeakTable<Projectile, OnProjectileHitSourceState> OnProjectileHitSources = new();
    private static readonly ConditionalWeakTable<Projectile, SpearRainFollowupProjectileState> SpearRainFollowupProjectiles = new();
    private static readonly ConditionalWeakTable<Character, SpearRainPendingState> PendingSpearRainByOwner = new();
    private static readonly Collider[] ImpactBurstColliders = new Collider[MaxImpactBurstColliders];
    private static readonly List<ImpactBurstTarget> ImpactBurstTargets = new();
    private static readonly HashSet<int> ImpactBurstTargetIds = new();
    private static int _characterMask;
    private static int _impactBurstMask;

    internal static bool IsApplyingImpactBurstDamage { get; private set; }

    internal static void RegisterOnProjectileHitSource(Projectile projectile, Attack attack, ItemDrop.ItemData weapon)
    {
        if (projectile == null ||
            attack == null ||
            weapon?.m_dropPrefab == null ||
            attack.m_attackProjectile == null)
        {
            return;
        }

        float baseAdrenaline = projectile.m_adrenaline;
        if (baseAdrenaline <= 0f)
        {
            baseAdrenaline = attack.m_attackAdrenaline > 0f
                ? attack.m_attackAdrenaline
                : attack.m_attackUseAdrenaline;
        }

        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition))
        {
            return;
        }

        if (definition.Behavior is not CopiedSecondaryBehavior)
        {
            return;
        }

        if (definition.OnProjectileHit == null)
        {
            return;
        }

        RegisterOnProjectileHitSource(
            projectile,
            definition,
            definition.OnProjectileHit,
            attack.m_attackProjectile,
            attack,
            attack.m_character,
            weapon,
            attack.m_lastUsedAmmo,
            projectile.m_hitNoise,
            baseAdrenaline);
    }

    private static void RegisterOnProjectileHitSource(
        Projectile projectile,
        SecondaryAttackDefinition definition,
        MeleeOnProjectileHitDefinition config,
        GameObject projectilePrefab,
        Attack sourceAttack,
        Character owner,
        ItemDrop.ItemData weapon,
        ItemDrop.ItemData? ammo,
        float hitNoise,
        float baseAdrenaline)
    {
        HitData? baseHitData = projectile.m_originalHitData?.Clone();
        if (baseHitData == null)
        {
            return;
        }

        SecondaryAttackProjectileToolTierSystem.ApplyToHitData(
            baseHitData,
            projectile,
            weapon);
        OnProjectileHitSources.Remove(projectile);
        OnProjectileHitSources.Add(
            projectile,
            new OnProjectileHitSourceState(
                definition,
                config,
                projectilePrefab,
                sourceAttack,
                owner,
                weapon,
                ammo,
                hitNoise,
                baseHitData,
                baseAdrenaline));
        projectile.m_adrenaline = 0f;
        if (IsSpearRainPreset(config.Preset))
        {
            RegisterPendingSpearRain(projectile, owner);
        }
    }

    internal static bool HasPendingSpearRain(Character owner)
    {
        if (owner == null ||
            !PendingSpearRainByOwner.TryGetValue(owner, out SpearRainPendingState? pending) ||
            pending.Count <= 0)
        {
            return false;
        }
        return true;
    }

    internal static void AddPendingSpearRain(Character owner)
    {
        if (owner == null)
        {
            return;
        }

        SpearRainPendingState pending = PendingSpearRainByOwner.GetValue(owner, _ => new SpearRainPendingState());
        pending.Count++;
    }

    internal static void RemovePendingSpearRain(Character owner)
    {
        if (owner == null ||
            !PendingSpearRainByOwner.TryGetValue(owner, out SpearRainPendingState? pending) ||
            pending.Count <= 0)
        {
            return;
        }

        pending.Count--;
    }

    internal static void TryTrigger(Projectile projectile, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
    {
        if (water ||
            projectile == null ||
            collider == null ||
            !OnProjectileHitSources.TryGetValue(projectile, out OnProjectileHitSourceState? state) ||
            state.Triggered)
        {
            return;
        }

        Character? target = ProjectileRuntimeSystem.GetHitCharacter(collider);
        if (state.Config.TriggerOnCharactersOnly && target == null)
        {
            return;
        }

        if (target != null && !IsValidTarget(state.Owner, target))
        {
            return;
        }

        state.Triggered = true;
        OnProjectileHitSources.Remove(projectile);
        ReleasePendingSpearRain(projectile);

        Vector3 targetPoint = target != null ? target.GetCenterPoint() : hitPoint;
        GameObject? directHitObject = Projectile.FindHitObject(collider);
        if (state.Config.Preset.Equals("impactBurst", System.StringComparison.OrdinalIgnoreCase))
        {
            TryGrantOnProjectileHitAdrenaline(state, target);
            TriggerImpactBurst(state, targetPoint, ResolveImpactBurstVfxPoint(hitPoint, targetPoint), target, directHitObject, normal);
            return;
        }

        if (!TryConsumeSpearRainCooldown(state))
        {
            return;
        }

        targetPoint = ResolveSpearRainTargetPoint(state, target, targetPoint);
        TryGrantOnProjectileHitAdrenaline(state, target);
        SpawnSpearRain(state, targetPoint, target);
    }

    private static void TryGrantOnProjectileHitAdrenaline(OnProjectileHitSourceState state, Character? target)
    {
        if (target == null ||
            state.BaseAdrenaline <= 0f ||
            state.SourceAttack == null ||
            state.Owner == null ||
            !BaseAI.IsEnemy(state.Owner, target))
        {
            return;
        }

        SecondaryAttackAdrenalineSystem.TryGrantOnceRaw(
            state.SourceAttack,
            target,
            state.BaseAdrenaline,
            1f,
            "meleeProjectileSource");
    }

    internal static bool ShouldIgnoreOnProjectileHitSourceHit(Projectile projectile, Collider collider)
    {
        if (projectile == null ||
            collider == null ||
            !OnProjectileHitSources.TryGetValue(projectile, out OnProjectileHitSourceState? state) ||
            state.Triggered ||
            state.Owner == null)
        {
            return false;
        }

        Character? target = ProjectileRuntimeSystem.GetHitCharacter(collider);
        if (target != state.Owner)
        {
            return false;
        }
        return true;
    }

    private static bool IsValidTarget(Character owner, Character target)
    {
        if (owner == null || target == null || target == owner || target.IsDead())
        {
            return false;
        }

        if (BaseAI.IsEnemy(owner, target))
        {
            return true;
        }

        return owner.IsPlayer() &&
               target.GetBaseAI() != null &&
               target.GetBaseAI().IsAggravatable();
    }

    private static void TriggerImpactBurst(
        OnProjectileHitSourceState state,
        Vector3 impactPoint,
        Vector3 vfxPoint,
        Character? directTarget,
        GameObject? directHitObject,
        Vector3 normal)
    {
        MeleeOnProjectileHitDefinition config = state.Config;
        if (config.Radius <= 0f || config.DamageFactor <= 0f && config.PushFactor <= 0f)
        {
            return;
        }

        PlayImpactBurstVfx(config, vfxPoint, normal);

        ImpactBurstTargets.Clear();
        ImpactBurstTargetIds.Clear();
        int hitCount = 0;
        try
        {
            float radiusSqr = config.Radius * config.Radius;
            hitCount = Physics.OverlapSphereNonAlloc(
                impactPoint,
                config.Radius,
                ImpactBurstColliders,
                config.IncludeDestructibles ? GetImpactBurstMask() : GetCharacterMask(),
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = ImpactBurstColliders[i];
                if (collider == null)
                {
                    continue;
                }

                GameObject hitObject = Projectile.FindHitObject(collider);
                if (hitObject == null || hitObject == state.Owner.gameObject)
                {
                    continue;
                }

                Character? character = ProjectileRuntimeSystem.GetHitCharacter(collider);
                IDestructible? destructible = character != null ? character : hitObject.GetComponent<IDestructible>();
                if (destructible == null)
                {
                    continue;
                }

                if (ShouldSkipImpactBurstTarget(
                        state,
                        config,
                        directTarget,
                        directHitObject,
                        hitObject,
                        character,
                        destructible) ||
                    !TryAddImpactBurstTarget(destructible, character, collider, hitObject))
                {
                    continue;
                }

                Vector3 point = ResolveImpactPoint(collider, impactPoint, destructible);
                float distanceSqr = ResolveImpactBurstDistanceSqr(impactPoint, point, character, destructible, radiusSqr);
                ImpactBurstTargets.Add(new ImpactBurstTarget(destructible, character, collider, point, distanceSqr));
            }

            ImpactBurstTargets.Sort((left, right) => left.DistanceSqr.CompareTo(right.DistanceSqr));
            foreach (ImpactBurstTarget target in ImpactBurstTargets)
            {
                ApplyImpactBurstHit(state, target, impactPoint, normal);
            }
        }
        finally
        {
            ImpactBurstTargets.Clear();
            ImpactBurstTargetIds.Clear();
            System.Array.Clear(ImpactBurstColliders, 0, ImpactBurstColliders.Length);
        }
    }

    private static void PlayImpactBurstVfx(
        MeleeOnProjectileHitDefinition config,
        Vector3 vfxPoint,
        Vector3 normal)
    {
        string vfx = config.Vfx?.Trim() ?? "";
        if (vfx.Length == 0)
        {
            return;
        }

        Quaternion rotation = SecondaryAttackNamedEffectSystem.RotationFromNormal(normal);
        SecondaryAttackNamedEffectSystem.Create(vfx, vfxPoint, rotation, "impactBurst.vfx");
    }

    private static Vector3 ResolveImpactBurstVfxPoint(Vector3 hitPoint, Vector3 fallbackPoint)
    {
        return hitPoint.sqrMagnitude > 0.001f ? hitPoint : fallbackPoint;
    }

    private static bool ShouldSkipImpactBurstTarget(
        OnProjectileHitSourceState state,
        MeleeOnProjectileHitDefinition config,
        Character? directTarget,
        GameObject? directHitObject,
        GameObject hitObject,
        Character? character,
        IDestructible destructible)
    {
        if (!config.IncludeDirectTarget &&
            ((directTarget != null &&
              (character == directTarget ||
               character != null && character.gameObject == directTarget.gameObject)) ||
             (directHitObject != null && hitObject == directHitObject)))
        {
            return true;
        }

        if (character != null)
        {
            if (IsValidTarget(state.Owner, character))
            {
                return false;
            }

            return true;
        }

        if (!config.IncludeDestructibles)
        {
            return true;
        }

        if (state.Weapon?.m_shared?.m_tamedOnly == true)
        {
            return true;
        }

        DestructibleType type = destructible.GetDestructibleType();
        if (type == DestructibleType.None)
        {
            return true;
        }

        if (type == DestructibleType.Character)
        {
            return true;
        }

        return false;
    }

    private static void ApplyImpactBurstHit(
        OnProjectileHitSourceState state,
        ImpactBurstTarget target,
        Vector3 impactPoint,
        Vector3 normal)
    {
        HitData hitData = state.BaseHitData.Clone();
        SecondaryAttackProjectileToolTierSystem.ApplyToHitData(
            hitData,
            null,
            state.Weapon);
        float damageScale = state.Config.DamageFactor;
        if (!Mathf.Approximately(damageScale, 1f))
        {
            hitData.m_damage.Modify(damageScale);
        }

        float totalDamage = hitData.m_damage.GetTotalDamage();
        if (totalDamage > 0f && totalDamage < MinPositiveImpactBurstDamage)
        {
            hitData.m_damage.Modify(MinPositiveImpactBurstDamage / totalDamage);
        }

        if (hitData.m_damage.GetTotalDamage() <= 0f && state.Config.PushFactor <= 0f)
        {
            return;
        }

        if (target.Character != null && hitData.m_dodgeable && target.Character.IsDodgeInvincible())
        {
            if (target.Character is Player dodgingPlayer)
            {
                dodgingPlayer.HitWhileDodging();
            }
            return;
        }

        hitData.m_pushForce *= state.Config.PushFactor;
        hitData.m_skillRaiseAmount = 0f;
        hitData.m_point = target.Point;
        hitData.m_dir = ResolveImpactDirection(impactPoint, target.Character != null ? target.Character.GetCenterPoint() : target.Point, normal, state.Owner);
        hitData.m_hitCollider = target.Collider;
        hitData.SetAttacker(state.Owner);

        bool wasApplyingImpactBurstDamage = IsApplyingImpactBurstDamage;
        IsApplyingImpactBurstDamage = true;
        try
        {
            target.Destructible.Damage(hitData);
            if (target.Character != null && BaseAI.IsEnemy(state.Owner, target.Character))
            {
                TryGrantOnProjectileHitAdrenaline(state, target.Character);
            }
        }
        finally
        {
            IsApplyingImpactBurstDamage = wasApplyingImpactBurstDamage;
        }
    }

    private static bool TryAddImpactBurstTarget(
        IDestructible destructible,
        Character? character,
        Collider collider,
        GameObject hitObject)
    {
        int targetId;
        if (character != null)
        {
            targetId = character.gameObject.GetInstanceID();
        }
        else if (destructible is Component component)
        {
            targetId = component.gameObject.GetInstanceID();
        }
        else if (hitObject != null)
        {
            targetId = hitObject.GetInstanceID();
        }
        else
        {
            targetId = collider.GetInstanceID();
        }

        return ImpactBurstTargetIds.Add(targetId);
    }

    private static Vector3 ResolveImpactPoint(Collider collider, Vector3 impactPoint, IDestructible destructible)
    {
        Vector3 point = SecondaryAttackManager.ResolveSafeClosestPoint(collider, impactPoint);
        if ((point - impactPoint).sqrMagnitude < 0.0001f)
        {
            point = destructible is Character character ? character.GetCenterPoint() : collider.bounds.center;
        }

        return point;
    }

    private static float ResolveImpactBurstDistanceSqr(
        Vector3 impactPoint,
        Vector3 targetPoint,
        Character? character,
        IDestructible destructible,
        float radiusSqr)
    {
        if (character != null)
        {
            return Mathf.Min(radiusSqr, (targetPoint - impactPoint).sqrMagnitude);
        }

        Vector3 horizontalOffset = Vector3.ProjectOnPlane(targetPoint - impactPoint, Vector3.up);
        if (horizontalOffset.sqrMagnitude > 0.0001f)
        {
            return Mathf.Min(radiusSqr, horizontalOffset.sqrMagnitude);
        }

        if (destructible is Component component)
        {
            Vector3 fallbackOffset = Vector3.ProjectOnPlane(component.transform.position - impactPoint, Vector3.up);
            return Mathf.Min(radiusSqr, fallbackOffset.sqrMagnitude);
        }

        return 0f;
    }

    private static Vector3 ResolveImpactDirection(Vector3 impactPoint, Vector3 targetPoint, Vector3 normal, Character owner)
    {
        Vector3 direction = Vector3.ProjectOnPlane(targetPoint - impactPoint, Vector3.up);
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.ProjectOnPlane(-normal, Vector3.up);
        }

        if (direction.sqrMagnitude < 0.001f && owner != null)
        {
            direction = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up);
        }

        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
    }

    private static int GetCharacterMask()
    {
        if (_characterMask == 0)
        {
            _characterMask = LayerMask.GetMask("character", "character_net", "character_ghost", "hitbox", "character_noenv");
        }

        return _characterMask;
    }

    private static int GetImpactBurstMask()
    {
        if (_impactBurstMask == 0)
        {
            _impactBurstMask = LayerMask.GetMask(
                "Default",
                "static_solid",
                "Default_small",
                "piece",
                "piece_nonsolid",
                "terrain",
                "character",
                "character_net",
                "character_ghost",
                "hitbox",
                "character_noenv",
                "vehicle");
        }

        return _impactBurstMask;
    }

    private static bool TryConsumeSpearRainCooldown(OnProjectileHitSourceState state)
    {
        if (MeleePresetCooldownSystem.TryConsume(
                state.Owner,
                state.Weapon,
                SpearRainPresetName,
                state.Config.PresetCooldown,
                out _))
        {
            return true;
        }
        return false;
    }

    private static Vector3 ResolveSpearRainTargetPoint(OnProjectileHitSourceState state, Character? target, Vector3 fallbackPoint)
    {
        if (target == null)
        {
            return fallbackPoint;
        }

        Vector3 targetPoint = target.GetCenterPoint();
        Vector3 horizontalVelocity = ResolveCharacterHorizontalVelocity(target);
        if (horizontalVelocity.sqrMagnitude <= 0.0001f)
        {
            return targetPoint;
        }

        Vector3 lead = horizontalVelocity * Mathf.Max(0.1f, state.Config.FlightTime) * SpearRainVelocityLeadFactor;
        float maxLead = Mathf.Max(0f, SpearRainMaxVelocityLeadDistance);
        if (maxLead > 0f && lead.sqrMagnitude > maxLead * maxLead)
        {
            lead = lead.normalized * maxLead;
        }

        Vector3 predictedPoint = targetPoint + lead;
        return predictedPoint;
    }

    private static Vector3 ResolveCharacterHorizontalVelocity(Character target)
    {
        Rigidbody? body = target.m_body != null ? target.m_body : target.GetComponent<Rigidbody>();
        if (body == null)
        {
            return Vector3.zero;
        }

        return Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);
    }

    private static void SpawnSpearRain(OnProjectileHitSourceState state, Vector3 targetPoint, Character? markedTarget)
    {
        Projectile? prefabProjectile = state.ProjectilePrefab.GetComponent<Projectile>();
        if (prefabProjectile == null)
        {
            return;
        }

        float gravity = prefabProjectile.m_gravity;
        SpearRainTargetMarker? targetMarker = CreateSpearRainTargetMarker(markedTarget, state.Config.FlightTime);
        CopiedThrowProjectileVisualSystem.SpawnedProjectileVisualContext visualContext =
            state.Definition.Behavior is CopiedSecondaryBehavior
                ? CopiedThrowProjectileVisualSystem.CreateSpawnedProjectileVisualContext(state.Weapon, state.ProjectilePrefab)
                : default;
        for (int spawned = 0; spawned < state.Config.Count; spawned++)
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * state.Config.SpawnRadius;
            Vector3 spawnPoint = targetPoint + new Vector3(offset.x, state.Config.SpawnHeight, offset.y);
            Vector3 launchVelocity = CalculateBallisticVelocity(spawnPoint, targetPoint, gravity, state.Config.FlightTime);
            if (launchVelocity.sqrMagnitude < 0.001f)
            {
                launchVelocity = Vector3.down;
            }

            SpawnFollowupProjectile(
                state,
                spawnPoint,
                launchVelocity,
                targetMarker,
                targetPoint,
                gravity,
                visualContext);
        }
    }

    private static void SpawnFollowupProjectile(
        OnProjectileHitSourceState state,
        Vector3 spawnPoint,
        Vector3 launchVelocity,
        SpearRainTargetMarker? targetMarker,
        Vector3 fallbackTargetPoint,
        float gravity,
        CopiedThrowProjectileVisualSystem.SpawnedProjectileVisualContext visualContext)
    {
        Quaternion rotation = Quaternion.LookRotation(launchVelocity.normalized);
        GameObject projectileObject = Object.Instantiate(state.ProjectilePrefab, spawnPoint, rotation);
        Projectile? projectile = projectileObject.GetComponent<Projectile>();
        IProjectile? projectileInterface = projectileObject.GetComponent<IProjectile>();
        if (projectile == null || projectileInterface == null)
        {
            ProjectileRuntimeSystem.DestroyProjectileObject(projectileObject);
            return;
        }

        RegisterSpearRainFollowupProjectile(projectile);
        SuppressProjectileItemDrops(projectile);
        HitData hitData = BuildFollowupHitData(state);
        projectileInterface.Setup(
            state.Owner,
            launchVelocity,
            state.HitNoise,
            hitData,
            state.Weapon,
            state.Ammo);
        projectile.m_adrenaline = 0f;
        SuppressProjectileItemDrops(projectile);

        if (targetMarker != null)
        {
            SpearRainGuidedProjectileController controller =
                projectile.GetComponent<SpearRainGuidedProjectileController>() ??
                projectile.gameObject.AddComponent<SpearRainGuidedProjectileController>();
            controller.Configure(
                projectile,
                targetMarker,
                fallbackTargetPoint,
                state.Config.FlightTime,
                gravity,
                launchVelocity);
        }

        if (visualContext.Active)
        {
            CopiedThrowProjectileVisualSystem.ApplyCurrentWeaponVisualForSpawnedProjectile(projectile, visualContext);
        }

        SecondaryAttackRuntimeFacade.SetProjectileAttackAttribution(
            projectile,
            state.WeaponPrefabName,
            secondaryAttack: true,
            state.Definition,
            disableCurrentAttackFallback: false);
    }

    internal static void DestroySpearRainFollowupAfterHit(Projectile? projectile)
    {
        if (projectile == null ||
            !SpearRainFollowupProjectiles.TryGetValue(projectile, out _))
        {
            return;
        }

        SpearRainFollowupProjectiles.Remove(projectile);
        ProjectileRuntimeSystem.DestroyProjectileObject(projectile.gameObject);
    }

    private static void RegisterSpearRainFollowupProjectile(Projectile projectile)
    {
        SpearRainFollowupProjectiles.Remove(projectile);
        SpearRainFollowupProjectiles.Add(projectile, new SpearRainFollowupProjectileState());
    }

    private static HitData BuildFollowupHitData(OnProjectileHitSourceState state)
    {
        HitData hitData = state.BaseHitData.Clone();
        SecondaryAttackProjectileToolTierSystem.ApplyToHitData(
            hitData,
            null,
            state.Weapon);
        if (!Mathf.Approximately(state.Config.DamageFactor, 1f))
        {
            hitData.m_damage.Modify(state.Config.DamageFactor);
        }

        hitData.m_skillRaiseAmount = 0f;
        hitData.SetAttacker(state.Owner);
        return hitData;
    }

    private static void SuppressProjectileItemDrops(Projectile projectile)
    {
        projectile.m_respawnItemOnHit = false;
        projectile.m_spawnItem = null;
        projectile.m_spawnOnTtl = false;
    }

    private static bool IsSpearRainPreset(string preset) =>
        preset.Equals(SpearRainPresetName, System.StringComparison.OrdinalIgnoreCase);

    private static void RegisterPendingSpearRain(Projectile projectile, Character owner)
    {
        if (projectile == null || owner == null)
        {
            return;
        }

        SpearRainPendingProjectileMarker marker =
            projectile.GetComponent<SpearRainPendingProjectileMarker>() ??
            projectile.gameObject.AddComponent<SpearRainPendingProjectileMarker>();
        marker.Initialize(owner);
    }

    private static void ReleasePendingSpearRain(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        projectile.GetComponent<SpearRainPendingProjectileMarker>()?.Release();
    }

    private static SpearRainTargetMarker? CreateSpearRainTargetMarker(Character? target, float flightTime)
    {
        if (target == null)
        {
            return null;
        }

        SpearRainTargetMarker marker =
            target.GetComponent<SpearRainTargetMarker>() ??
            target.gameObject.AddComponent<SpearRainTargetMarker>();
        marker.Configure(target, Mathf.Max(0.1f, flightTime) + 0.5f);
        return marker;
    }

    internal static Vector3 CalculateBallisticVelocity(
        Vector3 spawnPoint,
        Vector3 targetPoint,
        float gravity,
        float flightTime,
        float minimumFlightTime = 0.1f)
    {
        flightTime = Mathf.Max(minimumFlightTime, flightTime);
        Vector3 gravityVector = Vector3.down * gravity;
        return (targetPoint - spawnPoint - gravityVector * (0.5f * flightTime * flightTime)) / flightTime;
    }

    private sealed class OnProjectileHitSourceState
    {
        public OnProjectileHitSourceState(
            SecondaryAttackDefinition definition,
            MeleeOnProjectileHitDefinition config,
            GameObject projectilePrefab,
            Attack sourceAttack,
            Character owner,
            ItemDrop.ItemData weapon,
            ItemDrop.ItemData? ammo,
            float hitNoise,
            HitData baseHitData,
            float baseAdrenaline)
        {
            Definition = definition;
            Config = config;
            ProjectilePrefab = projectilePrefab;
            SourceAttack = sourceAttack;
            Owner = owner;
            Weapon = weapon;
            Ammo = ammo;
            HitNoise = hitNoise;
            BaseHitData = baseHitData;
            BaseAdrenaline = Mathf.Max(0f, baseAdrenaline);
            WeaponPrefabName = weapon.m_dropPrefab != null ? weapon.m_dropPrefab.name : definition.PrefabName;
        }

        public SecondaryAttackDefinition Definition { get; }

        public MeleeOnProjectileHitDefinition Config { get; }

        public GameObject ProjectilePrefab { get; }

        public Attack SourceAttack { get; }

        public Character Owner { get; }

        public ItemDrop.ItemData Weapon { get; }

        public ItemDrop.ItemData? Ammo { get; }

        public float HitNoise { get; }

        public HitData BaseHitData { get; }

        public float BaseAdrenaline { get; }

        public string WeaponPrefabName { get; }

        public bool Triggered { get; set; }
    }

    private readonly struct ImpactBurstTarget
    {
        public ImpactBurstTarget(IDestructible destructible, Character? character, Collider collider, Vector3 point, float distanceSqr)
        {
            Destructible = destructible;
            Character = character;
            Collider = collider;
            Point = point;
            DistanceSqr = distanceSqr;
        }

        public IDestructible Destructible { get; }

        public Character? Character { get; }

        public Collider Collider { get; }

        public Vector3 Point { get; }

        public float Distance => Mathf.Sqrt(DistanceSqr);

        public float DistanceSqr { get; }
    }

    private sealed class SpearRainPendingState
    {
        public int Count;
    }

    private sealed class SpearRainFollowupProjectileState
    {
    }
}

internal sealed class SpearRainPendingProjectileMarker : MonoBehaviour
{
    private Character? _owner;
    private bool _active;

    internal void Initialize(Character owner)
    {
        Release();
        _owner = owner;
        _active = true;
        MeleeProjectileHitCascadeSystem.AddPendingSpearRain(owner);
    }

    internal void Release()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        Character? owner = _owner;
        _owner = null;
        if (owner != null)
        {
            MeleeProjectileHitCascadeSystem.RemovePendingSpearRain(owner);
        }
    }

    private void OnDestroy()
    {
        Release();
    }
}

internal sealed class SpearRainTargetMarker : MonoBehaviour
{
    private Character? _target;
    private Vector3 _lastKnownPoint;
    private float _expiresAt;

    internal bool IsValid => Time.time <= _expiresAt;

    internal Vector3 CurrentPoint => _lastKnownPoint;

    internal void Configure(Character target, float lifetime)
    {
        _target = target;
        _lastKnownPoint = target.GetCenterPoint();
        _expiresAt = Time.time + Mathf.Max(0.1f, lifetime);
    }

    private void Update()
    {
        if (_target != null && !_target.IsDead())
        {
            _lastKnownPoint = _target.GetCenterPoint();
        }
        if (!IsValid)
        {
            Object.Destroy(this);
        }
    }
}

internal sealed class SpearRainGuidedProjectileController : MonoBehaviour
{
    private const float MinRemainingFlightTime = 0.05f;
    private const float MaxSpeedFactor = 1.75f;
    private const float MaxSpeedBonus = 8f;

    private Projectile? _projectile;
    private ZNetView? _nview;
    private SpearRainTargetMarker? _targetMarker;
    private Vector3 _fallbackTargetPoint;
    private float _flightTime;
    private float _gravity;
    private float _elapsed;
    private float _maxSpeed;
    private bool _active;

    internal void Configure(
        Projectile projectile,
        SpearRainTargetMarker targetMarker,
        Vector3 fallbackTargetPoint,
        float flightTime,
        float gravity,
        Vector3 initialVelocity)
    {
        _projectile = projectile;
        _nview = projectile.GetComponent<ZNetView>();
        _targetMarker = targetMarker;
        _fallbackTargetPoint = fallbackTargetPoint;
        _flightTime = Mathf.Max(0.1f, flightTime);
        _gravity = gravity;
        _elapsed = 0f;
        _maxSpeed = Mathf.Max(initialVelocity.magnitude * MaxSpeedFactor, initialVelocity.magnitude + MaxSpeedBonus);
        _active = true;
        projectile.m_ttl = Mathf.Max(projectile.m_ttl, _flightTime + 0.5f);
    }

    private void FixedUpdate()
    {
        if (!_active || _projectile == null)
        {
            return;
        }

        if (_nview != null && _nview.IsValid() && !_nview.IsOwner())
        {
            return;
        }

        _elapsed += Time.fixedDeltaTime;
        if (_elapsed >= _flightTime)
        {
            _active = false;
            return;
        }

        Vector3 targetPoint = _targetMarker != null && _targetMarker.IsValid
            ? _targetMarker.CurrentPoint
            : _fallbackTargetPoint;
        float remaining = Mathf.Max(MinRemainingFlightTime, _flightTime - _elapsed);
        Vector3 desiredVelocity = MeleeProjectileHitCascadeSystem.CalculateBallisticVelocity(
            transform.position,
            targetPoint,
            _gravity,
            remaining,
            MinRemainingFlightTime);
        if (desiredVelocity.sqrMagnitude < 0.001f)
        {
            return;
        }

        float speed = desiredVelocity.magnitude;
        if (_maxSpeed > 0f && speed > _maxSpeed)
        {
            desiredVelocity = desiredVelocity.normalized * _maxSpeed;
        }

        ProjectileAccess.SetVelocity(_projectile, desiredVelocity);
    }

}
