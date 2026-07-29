using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecondaryAttacks;

internal static class HarvestSweepSystem
{
    private const string PresetName = "harvestSweep";
    private const float RepeatDelay = 0f;
    private const float RotationSpeedFactor = 1f;
    private const float DefaultHarvestRadius = 1.5f;
    private const float DefaultHarvestRadiusMaxLevel = 2.5f;
    private static readonly List<float> SkillRaiseFactors = new();

    internal static bool TryStart(Attack attack, SecondaryAttackDefinition definition)
    {
        HarvestSweepDefinition? harvestSweep = definition.HarvestSweep;
        if (attack?.m_character is not Humanoid humanoid ||
            attack.m_weapon == null ||
            harvestSweep == null ||
            !SecondaryAttackManager.HasCharacterAuthority(humanoid))
        {
            return false;
        }

        HarvestSweepController controller = humanoid.GetComponent<HarvestSweepController>();
        if (controller != null && controller.IsActive)
        {
            if (!controller.MatchesWeapon(attack.m_weapon))
            {
                controller.StopAfterCurrentAttack();
                return false;
            }

            controller.AttachAttack(attack, harvestSweep);
            SweepObserverVisualSystem.SendRefresh(humanoid, true, attack.m_attackAnimation, harvestSweep.LoopStart, harvestSweep.LoopEnd, harvestSweep.AnimationSpeed);
            return true;
        }

        if (!MeleePresetCooldownSystem.TryConsume(
                humanoid,
                attack.m_weapon,
                PresetName,
                harvestSweep.PresetCooldown))
        {
            return false;
        }

        if (controller == null)
        {
            controller = humanoid.gameObject.AddComponent<HarvestSweepController>();
        }

        controller.Begin(attack, definition, harvestSweep);
        SweepObserverVisualSystem.SendStart(humanoid, true, attack.m_attackAnimation, harvestSweep.LoopStart, harvestSweep.LoopEnd, harvestSweep.AnimationSpeed);
        return true;
    }

    internal static void UpdateInput(Player player, bool secondaryAttackHold, bool primaryAttackHold)
    {
        if (player == null)
        {
            return;
        }

        HarvestSweepController controller = player.GetComponent<HarvestSweepController>();
        controller?.UpdateInput(secondaryAttackHold, primaryAttackHold);
    }

    internal static bool StartRepeatAttack(Humanoid humanoid)
    {
        return humanoid.StartAttack(null, true);
    }

    internal static float GetRepeatDelay() => RepeatDelay;

    internal static float GetRotationSpeedFactor() => RotationSpeedFactor;

    internal static float ResolveHarvestRadius(ItemDrop.ItemData? weapon, Player? player)
    {
        return Mathf.Lerp(
            ResolveHarvestRadius(weapon),
            ResolveHarvestRadiusMaxLevel(weapon),
            player != null ? player.GetSkillFactor(Skills.SkillType.Farming) : 0f);
    }

    internal static float ResolveHarvestRadius(ItemDrop.ItemData? weapon)
    {
        Attack? attack = weapon?.m_shared?.m_attack;
        return attack != null && attack.m_harvestRadius > 0f
            ? attack.m_harvestRadius
            : DefaultHarvestRadius;
    }

    internal static float ResolveHarvestRadiusMaxLevel(ItemDrop.ItemData? weapon)
    {
        Attack? attack = weapon?.m_shared?.m_attack;
        return attack != null && attack.m_harvestRadiusMaxLevel > 0f
            ? attack.m_harvestRadiusMaxLevel
            : DefaultHarvestRadiusMaxLevel;
    }

    internal static SkillRaiseFactorScope BeginSkillRaiseFactor(float factor)
    {
        SkillRaiseFactors.Add(Mathf.Max(0f, factor));
        return new SkillRaiseFactorScope();
    }

