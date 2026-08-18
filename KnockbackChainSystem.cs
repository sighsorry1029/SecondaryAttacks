using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class KnockbackChainSystem
{
    private const int MaxOverlapHits = 64;
    private const float MinChainPower = 0.05f;
    private const float CollisionRadius = 2f;
    private const string InitialHitVfx = "vfx_archerytarget_bullseye";
    private const string CollisionVfx = "vfx_HitSparks";
    private const string CollisionSfx = "sfx_club_hit";

    private static readonly Collider[] OverlapHits = new Collider[MaxOverlapHits];
    private static int _characterMask;

    internal static bool TryApplyForSecondaryHit(
        Player attacker,
        Character target,
        bool secondaryAttack,
        SecondaryAttackDefinition? definition,
        ref HitData hit)
    {
        KnockbackChainDefinition? knockbackChain = definition?.KnockbackChain;
        if (!secondaryAttack ||
            knockbackChain == null ||
            attacker == null ||
            target == null ||
            target.IsDead() ||
            (Object)(object)target == (Object)(object)attacker)
        {
            return false;
        }

        float originalPushForce = hit.m_pushForce;
        if (originalPushForce <= 0f || hit.m_damage.GetTotalDamage() <= 0f)
        {
            return false;
        }

        float boostedPushForce = originalPushForce * Mathf.Max(0f, knockbackChain.PushFactor);
        if (boostedPushForce <= 0f)
        {
            return false;
        }

        if (!MeleePresetCooldownSystem.TryConsume(
                attacker,
                null,
                "knockbackChain",
                knockbackChain.PresetCooldown))
        {
            return false;
        }

        hit.m_pushForce = boostedPushForce;
        SecondaryAttackNamedEffectSystem.Create(
            InitialHitVfx,
            ResolveVfxPoint(hit, target),
            target.transform.rotation,
            "knockback_chain_initial_hit_vfx_missing",
            6f);
        Vector3 direction = ResolveDirection(hit.m_dir, attacker, target);
        KnockbackChainState state = new(attacker, hit, knockbackChain);
        AttachTracker(target, state, boostedPushForce, 1f, direction);
        return true;
    }

    private static void AttachTracker(
        Character target,
        KnockbackChainState state,
        float pushForce,
        float chainPower,
        Vector3 direction)
    {
        if (target == null ||
            target.IsDead() ||
            chainPower < MinChainPower ||
            !SecondaryAttackManager.HasCharacterAuthority(target))
        {
            return;
        }

        KnockbackChainTracker tracker = target.GetComponent<KnockbackChainTracker>();
        if (tracker == null)
        {
            tracker = target.gameObject.AddComponent<KnockbackChainTracker>();
        }

        tracker.Initialize(target, state, pushForce, chainPower, direction);
    }

    internal static void TryHitNearbyTargets(
        Character source,
        KnockbackChainState state,
        float pushForce,
        float chainPower,
        Vector3 travelDirection)
    {
        KnockbackChainDefinition config = state.Config;
        if (state.HasReachedTargetLimit)
        {
            return;
        }

        Vector3 sourceCenter = source.GetCenterPoint();
        int hitCount = 0;
        try
        {
            hitCount = Physics.OverlapSphereNonAlloc(
                sourceCenter,
                CollisionRadius,
                OverlapHits,
                GetCharacterMask(),
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider collider = OverlapHits[i];
                if (collider == null)
                {
                    continue;
                }

                Character? target = ProjectileRuntimeSystem.GetHitCharacter(collider);
                if (!IsValidCollisionTarget(state.Attacker, source, target) ||
                    !state.CanHit(target!))
                {
                    continue;
                }

                Vector3 hitDirection = ResolveCollisionDirection(sourceCenter, target!.GetCenterPoint(), travelDirection);
                if (Vector3.Dot(hitDirection, travelDirection) < -0.25f)
                {
                    continue;
                }

                if (!ApplyCollisionHit(source, target, collider, state, pushForce, chainPower, hitDirection))
                {
                    continue;
                }

                float nextPower = chainPower * config.ChainDecay;
                if (!state.HasReachedTargetLimit && nextPower >= MinChainPower)
                {
                    AttachTracker(target, state, pushForce, nextPower, hitDirection);
                }

                if (state.HasReachedTargetLimit)
                {
                    return;
                }
            }
        }
        finally
        {
            System.Array.Clear(OverlapHits, 0, OverlapHits.Length);
        }
    }

    private static bool ApplyCollisionHit(
        Character source,
        Character target,
        Collider collider,
        KnockbackChainState state,
        float pushForce,
        float chainPower,
        Vector3 direction)
    {
        HitData.DamageTypes damage = state.SourceHit.m_damage.Clone();
        damage.Modify(chainPower);
        if (damage.GetTotalDamage() <= 0f)
        {
            return false;
        }

        if (state.SourceHit.m_dodgeable && target.IsDodgeInvincible())
        {
            if (target is Player dodgingPlayer)
            {
                dodgingPlayer.HitWhileDodging();
            }

            return false;
        }

        HitData chainHit = state.SourceHit.Clone();
        chainHit.m_damage = damage;
        chainHit.m_pushForce = pushForce * chainPower;
        chainHit.m_skillRaiseAmount = 0f;
        chainHit.m_blockable = false;
        chainHit.m_dodgeable = false;
        chainHit.m_point = target.GetCenterPoint();
        chainHit.m_dir = direction;
        chainHit.m_hitCollider = collider;
        chainHit.SetAttacker(state.Attacker);

        state.MarkHit(target);

        using (SecondaryAttackRuntimeContext.BeginGeneratedDamage())
        {
            target.Damage(chainHit);
        }

        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        SecondaryAttackNamedEffectSystem.Create(
            CollisionVfx,
            chainHit.m_point,
            rotation,
            "knockback_chain_collision_vfx_missing",
            6f);
        SecondaryAttackNamedEffectSystem.Create(
            CollisionSfx,
            chainHit.m_point,
            rotation,
            "knockback_chain_collision_sfx_missing",
            6f);

        return true;
    }

    private static Vector3 ResolveVfxPoint(HitData hit, Character target)
    {
        if (hit.m_point.sqrMagnitude > 0.001f)
        {
            return hit.m_point;
        }

        return target.GetCenterPoint();
    }

    private static bool IsValidCollisionTarget(Character attacker, Character source, Character? target)
    {
        if (attacker == null ||
            source == null ||
            target == null ||
            target.IsDead() ||
            target == attacker ||
            target == source)
        {
            return false;
        }

        return SecondaryAttackManager.IsEnemyOrAggravatableTarget(attacker, target);
    }

    private static Vector3 ResolveCollisionDirection(Vector3 sourcePoint, Vector3 targetPoint, Vector3 fallbackDirection)
    {
        Vector3 direction = Vector3.ProjectOnPlane(targetPoint - sourcePoint, Vector3.up);
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.ProjectOnPlane(fallbackDirection, Vector3.up);
        }

        return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
    }

    private static Vector3 ResolveDirection(Vector3 hitDirection, Character attacker, Character target)
    {
        Vector3 direction = Vector3.ProjectOnPlane(hitDirection, Vector3.up);
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.ProjectOnPlane(target.transform.position - attacker.transform.position, Vector3.up);
        }

        if (direction.sqrMagnitude < 0.001f)
        {
            direction = Vector3.ProjectOnPlane(attacker.transform.forward, Vector3.up);
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
}

