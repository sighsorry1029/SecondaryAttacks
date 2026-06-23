using System.Collections.Generic;
using System.Diagnostics;
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
        if (TryDescribeSpearRainFollowupProjectile(projectile, out string followupDescription))
        {
            LogDebug($"register observed spearRain follow-up as source candidate: {followupDescription} attackWeapon={attack?.m_weapon?.m_dropPrefab?.name ?? "<null>"} setupWeapon={weapon?.m_dropPrefab?.name ?? "<null>"}.");
        }

        if (projectile == null ||
            attack == null ||
            weapon?.m_dropPrefab == null ||
            attack.m_attackProjectile == null)
        {
            LogDebug("register skipped: missing projectile, attack, weapon prefab, or attack projectile.");
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
            LogDebug($"register skipped for {weapon.m_dropPrefab.name}: no definition.");
            return;
        }

        if (definition.Behavior is not CopiedSecondaryBehavior)
        {
            LogDebug($"register skipped for {weapon.m_dropPrefab.name}: behavior={definition.BehaviorType}.");
            return;
        }

        if (definition.OnProjectileHit == null)
        {
            LogDebug($"register skipped for {weapon.m_dropPrefab.name}: onProjectileHit is null.");
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
            LogDebug($"register skipped for {weapon.m_dropPrefab.name}: projectile original hit data is null.");
            return;
        }

        SecondaryAttackProjectileToolTierSystem.ApplyToHitData(
            baseHitData,
            projectile,
            weapon,
            "MeleeProjectileHitCascadeSystem.RegisterOnProjectileHitSource");
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
            RegisterPendingSpearRain(projectile, owner, weapon);
        }

        LogDebug(
            $"registered source projectile weapon={weapon.m_dropPrefab.name} projectile={projectile.name} preset={config.Preset} count={config.Count}.");
    }

    internal static bool HasPendingSpearRain(Character owner, ItemDrop.ItemData? weapon)
    {
        if (owner == null ||
            !PendingSpearRainByOwner.TryGetValue(owner, out SpearRainPendingState? pending) ||
            pending.Count <= 0)
        {
            return false;
        }

        LogDebug($"spearRain pending active owner={owner.name} weapon={weapon?.m_dropPrefab?.name ?? "<unknown>"} count={pending.Count}.");
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
        LogDebug($"spearRain pending added owner={owner.name} count={pending.Count}.");
    }

    internal static void RemovePendingSpearRain(Character owner, string reason)
    {
        if (owner == null ||
            !PendingSpearRainByOwner.TryGetValue(owner, out SpearRainPendingState? pending) ||
            pending.Count <= 0)
        {
            return;
        }

        pending.Count--;
        LogDebug($"spearRain pending removed owner={owner.name} count={pending.Count} reason={reason}.");
    }

    internal static void TryTrigger(Projectile projectile, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
    {
        if (water ||
            projectile == null ||
            collider == null ||
            !OnProjectileHitSources.TryGetValue(projectile, out OnProjectileHitSourceState? state) ||
            state.Triggered)
        {
            if (projectile != null)
            {
                bool hasState = OnProjectileHitSources.TryGetValue(projectile, out OnProjectileHitSourceState? skippedState);
                string followup = TryDescribeSpearRainFollowupProjectile(projectile, out string followupDescription)
                    ? $" followup=[{followupDescription}]"
                    : "";
                LogDebug($"trigger skipped: water={water} colliderNull={collider == null} hasState={hasState} alreadyTriggered={skippedState?.Triggered ?? false} projectile={projectile.name}.{followup}");
            }

            return;
        }

        Character? target = ProjectileRuntimeSystem.GetHitCharacter(collider);
        if (state.Config.TriggerOnCharactersOnly && target == null)
        {
            LogDebug($"trigger skipped for {state.WeaponPrefabName}: hit object is not a character collider={collider.name}.");
            return;
        }

        if (target != null && !IsValidTarget(state.Owner, target))
        {
            LogDebug($"trigger skipped for {state.WeaponPrefabName}: invalid target={target.name}.");
            return;
        }

        state.Triggered = true;
        OnProjectileHitSources.Remove(projectile);
        ReleasePendingSpearRain(projectile, "triggered");

        Vector3 targetPoint = target != null ? target.GetCenterPoint() : hitPoint;
        GameObject? directHitObject = Projectile.FindHitObject(collider);
        if (state.Config.Preset.Equals("impactBurst", System.StringComparison.OrdinalIgnoreCase))
        {
            TryGrantOnProjectileHitAdrenaline(state, target);
            IDestructible? directDestructible = target != null ? target : directHitObject?.GetComponent<IDestructible>();
            LogDebug(
                $"triggering impactBurst weapon={state.WeaponPrefabName} target={DescribeCharacter(target)} radius={state.Config.Radius:0.##} origin={FormatVector(targetPoint)} hitPoint={FormatVector(hitPoint)} collider={DescribeCollider(collider)} hitObject={DescribeGameObject(directHitObject)} destructible={DescribeDestructible(directDestructible)}.");
            TriggerImpactBurst(state, targetPoint, ResolveImpactBurstVfxPoint(hitPoint, targetPoint), target, directHitObject, normal);
            return;
        }

        if (!TryConsumeSpearRainCooldown(state))
        {
            return;
        }

        targetPoint = ResolveSpearRainTargetPoint(state, target, targetPoint);
        TryGrantOnProjectileHitAdrenaline(state, target);
        LogDebug($"triggering spearRain weapon={state.WeaponPrefabName} target={DescribeCharacter(target)} count={state.Config.Count} point={FormatVector(targetPoint)} hitPoint={FormatVector(hitPoint)} collider={DescribeCollider(collider)}.");
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

        LogDebug($"hit ignored for {state.WeaponPrefabName}: projectile touched owner={target.name} collider={collider.name}.");
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
        Stopwatch? totalPerf = SecondaryAttackPerformanceLog.Start();
        int hitCount = 0;
        int targetCount = 0;
        int appliedCount = 0;
        string result = "completed";
        try
        {
            if (config.Radius <= 0f || config.DamageFactor <= 0f && config.PushFactor <= 0f)
            {
                result = "skipped";
                LogDebug(
                    $"impactBurst skipped weapon={state.WeaponPrefabName}: radius={config.Radius:0.###} damageFactor={config.DamageFactor:0.###} pushFactor={config.PushFactor:0.###}.");
                return;
            }

            Stopwatch? vfxPerf = SecondaryAttackPerformanceLog.Start();
            try
            {
                PlayImpactBurstVfx(config, state, impactPoint, vfxPoint, normal);
            }
            finally
            {
                SecondaryAttackPerformanceLog.Stop(
                    vfxPerf,
                    "impactBurst.vfx",
                    $"weapon={state.WeaponPrefabName} vfx={config.Vfx} radius={config.Radius:0.###}");
            }

            ImpactBurstTargets.Clear();
            ImpactBurstTargetIds.Clear();
            float radiusSqr = config.Radius * config.Radius;
            Stopwatch? scanPerf = SecondaryAttackPerformanceLog.Start();
            try
            {
                hitCount = Physics.OverlapSphereNonAlloc(
                    impactPoint,
                    config.Radius,
                    ImpactBurstColliders,
                    config.IncludeDestructibles ? GetImpactBurstMask() : GetCharacterMask(),
                    QueryTriggerInteraction.Ignore);
            }
            finally
            {
                SecondaryAttackPerformanceLog.Stop(
                    scanPerf,
                    "impactBurst.scan",
                    $"weapon={state.WeaponPrefabName} radius={config.Radius:0.###} includeDestructibles={config.IncludeDestructibles} colliders={hitCount}/{MaxImpactBurstColliders}");
            }

            LogDebug(
                $"impactBurst scan weapon={state.WeaponPrefabName} origin={FormatVector(impactPoint)} radius={config.Radius:0.##} includeDestructibles={config.IncludeDestructibles} includeDirectTarget={config.IncludeDirectTarget} directTarget={DescribeCharacter(directTarget)} directHitObject={DescribeGameObject(directHitObject)} colliders={hitCount}/{MaxImpactBurstColliders}.");
            if (hitCount >= MaxImpactBurstColliders)
            {
                LogDebug($"impactBurst scan weapon={state.WeaponPrefabName}: collider buffer is full; nearby targets may be missing.");
            }

            Stopwatch? collectPerf = SecondaryAttackPerformanceLog.Start();
            try
            {
                for (int i = 0; i < hitCount; i++)
                {
                    Collider collider = ImpactBurstColliders[i];
                    ImpactBurstColliders[i] = null!;
                    if (collider == null)
                    {
                        continue;
                    }

                    GameObject hitObject = Projectile.FindHitObject(collider);
                    if (hitObject == null)
                    {
                        LogDebug($"impactBurst candidate[{i}] skipped: no hit object collider={DescribeCollider(collider)}.");
                        continue;
                    }

                    if (hitObject == state.Owner.gameObject)
                    {
                        LogDebug($"impactBurst candidate[{i}] skipped: owner object collider={DescribeCollider(collider)} hitObject={DescribeGameObject(hitObject)}.");
                        continue;
                    }

                    Character? character = ProjectileRuntimeSystem.GetHitCharacter(collider);
                    IDestructible? destructible = character != null ? character : hitObject.GetComponent<IDestructible>();
                    if (destructible == null)
                    {
                        LogDebug(
                            $"impactBurst candidate[{i}] skipped: no destructible collider={DescribeCollider(collider)} hitObject={DescribeGameObject(hitObject)} character={DescribeCharacter(character)}.");
                        continue;
                    }

                    if (TryResolveImpactBurstSkipReason(
                            state,
                            config,
                            directTarget,
                            directHitObject,
                            hitObject,
                            character,
                            destructible,
                            out string skipReason))
                    {
                        LogDebug(
                            $"impactBurst candidate[{i}] skipped: {skipReason} collider={DescribeCollider(collider)} hitObject={DescribeGameObject(hitObject)} character={DescribeCharacter(character)} destructible={DescribeDestructible(destructible)}.");
                        continue;
                    }

                    if (!TryAddImpactBurstTarget(destructible, character, collider, hitObject))
                    {
                        LogDebug(
                            $"impactBurst candidate[{i}] skipped: duplicate collider={DescribeCollider(collider)} hitObject={DescribeGameObject(hitObject)} character={DescribeCharacter(character)} destructible={DescribeDestructible(destructible)}.");
                        continue;
                    }

                    Vector3 point = ResolveImpactPoint(collider, impactPoint, destructible);
                    float distanceSqr = ResolveImpactBurstDistanceSqr(impactPoint, point, character, destructible, radiusSqr);
                    ImpactBurstTargets.Add(new ImpactBurstTarget(destructible, character, collider, point, distanceSqr));
                    LogDebug(
                        $"impactBurst candidate[{i}] accepted: collider={DescribeCollider(collider)} hitObject={DescribeGameObject(hitObject)} character={DescribeCharacter(character)} destructible={DescribeDestructible(destructible)} point={FormatVector(point)} distance={Mathf.Sqrt(distanceSqr):0.###}.");
                }

                ImpactBurstTargets.Sort((left, right) => left.DistanceSqr.CompareTo(right.DistanceSqr));
                targetCount = ImpactBurstTargets.Count;
            }
            finally
            {
                SecondaryAttackPerformanceLog.Stop(
                    collectPerf,
                    "impactBurst.collect",
                    $"weapon={state.WeaponPrefabName} colliders={hitCount} targets={ImpactBurstTargets.Count}");
            }

            Stopwatch? damagePerf = SecondaryAttackPerformanceLog.Start();
            try
            {
                foreach (ImpactBurstTarget target in ImpactBurstTargets)
                {
                    if (!ApplyImpactBurstHit(state, target, impactPoint, normal))
                    {
                        continue;
                    }

                    appliedCount++;
                }
            }
            finally
            {
                SecondaryAttackPerformanceLog.Stop(
                    damagePerf,
                    "impactBurst.damage",
                    $"weapon={state.WeaponPrefabName} targets={targetCount} applied={appliedCount}");
            }

            LogDebug($"impactBurst applied weapon={state.WeaponPrefabName} hits={appliedCount} radius={config.Radius:0.##}.");
            ImpactBurstTargets.Clear();
            ImpactBurstTargetIds.Clear();
        }
        finally
        {
            SecondaryAttackPerformanceLog.Stop(
                totalPerf,
                "impactBurst.total",
                $"weapon={state.WeaponPrefabName} result={result} radius={config.Radius:0.###} includeDestructibles={config.IncludeDestructibles} colliders={hitCount} targets={targetCount} applied={appliedCount}");
        }
    }

    private static void PlayImpactBurstVfx(
        MeleeOnProjectileHitDefinition config,
        OnProjectileHitSourceState state,
        Vector3 impactPoint,
        Vector3 vfxPoint,
        Vector3 normal)
    {
        string vfx = config.Vfx?.Trim() ?? "";
        if (vfx.Length == 0)
        {
            LogDebug($"impactBurst vfx skipped weapon={state.WeaponPrefabName}: empty vfx field origin={FormatVector(impactPoint)} vfxPoint={FormatVector(vfxPoint)} normal={FormatVector(normal)}.");
            return;
        }

        Quaternion rotation = SecondaryAttackNamedEffectSystem.RotationFromNormal(normal);
        bool created = SecondaryAttackNamedEffectSystem.Create(vfx, vfxPoint, rotation, "impactBurst.vfx");
        LogDebug(
            $"impactBurst vfx {(created ? "created" : "failed")} weapon={state.WeaponPrefabName} prefab={vfx} origin={FormatVector(impactPoint)} vfxPoint={FormatVector(vfxPoint)} normal={FormatVector(normal)} rotation={rotation.eulerAngles.ToString("F2")}.");
    }

    private static Vector3 ResolveImpactBurstVfxPoint(Vector3 hitPoint, Vector3 fallbackPoint)
    {
        return hitPoint.sqrMagnitude > 0.001f ? hitPoint : fallbackPoint;
    }

    private static bool TryResolveImpactBurstSkipReason(
        OnProjectileHitSourceState state,
        MeleeOnProjectileHitDefinition config,
        Character? directTarget,
        GameObject? directHitObject,
        GameObject hitObject,
        Character? character,
        IDestructible destructible,
        out string reason)
    {
        reason = string.Empty;
        if (!config.IncludeDirectTarget &&
            ((directTarget != null &&
              (character == directTarget ||
               character != null && character.gameObject == directTarget.gameObject)) ||
             (directHitObject != null && hitObject == directHitObject)))
        {
            reason = "direct target excluded";
            return true;
        }

        if (character != null)
        {
            if (IsValidTarget(state.Owner, character))
            {
                return false;
            }

            reason = $"invalid character dead={character.IsDead()} enemy={BaseAI.IsEnemy(state.Owner, character)}";
            return true;
        }

        if (!config.IncludeDestructibles)
        {
            reason = "destructibles disabled";
            return true;
        }

        if (state.Weapon?.m_shared?.m_tamedOnly == true)
        {
            reason = "weapon is tamedOnly";
            return true;
        }

        DestructibleType type = destructible.GetDestructibleType();
        if (type == DestructibleType.None)
        {
            reason = "destructible type is None";
            return true;
        }

        if (type == DestructibleType.Character)
        {
            reason = "destructible type is Character without Character target";
            return true;
        }

        return false;
    }

    private static bool ApplyImpactBurstHit(
        OnProjectileHitSourceState state,
        ImpactBurstTarget target,
        Vector3 impactPoint,
        Vector3 normal)
    {
        HitData hitData = state.BaseHitData.Clone();
        SecondaryAttackProjectileToolTierSystem.ApplyToHitData(
            hitData,
            null,
            state.Weapon,
            "MeleeProjectileHitCascadeSystem.ImpactBurst");
        float damageScale = state.Config.DamageFactor;
        if (!Mathf.Approximately(damageScale, 1f))
        {
            hitData.m_damage.Modify(damageScale);
        }

        float totalDamage = hitData.m_damage.GetTotalDamage();
        if (totalDamage > 0f && totalDamage < MinPositiveImpactBurstDamage)
        {
            hitData.m_damage.Modify(MinPositiveImpactBurstDamage / totalDamage);
            LogDebug(
                $"impactBurst damage raised to minimum target={DescribeImpactBurstTarget(target)} before={totalDamage:0.###} after={hitData.m_damage.GetTotalDamage():0.###}.");
        }

        if (hitData.m_damage.GetTotalDamage() <= 0f && state.Config.PushFactor <= 0f)
        {
            LogDebug(
                $"impactBurst hit skipped: no damage or push target={DescribeImpactBurstTarget(target)} damage={hitData.m_damage.GetTotalDamage():0.###} pushFactor={state.Config.PushFactor:0.###}.");
            return false;
        }

        if (target.Character != null && hitData.m_dodgeable && target.Character.IsDodgeInvincible())
        {
            if (target.Character is Player dodgingPlayer)
            {
                dodgingPlayer.HitWhileDodging();
            }

            LogDebug($"impactBurst hit skipped: target dodging target={DescribeImpactBurstTarget(target)}.");
            return false;
        }

        hitData.m_pushForce *= state.Config.PushFactor;
        hitData.m_skillRaiseAmount = 0f;
        hitData.m_point = target.Point;
        hitData.m_dir = ResolveImpactDirection(impactPoint, target.Character != null ? target.Character.GetCenterPoint() : target.Point, normal, state.Owner);
        hitData.m_hitCollider = target.Collider;
        hitData.SetAttacker(state.Owner);

        IsApplyingImpactBurstDamage = true;
        try
        {
            LogDebug(
                $"impactBurst damaging target={DescribeImpactBurstTarget(target)} damage={hitData.m_damage.GetTotalDamage():0.###} push={hitData.m_pushForce:0.###} toolTier={hitData.m_toolTier} itemWorldLevel={hitData.m_itemWorldLevel} point={FormatVector(hitData.m_point)} dir={FormatVector(hitData.m_dir)}.");
            target.Destructible.Damage(hitData);
            if (target.Character != null && BaseAI.IsEnemy(state.Owner, target.Character))
            {
                TryGrantOnProjectileHitAdrenaline(state, target.Character);
            }
        }
        finally
        {
            IsApplyingImpactBurstDamage = false;
        }

        return true;
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
            LogDebug(
                $"spearRain cooldown consumed on hit weapon={state.WeaponPrefabName} state=[{MeleePresetCooldownSystem.DescribeState(state.Owner, state.Weapon, SpearRainPresetName, state.Config.PresetCooldown)}].");
            return true;
        }

        LogDebug(
            $"spearRain skipped weapon={state.WeaponPrefabName}: cooldown active on projectile hit state=[{MeleePresetCooldownSystem.DescribeState(state.Owner, state.Weapon, SpearRainPresetName, state.Config.PresetCooldown)}].");
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
        LogDebug(
            $"spearRain lead weapon={state.WeaponPrefabName} target={target.name} center={FormatVector(targetPoint)} velocity={FormatVector(horizontalVelocity)} lead={FormatVector(lead)} predicted={FormatVector(predictedPoint)} flightTime={state.Config.FlightTime:0.###}.");
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
        Stopwatch? perf = SecondaryAttackPerformanceLog.Start();
        int attemptedCount = 0;
        string result = "completed";
        try
        {
            Projectile? prefabProjectile = state.ProjectilePrefab.GetComponent<Projectile>();
            if (prefabProjectile == null)
            {
                result = "missingProjectileComponent";
                LogDebug($"spawn skipped for {state.WeaponPrefabName}: projectile prefab has no Projectile component prefab={state.ProjectilePrefab.name}.");
                return;
            }

            float gravity = prefabProjectile.m_gravity;
            SpearRainTargetMarker? targetMarker = CreateSpearRainTargetMarker(markedTarget, state.Config.FlightTime);
            CopiedThrowProjectileVisualSystem.SpawnedProjectileVisualContext visualContext =
                state.Definition.Behavior is CopiedSecondaryBehavior
                    ? CopiedThrowProjectileVisualSystem.CreateSpawnedProjectileVisualContext(state.Weapon, state.ProjectilePrefab)
                    : default;
            LogDebug(
                $"spearRain spawn begin weapon={state.WeaponPrefabName} count={state.Config.Count} target={DescribeCharacter(markedTarget)} targetPoint={FormatVector(targetPoint)} marker={DescribeSpearRainMarker(targetMarker)} gravity={gravity:0.###} flightTime={state.Config.FlightTime:0.###}.");
            for (int projectileIndex = 0; projectileIndex < state.Config.Count; projectileIndex++)
            {
                Vector2 offset = UnityEngine.Random.insideUnitCircle * state.Config.SpawnRadius;
                Vector3 spawnPoint = targetPoint + new Vector3(offset.x, state.Config.SpawnHeight, offset.y);
                Vector3 launchVelocity = CalculateBallisticVelocity(spawnPoint, targetPoint, gravity, state.Config.FlightTime);
                if (launchVelocity.sqrMagnitude < 0.001f)
                {
                    launchVelocity = Vector3.down;
                }

                attemptedCount++;
                SpawnFollowupProjectile(
                    state,
                    projectileIndex,
                    spawnPoint,
                    launchVelocity,
                    targetMarker,
                    targetPoint,
                    gravity,
                    visualContext);
            }

            LogDebug($"spawned spearRain projectiles weapon={state.WeaponPrefabName} count={state.Config.Count}.");
        }
        finally
        {
            SecondaryAttackPerformanceLog.Stop(
                perf,
                "spearRain.spawn",
                $"weapon={state.WeaponPrefabName} result={result} count={state.Config.Count} attempted={attemptedCount} spawnHeight={state.Config.SpawnHeight:0.###} spawnRadius={state.Config.SpawnRadius:0.###} flightTime={state.Config.FlightTime:0.###}");
        }
    }

    private static void SpawnFollowupProjectile(
        OnProjectileHitSourceState state,
        int projectileIndex,
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
            LogDebug($"follow-up spawn skipped for {state.WeaponPrefabName}: spawned object missing Projectile/IProjectile prefab={state.ProjectilePrefab.name}.");
            ProjectileRuntimeSystem.DestroyProjectileObject(projectileObject);
            return;
        }

        RegisterSpearRainFollowupProjectile(
            projectile,
            state,
            projectileIndex,
            spawnPoint,
            launchVelocity,
            targetMarker,
            fallbackTargetPoint);
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
        LogDebug(
            $"spearRain follow-up spawned {DescribeSpearRainFollowupProjectile(projectile)} projectile={projectile.name} object={DescribeGameObject(projectile.gameObject)} marker={DescribeSpearRainMarker(targetMarker)} damageFactor={state.Config.DamageFactor:0.###}.");
    }

    [Conditional("SECONDARY_ATTACKS_DEBUG_LOGGING")]
    internal static void LogDebug(string message)
    {
    }

    private static string DescribeImpactBurstTarget(ImpactBurstTarget target)
    {
        return
            $"character={DescribeCharacter(target.Character)} destructible={DescribeDestructible(target.Destructible)} collider={DescribeCollider(target.Collider)} distance={target.Distance:0.###} point={FormatVector(target.Point)}";
    }

    private static string DescribeCharacter(Character? character)
    {
        if (character == null)
        {
            return "<none>";
        }

        return $"{character.name}(dead={character.IsDead()}, object={DescribeGameObject(character.gameObject)})";
    }

    private static string DescribeDestructible(IDestructible? destructible)
    {
        if (destructible == null)
        {
            return "<null>";
        }

        string objectName = destructible is Component component
            ? DescribeGameObject(component.gameObject)
            : "<non-component>";
        return $"{destructible.GetType().Name}(type={destructible.GetDestructibleType()}, object={objectName})";
    }

    private static string DescribeCollider(Collider? collider)
    {
        if (collider == null)
        {
            return "<null>";
        }

        return $"{collider.name}(layer={DescribeLayer(collider.gameObject.layer)}, object={DescribeGameObject(collider.gameObject)})";
    }

    private static string DescribeGameObject(GameObject? gameObject)
    {
        if (gameObject == null)
        {
            return "<null>";
        }

        return $"{gameObject.name}(layer={DescribeLayer(gameObject.layer)})";
    }

    private static string DescribeLayer(int layer)
    {
        string layerName = LayerMask.LayerToName(layer);
        return string.IsNullOrEmpty(layerName)
            ? layer.ToString()
            : $"{layer}:{layerName}";
    }

    internal static string FormatVector(Vector3 value)
    {
        return value.ToString("F2");
    }

    internal static bool TryDescribeSpearRainFollowupProjectile(Projectile? projectile, out string description)
    {
        if (projectile != null &&
            SpearRainFollowupProjectiles.TryGetValue(projectile, out SpearRainFollowupProjectileState? state))
        {
            description = state.Describe(projectile);
            return true;
        }

        description = "";
        return false;
    }

    internal static void DestroySpearRainFollowupAfterHit(
        Projectile? projectile,
        Collider? collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal)
    {
        if (projectile == null ||
            !SpearRainFollowupProjectiles.TryGetValue(projectile, out SpearRainFollowupProjectileState? state))
        {
            return;
        }

        string description = state.Describe(projectile);
        SpearRainFollowupProjectiles.Remove(projectile);
        LogDebug(
            $"spearRain follow-up destroyed after hit {description} collider={DescribeCollider(collider)} hitPoint={FormatVector(hitPoint)} water={water} normal={FormatVector(normal)}.");
        Stopwatch? perf = SecondaryAttackPerformanceLog.Start();
        ProjectileRuntimeSystem.DestroyProjectileObject(projectile.gameObject);
        SecondaryAttackPerformanceLog.Stop(
            perf,
            "spearRain.followup.destroy",
            () => $"projectile={projectile.name} collider={collider?.name ?? "<null>"} water={water}");
    }

    private static string DescribeSpearRainFollowupProjectile(Projectile projectile) =>
        TryDescribeSpearRainFollowupProjectile(projectile, out string description)
            ? description
            : "<unmarked follow-up>";

    private static void RegisterSpearRainFollowupProjectile(
        Projectile projectile,
        OnProjectileHitSourceState state,
        int projectileIndex,
        Vector3 spawnPoint,
        Vector3 launchVelocity,
        SpearRainTargetMarker? targetMarker,
        Vector3 fallbackTargetPoint)
    {
        SpearRainFollowupProjectiles.Remove(projectile);
        SpearRainFollowupProjectileState followupState = new(
            state.WeaponPrefabName,
            projectileIndex,
            state.Config.Count,
            targetMarker != null ? targetMarker.DebugTargetName : "<none>",
            targetMarker != null ? targetMarker.DebugId : 0,
            Time.time,
            spawnPoint,
            launchVelocity,
            fallbackTargetPoint);
        SpearRainFollowupProjectiles.Add(projectile, followupState);
        LogDebug($"spearRain follow-up marked {followupState.Describe(projectile)} projectile={projectile.name}.");
    }

    private static string DescribeSpearRainMarker(SpearRainTargetMarker? marker) =>
        marker != null ? marker.DebugDescription : "<none>";

    private static HitData BuildFollowupHitData(OnProjectileHitSourceState state)
    {
        HitData hitData = state.BaseHitData.Clone();
        SecondaryAttackProjectileToolTierSystem.ApplyToHitData(
            hitData,
            null,
            state.Weapon,
            "MeleeProjectileHitCascadeSystem.FollowupProjectile");
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

    private static void RegisterPendingSpearRain(Projectile projectile, Character owner, ItemDrop.ItemData weapon)
    {
        if (projectile == null || owner == null)
        {
            return;
        }

        SpearRainPendingProjectileMarker marker =
            projectile.GetComponent<SpearRainPendingProjectileMarker>() ??
            projectile.gameObject.AddComponent<SpearRainPendingProjectileMarker>();
        marker.Initialize(owner);
        LogDebug($"spearRain pending registered projectile={projectile.name} owner={owner.name} weapon={weapon?.m_dropPrefab?.name ?? "<unknown>"}.");
    }

    private static void ReleasePendingSpearRain(Projectile projectile, string reason)
    {
        if (projectile == null)
        {
            return;
        }

        projectile.GetComponent<SpearRainPendingProjectileMarker>()?.Release(reason);
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

    private static Vector3 CalculateBallisticVelocity(Vector3 spawnPoint, Vector3 targetPoint, float gravity, float flightTime)
    {
        flightTime = Mathf.Max(0.1f, flightTime);
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
        public SpearRainFollowupProjectileState(
            string weaponPrefabName,
            int projectileIndex,
            int projectileCount,
            string targetName,
            int markerId,
            float createdAt,
            Vector3 spawnPoint,
            Vector3 initialVelocity,
            Vector3 fallbackTargetPoint)
        {
            WeaponPrefabName = weaponPrefabName;
            ProjectileIndex = projectileIndex;
            ProjectileCount = projectileCount;
            TargetName = targetName;
            MarkerId = markerId;
            CreatedAt = createdAt;
            SpawnPoint = spawnPoint;
            InitialVelocity = initialVelocity;
            FallbackTargetPoint = fallbackTargetPoint;
        }

        private string WeaponPrefabName { get; }

        private int ProjectileIndex { get; }

        private int ProjectileCount { get; }

        private string TargetName { get; }

        private int MarkerId { get; }

        private float CreatedAt { get; }

        private Vector3 SpawnPoint { get; }

        private Vector3 InitialVelocity { get; }

        private Vector3 FallbackTargetPoint { get; }

        public string Describe(Projectile projectile) =>
            $"weapon={WeaponPrefabName} index={ProjectileIndex + 1}/{ProjectileCount} target={TargetName} marker={MarkerId} age={Time.time - CreatedAt:0.###}s spawn={FormatVector(SpawnPoint)} velocity={FormatVector(InitialVelocity)} fallback={FormatVector(FallbackTargetPoint)} current={FormatVector(projectile.transform.position)}";
    }
}

internal sealed class SpearRainPendingProjectileMarker : MonoBehaviour
{
    private Character? _owner;
    private bool _active;

    internal void Initialize(Character owner)
    {
        Release("reinitialized");
        _owner = owner;
        _active = true;
        MeleeProjectileHitCascadeSystem.AddPendingSpearRain(owner);
    }

    internal void Release(string reason)
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
            MeleeProjectileHitCascadeSystem.RemovePendingSpearRain(owner, reason);
        }
    }

    private void OnDestroy()
    {
        Release("destroyed");
    }
}