    internal static void ApplySkillRaiseFactor(Skills.SkillType skillType, ref float factor)
    {
        if (skillType == Skills.SkillType.Farming && SkillRaiseFactors.Count > 0)
        {
            factor *= SkillRaiseFactors[SkillRaiseFactors.Count - 1];
        }
    }

    internal readonly struct SkillRaiseFactorScope : IDisposable
    {
        public void Dispose()
        {
            if (SkillRaiseFactors.Count > 0)
            {
                SkillRaiseFactors.RemoveAt(SkillRaiseFactors.Count - 1);
            }
        }
    }
}

internal sealed class HarvestSweepController : MonoBehaviour
{
    private const int MaxHarvestHitBufferSize = 2048;
    private static readonly int AttackTagHash = ZSyncAnimation.GetHash("attack");
    private static Collider[] Hits = new Collider[256];
    private static int _harvestMask;
    private static bool _reportedHarvestSearchSaturation;

    private readonly HashSet<Pickable> _seenThisTick = new();
    private readonly HashSet<Destructible> _destroyedPlants = new();
    private Humanoid? _humanoid;
    private ItemDrop.ItemData? _weapon;
    private SecondaryAttackDefinition? _definition;
    private HarvestSweepDefinition? _harvestSweep;
    private Attack? _currentAttack;
    private Animator? _animator;
    private ZSyncAnimation? _zanim;
    private float _nextRepeatTime;
    private float _originalAnimatorSpeed = 1f;
    private int _loopStateHash;
    private int _lastLoopFrame = -1;
    private int _startedFrame;
    private bool _stopRequested;
    private bool _cancelArmed;
    private bool _primaryCancelArmed;
    private bool _lastSecondaryHold = true;
    private bool _lastPrimaryHold;
    private bool _hasOriginalAnimatorSpeed;
    private bool _speedApplied;
    private bool _initialLoopStartApplied;
    private bool _harvestedCurrentSweep;
    private bool _loopRearmed = true;

    internal bool IsActive => _harvestSweep != null && !_stopRequested;

    internal bool SuppressesHitStop => _harvestSweep != null;

    internal bool TryGetAnimationSpeed(out float speed)
    {
        speed = _harvestSweep?.AnimationSpeed ?? 1f;
        return _harvestSweep != null && !Mathf.Approximately(speed, 1f);
    }

    internal void Begin(Attack attack, SecondaryAttackDefinition definition, HarvestSweepDefinition harvestSweep)
    {
        _humanoid = attack.m_character as Humanoid;
        _weapon = attack.m_weapon;
        _definition = definition;
        _harvestSweep = harvestSweep;
        _animator = _humanoid != null ? _humanoid.GetComponentInChildren<Animator>() : null;
        _zanim = _humanoid?.GetZAnim();
        _destroyedPlants.Clear();
        _nextRepeatTime = Time.time + HarvestSweepSystem.GetRepeatDelay();
        _loopStateHash = 0;
        _lastLoopFrame = -1;
        _startedFrame = Time.frameCount;
        _stopRequested = false;
        _cancelArmed = false;
        _primaryCancelArmed = false;
        _lastSecondaryHold = true;
        _lastPrimaryHold = false;
        _initialLoopStartApplied = false;
        _loopRearmed = true;
        AttachAttack(attack, harvestSweep);
        enabled = true;
    }

    internal bool MatchesWeapon(ItemDrop.ItemData weapon)
    {
        return ReferenceEquals(_weapon, weapon) ||
               (_weapon?.m_dropPrefab != null &&
                weapon?.m_dropPrefab != null &&
                _weapon.m_dropPrefab.name == weapon.m_dropPrefab.name);
    }

    internal void AttachAttack(Attack attack, HarvestSweepDefinition harvestSweep)
    {
        bool newAttack = !ReferenceEquals(_currentAttack, attack);
        _currentAttack = attack;
        if (newAttack)
        {
            _loopStateHash = 0;
            _lastLoopFrame = -1;
            _initialLoopStartApplied = false;
            _loopRearmed = true;
        }

        ApplyMovementFactors(attack, harvestSweep);
        ApplyAnimationSpeed(harvestSweep);
        _nextRepeatTime = Time.time + HarvestSweepSystem.GetRepeatDelay();
        _harvestedCurrentSweep = false;
    }

