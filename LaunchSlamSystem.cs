using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class LaunchSlamSystem
{
    private const float MinAirTime = 0.2f;
    private const float LandingTimeout = 3f;
    private const float DefaultLandingAreaRadius = 0.75f;

    internal static bool IsApplyingLandingDamage { get; private set; }

    public static void TryApplyForSecondaryHit(
        Player attacker,
        Character target,
        bool secondaryAttack,
        SecondaryAttackDefinition? definition,
        ref HitData hit)
    {
        LaunchSlamDefinition? launchSlam = definition?.LaunchSlam;
        if (!secondaryAttack ||
            launchSlam == null ||
            attacker == null ||
            target == null ||
            target.IsDead() ||
            (Object)(object)target == (Object)(object)attacker)
        {
            return;
        }

        if (hit.m_damage.GetTotalDamage() <= 0f)
        {
            return;
        }

        if (!MeleePresetCooldownSystem.TryConsume(attacker, null, "launchSlam", launchSlam.PresetCooldown, out _))
        {
            return;
        }

        float launchHeight = Mathf.Max(0f, launchSlam.LaunchHeight);
        if (launchHeight <= 0f)
        {
            return;
        }

        HitData.DamageTypes landingDamage = hit.m_damage.Clone();
        landingDamage.Modify(Mathf.Max(0f, launchSlam.DamageFactor));
        if (landingDamage.GetTotalDamage() <= 0f || !TryLaunchTarget(target, launchHeight))
        {
            return;
        }

        hit.m_pushForce = 0f;
        LaunchSlamLandingTracker tracker = target.GetComponent<LaunchSlamLandingTracker>();
        if (tracker == null)
        {
            tracker = target.gameObject.AddComponent<LaunchSlamLandingTracker>();
        }

        tracker.Initialize(
            target,
            attacker,
            hit,
            landingDamage,
            launchHeight,
            launchSlam.LandingAreaRadiusFactor,
            launchSlam.LandingAreaRadiusMax,
            launchSlam.Vfx,
            launchSlam.VfxRotationOffset,
            launchSlam.Sfx,
            MinAirTime,
            LandingTimeout);
    }

    private static bool TryLaunchTarget(Character target, float liftHeight)
    {
        if (!SecondaryAttackManager.HasCharacterAuthority(target))
        {
            return false;
        }

        Rigidbody body = target.m_body != null ? target.m_body : target.GetComponent<Rigidbody>();
        if (body == null || body.isKinematic)
        {
            return false;
        }

        float gravity = Mathf.Max(0.1f, Mathf.Abs(Physics.gravity.y));
        float upVelocity = Mathf.Sqrt(2f * gravity * liftHeight);
        Vector3 velocity = body.linearVelocity;
        velocity.y = Mathf.Max(velocity.y, upVelocity);
        body.linearVelocity = velocity;
        target.ForceJump(velocity, effects: false);
        return true;
    }

    internal static void ApplyLandingDamage(
        Character target,
        Character attacker,
        HitData sourceHit,
        HitData.DamageTypes landingDamage,
        float landingAreaRadiusFactor,
        float landingAreaRadiusMax,
        string landingVfx,
        Vector3 landingVfxRotationOffset,
        string landingSfx)
    {
        if (target == null || attacker == null || target.IsDead() || landingDamage.GetTotalDamage() <= 0f)
        {
            return;
        }

        Vector3 impactOrigin = target.transform.position;
        float radius = ResolveLandingAreaRadius(target, landingAreaRadiusFactor, landingAreaRadiusMax);
        List<Character> targets = GatherLandingAreaTargets(target, attacker, impactOrigin, radius);
        if (targets.Count == 0)
        {
            return;
        }

        Quaternion effectRotation = SecondaryAttackNamedEffectSystem.RotationFromNormal(Vector3.up);
        Quaternion vfxRotation = effectRotation * Quaternion.Euler(landingVfxRotationOffset);
        SecondaryAttackNamedEffectSystem.Create(landingVfx, impactOrigin, vfxRotation, "launch_slam_landing_vfx_missing");
        SecondaryAttackNamedEffectSystem.Create(landingSfx, impactOrigin, effectRotation, "launch_slam_landing_sfx_missing");

        IsApplyingLandingDamage = true;
        try
        {
            foreach (Character landingTarget in targets)
            {
                ApplyLandingHit(landingTarget, attacker, sourceHit, landingDamage, impactOrigin);
            }
        }
        finally
        {
            IsApplyingLandingDamage = false;
        }
    }

    private static List<Character> GatherLandingAreaTargets(Character source, Character attacker, Vector3 impactOrigin, float radius)
    {
        List<Character> targets = new();
        HashSet<Character> seen = new();
        AddLandingAreaTarget(source, targets, seen);
        if (radius <= 0f)
        {
            return targets;
        }

        foreach (Character candidate in Character.GetAllCharacters())
        {
            if (!IsValidLandingAreaTarget(source, attacker, candidate) ||
                ResolveHorizontalDistanceToCharacter(candidate, impactOrigin) > radius)
            {
                continue;
            }

            AddLandingAreaTarget(candidate, targets, seen);
        }

        return targets;
    }

    private static bool IsValidLandingAreaTarget(Character source, Character attacker, Character? candidate)
    {
        if (candidate == null ||
            candidate.IsDead() ||
            (Object)(object)candidate == (Object)(object)source ||
            (Object)(object)candidate == (Object)(object)attacker)
        {
            return false;
        }

        return BaseAI.IsEnemy(attacker, candidate) ||
               attacker.IsPlayer() &&
               candidate.GetBaseAI() != null &&
               candidate.GetBaseAI().IsAggravatable();
    }

    private static void AddLandingAreaTarget(Character target, List<Character> targets, HashSet<Character> seen)
    {
        if (target == null || target.IsDead() || !seen.Add(target))
        {
            return;
        }

        targets.Add(target);
    }

    private static void ApplyLandingHit(
        Character target,
        Character attacker,
        HitData sourceHit,
        HitData.DamageTypes landingDamage,
        Vector3 impactOrigin)
    {
        if (target == null || attacker == null || target.IsDead())
        {
            return;
        }

        HitData landingHit = sourceHit.Clone();
        landingHit.m_damage = landingDamage.Clone();
        landingHit.m_pushForce = 0f;
        landingHit.m_staggerMultiplier = 0f;
        landingHit.m_skillRaiseAmount = 0f;
        landingHit.m_blockable = false;
        landingHit.m_dodgeable = false;
        landingHit.m_point = ResolveLandingHitPoint(target, impactOrigin);
        landingHit.m_dir = ResolveLandingHitDirection(target, impactOrigin);
        landingHit.m_hitCollider = null;
        landingHit.SetAttacker(attacker);

        target.Damage(landingHit);
    }

    private static float ResolveLandingAreaRadius(Character target, float radiusFactor, float radiusMax)
    {
        float factor = Mathf.Max(0f, radiusFactor);
        float max = Mathf.Max(0f, radiusMax);
        if (factor <= 0f || max <= 0f)
        {
            return 0f;
        }

        float footprintRadius = ResolveCharacterFootprintRadius(target);
        return Mathf.Min(max, footprintRadius * factor);
    }

    private static float ResolveCharacterFootprintRadius(Character target)
    {
        if (!TryResolveCharacterBounds(target, out Bounds bounds))
        {
            return DefaultLandingAreaRadius;
        }

        float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        return radius > 0.01f ? radius : DefaultLandingAreaRadius;
    }

    private static float ResolveHorizontalDistanceToCharacter(Character target, Vector3 origin)
    {
        float bestDistanceSquared = float.MaxValue;
        bool foundCollider = false;
        foreach (Collider collider in target.GetComponentsInChildren<Collider>())
        {
            if (!IsUsableFootprintCollider(collider))
            {
                continue;
            }

            Vector3 closestPoint = SecondaryAttackManager.ResolveSafeClosestPoint(collider, origin);
            Vector3 delta = closestPoint - origin;
            delta.y = 0f;
            float distanceSquared = delta.sqrMagnitude;
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            foundCollider = true;
        }

        if (foundCollider)
        {
            return Mathf.Sqrt(bestDistanceSquared);
        }

        Vector3 fallbackDelta = target.GetCenterPoint() - origin;
        fallbackDelta.y = 0f;
        return fallbackDelta.magnitude;
    }

    private static Vector3 ResolveLandingHitPoint(Character target, Vector3 origin)
    {
        float bestDistanceSquared = float.MaxValue;
        Vector3 bestPoint = target.GetCenterPoint();
        foreach (Collider collider in target.GetComponentsInChildren<Collider>())
        {
            if (!IsUsableFootprintCollider(collider))
            {
                continue;
            }

            Vector3 point = SecondaryAttackManager.ResolveSafeClosestPoint(collider, origin);
            float distanceSquared = (point - origin).sqrMagnitude;
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            bestPoint = point;
        }

        return bestPoint;
    }

    private static Vector3 ResolveLandingHitDirection(Character target, Vector3 origin)
    {
        Vector3 direction = target.GetCenterPoint() - origin;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.0001f)
        {
            return Vector3.down;
        }

        return direction.normalized;
    }

    private static bool TryResolveCharacterBounds(Character target, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        foreach (Collider collider in target.GetComponentsInChildren<Collider>())
        {
            if (!IsUsableFootprintCollider(collider))
            {
                continue;
            }

            Bounds colliderBounds = collider.bounds;
            if (colliderBounds.size.sqrMagnitude <= 0.0001f)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = colliderBounds;
                hasBounds = true;
                continue;
            }

            bounds.Encapsulate(colliderBounds);
        }

        return hasBounds;
    }

    private static bool IsUsableFootprintCollider(Collider collider)
    {
        return collider != null && collider.enabled && !collider.isTrigger;
    }
}

