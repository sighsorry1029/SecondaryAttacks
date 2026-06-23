using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class CleavingThrustSystem
{
    private const float FanStepDegrees = 4f;
    private const float TrailRangeScaleFactor = 3f;
    private static readonly List<CleavingThrustHitTarget> HitTargets = new();
    private static int _environmentMask;
    private static int _destructibleMask;

    internal static bool CanHandle(Attack attack)
    {
        return attack != null &&
               attack.m_character != null &&
               attack.m_weapon?.m_shared != null &&
               attack.m_attackType == Attack.AttackType.Horizontal &&
               attack.m_attackProjectile == null &&
               attack.m_attackRange > 0f &&
               attack.m_attackRayWidth > 0f &&
               string.Equals(attack.m_attackAnimation, "greatsword_secondary", System.StringComparison.OrdinalIgnoreCase);
    }

    internal static void Trigger(Attack attack, SecondaryAttackDefinition definition)
    {
        if (!CanHandle(attack) || definition.CleavingThrust == null)
        {
            return;
        }

        CleavingThrustDefinition cleavingThrust = definition.CleavingThrust;
        Character attacker = attack.m_character;
        Vector3 origin = ResolveOrigin(attack);
        Vector3 forward = ResolveForward(attacker, origin);
        SecondaryAttackManager.PlayTriggeredAttackEffects(attack, definition.CleavingThrust?.DurabilityFactor ?? definition.DurabilityFactor);
        GatherTargets(attack, cleavingThrust, origin, forward);

        if (HitTargets.Count == 0)
        {
            return;
        }

        for (int i = 0; i < HitTargets.Count; i++)
        {
            ApplyHit(attack, cleavingThrust, HitTargets[i], HitTargets.Count);
        }
    }

    internal static float ResolveVisualRangeScale(Attack attack, SecondaryAttackDefinition definition)
    {
        if (!CanHandle(attack) || definition.CleavingThrust == null || attack.m_attackRange <= 0.01f)
        {
            return 1f;
        }

        float rangeFactor = Mathf.Max(1f, definition.CleavingThrust.RangeFactor);
        return Mathf.Max(1f, 1f + (rangeFactor - 1f) * TrailRangeScaleFactor);
    }

    private static Vector3 ResolveOrigin(Attack attack)
    {
        Transform attackerTransform = attack.m_character.transform;
        return attackerTransform.position +
               Vector3.up * Mathf.Max(0f, attack.m_attackHeight) +
               attackerTransform.right * attack.m_attackOffset;
    }

    private static Vector3 ResolveForward(Character attacker, Vector3 origin)
    {
        Vector3 forward = attacker.transform.forward;
        Vector3 horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return horizontalForward.sqrMagnitude > 0.001f ? horizontalForward.normalized : attacker.transform.forward;
    }

    private static void GatherTargets(Attack attack, CleavingThrustDefinition cleavingThrust, Vector3 origin, Vector3 forward)
    {
        HitTargets.Clear();
        Character attacker = attack.m_character;
        CleavingThrustAttackShape shape = ResolveAttackShape(attack, cleavingThrust);

        foreach (Character candidate in Character.GetAllCharacters())
        {
            if (!TryResolveTarget(attack, candidate, origin, forward, shape, hitThroughWalls: false, out CleavingThrustHitTarget target))
            {
                continue;
            }

            HitTargets.Add(target);
        }

        GatherDestructibleTargets(attack, origin, forward, shape, hitThroughWalls: false);
        HitTargets.Sort((left, right) => left.Distance.CompareTo(right.Distance));
    }

    private static CleavingThrustAttackShape ResolveAttackShape(Attack attack, CleavingThrustDefinition cleavingThrust)
    {
        float rayWidth = Mathf.Max(0.01f, attack.m_attackRayWidth);
        float characterRayWidth = Mathf.Max(rayWidth, rayWidth + Mathf.Max(0f, attack.m_attackRayWidthCharExtra));
        return new CleavingThrustAttackShape(
            Mathf.Max(0.1f, attack.m_attackRange * cleavingThrust.RangeFactor),
            Mathf.Clamp(attack.m_attackAngle, 1f, 360f),
            rayWidth,
            characterRayWidth);
    }

    private static void GatherDestructibleTargets(
        Attack attack,
        Vector3 origin,
        Vector3 forward,
        CleavingThrustAttackShape shape,
        bool hitThroughWalls)
    {
        HashSet<IDestructible> hitDestructibles = new();
        foreach (CleavingThrustHitTarget existingTarget in HitTargets)
        {
            hitDestructibles.Add(existingTarget.Destructible);
        }

        Collider[] colliders = Physics.OverlapSphere(
            origin,
            shape.Range + shape.RayWidth,
            GetDestructibleMask(attack),
            QueryTriggerInteraction.Ignore);
        foreach (Collider collider in colliders)
        {
            if (collider == null)
            {
                continue;
            }

            IDestructible? destructible = ResolveDestructible(collider);
            if (destructible == null ||
                destructible is Character ||
                !hitDestructibles.Add(destructible) ||
                !IsValidDestructibleTarget(destructible) ||
                destructible is not MonoBehaviour destructibleBehaviour)
            {
                continue;
            }

            Vector3 point = ResolveDestructiblePoint(collider, destructibleBehaviour.transform.position, origin);
            if (!TryResolveAttackShapePoint(origin, forward, point, shape, useCharacterWidth: false, out float distance))
            {
                continue;
            }

            if (!hitThroughWalls && IsBlockedByEnvironment(origin, point, destructible))
            {
                continue;
            }

            HitTargets.Add(new CleavingThrustHitTarget(destructible, null, collider, point, distance));
        }
    }

    private static bool TryResolveTarget(
        Attack attack,
        Character? candidate,
        Vector3 origin,
        Vector3 forward,
        CleavingThrustAttackShape shape,
        bool hitThroughWalls,
        out CleavingThrustHitTarget target)
    {
        target = default;
        Character attacker = attack.m_character;
        if (candidate == null || candidate == attacker || candidate.IsDead())
        {
            return false;
        }

        if (!IsValidTarget(attack, candidate))
        {
            return false;
        }

        Vector3 point = candidate.GetCenterPoint();
        if (!TryResolveAttackShapePoint(origin, forward, point, shape, useCharacterWidth: true, out float distance))
        {
            return false;
        }

        if (!hitThroughWalls && IsBlockedByEnvironment(origin, point, null))
        {
            return false;
        }

        Collider? hitCollider = candidate.GetComponentInChildren<Collider>();
        target = new CleavingThrustHitTarget(candidate, candidate, hitCollider, point, distance);
        return true;
    }

    private static bool TryResolveAttackShapePoint(
        Vector3 origin,
        Vector3 forward,
        Vector3 point,
        CleavingThrustAttackShape shape,
        bool useCharacterWidth,
        out float distance)
    {
        distance = 0f;
        Vector3 toTarget = point - origin;
        Vector3 horizontal = Vector3.ProjectOnPlane(toTarget, Vector3.up);
        float rayWidth = useCharacterWidth ? shape.CharacterRayWidth : shape.RayWidth;
        float maxDistance = shape.Range + rayWidth;
        float horizontalSqrMagnitude = horizontal.sqrMagnitude;
        if (horizontalSqrMagnitude > maxDistance * maxDistance)
        {
            return false;
        }

        distance = Mathf.Sqrt(horizontalSqrMagnitude);
        if (toTarget.sqrMagnitude <= rayWidth * rayWidth)
        {
            return true;
        }

        float rayWidthSq = rayWidth * rayWidth;
        int steps = Mathf.Max(1, Mathf.CeilToInt(shape.Angle / FanStepDegrees));
        float halfAngle = shape.Angle * 0.5f;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 direction = (Quaternion.AngleAxis(angle, Vector3.up) * forward).normalized;
            float projectedDistance = Vector3.Dot(horizontal, direction);
            if (projectedDistance < -rayWidth || projectedDistance > shape.Range + rayWidth)
            {
                continue;
            }

            Vector3 closestPointOnRay = direction * Mathf.Clamp(projectedDistance, 0f, shape.Range);
            if ((toTarget - closestPointOnRay).sqrMagnitude <= rayWidthSq)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsValidTarget(Attack attack, Character target)
    {
        Character attacker = attack.m_character;
        bool isEnemy = BaseAI.IsEnemy(attacker, target) ||
                       (target.GetBaseAI() != null && target.GetBaseAI().IsAggravatable() && attacker.IsPlayer());
        if (((!attack.m_hitFriendly || attacker.IsTamed()) && !attacker.IsPlayer() && !isEnemy) ||
            (!attack.m_weapon.m_shared.m_tamedOnly && attacker.IsPlayer() && !attacker.IsPVPEnabled() && !isEnemy) ||
            (attack.m_weapon.m_shared.m_tamedOnly && !target.IsTamed()))
        {
            return false;
        }

        if (attack.m_weapon.m_shared.m_dodgeable && target.IsDodgeInvincible())
        {
            if (target.IsPlayer())
            {
                (target as Player)?.HitWhileDodging();
            }

            return false;
        }

        return true;
    }

    private static bool IsValidDestructibleTarget(IDestructible destructible)
    {
        DestructibleType type = destructible.GetDestructibleType();
        return type != DestructibleType.None && type != DestructibleType.Character;
    }

    private static bool IsBlockedByEnvironment(Vector3 origin, Vector3 targetPoint, IDestructible? allowedTarget)
    {
        if (_environmentMask == 0)
        {
            _environmentMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain");
        }

        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(origin, direction / distance, distance, _environmentMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
            {
                continue;
            }

            if (allowedTarget != null && ResolveDestructible(hit.collider) == allowedTarget)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static int GetDestructibleMask(Attack attack)
    {
        if (Attack.m_attackMask != 0)
        {
            return Attack.m_attackMask;
        }

        if (_destructibleMask == 0)
        {
            _destructibleMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "vehicle");
        }

        return _destructibleMask;
    }

    private static IDestructible? ResolveDestructible(Collider collider)
    {
        GameObject hitObject = Projectile.FindHitObject(collider);
        return hitObject != null ? hitObject.GetComponent<IDestructible>() : null;
    }

    private static Vector3 ResolveDestructiblePoint(Collider collider, Vector3 fallbackPoint, Vector3 origin)
    {
        Vector3 point = SecondaryAttackManager.ResolveSafeClosestPoint(collider, origin);
        if ((point - origin).sqrMagnitude < 0.0001f)
        {
            point = collider.bounds.center;
        }

        return point.sqrMagnitude > 0f ? point : fallbackPoint;
    }

    private static void ApplyHit(Attack attack, CleavingThrustDefinition cleavingThrust, CleavingThrustHitTarget target, int hitCount)
    {
        Character attacker = attack.m_character;
        ItemDrop.ItemData weapon = attack.m_weapon;
        Skills.SkillType skillType = weapon.m_shared.m_skillType;
        float skillFactor = attacker.GetRandomSkillFactor(skillType);
        if (attack.m_multiHit && attack.m_lowerDamagePerHit && hitCount > 1)
        {
            skillFactor /= hitCount * 0.75f;
        }

        HitData hitData = SecondaryAttackHitDataFactory.CreateMeleeHit(
            attack,
            target.Collider!,
            target.Point,
            ResolveHitDirection(attack, target.Point),
            skillFactor,
            cleavingThrust.DamageFactor,
            cleavingThrust.PushFactor,
            attack.m_raiseSkillAmount);
        attacker.GetSEMan().ModifyAttack(skillType, ref hitData);
        weapon.m_shared.m_hitEffect.Create(target.Point, Quaternion.identity);
        attack.m_hitEffect.Create(target.Point, Quaternion.identity);
        if (target.Character != null)
        {
            TrySpawnOnHit(attack, target.Character);
        }

        target.Destructible.Damage(hitData);

        if (target.Character != null && attack.m_attackHealthReturnHit > 0f)
        {
            attacker.Heal(attack.m_attackHealthReturnHit);
        }

        if (target.Character != null)
        {
            SecondaryAttackAdrenalineSystem.TryGrantOnce(attack, target.Character, 1f, "cleavingThrust");
        }
    }

    private static Vector3 ResolveHitDirection(Attack attack, Vector3 hitPoint)
    {
        Vector3 direction = hitPoint - ResolveOrigin(attack);
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = attack.m_character.transform.forward;
        }

        return direction.normalized;
    }

    private static void TrySpawnOnHit(Attack attack, Character target)
    {
        if (attack.m_spawnOnHitChance <= 0f ||
            attack.m_spawnOnHit == null ||
            Random.Range(0f, 1f) >= attack.m_spawnOnHitChance)
        {
            return;
        }

        GameObject spawned = Object.Instantiate(attack.m_spawnOnHit, target.transform.position, target.transform.rotation);
        spawned.GetComponentInChildren<IProjectile>()?.Setup(
            attack.m_character,
            attack.m_character.transform.forward,
            -1f,
            null,
            attack.m_weapon,
            attack.m_lastUsedAmmo);
    }

    private readonly struct CleavingThrustHitTarget
    {
        public CleavingThrustHitTarget(
            IDestructible destructible,
            Character? character,
            Collider? collider,
            Vector3 point,
            float distance)
        {
            Destructible = destructible;
            Character = character;
            Collider = collider;
            Point = point;
            Distance = distance;
        }

        public IDestructible Destructible { get; }

        public Character? Character { get; }

        public Collider? Collider { get; }

        public Vector3 Point { get; }

        public float Distance { get; }
    }

    private readonly struct CleavingThrustAttackShape
    {
        public CleavingThrustAttackShape(float range, float angle, float rayWidth, float characterRayWidth)
        {
            Range = range;
            Angle = angle;
            RayWidth = rayWidth;
            CharacterRayWidth = characterRayWidth;
        }

        public float Range { get; }

        public float Angle { get; }

        public float RayWidth { get; }

        public float CharacterRayWidth { get; }
    }
}