    internal void UpdateInput(bool secondaryAttackHold, bool primaryAttackHold)
    {
        if (!IsActive)
        {
            return;
        }

        UpdatePrimaryCancelInput(primaryAttackHold);
        if (!IsActive)
        {
            return;
        }

        if (!secondaryAttackHold)
        {
            _cancelArmed = Time.frameCount > _startedFrame + 1;
            _lastSecondaryHold = false;
            return;
        }

        bool pressedEdge = !_lastSecondaryHold;
        _lastSecondaryHold = true;
        if (_cancelArmed && pressedEdge)
        {
            StopAfterCurrentAttack();
        }
    }

    private void UpdatePrimaryCancelInput(bool primaryAttackHold)
    {
        if (!primaryAttackHold)
        {
            _primaryCancelArmed = Time.frameCount > _startedFrame + 1;
            _lastPrimaryHold = false;
            return;
        }

        bool pressedEdge = !_lastPrimaryHold;
        _lastPrimaryHold = true;
        if (_primaryCancelArmed && pressedEdge)
        {
            StopAfterCurrentAttack();
        }
    }

    internal void StopAfterCurrentAttack()
    {
        _stopRequested = true;
    }

    private void Update()
    {
        if (_humanoid == null ||
            _weapon == null ||
            _definition == null ||
            _harvestSweep == null ||
            _humanoid.IsDead() ||
            !SecondaryAttackManager.HasCharacterAuthority(_humanoid))
        {
            Destroy(this);
            return;
        }

        Attack? activeAttack = _humanoid.m_currentAttack;
        if (activeAttack != null && _humanoid.InAttack())
        {
            if (ReferenceEquals(activeAttack, _currentAttack))
            {
                ApplyMovementFactors(activeAttack, _harvestSweep);
                if (ShouldKeepLooping() && TryUpdateSeamlessLoop(activeAttack))
                {
                    return;
                }
            }
            return;
        }

        if (_currentAttack != null && !_harvestedCurrentSweep)
        {
            HarvestCurrentSweep(_currentAttack);
        }

        if (_stopRequested || !MatchesWeapon(_humanoid.GetCurrentWeapon()))
        {
            Destroy(this);
            return;
        }

        if (Time.time < _nextRepeatTime || _humanoid.IsStaggering() || _humanoid.InAttack())
        {
            return;
        }

        if (!CanPayNextAttackCost(_weapon))
        {
            Destroy(this);
            return;
        }

        if (!HarvestSweepSystem.StartRepeatAttack(_humanoid))
        {
            _nextRepeatTime = Time.time + 0.05f;
        }
    }

    private bool ShouldKeepLooping()
    {
        return !_stopRequested &&
               _humanoid != null &&
               MatchesWeapon(_humanoid.GetCurrentWeapon());
    }

    private bool TryUpdateSeamlessLoop(Attack activeAttack)
    {
        if (_humanoid == null || _weapon == null || _harvestSweep == null)
        {
            return false;
        }

        _animator ??= _humanoid.GetComponentInChildren<Animator>();
        if (_animator == null)
        {
            return false;
        }

        AnimatorStateInfo state = GetAttackAnimatorState(_animator);
        if (!IsAttackState(state))
        {
            return false;
        }

        if (_loopStateHash == 0 && state.fullPathHash != 0)
        {
            _loopStateHash = state.fullPathHash;
        }

        float loopStart = _harvestSweep.LoopStart;
        float loopEnd = _harvestSweep.LoopEnd;
        if (!_initialLoopStartApplied)
        {
            _initialLoopStartApplied = true;
            if (state.normalizedTime < loopStart)
            {
                SeekToLoopStart(state);
                _loopRearmed = false;
                return true;
            }
        }

        if (!TryRearmLoop(state, loopStart, loopEnd))
        {
            return true;
        }

        if (state.normalizedTime < loopEnd || _lastLoopFrame == Time.frameCount)
        {
            return false;
        }

        HarvestCurrentSweep(activeAttack);
        if (!CanPayNextAttackCost(_weapon))
        {
            _stopRequested = true;
            return false;
        }

        PayNextAttackCost(activeAttack);
        _lastLoopFrame = Time.frameCount;
        SeekToLoopStart(state);
        _harvestedCurrentSweep = false;
        _loopRearmed = false;
        return true;
    }