internal sealed class KnockbackChainState
{
    private const int MaxChainTargets = 5;
    private const float HitCooldown = 0.2f;

    private readonly Dictionary<Character, float> _lastHitTimes = new();

    public KnockbackChainState(Character attacker, HitData sourceHit, KnockbackChainDefinition config)
    {
        Attacker = attacker;
        SourceHit = sourceHit.Clone();
        Config = config;
    }

    public Character Attacker { get; }

    public HitData SourceHit { get; }

    public KnockbackChainDefinition Config { get; }

    public int CollateralHits { get; private set; }

    public bool HasReachedTargetLimit => CollateralHits >= MaxChainTargets;

    public bool CanHit(Character target)
    {
        if (target == null || HasReachedTargetLimit)
        {
            return false;
        }

        return !_lastHitTimes.TryGetValue(target, out float lastHitTime) ||
               Time.time - lastHitTime >= HitCooldown;
    }

    public void MarkHit(Character target)
    {
        _lastHitTimes[target] = Time.time;
        CollateralHits++;
    }
}

internal sealed class KnockbackChainTracker : MonoBehaviour
{
    private const float Duration = 1.5f;
    private const float MinSpeed = 0.5f;

    private Character? _target;
    private Rigidbody? _body;
    private KnockbackChainState? _state;
    private Vector3 _lastPosition;
    private Vector3 _travelDirection = Vector3.forward;
    private float _pushForce;
    private float _chainPower;
    private float _startTime;

    public void Initialize(
        Character target,
        KnockbackChainState state,
        float pushForce,
        float chainPower,
        Vector3 travelDirection)
    {
        if (_state != null && _chainPower * _pushForce > chainPower * pushForce)
        {
            return;
        }

        _target = target;
        _body = target.m_body != null ? target.m_body : target.GetComponent<Rigidbody>();
        _state = state;
        _pushForce = Mathf.Max(0f, pushForce);
        _chainPower = Mathf.Max(0f, chainPower);
        _travelDirection = ResolveHorizontalDirection(travelDirection, target.transform.forward);
        _lastPosition = target.transform.position;
        _startTime = Time.time;
        enabled = true;
    }

    private void Update()
    {
        if (_target == null ||
            _state == null ||
            _target.IsDead() ||
            !SecondaryAttackManager.HasCharacterAuthority(_target))
        {
            Destroy(this);
            return;
        }

        if (Time.time - _startTime > Duration ||
            _state.HasReachedTargetLimit)
        {
            Destroy(this);
            return;
        }

        Vector3 currentPosition = _target.transform.position;
        Vector3 displacement = Vector3.ProjectOnPlane(currentPosition - _lastPosition, Vector3.up);
        _lastPosition = currentPosition;

        Vector3 velocity = _body != null ? Vector3.ProjectOnPlane(_body.linearVelocity, Vector3.up) : Vector3.zero;
        if (velocity.sqrMagnitude > 0.01f)
        {
            _travelDirection = velocity.normalized;
        }
        else if (displacement.sqrMagnitude > 0.0001f)
        {
            _travelDirection = displacement.normalized;
        }

        float speed = Mathf.Max(velocity.magnitude, displacement.magnitude / Mathf.Max(Time.deltaTime, 0.001f));
        if (speed < MinSpeed)
        {
            return;
        }

        KnockbackChainSystem.TryHitNearbyTargets(_target, _state, _pushForce, _chainPower, _travelDirection);
    }

    private static Vector3 ResolveHorizontalDirection(Vector3 direction, Vector3 fallback)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (horizontal.sqrMagnitude < 0.001f)
        {
            horizontal = Vector3.ProjectOnPlane(fallback, Vector3.up);
        }

        return horizontal.sqrMagnitude > 0.001f ? horizontal.normalized : Vector3.forward;
    }
}
