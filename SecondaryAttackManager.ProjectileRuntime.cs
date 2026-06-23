using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static partial class ProjectileRuntimeSystem
{
    private static int AimRayMask;
    private static int ShieldChargeCollisionMask;
    private static int ShieldChargeImpactMask;
    private const float MeteorFallbackRange = 32f;
    private const float PiercingShotHitCooldown = 0.25f;
    private const float SentinelTargetScanInterval = 0.15f;
    private const float SentinelTargetScanJitter = 0.03f;
    private static readonly HashSet<Attack> DeferredBurstFireReloadResets = new();
    private static readonly HashSet<Attack> ActiveBurstFireControllers = new();
    private static readonly ConditionalWeakTable<Projectile, PiercingShotState> PiercingShotProjectiles = new();
    private static readonly ConditionalWeakTable<Projectile, ScatterRicochetState> ScatterRicochetProjectiles = new();
    private static readonly ConditionalWeakTable<Projectile, ScatterRicochetSplitState> ScatterRicochetSplitProjectiles = new();

    internal static bool TryGetProjectilePayload(Attack attack, SecondaryAttackDefinition definition, ProjectileLaunchData launchData, out Projectile projectilePrefab)
    {
        projectilePrefab = null!;
        ProjectileSecondaryBehavior? projectileBehavior = definition.Behavior as ProjectileSecondaryBehavior;
        if (projectileBehavior == null)
        {
            return false;
        }

        if (!launchData.IsValid)
        {
            return false;
        }

        if (!TryValidatePayloadPrefab(definition.PrefabName, launchData.ProjectilePrefab!, projectileBehavior.Preset, out string reason))
        {
            ReportCompatibilityIssue(attack, definition, reason);
            return false;
        }

        projectilePrefab = launchData.ProjectilePrefab!.GetComponent<Projectile>()!;
        return true;
    }

    private static void ReportCompatibilityIssue(Attack attack, SecondaryAttackDefinition definition, string reason)
    {
        string weaponPrefabName = attack.m_weapon?.m_dropPrefab != null ? attack.m_weapon.m_dropPrefab.name : definition.PrefabName;
        string presetName = definition.Behavior is ProjectileSecondaryBehavior projectileBehavior ? GetPresetName(projectileBehavior.Preset) : "unknown";
        string key = weaponPrefabName + "|" + presetName + "|" + reason;
        if (SecondaryAttackManager.TryMarkCompatibilityIssueReported(key))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping custom secondary for {weaponPrefabName}: {reason}");
        }
    }

    internal static void FireBarrage(Attack attack, SecondaryAttackDefinition definition)
    {
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!TryGetProjectilePayload(attack, definition, launchData, out Projectile _))
        {
            return;
        }

        ProjectileSecondaryBehavior projectileBehavior = (ProjectileSecondaryBehavior)definition.Behavior;

        PrepareCustomProjectileBurst(attack);
        attack.GetProjectileSpawnPoint(out Vector3 originPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        if (attack.m_burstEffect.HasEffects())
        {
            attack.m_burstEffect.Create(originPoint, Quaternion.LookRotation(aimDirection));
        }

        ResolveHorizontalAxes(attack, aimDirection, out Vector3 horizontalForward, out Vector3 horizontalRight);
        if (projectileBehavior.Interval > 0f)
        {
            CreateScheduledBurstController(
                attack,
                launchData,
                definition,
                ScheduledBurstMode.Barrage,
                originPoint,
                aimDirection,
                originPoint,
                horizontalForward,
                horizontalRight);
            return;
        }

        for (int projectileIndex = 0; projectileIndex < projectileBehavior.ProjectileCount; projectileIndex++)
        {
            SpawnBarrageShot(attack, launchData, projectileBehavior, originPoint, aimDirection, horizontalRight, projectileIndex);
        }
    }

    private static void SpawnBarrageShot(
        Attack attack,
        ProjectileLaunchData launchData,
        ProjectileSecondaryBehavior projectileBehavior,
        Vector3 originPoint,
        Vector3 aimDirection,
        Vector3 horizontalRight,
        int projectileIndex)
    {
        float centeredIndex = projectileIndex - (projectileBehavior.ProjectileCount - 1) * 0.5f;
        Vector3 spawnPoint = originPoint + horizontalRight * centeredIndex * projectileBehavior.BarrageSpacing;
        float startAngle = projectileBehavior.ProjectileCount > 1 ? -projectileBehavior.SpreadAngle * 0.5f : 0f;
        float angleStep = projectileBehavior.ProjectileCount > 1 ? projectileBehavior.SpreadAngle / (projectileBehavior.ProjectileCount - 1) : 0f;
        float angleOffset = startAngle + angleStep * projectileIndex;
        Vector3 rotationAxis = attack.m_character != null ? attack.m_character.transform.up : Vector3.up;
        Vector3 direction = Quaternion.AngleAxis(angleOffset, rotationAxis) * aimDirection;
        SpawnProjectile(attack, launchData, spawnPoint, direction, ResolveProjectileSpeed(launchData));
    }

    internal static bool FireVolley(Attack attack, SecondaryAttackDefinition definition)
    {
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!TryGetProjectilePayload(attack, definition, launchData, out Projectile _))
        {
            return false;
        }

        ProjectileSecondaryBehavior projectileBehavior = (ProjectileSecondaryBehavior)definition.Behavior;

        attack.GetProjectileSpawnPoint(out Vector3 originPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        if (!TryResolveVolleyTargetPoint(attack, definition, launchData, originPoint, aimDirection, out Vector3 targetPoint))
        {
            SecondaryAttackManager.LogRangedDebug($"volley skipped definition={definition.PrefabName}: no aimed target.");
            return false;
        }

        PrepareCustomProjectileBurst(attack);
        if (attack.m_burstEffect.HasEffects())
        {
            attack.m_burstEffect.Create(originPoint, Quaternion.LookRotation(aimDirection));
        }

        ResolveHorizontalAxes(attack, aimDirection, out Vector3 horizontalForward, out Vector3 horizontalRight);
        float gravity = GetProjectileGravity(launchData.ProjectilePrefab!);
        SecondaryAttackManager.LogRangedDebug(
            $"volley start definition={definition.PrefabName}"
            + $" count={projectileBehavior.ProjectileCount}"
            + $" projectile={launchData.ProjectilePrefab?.name ?? "<null>"}"
            + $" ammo=[{SecondaryAttackManager.DescribeItemForRangedDebug(launchData.AmmoItem)}]"
            + $" origin={FormatVector3(originPoint)}"
            + $" target={FormatVector3(targetPoint)}"
            + $" radius={projectileBehavior.VolleyRadius:0.###}"
            + $" arcAngle={projectileBehavior.VolleyArcAngleMin:0.###}-{projectileBehavior.VolleyArcAngleMax:0.###}"
            + $" maxRange={projectileBehavior.VolleyMaxRange:0.###}"
            + $" interval={projectileBehavior.Interval:0.###}"
            + $" attack={SecondaryAttackManager.DescribeAttackForRangedDebug(attack)}");

        if (projectileBehavior.Interval > 0f)
        {
            CreateScheduledBurstController(
                attack,
                launchData,
                definition,
                ScheduledBurstMode.Volley,
                originPoint,
                aimDirection,
                targetPoint,
                horizontalForward,
                horizontalRight);
            return true;
        }

        float volleyAngleOffset = UnityEngine.Random.value * 360f;
        for (int projectileIndex = 0; projectileIndex < projectileBehavior.ProjectileCount; projectileIndex++)
        {
            CreateVolleyShot(projectileBehavior, launchData, originPoint, targetPoint, horizontalForward, horizontalRight, gravity, projectileIndex, projectileBehavior.ProjectileCount, volleyAngleOffset, out Vector3 spawnPoint, out Vector3 impactPoint, out Vector3 launchVelocity, out float projectileGravity, out float flightTime);
            SecondaryAttackManager.LogRangedDebug(
                $"volley shot index={projectileIndex}"
                + $" spawn={FormatVector3(spawnPoint)}"
                + $" impact={FormatVector3(impactPoint)}"
                + $" flightTime={flightTime:0.###}"
                + $" gravity={projectileGravity:0.###}"
                + $" velocity={FormatVector3(launchVelocity)}");
            SpawnVolleyProjectile(attack, launchData, spawnPoint, launchVelocity, projectileGravity, flightTime);
        }

        return true;
    }

    internal static void FirePiercingShot(Attack attack, SecondaryAttackDefinition definition)
    {
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!TryGetProjectilePayload(attack, definition, launchData, out Projectile _))
        {
            return;
        }

        ProjectileSecondaryBehavior projectileBehavior = (ProjectileSecondaryBehavior)definition.Behavior;
        float speedFactor = Mathf.Max(0.01f, projectileBehavior.ProjectileSpeedFactor);

        PrepareCustomProjectileBurst(attack);
        attack.GetProjectileSpawnPoint(out Vector3 spawnPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        if (attack.m_burstEffect.HasEffects())
        {
            attack.m_burstEffect.Create(spawnPoint, Quaternion.LookRotation(aimDirection));
        }

        int projectileCount = Mathf.Max(1, attack.m_projectiles);
        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            if (attack.m_destroyPreviousProjectile && attack.m_weapon.m_lastProjectile != null)
            {
                DestroyProjectileObject(attack.m_weapon.m_lastProjectile);
                attack.m_weapon.m_lastProjectile = null;
            }

            Vector3 direction = ResolvePrimaryProjectileDirection(
                attack,
                aimDirection,
                launchData.ProjectileAccuracy,
                projectileIndex,
                projectileCount);
            float speed = ResolveProjectileSpeed(launchData) * speedFactor;
            GameObject projectileObject = SpawnProjectileObject(attack, launchData, spawnPoint, direction, speed, setLastProjectile: true, out Projectile? projectile);
            if (projectileObject == null || projectile == null)
            {
                continue;
            }

            ApplyRangePreservingProjectileSpeedModifiers(projectile, speedFactor);
            OverchargedBombSystem.RegisterProjectile(projectile, projectileBehavior.ProjectileScaleFactor, 1f);
            RegisterPiercingShotProjectile(projectile, projectileBehavior);
        }
    }

    private static void RegisterPiercingShotProjectile(
        Projectile projectile,
        ProjectileSecondaryBehavior projectileBehavior)
    {
        if (projectile == null)
        {
            return;
        }

        PiercingShotProjectiles.Remove(projectile);
        PiercingShotProjectiles.Add(projectile, new PiercingShotState(projectileBehavior));
    }

    internal static bool TryHandlePiercingShotProjectileHit(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal)
    {
        if (projectile == null || collider == null || !PiercingShotProjectiles.TryGetValue(projectile, out PiercingShotState? state))
        {
            return false;
        }

        if (water)
        {
            PiercingShotProjectiles.Remove(projectile);
            return false;
        }

        Character? character = GetHitCharacter(collider);
        if (character == null)
        {
            PiercingShotProjectiles.Remove(projectile);
            return false;
        }

        Character? owner = ProjectileAccess.GetOwner(projectile);
        if (!IsValidPiercingShotTarget(owner, character))
        {
            return true;
        }

        if (state.IsOnHitCooldown(character))
        {
            return true;
        }

        if (ApplyPiercingShotHit(projectile, state, owner, character, collider, hitPoint, normal))
        {
            state.RegisterHit(character);
        }

        return true;
    }

    private static bool ApplyPiercingShotHit(
        Projectile projectile,
        PiercingShotState state,
        Character? owner,
        Character character,
        Collider collider,
        Vector3 hitPoint,
        Vector3 normal)
    {
        HitData? hitData = ProjectileAccess.GetOriginalHitData(projectile)?.Clone();
        if (hitData == null)
        {
            return false;
        }

        if (hitData.m_dodgeable && character.IsDodgeInvincible())
        {
            if (character is Player dodgingPlayer)
            {
                dodgingPlayer.HitWhileDodging();
            }

            return false;
        }

        float decayScale = Mathf.Pow(1f - state.Behavior.PierceDamageDecay, state.HitCount);
        if (!Mathf.Approximately(decayScale, 1f))
        {
            hitData.m_damage.Modify(decayScale);
            hitData.m_pushForce *= decayScale;
        }

        if (state.HitCount > 0)
        {
            hitData.m_skillRaiseAmount = 0f;
        }

        hitData.m_point = hitPoint;
        hitData.m_dir = ResolvePiercingShotHitDirection(projectile, owner, character);
        hitData.m_hitCollider = collider;
        if (owner != null)
        {
            hitData.SetAttacker(owner);
        }

        bool contextActive = SecondaryAttackRuntimeFacade.BeginProjectileHitContext(projectile, collider, hitPoint, water: false, normal);
            try
            {
                SecondaryAttackProjectileToolTierSystem.ApplyToHitData(
                    hitData,
                    projectile,
                    ProjectileAccess.GetWeapon(projectile),
                    "ProjectileRuntimeSystem.ApplyPiercingShotHit");
                character.Damage(hitData);
                if (state.HitCount == 0 &&
                    owner != null &&
                    projectile.m_adrenaline > 0f &&
                    character.m_enemyAdrenalineMultiplier > 0f &&
                    BaseAI.IsEnemy(owner, character))
                {
                    owner.AddAdrenaline(projectile.m_adrenaline * character.m_enemyAdrenalineMultiplier);
                }
            }
            finally
            {
                SecondaryAttackRuntimeFacade.EndProjectileHitContext(contextActive);
            }

        Quaternion rotation = SecondaryAttackNamedEffectSystem.RotationFromNormal(normal);
        projectile.m_hitEffects.Create(hitPoint, rotation);
        if (owner != null && projectile.m_hitNoise > 0f)
        {
            owner.AddNoise(projectile.m_hitNoise);
        }

        return true;
    }

    private static bool IsValidPiercingShotTarget(Character? owner, Character target)
    {
        if (target == null || target.IsDead())
        {
            return false;
        }

        if (owner == null)
        {
            return true;
        }

        if (target == owner)
        {
            return false;
        }

        bool isEnemy = BaseAI.IsEnemy(owner, target) ||
                       (target.GetBaseAI() != null && target.GetBaseAI().IsAggravatable() && owner.IsPlayer());
        if (owner.IsPlayer() && !owner.IsPVPEnabled() && !isEnemy)
        {
            return false;
        }

        return true;
    }

    private static Vector3 ResolvePiercingShotHitDirection(Projectile projectile, Character? owner, Character target)
    {
        Vector3 velocity = projectile.GetVelocity();
        if (velocity.sqrMagnitude > 0.001f)
        {
            return velocity.normalized;
        }

        if (owner != null)
        {
            Vector3 direction = target.GetCenterPoint() - owner.GetCenterPoint();
            if (direction.sqrMagnitude > 0.001f)
            {
                return direction.normalized;
            }
        }

        Vector3 forward = projectile.transform.forward;
        return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
    }

    internal static void FireScatterRicochet(Attack attack, SecondaryAttackDefinition definition)
    {
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!TryGetProjectilePayload(attack, definition, launchData, out Projectile _))
        {
            return;
        }

        PrepareCustomProjectileBurst(attack);
        attack.GetProjectileSpawnPoint(out Vector3 spawnPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        if (attack.m_burstEffect.HasEffects())
        {
            attack.m_burstEffect.Create(spawnPoint, Quaternion.LookRotation(aimDirection));
        }

        float speed = ResolveProjectileSpeed(launchData);
        GameObject projectileObject = SpawnProjectileObject(attack, launchData, spawnPoint, aimDirection, speed, setLastProjectile: true, out Projectile? projectile);
        if (projectileObject == null || projectile == null)
        {
            return;
        }

        RegisterScatterRicochetProjectile(
            projectile,
            attack,
            launchData,
            (ProjectileSecondaryBehavior)definition.Behavior,
            speed);
    }

    private static void RegisterScatterRicochetProjectile(
        Projectile projectile,
        Attack attack,
        ProjectileLaunchData launchData,
        ProjectileSecondaryBehavior projectileBehavior,
        float speed)
    {
        if (projectile == null)
        {
            return;
        }

        ScatterRicochetProjectiles.Remove(projectile);
        ScatterRicochetProjectiles.Add(
            projectile,
            new ScatterRicochetState(attack, launchData, projectileBehavior, Mathf.Max(0.01f, speed)));

        SecondaryAttackManager.LogRangedDebug(
            "[Scatter] registered"
            + $" projectile={DescribeProjectileForScatterDebug(projectile)}"
            + $" payload={launchData.ProjectilePrefab?.name ?? "<null>"}"
            + $" speed={speed:0.###}"
            + $" count={projectileBehavior.ProjectileCount}"
            + $" splitAngle={projectileBehavior.SplitAngle:0.###}"
            + $" ricochetBounces={projectileBehavior.RicochetBounces}"
            + $" ricochetDecay={projectileBehavior.RicochetDecay:0.###}"
            + $" weapon=[{SecondaryAttackManager.DescribeItemForRangedDebug(attack.m_weapon)}]"
            + $" ammo=[{SecondaryAttackManager.DescribeItemForRangedDebug(launchData.AmmoItem)}]");
    }

    internal static bool TryHandleScatterRicochetProjectileHit(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal)
    {
        if (projectile == null || !ScatterRicochetProjectiles.TryGetValue(projectile, out ScatterRicochetState? state))
        {
            return false;
        }

        if (collider == null)
        {
            SecondaryAttackManager.LogRangedDebug(
                "[Scatter] hit skipped null collider"
                + $" projectile={DescribeProjectileForScatterDebug(projectile)}"
                + $" hitPoint={FormatVector3(hitPoint)}"
                + $" water={water}"
                + $" normal={FormatVector3(normal)}"
                + $" velocity={FormatVector3(projectile.GetVelocity())}");
            return false;
        }

        Character? hitCharacter = GetHitCharacter(collider);
        Character? owner = ProjectileAccess.GetOwner(projectile) ?? state.Attack?.m_character;
        SecondaryAttackManager.LogRangedDebug(
            "[Scatter] hit"
            + $" projectile={DescribeProjectileForScatterDebug(projectile)}"
            + $" collider={DescribeColliderForScatterDebug(collider)}"
            + $" character={hitCharacter?.name ?? "<none>"}"
            + $" owner={owner?.name ?? "<none>"}"
            + $" hitPoint={FormatVector3(hitPoint)}"
            + $" water={water}"
            + $" normal={FormatVector3(normal)}"
            + $" velocity={FormatVector3(projectile.GetVelocity())}");

        if (hitCharacter != null && hitCharacter == owner)
        {
            SecondaryAttackManager.LogRangedDebug(
                "[Scatter] ignored owner hit; source projectile remains armed"
                + $" character={hitCharacter.name}"
                + $" projectile={DescribeProjectileForScatterDebug(projectile)}");
            return true;
        }

        if (hitCharacter != null)
        {
            ScatterRicochetProjectiles.Remove(projectile);
            SecondaryAttackManager.LogRangedDebug(
                "[Scatter] source projectile hit a character; vanilla hit continues without splitting"
                + $" character={hitCharacter.name}"
                + $" projectile={DescribeProjectileForScatterDebug(projectile)}");
            return false;
        }

        ScatterRicochetProjectiles.Remove(projectile);
        SecondaryAttackManager.LogRangedDebug(
            "[Scatter] splitting on non-character hit"
            + $" count={state.Behavior.ProjectileCount}"
            + $" hitPoint={FormatVector3(hitPoint)}"
            + $" normal={FormatVector3(normal)}"
            + $" collider={DescribeColliderForScatterDebug(collider)}");
        SpawnScatterRicochetProjectiles(projectile, state, hitPoint, normal);
        if (state.Attack?.m_weapon != null && state.Attack.m_weapon.m_lastProjectile == projectile.gameObject)
        {
            state.Attack.m_weapon.m_lastProjectile = null;
        }

        DestroyProjectileObject(projectile.gameObject);
        return true;
    }

    private static string DescribeProjectileForScatterDebug(Projectile projectile)
    {
        if (projectile == null)
        {
            return "<null>";
        }

        string spawnOnHit = projectile.m_spawnOnHit != null ? projectile.m_spawnOnHit.name : "<null>";
        return projectile.name
               + $" aoe={projectile.m_aoe:0.###}"
               + $" ttl={projectile.m_ttl:0.###}"
               + $" gravity={projectile.m_gravity:0.###}"
               + $" drag={projectile.m_drag:0.###}"
               + $" rayRadius={projectile.m_rayRadius:0.###}"
               + $" doOwnerRaytest={projectile.m_doOwnerRaytest}"
               + $" canHitWater={projectile.m_canHitWater}"
               + $" spawnOnHit={spawnOnHit}"
               + $" spawnOnTtl={projectile.m_spawnOnTtl}"
               + $" stayStatic={projectile.m_stayAfterHitStatic}"
               + $" stayDynamic={projectile.m_stayAfterHitDynamic}"
               + $" bounce={projectile.m_bounce}";
    }

    private static string DescribeColliderForScatterDebug(Collider collider)
    {
        if (collider == null)
        {
            return "<null>";
        }

        GameObject colliderObject = collider.gameObject;
        string layerName = LayerMask.LayerToName(colliderObject.layer);
        string rigidbodyName = collider.attachedRigidbody != null ? collider.attachedRigidbody.name : "<none>";
        return colliderObject.name
               + $" layer={colliderObject.layer}:{layerName}"
               + $" tag={colliderObject.tag}"
               + $" rigidbody={rigidbodyName}";
    }

    private static void SpawnScatterRicochetProjectiles(
        Projectile sourceProjectile,
        ScatterRicochetState state,
        Vector3 hitPoint,
        Vector3 normal)
    {
        if (state.Attack == null || state.Attack.m_character == null || state.Attack.m_character.IsDead())
        {
            return;
        }

        Vector3 velocity = sourceProjectile.GetVelocity();
        Vector3 incomingDirection = velocity.sqrMagnitude > 0.001f
            ? velocity.normalized
            : sourceProjectile.transform.forward;
        if (incomingDirection.sqrMagnitude < 0.001f)
        {
            incomingDirection = state.Attack.m_character.transform.forward;
        }

        Vector3 hitNormal = normal.sqrMagnitude > 0.001f ? normal.normalized : -incomingDirection;
        Vector3 reflectedDirection = Vector3.Reflect(incomingDirection, hitNormal);
        if (reflectedDirection.sqrMagnitude < 0.001f)
        {
            reflectedDirection = -incomingDirection;
        }

        reflectedDirection.Normalize();
        ResolveScatterRicochetAxes(reflectedDirection, hitNormal, state.Attack, out Vector3 scatterRight, out _);
        int projectileCount = Mathf.Max(1, state.Behavior.ProjectileCount);
        float ricochetRetainFactor = ResolveRicochetRetainFactor(state.Behavior);
        float launchSpeed = state.Speed * Mathf.Max(0.01f, ricochetRetainFactor);
        float clearDistance = Mathf.Max(0.1f, sourceProjectile.m_rayRadius + 0.1f);
        Vector3 spawnPoint = hitPoint + hitNormal * clearDistance + reflectedDirection * clearDistance;

        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            Vector3 direction = ResolveScatterRicochetDirection(
                reflectedDirection,
                scatterRight,
                state.Behavior.SplitAngle,
                projectileIndex,
                projectileCount);
            GameObject projectileObject = SpawnProjectileObject(
                state.Attack,
                state.LaunchData,
                spawnPoint,
                direction,
                launchSpeed,
                setLastProjectile: true,
                out Projectile? projectile);
            if (projectileObject == null || projectile == null)
            {
                continue;
            }

            ApplyRicochetBounce(projectile, state.Behavior);
            RegisterScatterRicochetSplitProjectile(projectile, state.Behavior);
        }
    }

    private static void ResolveScatterRicochetAxes(
        Vector3 reflectedDirection,
        Vector3 hitNormal,
        Attack attack,
        out Vector3 right,
        out Vector3 up)
    {
        right = Vector3.Cross(reflectedDirection, Vector3.up);
        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.Cross(reflectedDirection, hitNormal);
        }

        if (right.sqrMagnitude < 0.001f)
        {
            right = attack.m_character != null ? attack.m_character.transform.right : Vector3.right;
        }

        right.Normalize();
        up = Vector3.Cross(right, reflectedDirection);
        if (up.sqrMagnitude < 0.001f)
        {
            up = attack.m_character != null ? attack.m_character.transform.up : Vector3.up;
        }

        up.Normalize();
    }

    private static Vector3 ResolveScatterRicochetDirection(
        Vector3 reflectedDirection,
        Vector3 scatterRight,
        float splitAngle,
        int projectileIndex,
        int projectileCount)
    {
        if (projectileCount <= 1 || splitAngle <= 0f)
        {
            return reflectedDirection;
        }

        float angleAround = projectileIndex * 137.50776f;
        float coneAngle = Mathf.Sqrt((projectileIndex + 0.5f) / projectileCount) * splitAngle * 0.5f;
        Vector3 direction = Quaternion.AngleAxis(coneAngle, scatterRight) * reflectedDirection;
        direction = Quaternion.AngleAxis(angleAround, reflectedDirection) * direction;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : reflectedDirection;
    }

    private static void ApplyRicochetBounce(Projectile projectile, ProjectileSecondaryBehavior projectileBehavior)
    {
        if (projectile == null || projectileBehavior.RicochetBounces <= 0)
        {
            return;
        }

        projectile.m_bounce = true;
        projectile.m_maxBounces = Mathf.Max(1, projectileBehavior.RicochetBounces);
        projectile.m_bouncePower = ResolveRicochetRetainFactor(projectileBehavior);
        projectile.m_bounceRoughness = projectileBehavior.RicochetRoughness;
    }

    private static float ResolveRicochetRetainFactor(ProjectileSecondaryBehavior projectileBehavior)
    {
        return Mathf.Clamp01(1f - projectileBehavior.RicochetDecay);
    }

    private static void RegisterScatterRicochetSplitProjectile(
        Projectile projectile,
        ProjectileSecondaryBehavior projectileBehavior)
    {
        ScatterRicochetSplitProjectiles.Remove(projectile);
        ScatterRicochetSplitProjectiles.Add(projectile, new ScatterRicochetSplitState(projectileBehavior));
    }

    internal static ScatterRicochetDamageScope BeginScatterRicochetDamageScale(
        Projectile projectile,
        Collider collider,
        bool water,
        Vector3 normal)
    {
        if (projectile == null ||
            !ScatterRicochetSplitProjectiles.TryGetValue(projectile, out ScatterRicochetSplitState? state) ||
            state == null ||
            ShouldScatterRicochetProjectileBounce(projectile, collider, water, normal))
        {
            return default;
        }

        float retainFactor = ResolveRicochetRetainFactor(state.Behavior);
        int ricochetSteps = Mathf.Max(1, projectile.m_bounceCount + 1);
        float damageScale = Mathf.Pow(retainFactor, ricochetSteps);
        ScatterRicochetSplitProjectiles.Remove(projectile);
        if (Mathf.Approximately(damageScale, 1f))
        {
            return default;
        }

        ScatterRicochetDamageScope scope = new(projectile, projectile.m_damage);
        projectile.m_damage.Modify(damageScale);
        return scope;
    }

    internal static void EndScatterRicochetDamageScale(ScatterRicochetDamageScope scope)
    {
        if (scope.Active && scope.Projectile != null)
        {
            scope.Projectile.m_damage = scope.OriginalDamage;
        }
    }

    private static bool ShouldScatterRicochetProjectileBounce(
        Projectile projectile,
        Collider collider,
        bool water,
        Vector3 normal)
    {
        if (!projectile.m_bounce || normal == Vector3.zero)
        {
            return false;
        }

        if (water && !projectile.m_bounceOnWater)
        {
            return false;
        }

        if (collider != null)
        {
            GameObject hitObject = Projectile.FindHitObject(collider);
            IDestructible? destructible = hitObject != null ? hitObject.GetComponent<IDestructible>() : null;
            if (destructible is Character)
            {
                return false;
            }
        }

        return projectile.m_bounceCount < projectile.m_maxBounces &&
               projectile.m_vel.magnitude > projectile.m_minBounceVel;
    }

    internal static void FireSpiralBurst(Attack attack, SecondaryAttackDefinition definition)
    {
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!TryGetProjectilePayload(attack, definition, launchData, out Projectile _))
        {
            return;
        }

        ProjectileSecondaryBehavior projectileBehavior = (ProjectileSecondaryBehavior)definition.Behavior;

        PrepareCustomProjectileBurst(attack);
        attack.GetProjectileSpawnPoint(out Vector3 spawnPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        if (attack.m_burstEffect.HasEffects())
        {
            attack.m_burstEffect.Create(spawnPoint, Quaternion.LookRotation(aimDirection));
        }

        ResolveBurstAxes(attack, aimDirection, out Vector3 burstRight, out Vector3 burstUp);
        int projectileCount = Mathf.Max(1, projectileBehavior.ProjectileCount);
        if (projectileBehavior.Interval > 0f && projectileCount > 1)
        {
            CreateScheduledBurstController(
                attack,
                launchData,
                definition,
                ScheduledBurstMode.Spiral,
                spawnPoint,
                aimDirection,
                spawnPoint,
                burstUp,
                burstRight);
            return;
        }

        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            SpawnSpiralBurstProjectile(
                attack,
                launchData,
                projectileBehavior,
                spawnPoint,
                aimDirection,
                burstRight,
                burstUp,
                projectileIndex,
                projectileCount);
        }
    }

    internal static void FireSentinel(Attack attack, SecondaryAttackDefinition definition)
    {
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!TryGetProjectilePayload(attack, definition, launchData, out Projectile _))
        {
            return;
        }

        ProjectileSecondaryBehavior projectileBehavior = (ProjectileSecondaryBehavior)definition.Behavior;

        PrepareCustomProjectileBurst(attack);
        attack.GetProjectileSpawnPoint(out Vector3 burstPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        if (attack.m_burstEffect.HasEffects())
        {
            attack.m_burstEffect.Create(burstPoint, Quaternion.LookRotation(aimDirection));
        }

        int sentinelCount = Mathf.Max(1, projectileBehavior.ProjectileCount);
        for (int projectileIndex = 0; projectileIndex < sentinelCount; projectileIndex++)
        {
            Vector3 spawnPoint = GetSentinelHoverPosition(attack.m_character, definition, projectileIndex, sentinelCount, Time.time);
            Quaternion rotation = Quaternion.LookRotation(GetSentinelForward(attack.m_character), Vector3.up);
            GameObject projectileObject = Object.Instantiate(launchData.ProjectilePrefab!, spawnPoint, rotation);
            Projectile? projectile = projectileObject.GetComponent<Projectile>();
            if (projectile == null)
            {
                DestroyProjectileObject(projectileObject);
                SpawnProjectile(attack, launchData, burstPoint, aimDirection);
                continue;
            }

            float launchSpeed = ResolveProjectileSpeed(launchData);
            HitData hitData = CreateProjectileHitData(attack, launchData.AmmoItem, launchData.DamageFactor, launchData.ConfiguredDamageFactor, launchData.ConfiguredSkillRaiseFactor);
            SentinelController controller = projectileObject.AddComponent<SentinelController>();
            controller.Initialize(
                attack,
                attack.m_character,
                attack.m_weapon,
                attack.m_lastUsedAmmo,
                projectile,
                launchData.AttackHitNoise,
                hitData,
                launchSpeed,
                definition,
                projectileIndex,
                sentinelCount);
            attack.m_weapon.m_lastProjectile = projectileObject;

            if (attack.m_spawnOnHitChance > 0f && attack.m_spawnOnHit != null)
            {
                projectile.m_spawnOnHit = attack.m_spawnOnHit;
                projectile.m_spawnOnHitChance = attack.m_spawnOnHitChance;
            }
        }
    }

    internal static void FireMeteor(Attack attack, SecondaryAttackDefinition definition)
    {
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!TryGetProjectilePayload(attack, definition, launchData, out Projectile _))
        {
            return;
        }

        ProjectileSecondaryBehavior projectileBehavior = (ProjectileSecondaryBehavior)definition.Behavior;

        PrepareCustomProjectileBurst(attack);
        attack.GetProjectileSpawnPoint(out Vector3 originPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        Vector3 targetPoint = ResolveMeteorTargetPoint(attack, definition, originPoint, aimDirection);
        ResolveHorizontalAxes(attack, aimDirection, out Vector3 horizontalForward, out Vector3 horizontalRight);
        if (attack.m_burstEffect.HasEffects())
        {
            attack.m_burstEffect.Create(originPoint, Quaternion.LookRotation(aimDirection));
        }

        if (projectileBehavior.Interval > 0f && projectileBehavior.ProjectileCount > 1)
        {
            CreateScheduledBurstController(
                attack,
                launchData,
                definition,
                ScheduledBurstMode.Meteor,
                originPoint,
                aimDirection,
                targetPoint,
                horizontalForward,
                horizontalRight);
            return;
        }

        for (int projectileIndex = 0; projectileIndex < projectileBehavior.ProjectileCount; projectileIndex++)
        {
            SpawnMeteor(attack, launchData, projectileBehavior, targetPoint, horizontalForward, horizontalRight);
        }
    }

    private static void SpawnMeteor(
        Attack attack,
        ProjectileLaunchData launchData,
        ProjectileSecondaryBehavior projectileBehavior,
        Vector3 targetPoint,
        Vector3 horizontalForward,
        Vector3 horizontalRight)
    {
        Vector2 impactOffset2D = projectileBehavior.MeteorRadius > 0f
            ? UnityEngine.Random.insideUnitCircle * projectileBehavior.MeteorRadius
            : Vector2.zero;
        Vector3 impactPoint = targetPoint + horizontalRight * impactOffset2D.x + horizontalForward * impactOffset2D.y;
        Vector3 spawnPoint = impactPoint + Vector3.up * projectileBehavior.MeteorSpawnHeight;
        Vector3 launchDirection = impactPoint - spawnPoint;
        if (launchDirection.sqrMagnitude < 0.001f)
        {
            launchDirection = Vector3.down;
        }

        float speed = ResolveProjectileSpeed(launchData) * Mathf.Max(0.01f, projectileBehavior.ProjectileSpeedFactor);
        GameObject projectileObject = SpawnProjectileObject(
            attack,
            launchData,
            spawnPoint,
            launchDirection.normalized,
            speed,
            setLastProjectile: true,
            out Projectile? projectile);
        if (projectileObject == null || projectile == null)
        {
            return;
        }

        OverchargedBombSystem.RegisterProjectile(
            projectile,
            projectileBehavior.ProjectileScaleFactor,
            projectileBehavior.AoeRadiusFactor);
    }

    private static void SpawnSpiralBurstProjectile(
        Attack attack,
        ProjectileLaunchData launchData,
        ProjectileSecondaryBehavior projectileBehavior,
        Vector3 spawnPoint,
        Vector3 aimDirection,
        Vector3 burstRight,
        Vector3 burstUp,
        int projectileIndex,
        int projectileCount)
    {
        float progress = projectileCount <= 1 ? 0f : projectileIndex / (float)(projectileCount - 1);
        float angleRadians = progress * projectileBehavior.SpiralTurns * Mathf.PI * 2f;
        Vector3 radialOffset = burstRight * Mathf.Cos(angleRadians) * projectileBehavior.SpiralRadius
                               + burstUp * Mathf.Sin(angleRadians) * projectileBehavior.SpiralRadius;
        Vector3 direction = (aimDirection + radialOffset).normalized;
        SpawnProjectile(attack, launchData, spawnPoint, direction);
    }

    internal static void FireBurstFire(Attack attack, SecondaryAttackDefinition definition)
    {
        if (definition.Behavior is not ProjectileSecondaryBehavior projectileBehavior)
        {
            return;
        }

        int shotCount = Mathf.Max(1, projectileBehavior.ProjectileCount);
        if (!FireSingleBurstFireShot(attack, definition))
        {
            return;
        }

        if (shotCount <= 1)
        {
            return;
        }

        DeferredBurstFireReloadResets.Add(attack);
        GameObject controllerObject = new($"SecondaryAttacks_{GetPresetName(projectileBehavior.Preset)}");
        BurstFireController controller = controllerObject.AddComponent<BurstFireController>();
        controller.Initialize(attack, definition, shotCount - 1);
    }

    internal static bool ShouldDeferBurstFireReloadReset(Attack attack)
    {
        return attack != null && DeferredBurstFireReloadResets.Contains(attack);
    }

    internal static bool IsBurstFireControllerActive(Attack attack)
    {
        return attack != null && ActiveBurstFireControllers.Contains(attack);
    }

    private static void ClearDeferredBurstFireReloadReset(Attack attack)
    {
        if (attack != null)
        {
            DeferredBurstFireReloadResets.Remove(attack);
        }
    }

    private static bool FireSingleBurstFireShot(Attack attack, SecondaryAttackDefinition definition)
    {
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!TryGetProjectilePayload(attack, definition, launchData, out Projectile _))
        {
            return false;
        }

        PrepareCustomProjectileBurst(attack);
        attack.GetProjectileSpawnPoint(out Vector3 spawnPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        OrientCharacterBodyToProjectileAim(attack, aimDirection);
        if (attack.m_burstEffect.HasEffects())
        {
            attack.m_burstEffect.Create(spawnPoint, Quaternion.LookRotation(aimDirection));
        }

        SpawnPrimaryProjectileCluster(attack, launchData, spawnPoint, aimDirection);
        return true;
    }

    private static void OrientCharacterBodyToProjectileAim(Attack attack, Vector3 aimDirection)
    {
        Character? character = attack.m_character;
        if (character == null || !SecondaryAttackManager.HasCharacterAuthority(character))
        {
            return;
        }

        Vector3 horizontalForward = Vector3.ProjectOnPlane(aimDirection, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.001f)
        {
            return;
        }

        character.transform.rotation = Quaternion.LookRotation(horizontalForward.normalized, Vector3.up);
    }

    private static void SpawnPrimaryProjectileCluster(
        Attack attack,
        ProjectileLaunchData launchData,
        Vector3 spawnPoint,
        Vector3 aimDirection)
    {
        int projectileCount = Mathf.Max(1, attack.m_projectiles);
        for (int projectileIndex = 0; projectileIndex < projectileCount; projectileIndex++)
        {
            if (attack.m_destroyPreviousProjectile && attack.m_weapon.m_lastProjectile != null)
            {
                DestroyProjectileObject(attack.m_weapon.m_lastProjectile);
                attack.m_weapon.m_lastProjectile = null;
            }

            Vector3 direction = ResolvePrimaryProjectileDirection(
                attack,
                aimDirection,
                launchData.ProjectileAccuracy,
                projectileIndex,
                projectileCount);
            SpawnProjectile(attack, launchData, spawnPoint, direction);
        }
    }

    private static void CreateScheduledBurstController(
        Attack attack,
        ProjectileLaunchData launchData,
        SecondaryAttackDefinition definition,
        ScheduledBurstMode mode,
        Vector3 originPoint,
        Vector3 aimDirection,
        Vector3 targetPoint,
        Vector3 horizontalForward,
        Vector3 horizontalRight)
    {
        GameObject controllerObject = new($"SecondaryAttacks_{GetPresetName(((ProjectileSecondaryBehavior)definition.Behavior).Preset)}");
        ScheduledProjectileBurstController controller = controllerObject.AddComponent<ScheduledProjectileBurstController>();
        controller.Initialize(attack, launchData, definition, mode, originPoint, aimDirection, targetPoint, horizontalForward, horizontalRight);
    }

    private static ProjectileLaunchData CreateLaunchData(Attack attack, SecondaryAttackDefinition definition)
    {
        ItemDrop.ItemData ammoItem = attack.m_ammoItem;
        GameObject projectilePrefab = attack.m_attackProjectile;
        float projectileVelocity = attack.m_projectileVel;
        float projectileVelocityMin = attack.m_projectileVelMin;
        float projectileAccuracy = attack.m_projectileAccuracy;
        float projectileAccuracyMin = attack.m_projectileAccuracyMin;
        float attackHitNoise = attack.m_attackHitNoise;
        AnimationCurve drawVelocityCurve = attack.m_drawVelocityCurve;

        if (ammoItem != null && ammoItem.m_shared.m_attack.m_attackProjectile != null)
        {
            projectilePrefab = ammoItem.m_shared.m_attack.m_attackProjectile;
            projectileVelocity += ammoItem.m_shared.m_attack.m_projectileVel;
            projectileVelocityMin += ammoItem.m_shared.m_attack.m_projectileVelMin;
            projectileAccuracy += ammoItem.m_shared.m_attack.m_projectileAccuracy;
            projectileAccuracyMin += ammoItem.m_shared.m_attack.m_projectileAccuracyMin;
            attackHitNoise += ammoItem.m_shared.m_attack.m_attackHitNoise;
            drawVelocityCurve = ammoItem.m_shared.m_attack.m_drawVelocityCurve;
        }

        if (projectilePrefab == null)
        {
            return ProjectileLaunchData.Invalid;
        }

        ProjectileSecondaryBehavior? projectileBehavior = definition.Behavior as ProjectileSecondaryBehavior;
        float damageFactor = attack.m_character.GetRandomSkillFactor(attack.m_weapon.m_shared.m_skillType);
        float configuredDamageFactor = Mathf.Max(0f, projectileBehavior?.DamageFactor ?? 1f);
        float configuredSkillRaiseFactor = Mathf.Max(0f, projectileBehavior?.SkillRaiseFactor ?? 1f);
        float configuredAdrenalineFactor = Mathf.Max(0f, projectileBehavior?.AdrenalineFactor ?? 1f);
        if (attack.m_bowDraw)
        {
            projectileAccuracy = Mathf.Lerp(projectileAccuracyMin, projectileAccuracy, Mathf.Pow(attack.m_attackDrawPercentage, 0.5f));
            damageFactor *= attack.m_attackDrawPercentage;
            projectileVelocity = Mathf.Lerp(projectileVelocityMin, projectileVelocity, drawVelocityCurve.Evaluate(attack.m_attackDrawPercentage));
            if (attack.m_character is Player)
            {
                Game.instance.IncrementPlayerStat(PlayerStatType.ArrowsShot);
            }
        }
        else if (attack.m_skillAccuracy)
        {
            float skillFactor = attack.m_character.GetSkillFactor(attack.m_weapon.m_shared.m_skillType);
            projectileAccuracy = Mathf.Lerp(projectileAccuracyMin, projectileAccuracy, skillFactor);
        }

        ProjectileLaunchData launchData = new(projectilePrefab, ammoItem, projectileVelocity, projectileVelocityMin, projectileAccuracy, projectileAccuracyMin, attackHitNoise, damageFactor, configuredDamageFactor, configuredSkillRaiseFactor, configuredAdrenalineFactor, attack.m_randomVelocity && !attack.m_bowDraw);
        DumpRuntimeLaunchProfile(attack, launchData);
        return launchData;
    }

    private static Vector3 ApplyLaunchAngle(Attack attack, Vector3 aimDirection)
    {
        if (attack.m_launchAngle == 0f)
        {
            return aimDirection;
        }

        Vector3 axis = Vector3.Cross(Vector3.up, aimDirection);
        if (axis == Vector3.zero)
        {
            axis = attack.m_character.transform.right;
        }

        return Quaternion.AngleAxis(attack.m_launchAngle, axis) * aimDirection;
    }

    private static void ResolveHorizontalAxes(Attack attack, Vector3 aimDirection, out Vector3 horizontalForward, out Vector3 horizontalRight)
    {
        horizontalForward = Vector3.ProjectOnPlane(aimDirection, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.001f)
        {
            horizontalForward = Vector3.ProjectOnPlane(attack.m_character.transform.forward, Vector3.up);
        }

        if (horizontalForward.sqrMagnitude < 0.001f)
        {
            horizontalForward = attack.m_character.transform.forward;
        }

        horizontalForward.Normalize();
        horizontalRight = Vector3.Cross(Vector3.up, horizontalForward);
        if (horizontalRight.sqrMagnitude < 0.001f)
        {
            horizontalRight = attack.m_character.transform.right;
        }

        horizontalRight.Normalize();
    }

    private static void ResolveBurstAxes(Attack attack, Vector3 aimDirection, out Vector3 right, out Vector3 up)
    {
        right = Vector3.Cross(aimDirection, Vector3.up);
        if (right.sqrMagnitude < 0.001f)
        {
            right = attack.m_character.transform.right;
        }

        right.Normalize();
        up = Vector3.Cross(right, aimDirection);
        if (up.sqrMagnitude < 0.001f)
        {
            up = attack.m_character.transform.up;
        }

        up.Normalize();
    }

    private static Vector3 ResolvePrimaryProjectileDirection(
        Attack attack,
        Vector3 aimDirection,
        float projectileAccuracy,
        int projectileIndex,
        int projectileCount)
    {
        Vector3 direction = aimDirection.sqrMagnitude > 0.001f ? aimDirection.normalized : attack.m_character.transform.forward;
        Vector3 verticalSpreadAxis = Vector3.Cross(direction, Vector3.up);
        if (verticalSpreadAxis.sqrMagnitude < 0.001f)
        {
            verticalSpreadAxis = attack.m_character.transform.right;
        }

        verticalSpreadAxis.Normalize();
        Quaternion horizontalSpread = Quaternion.AngleAxis(UnityEngine.Random.Range(-projectileAccuracy, projectileAccuracy), Vector3.up);
        if (attack.m_circularProjectileLaunch && !attack.m_distributeProjectilesAroundCircle)
        {
            horizontalSpread = Quaternion.AngleAxis(UnityEngine.Random.value * 360f, Vector3.up);
        }
        else if (attack.m_circularProjectileLaunch && attack.m_distributeProjectilesAroundCircle)
        {
            float step = projectileCount > 0 ? 360f / projectileCount : 360f;
            horizontalSpread = Quaternion.AngleAxis(UnityEngine.Random.Range(-projectileAccuracy, projectileAccuracy) + projectileIndex * step, Vector3.up);
        }

        direction = Quaternion.AngleAxis(UnityEngine.Random.Range(-projectileAccuracy, projectileAccuracy), verticalSpreadAxis) * direction;
        direction = horizontalSpread * direction;
        return direction.sqrMagnitude > 0.001f ? direction.normalized : attack.m_character.transform.forward;
    }

    private static void ApplyRangePreservingProjectileSpeedModifiers(Projectile projectile, float speedFactor)
    {
        speedFactor = Mathf.Max(0.01f, speedFactor);
        float rangePreservingTimeFactor = 1f / speedFactor;
        projectile.m_ttl *= rangePreservingTimeFactor;
        projectile.m_gravity *= speedFactor * speedFactor;
        projectile.m_drag *= speedFactor;
    }

    private static void PrepareCustomProjectileBurst(Attack attack)
    {
        if (attack.m_destroyPreviousProjectile && attack.m_weapon.m_lastProjectile != null)
        {
            DestroyProjectileObject(attack.m_weapon.m_lastProjectile);
            attack.m_weapon.m_lastProjectile = null;
        }
    }

    private static void SpawnProjectile(Attack attack, ProjectileLaunchData launchData, Vector3 spawnPoint, Vector3 direction)
    {
        float speed = ResolveProjectileSpeed(launchData);
        SpawnProjectile(attack, launchData, spawnPoint, direction, speed);
    }

    private static void SpawnProjectile(Attack attack, ProjectileLaunchData launchData, Vector3 spawnPoint, Vector3 direction, float speed)
    {
        SpawnProjectileObject(attack, launchData, spawnPoint, direction, speed, setLastProjectile: true, out _);
    }

    private static GameObject SpawnProjectileObject(
        Attack attack,
        ProjectileLaunchData launchData,
        Vector3 spawnPoint,
        Vector3 direction,
        float speed,
        bool setLastProjectile,
        out Projectile? projectileComponent)
    {
        projectileComponent = null;
        if (direction == Vector3.zero)
        {
            direction = attack.m_character.transform.forward;
        }

        direction.Normalize();
        SecondaryAttackManager.LogRangedDebug(
            $"projectile spawn prefab={launchData.ProjectilePrefab?.name ?? "<null>"}"
            + $" setLast={setLastProjectile}"
            + $" speed={speed:0.###}"
            + $" spawn={FormatVector3(spawnPoint)}"
            + $" direction={FormatVector3(direction)}"
            + $" weapon=[{SecondaryAttackManager.DescribeItemForRangedDebug(attack.m_weapon)}]"
            + $" ammo=[{SecondaryAttackManager.DescribeItemForRangedDebug(launchData.AmmoItem)}]"
            + $" lastAmmo=[{SecondaryAttackManager.DescribeItemForRangedDebug(attack.m_lastUsedAmmo)}]");
        GameObject projectileObject = Object.Instantiate(launchData.ProjectilePrefab!, spawnPoint, Quaternion.LookRotation(direction));
        HitData hitData = CreateProjectileHitData(attack, launchData.AmmoItem, launchData.DamageFactor, launchData.ConfiguredDamageFactor, launchData.ConfiguredSkillRaiseFactor);
        IProjectile projectile = projectileObject.GetComponent<IProjectile>();
        projectileComponent = projectileObject.GetComponent<Projectile>();
        projectile?.Setup(attack.m_character, direction * speed, launchData.AttackHitNoise, hitData, attack.m_weapon, attack.m_lastUsedAmmo);

        if (projectileComponent != null)
        {
            SecondaryAttackAdrenalineSystem.ApplyProjectileFactor(projectileComponent, attack, launchData.ConfiguredAdrenalineFactor);
            RegisterProjectileAttackAttribution(projectileComponent, attack);
        }

        if (setLastProjectile)
        {
            attack.m_weapon.m_lastProjectile = projectileObject;
        }

        if (attack.m_spawnOnHitChance > 0f && attack.m_spawnOnHit != null && projectile is Projectile baseProjectile)
        {
            baseProjectile.m_spawnOnHit = attack.m_spawnOnHit;
            baseProjectile.m_spawnOnHitChance = attack.m_spawnOnHitChance;
        }

        return projectileObject;
    }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:0.###},{value.y:0.###},{value.z:0.###})";
    }

    internal static void RegisterProjectileAttackAttribution(Projectile projectile, Attack attack)
    {
        if (projectile == null || attack?.m_weapon?.m_dropPrefab == null)
        {
            return;
        }

        if (!SecondaryAttackRuntimeFacade.TryResolveProjectileAttackAttributionData(attack, out string weaponPrefabName, out bool secondaryAttack, out SecondaryAttackDefinition? definition))
        {
            return;
        }

        SecondaryAttackRuntimeFacade.SetProjectileAttackAttribution(projectile, weaponPrefabName, secondaryAttack, definition, disableCurrentAttackFallback: false);
    }

    internal static void RegisterProjectileAttackAttribution(Projectile projectile, bool disableCurrentAttackFallback)
    {
        if (projectile == null)
        {
            return;
        }

        SecondaryAttackRuntimeFacade.SetProjectileAttackAttribution(projectile, string.Empty, secondaryAttack: false, definition: null, disableCurrentAttackFallback);
    }

    private static HitData CreateProjectileHitData(Attack attack, ItemDrop.ItemData? ammoItem, float damageFactor, float configuredDamageFactor, float configuredSkillRaiseFactor)
    {
        HitData hitData = new();
        hitData.m_toolTier = (short)attack.m_weapon.m_shared.m_toolTier;
        hitData.m_pushForce = attack.m_weapon.m_shared.m_attackForce * attack.m_forceMultiplier;
        hitData.m_backstabBonus = attack.m_weapon.m_shared.m_backstabBonus;
        hitData.m_staggerMultiplier = attack.m_staggerMultiplier;
        hitData.m_damage.Add(attack.m_weapon.GetDamage());
        hitData.m_statusEffectHash = attack.m_weapon.m_shared.m_attackStatusEffect != null &&
                                     (attack.m_weapon.m_shared.m_attackStatusEffectChance == 1f || UnityEngine.Random.Range(0f, 1f) < attack.m_weapon.m_shared.m_attackStatusEffectChance)
            ? attack.m_weapon.m_shared.m_attackStatusEffect.NameHash()
            : 0;
        hitData.m_skillLevel = attack.m_character.GetSkillLevel(attack.m_weapon.m_shared.m_skillType);
        hitData.m_itemLevel = (short)attack.m_weapon.m_quality;
        hitData.m_itemWorldLevel = (byte)attack.m_weapon.m_worldLevel;
        hitData.m_blockable = attack.m_weapon.m_shared.m_blockable;
        hitData.m_dodgeable = attack.m_weapon.m_shared.m_dodgeable;
        hitData.m_skill = attack.m_weapon.m_shared.m_skillType;
        hitData.m_skillRaiseAmount = attack.m_raiseSkillAmount * Mathf.Max(0f, configuredSkillRaiseFactor);
        hitData.SetAttacker(attack.m_character);
        hitData.m_hitType = hitData.GetAttacker() is Player ? HitData.HitType.PlayerHit : HitData.HitType.EnemyHit;
        hitData.m_healthReturn = attack.m_attackHealthReturnHit;

        if (ammoItem != null)
        {
            hitData.m_damage.Add(ammoItem.GetDamage());
            hitData.m_pushForce += ammoItem.m_shared.m_attackForce;
            if (ammoItem.m_shared.m_attackStatusEffect != null &&
                (ammoItem.m_shared.m_attackStatusEffectChance == 1f || UnityEngine.Random.Range(0f, 1f) < ammoItem.m_shared.m_attackStatusEffectChance))
            {
                hitData.m_statusEffectHash = ammoItem.m_shared.m_attackStatusEffect.NameHash();
            }

            if (!ammoItem.m_shared.m_blockable)
            {
                hitData.m_blockable = false;
            }

            if (!ammoItem.m_shared.m_dodgeable)
            {
                hitData.m_dodgeable = false;
            }
        }

        hitData.m_pushForce *= damageFactor;
        attack.ModifyDamage(hitData, damageFactor);
        if (!Mathf.Approximately(configuredDamageFactor, 1f))
        {
            hitData.m_damage.Modify(Mathf.Max(0f, configuredDamageFactor));
        }

        attack.m_character.GetSEMan().ModifyAttack(attack.m_weapon.m_shared.m_skillType, ref hitData);
        return hitData;
    }

    private static float GetProjectileGravity(GameObject projectilePrefab)
    {
        Projectile projectile = projectilePrefab.GetComponent<Projectile>();
        return projectile != null ? projectile.m_gravity : 0f;
    }

    internal static bool CanStartBurstPreset(Attack attack, SecondaryAttackDefinition definition, SecondaryAttackPreset preset)
    {
        if (preset is not SecondaryAttackPreset.Volley)
        {
            return true;
        }

        attack.GetProjectileSpawnPoint(out Vector3 originPoint, out Vector3 aimDirection);
        aimDirection = ApplyLaunchAngle(attack, aimDirection);
        ProjectileLaunchData launchData = CreateLaunchData(attack, definition);
        if (!launchData.IsValid)
        {
            return false;
        }

        return TryResolveVolleyTargetPoint(attack, definition, launchData, originPoint, aimDirection, out _);
    }

    private static bool TryResolveVolleyTargetPoint(
        Attack attack,
        SecondaryAttackDefinition definition,
        ProjectileLaunchData launchData,
        Vector3 originPoint,
        Vector3 aimDirection,
        out Vector3 targetPoint)
    {
        targetPoint = Vector3.zero;
        ProjectileSecondaryBehavior? projectileBehavior = definition.Behavior as ProjectileSecondaryBehavior;
        if (projectileBehavior == null)
        {
            return false;
        }

        if (attack.m_baseAI != null)
        {
            Character target = attack.m_baseAI.GetTargetCreature();
            if (target != null)
            {
                targetPoint = target.GetCenterPoint();
                return true;
            }
        }

        if (attack.m_character is Player player && GameCamera.instance != null)
        {
            Vector3 rayOrigin = GameCamera.instance.transform.position;
            Vector3 rayDirection = GameCamera.instance.transform.forward;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayDirection, Mathf.Infinity, GetAimRayMask());
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                Character? hitCharacter = GetHitCharacter(hit.collider);
                if (hitCharacter != null && hitCharacter != player)
                {
                    targetPoint = ClampVolleyTargetPoint(originPoint, hitCharacter.GetCenterPoint(), projectileBehavior.VolleyMaxRange);
                    return true;
                }

                if (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.gameObject == player.gameObject)
                {
                    continue;
                }

                targetPoint = ClampVolleyTargetPoint(originPoint, hit.point, projectileBehavior.VolleyMaxRange);
                return true;
            }
        }

        Vector3 fallbackDirection = aimDirection.sqrMagnitude > 0.001f ? aimDirection.normalized : attack.m_character.transform.forward;
        targetPoint = originPoint + fallbackDirection.normalized * projectileBehavior.VolleyMaxRange;
        return true;
    }

    private static Vector3 ClampVolleyTargetPoint(Vector3 originPoint, Vector3 targetPoint, float maxRange)
    {
        maxRange = Mathf.Max(1f, maxRange);
        Vector3 offset = targetPoint - originPoint;
        float distance = offset.magnitude;
        if (distance <= maxRange || distance <= 0.001f)
        {
            return targetPoint;
        }

        return originPoint + offset / distance * maxRange;
    }

    private static Vector3 ResolveMeteorTargetPoint(Attack attack, SecondaryAttackDefinition definition, Vector3 originPoint, Vector3 aimDirection)
    {
        if (attack.m_baseAI != null)
        {
            Character target = attack.m_baseAI.GetTargetCreature();
            if (target != null)
            {
                return target.GetCenterPoint();
            }
        }

        if (attack.m_character is Player player && GameCamera.instance != null)
        {
            Vector3 rayOrigin = GameCamera.instance.transform.position;
            Vector3 rayDirection = GameCamera.instance.transform.forward;
            RaycastHit[] hits = Physics.RaycastAll(rayOrigin, rayDirection, Mathf.Infinity, GetAimRayMask());
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.attachedRigidbody != null && hit.collider.attachedRigidbody.gameObject == player.gameObject)
                {
                    continue;
                }

                return hit.point;
            }
        }

        return originPoint + aimDirection * MeteorFallbackRange;
    }

    internal static Character? GetHitCharacter(Collider collider)
    {
        if (collider.attachedRigidbody != null)
        {
            Character rigidbodyCharacter = collider.attachedRigidbody.GetComponent<Character>();
            if (rigidbodyCharacter != null)
            {
                return rigidbodyCharacter;
            }
        }

        return collider.GetComponentInParent<Character>();
    }

    internal static int GetAimRayMask()
    {
        if (AimRayMask == 0)
        {
            AimRayMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
        }

        return AimRayMask;
    }

    internal static int GetShieldChargeCollisionMask()
    {
        if (ShieldChargeCollisionMask == 0)
        {
            ShieldChargeCollisionMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "blocker", "vehicle");
        }

        return ShieldChargeCollisionMask;
    }

    internal static int GetShieldChargeImpactMask()
    {
        if (ShieldChargeImpactMask == 0)
        {
            ShieldChargeImpactMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "blocker", "vehicle", "character", "character_net", "character_ghost", "hitbox", "character_noenv");
        }

        return ShieldChargeImpactMask;
    }

    private static Vector3 CalculateGravityAdjustedBallisticVelocity(
        Vector3 spawnPoint,
        Vector3 targetPoint,
        float originalGravity,
        float angleDegrees,
        float speed,
        out float projectileGravity,
        out float flightTime)
    {
        speed = Mathf.Max(0.01f, speed);
        projectileGravity = Mathf.Max(0f, originalGravity);
        Vector3 delta = targetPoint - spawnPoint;
        if (delta.sqrMagnitude < 0.001f)
        {
            flightTime = 0.1f;
            return Vector3.forward * speed;
        }

        Vector3 horizontal = new(delta.x, 0f, delta.z);
        float horizontalDistance = horizontal.magnitude;
        if (horizontalDistance < 0.001f)
        {
            flightTime = delta.magnitude / speed;
            return delta.normalized * speed;
        }

        angleDegrees = Mathf.Clamp(angleDegrees, 1f, 89f);
        float verticalDistance = delta.y;
        float minimumPositiveGravityAngle = Mathf.Atan2(verticalDistance, horizontalDistance) * Mathf.Rad2Deg + 0.1f;
        if (minimumPositiveGravityAngle > angleDegrees)
        {
            angleDegrees = Mathf.Clamp(minimumPositiveGravityAngle, 1f, 89f);
        }

        float angleRadians = angleDegrees * Mathf.Deg2Rad;
        float cosTheta = Mathf.Cos(angleRadians);
        float sinTheta = Mathf.Sin(angleRadians);
        float horizontalSpeed = speed * cosTheta;
        if (horizontalSpeed <= 0.001f)
        {
            flightTime = delta.magnitude / speed;
            return delta.normalized * speed;
        }

        flightTime = horizontalDistance / horizontalSpeed;
        float requiredGravity = 2f * (speed * sinTheta * flightTime - verticalDistance) / Mathf.Max(0.001f, flightTime * flightTime);
        if (requiredGravity <= 0.001f || float.IsNaN(requiredGravity) || float.IsInfinity(requiredGravity))
        {
            flightTime = delta.magnitude / speed;
            return delta.normalized * speed;
        }

        projectileGravity = requiredGravity;
        Vector3 horizontalDirection = horizontal / horizontalDistance;
        return (horizontalDirection * cosTheta + Vector3.up * sinTheta) * speed;
    }

    private static void CreateVolleyShot(
        ProjectileSecondaryBehavior behavior,
        ProjectileLaunchData launchData,
        Vector3 originPoint,
        Vector3 targetPoint,
        Vector3 horizontalForward,
        Vector3 horizontalRight,
        float gravity,
        int projectileIndex,
        int projectileCount,
        float volleyAngleOffset,
        out Vector3 spawnPoint,
        out Vector3 impactPoint,
        out Vector3 launchVelocity,
        out float projectileGravity,
        out float flightTime)
    {
        Vector2 offset = ResolveVolleyScatterOffset(behavior.VolleyRadius, projectileIndex, projectileCount, volleyAngleOffset);
        impactPoint = targetPoint + horizontalRight * offset.x + horizontalForward * offset.y;
        spawnPoint = originPoint;
        float arcAngle = ResolveVolleyArcAngle(behavior, spawnPoint, impactPoint);
        float speed = ResolveProjectileSpeed(launchData) * Mathf.Max(0.01f, behavior.ProjectileSpeedFactor);
        launchVelocity = CalculateGravityAdjustedBallisticVelocity(spawnPoint, impactPoint, gravity, arcAngle, speed, out projectileGravity, out flightTime);
    }

    private static float ResolveVolleyArcAngle(ProjectileSecondaryBehavior behavior, Vector3 spawnPoint, Vector3 impactPoint)
    {
        Vector3 horizontalOffset = impactPoint - spawnPoint;
        horizontalOffset.y = 0f;
        float distanceFactor = Mathf.Clamp01(horizontalOffset.magnitude / Mathf.Max(1f, behavior.VolleyMaxRange));
        return Mathf.Lerp(behavior.VolleyArcAngleMax, behavior.VolleyArcAngleMin, distanceFactor);
    }

    private static void SpawnVolleyProjectile(Attack attack, ProjectileLaunchData launchData, Vector3 spawnPoint, Vector3 launchVelocity, float projectileGravity, float flightTime)
    {
        float speed = launchVelocity.magnitude;
        Vector3 direction = speed > 0.001f ? launchVelocity / speed : attack.m_character.transform.forward;
        SpawnProjectileObject(attack, launchData, spawnPoint, direction, speed, setLastProjectile: true, out Projectile? projectile);
        if (projectile != null)
        {
            projectile.m_gravity = Mathf.Max(0f, projectileGravity);
            projectile.m_ttl = Mathf.Max(projectile.m_ttl, flightTime + 0.5f);
        }
    }

    private static Vector2 ResolveVolleyScatterOffset(float radius, int projectileIndex, int projectileCount, float volleyAngleOffset)
    {
        if (radius <= 0.001f)
        {
            return Vector2.zero;
        }

        if (projectileCount <= 1)
        {
            return UnityEngine.Random.insideUnitCircle * radius;
        }

        float count = Mathf.Max(1, projectileCount);
        float angle = (volleyAngleOffset + projectileIndex * 137.50776f) * Mathf.Deg2Rad;
        float radialBand = Mathf.Clamp01((projectileIndex + 0.5f) / count);
        float distance = Mathf.Sqrt(radialBand) * radius;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
    }

    internal static float ResolveProjectileSpeed(ProjectileLaunchData launchData)
    {
        float speed = launchData.UseRandomVelocity
            ? UnityEngine.Random.Range(launchData.ProjectileVelocityMin, launchData.ProjectileVelocity)
            : launchData.ProjectileVelocity;
        return Mathf.Max(0.01f, speed);
    }

    private static Character? FindSentinelTarget(Character owner, Vector3 origin, float detectionRange)
    {
        Character? closestTarget = null;
        float detectionRangeSqr = Mathf.Max(0f, detectionRange);
        detectionRangeSqr *= detectionRangeSqr;
        float closestDistanceSqr = detectionRangeSqr;
        foreach (Character candidate in Character.GetAllCharacters())
        {
            if (candidate == null || candidate == owner || candidate.IsDead())
            {
                continue;
            }

            if (candidate.GetBaseAI() is not MonsterAI)
            {
                continue;
            }

            if (!BaseAI.IsEnemy(owner, candidate))
            {
                continue;
            }

            float distanceSqr = (candidate.GetCenterPoint() - origin).sqrMagnitude;
            if (distanceSqr > closestDistanceSqr)
            {
                continue;
            }

            closestDistanceSqr = distanceSqr;
            closestTarget = candidate;
        }

        return closestTarget;
    }

    private static bool IsValidSentinelTarget(Character owner, Character? candidate, Vector3 origin, float detectionRange)
    {
        if (candidate == null || candidate == owner || candidate.IsDead())
        {
            return false;
        }

        if (candidate.GetBaseAI() is not MonsterAI)
        {
            return false;
        }

        if (!BaseAI.IsEnemy(owner, candidate))
        {
            return false;
        }

        float detectionRangeSqr = Mathf.Max(0f, detectionRange);
        detectionRangeSqr *= detectionRangeSqr;
        return (candidate.GetCenterPoint() - origin).sqrMagnitude <= detectionRangeSqr;
    }

    internal static Vector3 GetSentinelForward(Character owner)
    {
        Vector3 forward = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = owner.transform.forward;
        }

        if (forward.sqrMagnitude < 0.001f)
        {
            forward = Vector3.forward;
        }

        return forward.normalized;
    }

    private static Vector3 GetSentinelHoverPosition(Character owner, SecondaryAttackDefinition definition, int index, int count, float time)
    {
        ProjectileSecondaryBehavior? projectileBehavior = definition.Behavior as ProjectileSecondaryBehavior;
        if (projectileBehavior == null)
        {
            return owner.transform.position;
        }

        Vector3 forward = GetSentinelForward(owner);
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        if (right.sqrMagnitude < 0.001f)
        {
            right = owner.transform.right;
        }

        right.Normalize();
        float hoverElevationRadians = Mathf.Clamp(projectileBehavior.SentinelHoverElevationAngle, 0f, 90f) * Mathf.Deg2Rad;
        Vector3 hoverAxis = -forward * Mathf.Cos(hoverElevationRadians) + Vector3.up * Mathf.Sin(hoverElevationRadians);
        if (hoverAxis.sqrMagnitude < 0.001f)
        {
            hoverAxis = -forward;
        }

        hoverAxis.Normalize();
        Vector3 orbitAxis = Vector3.Cross(right, hoverAxis);
        if (orbitAxis.sqrMagnitude < 0.001f)
        {
            orbitAxis = Vector3.up;
        }

        orbitAxis.Normalize();
        Vector3 anchor = owner.transform.position + hoverAxis * projectileBehavior.SentinelHoverDistance + Vector3.up * projectileBehavior.SentinelHoverHeight;
        if (projectileBehavior.SentinelOrbitRadius <= 0f || count <= 1)
        {
            return anchor;
        }

        float angleRadians = (time * projectileBehavior.SentinelOrbitSpeed + index * (360f / count)) * Mathf.Deg2Rad;
        Vector3 orbitOffset = right * Mathf.Cos(angleRadians) * projectileBehavior.SentinelOrbitRadius
                              + orbitAxis * Mathf.Sin(angleRadians) * projectileBehavior.SentinelOrbitRadius;
        return anchor + orbitOffset;
    }

    private static void SetCollidersEnabled(Collider[] colliders, bool enabled)
    {
        foreach (Collider collider in colliders)
        {
            if (collider != null)
            {
                collider.enabled = enabled;
            }
        }
    }

    internal static void DestroyProjectileObject(GameObject? projectileObject)
    {
        if (projectileObject == null)
        {
            return;
        }

        if (projectileObject.GetComponent<ZNetView>() != null && ZNetScene.instance != null)
        {
            ZNetScene.instance.Destroy(projectileObject);
            return;
        }

        Object.Destroy(projectileObject);
    }

    private static void DumpRuntimeLaunchProfile(Attack attack, ProjectileLaunchData launchData)
    {
        string weaponPrefabName = attack.m_weapon?.m_dropPrefab?.name ?? string.Empty;
        if (!SecondaryAttackManager.ShouldDumpRuntimeProfileForProjectileRuntime(weaponPrefabName))
        {
            return;
        }

        string key = "launch|" + weaponPrefabName;
        if (!SecondaryAttackManager.TryMarkRuntimeDumpReported(key))
        {
            return;
        }

        Projectile? projectile = launchData.ProjectilePrefab?.GetComponent<Projectile>();
        SecondaryAttacksPlugin.ModLogger.LogInfo(
            "[RuntimeDump] "
            + weaponPrefabName
            + " launch"
            + $" payload={launchData.ProjectilePrefab?.name ?? "<null>"}"
            + $" speed={launchData.ProjectileVelocity}"
            + $" speedMin={launchData.ProjectileVelocityMin}"
            + $" accuracy={launchData.ProjectileAccuracy}"
            + $" accuracyMin={launchData.ProjectileAccuracyMin}"
            + $" useRandomVelocity={launchData.UseRandomVelocity}"
            + $" damageFactor={launchData.DamageFactor}"
            + $" configuredDamageFactor={launchData.ConfiguredDamageFactor}"
            + $" configuredSkillRaiseFactor={launchData.ConfiguredSkillRaiseFactor}"
            + $" configuredAdrenalineFactor={launchData.ConfiguredAdrenalineFactor}"
            + $" hitNoise={launchData.AttackHitNoise}"
            + $" gravity={projectile?.m_gravity ?? 0f}"
            + $" drag={projectile?.m_drag ?? 0f}"
            + $" ttl={projectile?.m_ttl ?? 0f}"
            + $" rayRadius={projectile?.m_rayRadius ?? 0f}"
            + $" aoe={projectile?.m_aoe ?? 0f}");
    }

    internal readonly struct ProjectileLaunchData
    {
        public static readonly ProjectileLaunchData Invalid = new(null, null, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, false);

        public ProjectileLaunchData(
            GameObject? projectilePrefab,
            ItemDrop.ItemData? ammoItem,
            float projectileVelocity,
            float projectileVelocityMin,
            float projectileAccuracy,
            float projectileAccuracyMin,
            float attackHitNoise,
            float damageFactor,
            float configuredDamageFactor,
            float configuredSkillRaiseFactor,
            float configuredAdrenalineFactor,
            bool useRandomVelocity)
        {
            ProjectilePrefab = projectilePrefab;
            AmmoItem = ammoItem;
            ProjectileVelocity = projectileVelocity;
            ProjectileVelocityMin = projectileVelocityMin;
            ProjectileAccuracy = projectileAccuracy;
            ProjectileAccuracyMin = projectileAccuracyMin;
            AttackHitNoise = attackHitNoise;
            DamageFactor = damageFactor;
            ConfiguredDamageFactor = configuredDamageFactor;
            ConfiguredSkillRaiseFactor = configuredSkillRaiseFactor;
            ConfiguredAdrenalineFactor = configuredAdrenalineFactor;
            UseRandomVelocity = useRandomVelocity;
        }

        public GameObject? ProjectilePrefab { get; }

        public ItemDrop.ItemData? AmmoItem { get; }

        public float ProjectileVelocity { get; }

        public float ProjectileVelocityMin { get; }

        public float ProjectileAccuracy { get; }

        public float ProjectileAccuracyMin { get; }

        public float AttackHitNoise { get; }

        public float DamageFactor { get; }

        public float ConfiguredDamageFactor { get; }

        public float ConfiguredSkillRaiseFactor { get; }

        public float ConfiguredAdrenalineFactor { get; }

        public bool UseRandomVelocity { get; }

        public bool IsValid => ProjectilePrefab != null;
    }

    private sealed class ScatterRicochetState
    {
        public ScatterRicochetState(
            Attack attack,
            ProjectileLaunchData launchData,
            ProjectileSecondaryBehavior behavior,
            float speed)
        {
            Attack = attack;
            LaunchData = launchData;
            Behavior = behavior;
            Speed = speed;
        }

        public Attack Attack { get; }

        public ProjectileLaunchData LaunchData { get; }

        public ProjectileSecondaryBehavior Behavior { get; }

        public float Speed { get; }
    }

    internal readonly struct ScatterRicochetDamageScope
    {
        public ScatterRicochetDamageScope(Projectile projectile, HitData.DamageTypes originalDamage)
        {
            Active = true;
            Projectile = projectile;
            OriginalDamage = originalDamage;
        }

        public bool Active { get; }

        public Projectile? Projectile { get; }

        public HitData.DamageTypes OriginalDamage { get; }
    }

    private sealed class ScatterRicochetSplitState
    {
        public ScatterRicochetSplitState(ProjectileSecondaryBehavior behavior)
        {
            Behavior = behavior;
        }

        public ProjectileSecondaryBehavior Behavior { get; }
    }

    private sealed class PiercingShotState
    {
        private readonly Dictionary<Character, float> _lastHitTimes = new();

        public PiercingShotState(ProjectileSecondaryBehavior behavior)
        {
            Behavior = behavior;
        }

        public ProjectileSecondaryBehavior Behavior { get; }

        public int HitCount { get; private set; }

        public bool IsOnHitCooldown(Character character)
        {
            return _lastHitTimes.TryGetValue(character, out float lastHitTime) &&
                   Time.time - lastHitTime < PiercingShotHitCooldown;
        }

        public void RegisterHit(Character character)
        {
            _lastHitTimes[character] = Time.time;
            HitCount++;
        }
    }

    private enum ScheduledBurstMode
    {
        Barrage,
        Volley,
        Meteor,
        Spiral
    }

    private sealed class BurstFireController : MonoBehaviour
    {
        private Attack _attack = null!;
        private SecondaryAttackDefinition _definition = null!;
        private Character? _owner;
        private float _interval;
        private float _nextShotAt;
        private int _remainingShots;
        private bool _reloadConsumed;
        private bool _registeredAsyncWork;

        public void Initialize(Attack attack, SecondaryAttackDefinition definition, int remainingShots)
        {
            _attack = attack;
            _definition = definition;
            _owner = attack.m_character;
            _remainingShots = Mathf.Max(0, remainingShots);
            _interval = definition.Behavior is ProjectileSecondaryBehavior projectileBehavior
                ? Mathf.Max(0.01f, projectileBehavior.Interval)
                : 0.2f;
            _nextShotAt = Time.time + _interval;
            ActiveBurstFireControllers.Add(_attack);
            SecondaryAttackManager.RegisterAsyncSecondaryWork(_owner);
            _registeredAsyncWork = true;
        }

        private void OnDestroy()
        {
            ConsumeReloadIfNeeded();
            if (_attack != null)
            {
                ActiveBurstFireControllers.Remove(_attack);
            }

            if (_registeredAsyncWork)
            {
                SecondaryAttackManager.UnregisterAsyncSecondaryWork(_owner);
                _registeredAsyncWork = false;
            }
        }

        private void Update()
        {
            if (_remainingShots <= 0)
            {
                Destroy(gameObject);
                return;
            }

            if (!CanContinue())
            {
                Destroy(gameObject);
                return;
            }

            if (Time.time < _nextShotAt)
            {
                return;
            }

            _nextShotAt = Time.time + _interval;
            _remainingShots--;
            ReplayFireAnimation();
            FireSingleBurstFireShot(_attack, _definition);
            if (_remainingShots <= 0)
            {
                Destroy(gameObject);
            }
        }

        private bool CanContinue()
        {
            if (_attack == null ||
                _owner == null ||
                _owner.IsDead() ||
                _owner.IsStaggering() ||
                _attack.m_weapon == null)
            {
                return false;
            }

            return _owner is not Humanoid humanoid || humanoid.GetCurrentWeapon() == _attack.m_weapon;
        }

        private void ReplayFireAnimation()
        {
            if (_owner == null || string.IsNullOrWhiteSpace(_attack.m_attackAnimation))
            {
                return;
            }

            _owner.GetZAnim()?.SetTrigger(_attack.m_attackAnimation);
        }

        private void ConsumeReloadIfNeeded()
        {
            if (_reloadConsumed || _attack == null)
            {
                return;
            }

            _reloadConsumed = true;
            ClearDeferredBurstFireReloadReset(_attack);
            if (_attack.m_character == null || !_attack.m_requiresReload)
            {
                return;
            }

            bool reloadState = SecondaryAttackManager.BeginReloadStateConsumption(_attack);
            _attack.m_character.ResetLoadedWeapon();
            SecondaryAttackManager.EndReloadStateConsumption(_attack, reloadState);
        }
    }

    private sealed class ScheduledProjectileBurstController : MonoBehaviour
    {
        private Attack _attack = null!;
        private Character? _owner;
        private ProjectileLaunchData _launchData;
        private SecondaryAttackDefinition _definition = null!;
        private ProjectileSecondaryBehavior _behavior = null!;
        private ScheduledBurstMode _mode;
        private Vector3 _originPoint;
        private Vector3 _aimDirection;
        private Vector3 _targetPoint;
        private Vector3 _horizontalForward;
        private Vector3 _horizontalRight;
        private float _volleyAngleOffset;
        private float _nextFireAt;
        private int _emittedCount;
        private bool _registeredAsyncWork;

        public void Initialize(
            Attack attack,
            ProjectileLaunchData launchData,
            SecondaryAttackDefinition definition,
            ScheduledBurstMode mode,
            Vector3 originPoint,
            Vector3 aimDirection,
            Vector3 targetPoint,
            Vector3 horizontalForward,
            Vector3 horizontalRight)
        {
            _attack = attack;
            _owner = attack.m_character;
            _launchData = launchData;
            _definition = definition;
            _behavior = (ProjectileSecondaryBehavior)definition.Behavior;
            _mode = mode;
            _originPoint = originPoint;
            _aimDirection = aimDirection;
            _targetPoint = targetPoint;
            _horizontalForward = horizontalForward;
            _horizontalRight = horizontalRight;
            _volleyAngleOffset = UnityEngine.Random.value * 360f;
            _nextFireAt = Time.time;
            SecondaryAttackManager.RegisterAsyncSecondaryWork(_owner);
            _registeredAsyncWork = true;
        }

        private void OnDestroy()
        {
            if (_registeredAsyncWork)
            {
                SecondaryAttackManager.UnregisterAsyncSecondaryWork(_owner);
                _registeredAsyncWork = false;
            }
        }

        private void Update()
        {
            if (_attack == null || _attack.m_character == null || _attack.m_character.IsDead())
            {
                Destroy(gameObject);
                return;
            }

            while (_emittedCount < _behavior.ProjectileCount && Time.time >= _nextFireAt)
            {
                EmitShot(_emittedCount);
                _emittedCount++;
                _nextFireAt += _behavior.Interval;
            }

            if (_emittedCount >= _behavior.ProjectileCount)
            {
                Destroy(gameObject);
            }
        }

        private void EmitShot(int shotIndex)
        {
            switch (_mode)
            {
                case ScheduledBurstMode.Barrage:
                    EmitBarrageShot(shotIndex);
                    break;
                case ScheduledBurstMode.Volley:
                    EmitVolleyShot(shotIndex);
                    break;
                case ScheduledBurstMode.Meteor:
                    EmitMeteorShot();
                    break;
                case ScheduledBurstMode.Spiral:
                    EmitSpiralBurstShot(shotIndex);
                    break;
            }
        }

        private void EmitVolleyShot(int shotIndex)
        {
            float gravity = GetProjectileGravity(_launchData.ProjectilePrefab!);
            CreateVolleyShot(_behavior, _launchData, _originPoint, _targetPoint, _horizontalForward, _horizontalRight, gravity, shotIndex, _behavior.ProjectileCount, _volleyAngleOffset, out Vector3 spawnPoint, out _, out Vector3 launchVelocity, out float projectileGravity, out float flightTime);
            SpawnVolleyProjectile(_attack, _launchData, spawnPoint, launchVelocity, projectileGravity, flightTime);
        }

        private void EmitMeteorShot()
        {
            SpawnMeteor(_attack, _launchData, _behavior, _targetPoint, _horizontalForward, _horizontalRight);
        }

        private void EmitSpiralBurstShot(int shotIndex)
        {
            SpawnSpiralBurstProjectile(
                _attack,
                _launchData,
                _behavior,
                _originPoint,
                _aimDirection,
                _horizontalRight,
                _horizontalForward,
                shotIndex,
                _behavior.ProjectileCount);
        }

        private void EmitBarrageShot(int shotIndex)
        {
            SpawnBarrageShot(_attack, _launchData, _behavior, _originPoint, _aimDirection, _horizontalRight, shotIndex);
        }
    }

    private sealed class SentinelController : MonoBehaviour
    {
        private Character _owner = null!;
        private Attack _attack = null!;
        private ItemDrop.ItemData _weapon = null!;
        private ItemDrop.ItemData? _ammo;
        private Projectile _projectile = null!;
        private SecondaryAttackDefinition _definition = null!;
        private ProjectileSecondaryBehavior _behavior = null!;
        private Collider[] _colliders = Array.Empty<Collider>();
        private Rigidbody[] _rigidbodies = Array.Empty<Rigidbody>();
        private bool[] _originalKinematicStates = Array.Empty<bool>();
        private HitData _hitData = new();
        private float _hitNoise;
        private float _launchSpeed;
        private float _expireAt;
        private float _attackReadyAt;
        private float _nextTargetScanAt;
        private Character? _cachedTarget;
        private int _index;
        private int _count;
        private bool _released;
        private bool _registeredAsyncWork;

        public void Initialize(
            Attack attack,
            Character owner,
            ItemDrop.ItemData weapon,
            ItemDrop.ItemData? ammo,
            Projectile projectile,
            float hitNoise,
            HitData hitData,
            float launchSpeed,
            SecondaryAttackDefinition definition,
            int index,
            int count)
        {
            _attack = attack;
            _owner = owner;
            _weapon = weapon;
            _ammo = ammo;
            _projectile = projectile;
            _definition = definition;
            _behavior = (ProjectileSecondaryBehavior)definition.Behavior;
            _hitNoise = hitNoise;
            _hitData = hitData;
            _launchSpeed = launchSpeed;
            _index = index;
            _count = Mathf.Max(1, count);
            _expireAt = Time.time + _behavior.SentinelLifetime;
            _attackReadyAt = Time.time + _behavior.SentinelAttackDelay + _index * Mathf.Max(0f, _behavior.Interval);
            _nextTargetScanAt = _attackReadyAt + UnityEngine.Random.Range(0f, SentinelTargetScanJitter);
            _cachedTarget = null;
            _colliders = GetComponentsInChildren<Collider>(true);
            _rigidbodies = GetComponentsInChildren<Rigidbody>(true);
            _originalKinematicStates = new bool[_rigidbodies.Length];

            for (int rigidbodyIndex = 0; rigidbodyIndex < _rigidbodies.Length; rigidbodyIndex++)
            {
                Rigidbody rigidbody = _rigidbodies[rigidbodyIndex];
                _originalKinematicStates[rigidbodyIndex] = rigidbody.isKinematic;
                rigidbody.isKinematic = true;
                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
            }

            SetCollidersEnabled(_colliders, enabled: false);
            _projectile.enabled = false;
            transform.position = GetSentinelHoverPosition(_owner, _definition, _index, _count, Time.time);
            transform.rotation = Quaternion.LookRotation(GetSentinelForward(_owner), Vector3.up);
            SecondaryAttackManager.RegisterAsyncSecondaryWork(_owner);
            _registeredAsyncWork = true;
        }

        private void OnDestroy()
        {
            if (_registeredAsyncWork)
            {
                SecondaryAttackManager.UnregisterAsyncSecondaryWork(_owner);
                _registeredAsyncWork = false;
            }
        }

        private void FixedUpdate()
        {
            if (_released)
            {
                return;
            }

            if (_owner == null || _owner.IsDead())
            {
                DestroyProjectileObject(gameObject);
                return;
            }

            transform.position = GetSentinelHoverPosition(_owner, _definition, _index, _count, Time.time);
            transform.rotation = Quaternion.LookRotation(GetSentinelForward(_owner), Vector3.up);

            Character? target = TryAcquireTarget();
            if (target != null)
            {
                Release(target.GetCenterPoint());
                return;
            }

            if (Time.time >= _expireAt)
            {
                DestroyProjectileObject(gameObject);
            }
        }

        private Character? TryAcquireTarget()
        {
            if (Time.time < _attackReadyAt)
            {
                return null;
            }

            Vector3 origin = transform.position;
            float detectionRange = _behavior.SentinelDetectionRange;
            if (IsValidSentinelTarget(_owner, _cachedTarget, origin, detectionRange))
            {
                return _cachedTarget;
            }

            _cachedTarget = null;
            if (Time.time < _nextTargetScanAt)
            {
                return null;
            }

            _nextTargetScanAt = Time.time + SentinelTargetScanInterval + UnityEngine.Random.Range(0f, SentinelTargetScanJitter);
            _cachedTarget = FindSentinelTarget(_owner, origin, detectionRange);
            return _cachedTarget;
        }

        private void Release(Vector3 targetPoint)
        {
            Vector3 direction = targetPoint - transform.position;
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = GetSentinelForward(_owner);
            }

            direction.Normalize();
            SetCollidersEnabled(_colliders, enabled: true);
            for (int rigidbodyIndex = 0; rigidbodyIndex < _rigidbodies.Length; rigidbodyIndex++)
            {
                Rigidbody rigidbody = _rigidbodies[rigidbodyIndex];
                rigidbody.isKinematic = _originalKinematicStates[rigidbodyIndex];
                rigidbody.linearVelocity = Vector3.zero;
                rigidbody.angularVelocity = Vector3.zero;
            }

            transform.rotation = Quaternion.LookRotation(direction, Vector3.up);
            _projectile.enabled = true;
            _projectile.Setup(_owner, direction * _launchSpeed, _hitNoise, _hitData, _weapon, _ammo);
            SecondaryAttackAdrenalineSystem.ApplyProjectileFactor(_projectile, _attack, _behavior.AdrenalineFactor);
            SecondaryAttackRuntimeFacade.SetProjectileAttackAttribution(
                _projectile,
                _definition.PrefabName,
                secondaryAttack: true,
                _definition,
                disableCurrentAttackFallback: false);
            _released = true;
            Destroy(this);
        }
    }

}