    private bool TryRearmLoop(AnimatorStateInfo state, float loopStart, float loopEnd)
    {
        if (_loopRearmed)
        {
            return true;
        }

        if (state.normalizedTime < loopEnd)
        {
            _loopRearmed = true;
            return true;
        }

        return false;
    }

    private void SeekToLoopStart(AnimatorStateInfo state)
    {
        if (_animator == null || _harvestSweep == null)
        {
            return;
        }

        int stateHash = _loopStateHash != 0 ? _loopStateHash : state.fullPathHash;
        if (stateHash != 0)
        {
            SweepTrailResetSystem.ClearWeaponTrails(_currentAttack);
            _animator.Play(stateHash, 0, _harvestSweep.LoopStart);
            if (_humanoid != null && SecondaryAttackManager.HasCharacterAuthority(_humanoid))
            {
                SweepObserverVisualSystem.SendRefresh(_humanoid, true, _currentAttack?.m_attackAnimation ?? string.Empty, _harvestSweep.LoopStart, _harvestSweep.LoopEnd, _harvestSweep.AnimationSpeed);
            }
        }
    }

    private static AnimatorStateInfo GetAttackAnimatorState(Animator animator)
    {
        if (animator.IsInTransition(0))
        {
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            if (IsAttackState(next))
            {
                return next;
            }
        }

        return animator.GetCurrentAnimatorStateInfo(0);
    }

    private static bool IsAttackState(AnimatorStateInfo state)
    {
        return state.fullPathHash != 0 && state.tagHash == AttackTagHash;
    }

    private bool CanPayNextAttackCost(ItemDrop.ItemData weapon)
    {
        ItemDrop.ItemData.SharedData? sharedData = weapon.m_shared;
        Attack? secondaryAttack = sharedData?.m_secondaryAttack;
        if (secondaryAttack == null)
        {
            return false;
        }

        float durabilityCost = Mathf.Max(0f, sharedData!.m_useDurabilityDrain * (_definition?.DurabilityFactor ?? 1f));
        if (durabilityCost > 0f && weapon.m_durability + 0.001f < durabilityCost)
        {
            return false;
        }

        float stamina = Mathf.Max(0f, secondaryAttack.m_attackStamina);
        if (stamina > 0f && !_humanoid!.HaveStamina(stamina))
        {
            return false;
        }

        float eitr = Mathf.Max(0f, secondaryAttack.m_attackEitr);
        if (eitr > 0f && !_humanoid!.HaveEitr(eitr))
        {
            return false;
        }

        float health = Mathf.Max(0f, secondaryAttack.m_attackHealth);
        if (health > 0f && !_humanoid!.HaveHealth(health) && secondaryAttack.m_attackHealthLowBlockUse)
        {
            return false;
        }

        return true;
    }