internal sealed class LaunchSlamLandingTracker : MonoBehaviour
{
    private Character? _target;
    private Character? _attacker;
    private Rigidbody? _body;
    private HitData? _sourceHit;
    private HitData.DamageTypes _landingDamage = new();
    private float _targetApexHeight;
    private float _landingAreaRadiusFactor;
    private float _landingAreaRadiusMax;
    private string _landingVfx = "";
    private Vector3 _landingVfxRotationOffset = Vector3.zero;
    private string _landingSfx = "";
    private float _minAirTime;
    private float _landingTimeout;
    private float _startTime;
    private float _startHeight;
    private bool _hasBeenAirborne;
    private bool _ignoredCharacterCollisions;
    private readonly List<CollisionIgnorePair> _ignoredCollisionPairs = new();

    public void Initialize(
        Character target,
        Character attacker,
        HitData sourceHit,
        HitData.DamageTypes landingDamage,
        float launchHeight,
        float landingAreaRadiusFactor,
        float landingAreaRadiusMax,
        string landingVfx,
        Vector3 landingVfxRotationOffset,
        string landingSfx,
        float minAirTime,
        float landingTimeout)
    {
        float newDamage = landingDamage.GetTotalDamage();
        if (_sourceHit != null && newDamage <= _landingDamage.GetTotalDamage())
        {
            return;
        }

        RestoreIgnoredCharacterCollisions();
        _target = target;
        _attacker = attacker;
        _body = target.m_body != null ? target.m_body : target.GetComponent<Rigidbody>();
        _sourceHit = sourceHit.Clone();
        _landingDamage = landingDamage.Clone();
        _targetApexHeight = target.transform.position.y + Mathf.Max(0f, launchHeight);
        _landingAreaRadiusFactor = landingAreaRadiusFactor;
        _landingAreaRadiusMax = landingAreaRadiusMax;
        _landingVfx = landingVfx.Trim();
        _landingVfxRotationOffset = landingVfxRotationOffset;
        _landingSfx = landingSfx.Trim();
        _minAirTime = minAirTime;
        _landingTimeout = landingTimeout;
        _startTime = Time.time;
        _startHeight = target.transform.position.y;
        _hasBeenAirborne = false;
        IgnoreOtherCharacterCollisions(target);
        enabled = true;
    }

