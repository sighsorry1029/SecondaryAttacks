using System.Diagnostics;
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

        bool debugLogging = IsDebugLoggingEnabled();
        SpinningSweepController controller = humanoid.GetComponent<SpinningSweepController>();
        if (controller != null && controller.IsActive)
        {
            if (!controller.MatchesWeapon(attack.m_weapon))
            {
                if (debugLogging)
                {
                    LogDebug($"active controller weapon mismatch; stopping. attackWeapon={DescribeWeapon(attack.m_weapon)}.");
                }

                controller.StopAfterCurrentAttack();
                return false;
            }

            if (debugLogging)
            {
                LogDebug($"attach repeat attack weapon={DescribeWeapon(attack.m_weapon)} animation={attack.m_attackAnimation} inAttack={humanoid.InAttack()} currentAttackNull={(humanoid.m_currentAttack == null)}.");
            }

            controller.AttachAttack(attack, spinningSweep);
            return true;
        }

        if (!MeleePresetCooldownSystem.TryConsume(humanoid, attack.m_weapon, PresetName, spinningSweep.PresetCooldown, out _))
        {
            if (debugLogging)
            {
                LogDebug($"begin skipped: cooldown active weapon={DescribeWeapon(attack.m_weapon)}.");
            }

            return false;
        }

        if (controller == null)
        {
            controller = humanoid.gameObject.AddComponent<SpinningSweepController>();
        }

        controller.Begin(attack, definition, spinningSweep);
        if (debugLogging)
        {
            LogDebug($"begin weapon={DescribeWeapon(attack.m_weapon)} animation={attack.m_attackAnimation} loop={spinningSweep.LoopStart:0.###}-{spinningSweep.LoopEnd:0.###} speed={spinningSweep.AnimationSpeed:0.###} move={spinningSweep.MoveSpeedFactor:0.###}.");
        }

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
        bool started = humanoid.StartAttack(null, true);
        if (IsDebugLoggingEnabled())
        {
            LogDebug($"repeat StartAttack result={started} inAttack={humanoid.InAttack()} currentAttackNull={(humanoid.m_currentAttack == null)} currentSecondary={humanoid.m_currentAttackIsSecondary}.");
        }

        return started;
    }

    [Conditional("SECONDARY_ATTACKS_DEBUG_LOGGING")]
    internal static void LogDebug(string message)
    {
    }

    internal static bool IsDebugLoggingEnabled() => false;

    internal static string DescribeWeapon(ItemDrop.ItemData? weapon)
    {
        return weapon?.m_dropPrefab?.name ?? weapon?.m_shared?.m_name ?? "<null>";
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
    private bool _speedApplied;
    private bool _initialLoopStartApplied;
    private bool _loopRearmed = true;
    private Attack? _skillRaiseAttack;

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
            bool wasArmed = _cancelArmed;
            _cancelArmed = Time.frameCount > _startedFrame + 1;
            _lastSecondaryHold = false;
            if (!wasArmed && _cancelArmed)
            {
                SpinningSweepSystem.LogDebug("cancel armed after secondary input release.");
            }
            return;
        }

        bool pressedEdge = !_lastSecondaryHold;
        _lastSecondaryHold = true;
        if (!_cancelArmed || !pressedEdge)
        {
            return;
        }

        StopAfterCurrentAttack();
        SpinningSweepSystem.LogDebug("stop requested by secondary hold edge.");
    }

    private void UpdatePrimaryCancelInput(bool primaryAttackHold)
    {
        if (!primaryAttackHold)
        {
            bool wasArmed = _primaryCancelArmed;
            _primaryCancelArmed = Time.frameCount > _startedFrame + 1;
            _lastPrimaryHold = false;
            if (!wasArmed && _primaryCancelArmed)
            {
                SpinningSweepSystem.LogDebug("primary cancel armed after primary input release.");
            }
            return;
        }

        bool pressedEdge = !_lastPrimaryHold;
        _lastPrimaryHold = true;
        if (!_primaryCancelArmed || !pressedEdge)
        {
            return;
        }

        StopAfterCurrentAttack();
        SpinningSweepSystem.LogDebug("stop requested by primary hold edge.");
    }

    internal void StopAfterCurrentAttack()
    {
        _stopRequested = true;
    }

    private void Update()
    {
        bool debugLogging = SpinningSweepSystem.IsDebugLoggingEnabled();
        if (_humanoid == null ||
            _weapon == null ||
            _definition == null ||
            _spinningSweep == null ||
            _humanoid.IsDead() ||
            !SecondaryAttackManager.HasCharacterAuthority(_humanoid))
        {
            if (debugLogging)
            {
                SpinningSweepSystem.LogDebug($"destroy: invalid state humanoidNull={(_humanoid == null)} weaponNull={(_weapon == null)} definitionNull={(_definition == null)} configNull={(_spinningSweep == null)} dead={(_humanoid?.IsDead() ?? false)} authority={(_humanoid != null && SecondaryAttackManager.HasCharacterAuthority(_humanoid))}.");
            }

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
            else if (debugLogging && Time.frameCount % 10 == 0)
            {
                SpinningSweepSystem.LogDebug($"active attack mismatch frame={Time.frameCount} activeAnimation={activeAttack.m_attackAnimation} currentAnimation={_currentAttack?.m_attackAnimation ?? "<null>"} inAttack={_humanoid.InAttack()} currentSecondary={_humanoid.m_currentAttackIsSecondary}.");
            }

            return;
        }

        if (_stopRequested || !MatchesWeapon(_humanoid.GetCurrentWeapon()))
        {
            if (debugLogging)
            {
                SpinningSweepSystem.LogDebug($"destroy: stop={_stopRequested} weaponMatch={MatchesWeapon(_humanoid.GetCurrentWeapon())} currentWeapon={SpinningSweepSystem.DescribeWeapon(_humanoid.GetCurrentWeapon())}.");
            }

            Destroy(this);
            return;
        }

        if (Time.time < _nextRepeatTime || _humanoid.IsStaggering() || _humanoid.InAttack())
        {
            if (debugLogging && Time.frameCount % 20 == 0)
            {
                SpinningSweepSystem.LogDebug($"waiting: time={Time.time:0.###} next={_nextRepeatTime:0.###} stagger={_humanoid.IsStaggering()} inAttack={_humanoid.InAttack()} currentAttackNull={(_humanoid.m_currentAttack == null)}.");
            }

            return;
        }

        if (!CanPayNextAttackCost(_weapon, out string costReason))
        {
            if (debugLogging)
            {
                SpinningSweepSystem.LogDebug($"destroy: cannot pay next cost reason={costReason} weapon={SpinningSweepSystem.DescribeWeapon(_weapon)}.");
            }

            Destroy(this);
            return;
        }

        if (debugLogging)
        {
            SpinningSweepSystem.LogDebug($"try repeat weapon={SpinningSweepSystem.DescribeWeapon(_weapon)} currentAttackNull={(_humanoid.m_currentAttack == null)} inAttack={_humanoid.InAttack()} timeSinceLast={_humanoid.GetTimeSinceLastAttack():0.###}.");
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

        bool debugLogging = SpinningSweepSystem.IsDebugLoggingEnabled();
        _animator ??= _humanoid.GetComponentInChildren<Animator>();
        _zanim ??= _humanoid.GetZAnim();
        if (_animator == null)
        {
            return false;
        }

        AnimatorStateInfo state = GetAttackAnimatorState(_animator);
        if (!IsAttackState(state))
        {
            if (debugLogging && Time.frameCount % 10 == 0)
            {
                SpinningSweepSystem.LogDebug($"loop wait: animator is not attack state frame={Time.frameCount} {DescribeState(state)} inTransition={_animator.IsInTransition(0)}.");
            }

            return false;
        }

        if (_loopStateHash == 0 && state.fullPathHash != 0)
        {
            _loopStateHash = state.fullPathHash;
            if (debugLogging)
            {
                SpinningSweepSystem.LogDebug($"captured loop state hash={_loopStateHash} normalized={state.normalizedTime:0.###}.");
            }
        }

        if (!_initialLoopStartApplied)
        {
            _initialLoopStartApplied = true;
            if (state.normalizedTime < _spinningSweep.LoopStart)
            {
                SeekToLoopStart(state);
                _loopRearmed = false;
                if (debugLogging)
                {
                    SpinningSweepSystem.LogDebug($"seamless loop initial skip animation={activeAttack.m_attackAnimation} start={_spinningSweep.LoopStart:0.###} current={state.normalizedTime:0.###}.");
                }

                return true;
            }
        }

        if (!TryRearmLoop(state, _spinningSweep.LoopStart, _spinningSweep.LoopEnd))
        {
            return true;
        }

        if (state.normalizedTime < _spinningSweep.LoopEnd || _lastLoopFrame == Time.frameCount)
        {
            if (debugLogging && Time.frameCount % 10 == 0)
            {
                SpinningSweepSystem.LogDebug($"loop wait frame={Time.frameCount} normalized={state.normalizedTime:0.###} end={_spinningSweep.LoopEnd:0.###} lastLoopFrame={_lastLoopFrame} animatorSpeed={_animator.speed:0.###}.");
            }

            return false;
        }

        if (!CanPayNextAttackCost(_weapon, out string costReason))
        {
            _stopRequested = true;
            if (debugLogging)
            {
                SpinningSweepSystem.LogDebug($"seamless loop ending: cannot pay next cost reason={costReason} weapon={SpinningSweepSystem.DescribeWeapon(_weapon)}.");
            }

            return false;
        }

        PayNextAttackCost(activeAttack);
        _lastLoopFrame = Time.frameCount;

        SeekToLoopStart(state);
        _loopRearmed = false;
        if (debugLogging)
        {
            SpinningSweepSystem.LogDebug($"seamless loop rewind frame={Time.frameCount} animation={activeAttack.m_attackAnimation} normalizedBefore={state.normalizedTime:0.###} start={_spinningSweep.LoopStart:0.###} end={_spinningSweep.LoopEnd:0.###} configuredSpeed={_spinningSweep.AnimationSpeed:0.###} animatorSpeed={_animator.speed:0.###} inTransition={_animator.IsInTransition(0)}.");
        }

        return true;
    }

    private bool TryRearmLoop(AnimatorStateInfo state, float loopStart, float loopEnd)
    {
        bool debugLogging = SpinningSweepSystem.IsDebugLoggingEnabled();
        if (_loopRearmed)
        {
            return true;
        }

        if (state.normalizedTime < loopEnd)
        {
            _loopRearmed = true;
            if (debugLogging)
            {
                SpinningSweepSystem.LogDebug($"loop rearmed frame={Time.frameCount} normalized={state.normalizedTime:0.###} start={loopStart:0.###} end={loopEnd:0.###} animatorSpeed={_animator?.speed ?? 0f:0.###}.");
            }

            return true;
        }

        if (debugLogging && Time.frameCount % 10 == 0)
        {
            SpinningSweepSystem.LogDebug($"loop wait: waiting for seek to apply frame={Time.frameCount} normalized={state.normalizedTime:0.###} start={loopStart:0.###} end={loopEnd:0.###} animatorSpeed={_animator?.speed ?? 0f:0.###}.");
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

    private static string DescribeState(AnimatorStateInfo state)
    {
        return $"stateHash={state.fullPathHash} tagHash={state.tagHash} normalized={state.normalizedTime:0.###} length={state.length:0.###} speed={state.speed:0.###}";
    }

    private bool CanPayNextAttackCost(ItemDrop.ItemData weapon, out string reason)
    {
        reason = "";
        ItemDrop.ItemData.SharedData? sharedData = weapon.m_shared;
        Attack? secondaryAttack = sharedData?.m_secondaryAttack;
        if (secondaryAttack == null)
        {
            reason = "secondary attack is null";
            return false;
        }

        float durabilityCost = Mathf.Max(0f, sharedData!.m_useDurabilityDrain * (_definition?.DurabilityFactor ?? 1f));
        if (durabilityCost > 0f && weapon.m_durability + 0.001f < durabilityCost)
        {
            reason = $"durability {weapon.m_durability:0.###} < {durabilityCost:0.###}";
            return false;
        }

        float stamina = Mathf.Max(0f, secondaryAttack.m_attackStamina);
        if (stamina > 0f && !_humanoid!.HaveStamina(stamina))
        {
            reason = $"stamina cost={stamina:0.###}";
            return false;
        }

        float eitr = Mathf.Max(0f, secondaryAttack.m_attackEitr);
        if (eitr > 0f && !_humanoid!.HaveEitr(eitr))
        {
            reason = $"eitr cost={eitr:0.###}";
            return false;
        }

        float health = Mathf.Max(0f, secondaryAttack.m_attackHealth);
        if (health > 0f && !_humanoid!.HaveHealth(health) && secondaryAttack.m_attackHealthLowBlockUse)
        {
            reason = $"health cost={health:0.###}";
            return false;
        }

        reason = "ok";
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
        SweepTrailResetSystem.ClearWeaponTrails(_currentAttack);
        RestoreSkillRaiseFactor();
        RestoreAnimationSpeed();
    }
}