    private void PayNextAttackCost(Attack activeAttack)
    {
        if (_humanoid == null || _weapon?.m_shared == null)
        {
            return;
        }

        Attack? secondaryAttack = _weapon.m_shared.m_secondaryAttack;
        if (secondaryAttack == null)
        {
            return;
        }

        float stamina = Mathf.Max(0f, secondaryAttack.m_attackStamina);
        if (stamina > 0f)
        {
            _humanoid.UseStamina(stamina);
        }

        float eitr = Mathf.Max(0f, secondaryAttack.m_attackEitr);
        if (eitr > 0f)
        {
            _humanoid.UseEitr(eitr);
        }

        float health = Mathf.Max(0f, secondaryAttack.m_attackHealth);
        if (health > 0f)
        {
            _humanoid.UseHealth(Mathf.Max(0f, Mathf.Min(_humanoid.GetHealth() - 1f, health)));
        }

        Transform attackOrigin = activeAttack.GetAttackOrigin();
        _weapon.m_shared.m_startEffect.Create(attackOrigin.position, _humanoid.transform.rotation, attackOrigin);
        activeAttack.m_startEffect.Create(attackOrigin.position, _humanoid.transform.rotation, attackOrigin);
        _humanoid.AddNoise(activeAttack.m_attackStartNoise);
    }

    private void HarvestCurrentSweep(Attack activeAttack)
    {
        if (_harvestedCurrentSweep)
        {
            return;
        }

        _harvestedCurrentSweep = true;
        HarvestOnce(activeAttack);
    }

    private void HarvestOnce(Attack activeAttack)
    {
        if (activeAttack?.m_character == null || _harvestSweep == null || _weapon == null)
        {
            return;
        }

        Player? player = activeAttack.m_character as Player;
        if (player == null)
        {
            return;
        }

        Attack? harvestAttack = _weapon.m_shared?.m_attack;
        float radius = HarvestSweepSystem.ResolveHarvestRadius(_weapon, player);
        Vector3 center = ResolveHarvestCenter(activeAttack, harvestAttack);
        int hitCount = 0;
        _seenThisTick.Clear();
        try
        {
            hitCount = QueryHarvestHits(center, radius);
            for (int i = 0; i < hitCount; i++)
            {
                Collider hit = Hits[i];
                if (hit == null)
                {
                    continue;
                }

                Pickable? pickable = hit.GetComponentInParent<Pickable>();
                if (pickable != null)
                {
                    if (!_seenThisTick.Add(pickable) ||
                        !CanHarvest(pickable))
                    {
                        continue;
                    }

                    using (GroundworkCompat.SuppressForagingRangePickup())
                    using (HarvestSweepSystem.BeginSkillRaiseFactor(_harvestSweep.SkillRaiseFactor))
                    {
                        pickable.Interact(player, repeat: false, alt: false);
                    }

                    continue;
                }

                DestroyUnhealthyPlantIfNeeded(hit);
            }
        }
        finally
        {
            _seenThisTick.Clear();
            System.Array.Clear(Hits, 0, Hits.Length);
        }
    }

    private void DestroyUnhealthyPlantIfNeeded(Collider hit)
    {
        Plant? plant = hit.GetComponentInParent<Plant>();
        if (plant == null || plant.GetStatus() == Plant.Status.Healthy)
        {
            return;
        }

        Destructible? destructible = hit.GetComponentInParent<Destructible>();
        if (destructible == null || !_destroyedPlants.Add(destructible))
        {
            return;
        }

        destructible.Destroy();
    }

    private static bool CanHarvest(Pickable pickable)
    {
        return GroundworkCompat.CanScytheHarvest(pickable) &&
               pickable.CanBePicked();
    }

    private static Vector3 ResolveHarvestCenter(Attack activeAttack, Attack? harvestAttack)
    {
        Attack geometryAttack = harvestAttack ?? activeAttack;
        Character character = activeAttack.m_character;
        Transform origin = ResolveAttackOrigin(geometryAttack, character);
        Vector3 attackDirection = ResolveMeleeAttackDirection(geometryAttack, character, origin);
        float attackRange = Mathf.Max(0f, geometryAttack.m_attackRange);
        if (attackRange <= 0f)
        {
            attackRange = HarvestSweepSystem.ResolveHarvestRadius(null);
        }

        return origin.position +
               Vector3.up * Mathf.Max(0f, geometryAttack.m_attackHeight) +
               character.transform.right * geometryAttack.m_attackOffset +
               attackDirection * attackRange;
    }