    private void Update()
    {
        if (_target == null || _attacker == null || _sourceHit == null || _target.IsDead())
        {
            Destroy(this);
            return;
        }

        float elapsed = Time.time - _startTime;
        if (elapsed > _landingTimeout)
        {
            Destroy(this);
            return;
        }

        bool onGround = _target.IsOnGround();
        if (_target.transform.position.y > _startHeight + 0.15f)
        {
            _hasBeenAirborne = true;
        }

        if (_ignoredCharacterCollisions && HasAscentEnded())
        {
            RestoreIgnoredCharacterCollisions();
        }

        if (!_hasBeenAirborne || elapsed < _minAirTime || !onGround)
        {
            return;
        }

        LaunchSlamSystem.ApplyLandingDamage(
            _target,
            _attacker,
            _sourceHit,
            _landingDamage,
            _landingAreaRadiusFactor,
            _landingAreaRadiusMax,
            _landingVfx,
            _landingVfxRotationOffset,
            _landingSfx);
        Destroy(this);
    }

    private bool HasAscentEnded()
    {
        if (_target == null)
        {
            return true;
        }

        if (_target.transform.position.y >= _targetApexHeight - 0.05f)
        {
            return true;
        }

        if (_body == null)
        {
            return false;
        }

        return _hasBeenAirborne && _body.linearVelocity.y <= 0.05f;
    }

