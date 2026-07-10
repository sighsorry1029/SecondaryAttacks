using UnityEngine;

namespace SecondaryAttacks;

internal static class SpinningSweepSystem
{
    private const string PresetName = "spinningSweep";
    private const float RepeatDelay = 0f;
    private const float RotationSpeedFactor = 1f;

    internal static bool TryStart(Attack attack, SecondaryAttackDefinition definition)
    {
        SpinningSweepDefinition? spinningSweep = definition.SpinningSweep;
        if (attack?.m_character is not Humanoid humanoid ||
            attack.m_weapon == null ||
            spinningSweep == null ||
            !SecondaryAttackManager.HasCharacterAuthority(humanoid))
        {
            return false;
        }

        SpinningSweepController controller = humanoid.GetComponent<SpinningSweepController>();
        if (controller != null && controller.IsActive)
        {
            if (!controller.MatchesWeapon(attack.m_weapon))
            {
                controller.StopAfterCurrentAttack();
                return false;
            }

            controller.AttachAttack(attack, spinningSweep);
            SweepObserverVisualSystem.SendRefresh(humanoid, false, attack.m_attackAnimation, spinningSweep.LoopStart, spinningSweep.LoopEnd, spinningSweep.AnimationSpeed);
            return true;
        }

        if (!MeleePresetCooldownSystem.TryConsume(humanoid, attack.m_weapon, PresetName, spinningSweep.PresetCooldown, out _))
        {
            return false;
        }

        if (controller == null)
        {
            controller = humanoid.gameObject.AddComponent<SpinningSweepController>();
        }

        controller.Begin(attack, definition, spinningSweep);
        SweepObserverVisualSystem.SendStart(humanoid, false, attack.m_attackAnimation, spinningSweep.LoopStart, spinningSweep.LoopEnd, spinningSweep.AnimationSpeed);
        return true;
    }

    internal static float GetRepeatDelay() => RepeatDelay;

    internal static float GetRotationSpeedFactor() => RotationSpeedFactor;

    internal static void UpdateInput(Player player, bool secondaryAttackHold, bool primaryAttackHold)
    {
        if (player == null)
        {
            return;
        }

        SpinningSweepController controller = player.GetComponent<SpinningSweepController>();
        controller?.UpdateInput(secondaryAttackHold, primaryAttackHold);
    }

    internal static bool StartRepeatAttack(Humanoid humanoid)
    {
        return humanoid.StartAttack(null, true);
    }

}

internal sealed class SpinningSweepController : MonoBehaviour
{
    private static readonly int AttackTagHash = ZSyncAnimation.GetHash("attack");

    private Humanoid? _humanoid;
    private ItemDrop.ItemData? _weapon;
    private SecondaryAttackDefinition? _definition;
    private SpinningSweepDefinition? _spinningSweep;
    private Attack? _currentAttack;
    private Animator? _animator;
    private ZSyncAnimation? _zanim;
    private float _nextRepeatTime;
    private float _originalAnimatorSpeed = 1f;
    private float _originalRaiseSkillAmount;
    private float _originalDamageMultiplier = 1f;
    private float _originalForceMultiplier = 1f;
    private int _loopStateHash;
    private int _lastLoopFrame = -1;
    private int _startedFrame;
    private bool _stopRequested;
    private bool _cancelArmed;
    private bool _primaryCancelArmed;
    private bool _lastSecondaryHold = true;
    private bool _lastPrimaryHold;
    private bool _hasOriginalAnimatorSpeed;
    private bool _hasOriginalRaiseSkillAmount;
    private bool _hasOriginalAttackMultipliers;
    private bool _speedApplied;
    private bool _initialLoopStartApplied;
    private bool _loopRearmed = true;
    private Attack? _skillRaiseAttack;
    private Attack? _multiplierAttack;

    internal bool IsActive => _spinningSweep != null && !_stopRequested;

    internal bool SuppressesHitStop => _spinningSweep != null;

    internal bool TryGetAnimationSpeed(out float speed)
    {
        speed = _spinningSweep?.AnimationSpeed ?? 1f;
        return _spinningSweep != null && !Mathf.Approximately(speed, 1f);
    }

    internal void Begin(Attack attack, SecondaryAttackDefinition definition, SpinningSweepDefinition spinningSweep)
    {
        _humanoid = attack.m_character as Humanoid;
        _weapon = attack.m_weapon;
        _definition = definition;
        _spinningSweep = spinningSweep;
        _animator = _humanoid != null ? _humanoid.GetComponentInChildren<Animator>() : null;
        _zanim = _humanoid?.GetZAnim();
        _nextRepeatTime = Time.time + SpinningSweepSystem.GetRepeatDelay();
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
        AttachAttack(attack, spinningSweep);
        enabled = true;
    }

    internal bool MatchesWeapon(ItemDrop.ItemData weapon)
    {
        return ReferenceEquals(_weapon, weapon) ||
               (_weapon?.m_dropPrefab != null &&
                weapon?.m_dropPrefab != null &&
                _weapon.m_dropPrefab.name == weapon.m_dropPrefab.name);
    }

    internal void AttachAttack(Attack attack, SpinningSweepDefinition spinningSweep)
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

        ApplyMovementFactors(attack, spinningSweep);
        ApplyAttackMultipliers(attack, spinningSweep);
        ApplySkillRaiseFactor(attack, spinningSweep);
        ApplyAnimationSpeed(spinningSweep);
        _nextRepeatTime = Time.time + SpinningSweepSystem.GetRepeatDelay();
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
        if (!_cancelArmed || !pressedEdge)
        {
            return;
        }