    private static Transform ResolveAttackOrigin(Attack attack, Character character)
    {
        if (!string.IsNullOrEmpty(attack.m_attackOriginJoint))
        {
            Transform? visual = character.GetVisual()?.transform;
            if (visual != null)
            {
                Transform? joint = Utils.FindChild(visual, attack.m_attackOriginJoint);
                if (joint != null)
                {
                    return joint;
                }
            }
        }

        return character.transform;
    }

    private static Vector3 ResolveMeleeAttackDirection(Attack attack, Character character, Transform origin)
    {
        Vector3 forward = character.transform.forward;
        Vector3 aimDirection = character is Humanoid humanoid
            ? humanoid.GetAimDir(origin.position)
            : forward;
        aimDirection.x = forward.x;
        aimDirection.z = forward.z;
        if (aimDirection.sqrMagnitude < 0.001f)
        {
            return forward;
        }

        aimDirection.Normalize();
        return Vector3.RotateTowards(
            forward,
            aimDirection,
            Mathf.Deg2Rad * attack.m_maxYAngle,
            10f);
    }

    private void ApplyAnimationSpeed(HarvestSweepDefinition harvestSweep)
    {
        if (Mathf.Approximately(harvestSweep.AnimationSpeed, 1f))
        {
            return;
        }

        _animator ??= _humanoid != null ? _humanoid.GetComponentInChildren<Animator>() : null;
        _zanim ??= _humanoid?.GetZAnim();
        if (_animator == null)
        {
            return;
        }

        if (!_hasOriginalAnimatorSpeed)
        {
            _originalAnimatorSpeed = _animator.speed;
            _hasOriginalAnimatorSpeed = true;
        }

        _zanim?.SetSpeed(harvestSweep.AnimationSpeed);
        _animator.speed = harvestSweep.AnimationSpeed;
        _speedApplied = true;
    }

    private void RestoreAnimationSpeed()
    {
        if (!_speedApplied || _animator == null)
        {
            return;
        }

        _zanim?.SetSpeed(_originalAnimatorSpeed);
        _animator.speed = _originalAnimatorSpeed;
        _speedApplied = false;
    }

    private static void ApplyMovementFactors(Attack attack, HarvestSweepDefinition harvestSweep)
    {
        attack.m_speedFactor = harvestSweep.MoveSpeedFactor;
        attack.m_speedFactorRotation = HarvestSweepSystem.GetRotationSpeedFactor();
    }

    private void OnDestroy()
    {
        if (_humanoid != null && SecondaryAttackManager.HasCharacterAuthority(_humanoid))
        {
            SweepObserverVisualSystem.SendStop(_humanoid);
        }

        SweepTrailResetSystem.ClearWeaponTrails(_currentAttack);
        RestoreAnimationSpeed();
    }

    private static int GetHarvestMask()
    {
        if (_harvestMask == 0)
        {
            _harvestMask = LayerMask.GetMask("piece", "piece_nonsolid", "item", "Default", "Default_small");
        }

        return _harvestMask;
    }

    private static int QueryHarvestHits(Vector3 center, float radius)
    {
        while (true)
        {
            int hitCount = Physics.OverlapSphereNonAlloc(
                center,
                radius,
                Hits,
                GetHarvestMask(),
                QueryTriggerInteraction.UseGlobal);
            if (hitCount < Hits.Length)
            {
                return hitCount;
            }

            if (Hits.Length >= MaxHarvestHitBufferSize)
            {
                if (!_reportedHarvestSearchSaturation)
                {
                    _reportedHarvestSearchSaturation = true;
                    SecondaryAttacksPlugin.ModLogger.LogWarning(
                        $"Harvest sweep search reached the {Hits.Length}-collider maximum buffer capacity. " +
                        "Results may be truncated in this unusually dense area.");
                }

                return hitCount;
            }

            Array.Resize(ref Hits, Math.Min(Hits.Length * 2, MaxHarvestHitBufferSize));
        }
    }
}