    private void IgnoreOtherCharacterCollisions(Character target)
    {
        Collider[] targetColliders = GetCollisionColliders(target);
        if (targetColliders.Length == 0)
        {
            return;
        }

        foreach (Character character in Character.GetAllCharacters())
        {
            if (character == null || character == target || character.IsDead())
            {
                continue;
            }

            Collider[] otherColliders = GetCollisionColliders(character);
            foreach (Collider targetCollider in targetColliders)
            {
                foreach (Collider otherCollider in otherColliders)
                {
                    if (targetCollider == otherCollider)
                    {
                        continue;
                    }

                    if (Physics.GetIgnoreCollision(targetCollider, otherCollider))
                    {
                        continue;
                    }

                    Physics.IgnoreCollision(targetCollider, otherCollider, ignore: true);
                    _ignoredCollisionPairs.Add(new CollisionIgnorePair(targetCollider, otherCollider));
                }
            }
        }

        _ignoredCharacterCollisions = _ignoredCollisionPairs.Count > 0;
    }

    private void RestoreIgnoredCharacterCollisions()
    {
        foreach (CollisionIgnorePair pair in _ignoredCollisionPairs)
        {
            if (pair.First != null && pair.Second != null)
            {
                Physics.IgnoreCollision(pair.First, pair.Second, ignore: false);
            }
        }

        _ignoredCollisionPairs.Clear();
        _ignoredCharacterCollisions = false;
    }

    private void OnDestroy()
    {
        RestoreIgnoredCharacterCollisions();
    }

    private static Collider[] GetCollisionColliders(Character character)
    {
        List<Collider> colliders = new();
        foreach (Collider collider in character.GetComponentsInChildren<Collider>())
        {
            if (collider != null && collider.enabled && !collider.isTrigger)
            {
                colliders.Add(collider);
            }
        }

        return colliders.ToArray();
    }

    private readonly struct CollisionIgnorePair
    {
        public CollisionIgnorePair(Collider first, Collider second)
        {
            First = first;
            Second = second;
        }

        public Collider First { get; }

        public Collider Second { get; }
    }
}
