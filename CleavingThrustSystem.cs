using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class CleavingThrustSystem
{
    private const float FanStepDegrees = 4f;
    private const float TrailRangeScaleFactor = 3f;
    private const int MaxRetainedCharacterHits = 1024;
    private static readonly List<CleavingThrustHitTarget> HitTargets = new();
    private static readonly HashSet<Character> CheckedCharacters = new();
    private static Collider[] CharacterHits = new Collider[64];
    private static int _environmentMask;
    private static int _destructibleMask;
    private static int _characterMask;
    private delegate void MeleeAttackDirection(Attack attack, out Transform originJoint, out Vector3 direction);
    // Original game method is private. Cache an explicit accessor rather than
    // relying on the compile-time publicizer for a new runtime access path.
    private static readonly MeleeAttackDirection GetMeleeAttackDirection =
        AccessTools.MethodDelegate<MeleeAttackDirection>(AccessTools.DeclaredMethod(
            typeof(Attack), "GetMeleeAttackDir", new[] { typeof(Transform).MakeByRefType(), typeof(Vector3).MakeByRefType() }));

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
        Vector3 forward = ResolveForward(attacker);
        SecondaryAttackManager.PlayTriggeredAttackEffects(attack, definition.CleavingThrust?.DurabilityFactor ?? definition.DurabilityFactor);
        try
        {
            GatherTargets(attack, cleavingThrust, origin, forward);
            int targetCount = HitTargets.Count;
            bool hitCharacter = false;
            for (int i = 0; i < targetCount; i++)
            {
                CleavingThrustHitTarget target = HitTargets[i];
                bool fullCharacterDamage = target.Character != null && !hitCharacter;
                hitCharacter |= target.Character != null;
                ApplyHit(attack, cleavingThrust, target, targetCount, fullCharacterDamage);
            }
        }
        finally
        {
            HitTargets.Clear();
            CheckedCharacters.Clear();
            System.Array.Clear(CharacterHits, 0, CharacterHits.Length);
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

    private static Vector3 ResolveForward(Character attacker)
    {
        Vector3 forward = attacker.transform.forward;
        Vector3 horizontalForward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return horizontalForward.sqrMagnitude > 0.001f ? horizontalForward.normalized : attacker.transform.forward;
    }

    private static void GatherTargets(Attack attack, CleavingThrustDefinition cleavingThrust, Vector3 origin, Vector3 forward)
    {
        HitTargets.Clear();
        CleavingThrustAttackShape shape = ResolveAttackShape(attack, cleavingThrust);
        GatherCharacterTargets(attack, shape);
        GatherDestructibleTargets(attack, origin, forward, shape);
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
        CleavingThrustAttackShape shape)
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

            if (IsBlockedByEnvironment(origin, point, destructible))
            {
                continue;
            }

            HitTargets.Add(new CleavingThrustHitTarget(destructible, null, collider, point, distance));
        }
    }

    private static void GatherCharacterTargets(Attack attack, CleavingThrustAttackShape shape)
    {
        CheckedCharacters.Clear();
        GetMeleeAttackDirection(attack, out Transform originJoint, out Vector3 attackDirection);
        Transform attackerTransform = attack.m_character.transform;
        Vector3 origin = originJoint.position + Vector3.up * attack.m_attackHeight +
                         attackerTransform.right * attack.m_attackOffset;
        Vector3 localDirection = attackerTransform.InverseTransformDirection(attackDirection);
        // Use the original fan directions and radius-subtracted sweep length.
        // Overlaps also cover colliders already touching the start of the thrust.
        for (float angle = -shape.Angle * 0.5f; angle <= shape.Angle * 0.5f; angle += FanStepDegrees)
        {
            Vector3 direction = attackerTransform.TransformDirection(
                Quaternion.Euler(0f, -angle, 0f) * localDirection);
            GatherCharacterSweep(attack, origin, origin, direction, shape.Range, shape.RayWidth, GetDestructibleMask(attack));
            if (attack.m_attackRayWidthCharExtra > 0f || attack.m_attackHeightChar1 != 0f)
            {
                if (_characterMask == 0)
                {
                    _characterMask = LayerMask.GetMask("character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
                }

                GatherCharacterSweep(attack, origin, origin + Vector3.up * attack.m_attackHeightChar1,
                    direction, shape.Range, shape.CharacterRayWidth, _characterMask);
                if (attack.m_attackHeightChar2 != attack.m_attackHeightChar1)
                {
                    GatherCharacterSweep(attack, origin, origin + Vector3.up * attack.m_attackHeightChar2,
                        direction, shape.Range, shape.CharacterRayWidth, _characterMask);
                }
            }
        }
    }

    private static void GatherCharacterSweep(Attack attack, Vector3 origin, Vector3 sweepOrigin,
        Vector3 direction, float range, float radius, int mask)
    {
        Vector3 end = sweepOrigin + direction * Mathf.Max(0f, range - radius);
        Collider[] colliders = CharacterHits;
        int count;
        while (true)
        {
            count = Physics.OverlapCapsuleNonAlloc(sweepOrigin, end, radius, colliders, mask, QueryTriggerInteraction.Ignore);
            if (count < colliders.Length)
            {
                break;
            }

            if (colliders.Length >= MaxRetainedCharacterHits)
            {
                // Rare crowded scenes must not silently lose targets. Keep the
                // retained buffer bounded and use a complete one-shot query.
                colliders = Physics.OverlapCapsule(sweepOrigin, end, radius, mask, QueryTriggerInteraction.Ignore);
                count = colliders.Length;
                break;
            }

            System.Array.Clear(colliders, 0, colliders.Length);
            CharacterHits = colliders = new Collider[colliders.Length * 2];
        }

        for (int i = 0; i < count; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || ResolveDestructible(collider) is not Character candidate ||
                !CheckedCharacters.Add(candidate) || candidate == attack.m_character || candidate.IsDead() ||
                !IsValidTarget(attack, candidate))
            {
                continue;
            }

            // Keep the existing center-based visibility and nearest-character
            // policy; only admission now uses the actual collider volume.
            Vector3 point = candidate.GetCenterPoint();
            if (!IsBlockedByEnvironment(origin, point, candidate))
            {
                float distance = Vector3.ProjectOnPlane(point - origin, Vector3.up).magnitude;
                HitTargets.Add(new CleavingThrustHitTarget(candidate, candidate, collider, point, distance));
            }
        }
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
        bool isEnemy = SecondaryAttackManager.IsEnemyOrAggravatableTarget(attacker, target);
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
            _destructibleMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain",
                "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
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

    private static void ApplyHit(Attack attack, CleavingThrustDefinition cleavingThrust, CleavingThrustHitTarget target, int hitCount, bool fullCharacterDamage)
    {
        Character attacker = attack.m_character;
        ItemDrop.ItemData weapon = attack.m_weapon;
        Skills.SkillType skillType = weapon.m_shared.m_skillType;
        float skillFactor = attacker.GetRandomSkillFactor(skillType);
        float penalty = attack.m_multiHit && attack.m_lowerDamagePerHit && hitCount > 1 ? hitCount * 0.75f : 1f;
        skillFactor /= penalty;

        HitData hitData = SecondaryAttackHitDataFactory.CreateMeleeHit(
            attack,
            target.Collider!,
            target.Point,
            ResolveHitDirection(attack, target.Point),
            skillFactor,
            cleavingThrust.DamageFactor,
            cleavingThrust.PushFactor,
            attack.m_raiseSkillAmount);
        if (fullCharacterDamage)
        {
            // Restore damage only. The shared skill factor also scales knockback,
            // so skipping its penalty would unintentionally increase push force.
            hitData.m_damage.Modify(penalty);
        }
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