internal sealed class SpearRainTargetMarker : MonoBehaviour
{
    private Character? _target;
    private Vector3 _lastKnownPoint;
    private float _expiresAt;
    private bool _loggedDeathFreeze;

    internal bool IsValid => Time.time <= _expiresAt;

    internal Vector3 CurrentPoint => _lastKnownPoint;

    internal int DebugId => GetInstanceID();

    internal string DebugTargetName => _target != null ? _target.name : "<none>";

    internal string DebugDescription =>
        $"id={DebugId} target={DebugTargetName} dead={(_target?.IsDead() ?? false)} point={MeleeProjectileHitCascadeSystem.FormatVector(_lastKnownPoint)} expiresIn={_expiresAt - Time.time:0.###}";

    internal void Configure(Character target, float lifetime)
    {
        _target = target;
        _lastKnownPoint = target.GetCenterPoint();
        _expiresAt = Time.time + Mathf.Max(0.1f, lifetime);
        _loggedDeathFreeze = false;
        MeleeProjectileHitCascadeSystem.LogDebug($"spearRain marker configured {DebugDescription} lifetime={lifetime:0.###}.");
    }

    private void Update()
    {
        if (_target != null && !_target.IsDead())
        {
            _lastKnownPoint = _target.GetCenterPoint();
        }
        else if (_target != null && !_loggedDeathFreeze)
        {
            _loggedDeathFreeze = true;
            MeleeProjectileHitCascadeSystem.LogDebug($"spearRain marker froze on dead target {DebugDescription}.");
        }

        if (!IsValid)
        {
            MeleeProjectileHitCascadeSystem.LogDebug($"spearRain marker expired {DebugDescription}.");
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
        MeleeProjectileHitCascadeSystem.LogDebug(
            $"spearRain guidance configured projectile={projectile.name} marker={targetMarker.DebugDescription} fallback={MeleeProjectileHitCascadeSystem.FormatVector(fallbackTargetPoint)} flightTime={_flightTime:0.###} gravity={_gravity:0.###} initialVelocity={MeleeProjectileHitCascadeSystem.FormatVector(initialVelocity)} maxSpeed={_maxSpeed:0.###} ttl={projectile.m_ttl:0.###}.");
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
            MeleeProjectileHitCascadeSystem.LogDebug($"spearRain guidance ended projectile={_projectile.name} elapsed={_elapsed:0.###} flightTime={_flightTime:0.###}.");
            return;
        }

        Vector3 targetPoint = _targetMarker != null && _targetMarker.IsValid
            ? _targetMarker.CurrentPoint
            : _fallbackTargetPoint;
        float remaining = Mathf.Max(MinRemainingFlightTime, _flightTime - _elapsed);
        Vector3 desiredVelocity = CalculateBallisticVelocity(transform.position, targetPoint, _gravity, remaining);
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

    private static Vector3 CalculateBallisticVelocity(Vector3 spawnPoint, Vector3 targetPoint, float gravity, float flightTime)
    {
        flightTime = Mathf.Max(MinRemainingFlightTime, flightTime);
        Vector3 gravityVector = Vector3.down * gravity;
        return (targetPoint - spawnPoint - gravityVector * (0.5f * flightTime * flightTime)) / flightTime;
    }
}