        StopAfterCurrentAttack();
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
        if (!_primaryCancelArmed || !pressedEdge)
        {
            return;
        }

        StopAfterCurrentAttack();
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
            _spinningSweep == null ||
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
                ApplyMovementFactors(activeAttack, _spinningSweep);
                if (ShouldKeepLooping() && TryUpdateSeamlessLoop(activeAttack))
                {
                    return;
                }
            }
            return;
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

        if (!SpinningSweepSystem.StartRepeatAttack(_humanoid))
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
        if (_spinningSweep == null || _humanoid == null || _weapon == null)
        {
            return false;
        }

        _animator ??= _humanoid.GetComponentInChildren<Animator>();
        _zanim ??= _humanoid.GetZAnim();
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

        if (!_initialLoopStartApplied)
        {
            _initialLoopStartApplied = true;
            if (state.normalizedTime < _spinningSweep.LoopStart)
            {
                SeekToLoopStart(state);
                _loopRearmed = false;
                return true;
            }
        }

        if (!TryRearmLoop(state, _spinningSweep.LoopStart, _spinningSweep.LoopEnd))
        {
            return true;
        }

        if (state.normalizedTime < _spinningSweep.LoopEnd || _lastLoopFrame == Time.frameCount)
        {
            return false;
        }

        if (!CanPayNextAttackCost(_weapon))
        {
            _stopRequested = true;
            return false;
        }

        PayNextAttackCost(activeAttack);
        _lastLoopFrame = Time.frameCount;

        SeekToLoopStart(state);
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
        if (_animator == null || _spinningSweep == null)
        {
            return;
        }

        int stateHash = _loopStateHash != 0 ? _loopStateHash : state.fullPathHash;
        if (stateHash == 0)
        {
            return;
        }

        SweepTrailResetSystem.ClearWeaponTrails(_currentAttack);
        _animator.Play(stateHash, 0, _spinningSweep.LoopStart);
        if (_humanoid != null && SecondaryAttackManager.HasCharacterAuthority(_humanoid))
        {
            SweepObserverVisualSystem.SendRefresh(_humanoid, false, _currentAttack?.m_attackAnimation ?? string.Empty, _spinningSweep.LoopStart, _spinningSweep.LoopEnd, _spinningSweep.AnimationSpeed);
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

    private void ApplyAnimationSpeed(SpinningSweepDefinition spinningSweep)
    {
        if (Mathf.Approximately(spinningSweep.AnimationSpeed, 1f))
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

        _zanim?.SetSpeed(spinningSweep.AnimationSpeed);
        _animator.speed = spinningSweep.AnimationSpeed;
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

    private static void ApplyMovementFactors(Attack attack, SpinningSweepDefinition spinningSweep)
    {
        attack.m_speedFactor = spinningSweep.MoveSpeedFactor;
        attack.m_speedFactorRotation = SpinningSweepSystem.GetRotationSpeedFactor();
    }

    private void ApplyAttackMultipliers(Attack attack, SpinningSweepDefinition spinningSweep)
    {
        if (!ReferenceEquals(_multiplierAttack, attack))
        {
            RestoreAttackMultipliers();
            _multiplierAttack = attack;
            _originalDamageMultiplier = attack.m_damageMultiplier;
            _originalForceMultiplier = attack.m_forceMultiplier;
            _hasOriginalAttackMultipliers = true;
        }

        attack.m_damageMultiplier = _originalDamageMultiplier * Mathf.Max(0f, spinningSweep.DamageFactor);
        attack.m_forceMultiplier = _originalForceMultiplier * Mathf.Max(0f, spinningSweep.PushFactor);
    }

    private void RestoreAttackMultipliers()
    {
        if (!_hasOriginalAttackMultipliers || _multiplierAttack == null)
        {
            return;
        }

        _multiplierAttack.m_damageMultiplier = _originalDamageMultiplier;
        _multiplierAttack.m_forceMultiplier = _originalForceMultiplier;
        _multiplierAttack = null;
        _hasOriginalAttackMultipliers = false;
    }

    private void ApplySkillRaiseFactor(Attack attack, SpinningSweepDefinition spinningSweep)
    {
        if (!ReferenceEquals(_skillRaiseAttack, attack))
        {
            RestoreSkillRaiseFactor();
            _skillRaiseAttack = attack;
            _originalRaiseSkillAmount = attack.m_raiseSkillAmount;
            _hasOriginalRaiseSkillAmount = true;
        }

        attack.m_raiseSkillAmount = _originalRaiseSkillAmount * Mathf.Max(0f, spinningSweep.SkillRaiseFactor);
    }

    private void RestoreSkillRaiseFactor()
    {
        if (!_hasOriginalRaiseSkillAmount || _skillRaiseAttack == null)
        {
            return;
        }

        _skillRaiseAttack.m_raiseSkillAmount = _originalRaiseSkillAmount;
        _skillRaiseAttack = null;
        _hasOriginalRaiseSkillAmount = false;
    }

    private void OnDestroy()
    {
        if (_humanoid != null && SecondaryAttackManager.HasCharacterAuthority(_humanoid))
        {
            SweepObserverVisualSystem.SendStop(_humanoid);
        }

        SweepTrailResetSystem.ClearWeaponTrails(_currentAttack);
        RestoreAttackMultipliers();
        RestoreSkillRaiseFactor();
        RestoreAnimationSpeed();
    }
}
