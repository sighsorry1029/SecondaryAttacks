using System;
using System.Globalization;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SweepObserverVisualSystem
{
    internal const string RpcName = "SecondaryAttacks_SweepVisual";
    private const int StopAction = 0;
    private const int StartSpinningSweepAction = 1;
    private const int StartHarvestSweepAction = 2;
    private const int RefreshSpinningSweepAction = 3;
    private const int RefreshHarvestSweepAction = 4;
    private const float ObserverTimeoutSeconds = 2f;

    internal static void SendStart(Humanoid humanoid, bool harvestSweep, string animation, float loopStart, float loopEnd, float animationSpeed)
    {
        Send(humanoid, harvestSweep ? StartHarvestSweepAction : StartSpinningSweepAction, animation, loopStart, loopEnd, animationSpeed);
    }

    internal static void SendRefresh(Humanoid humanoid, bool harvestSweep, string animation, float loopStart, float loopEnd, float animationSpeed)
    {
        Send(humanoid, harvestSweep ? RefreshHarvestSweepAction : RefreshSpinningSweepAction, animation, loopStart, loopEnd, animationSpeed);
    }

    internal static void SendStop(Humanoid humanoid)
    {
        if (SecondaryAttackManager.TryGetCharacterZdo(humanoid, out ZNetView? nview, out _) && ZRoutedRpc.instance != null)
        {
            nview!.InvokeRPC(ZNetView.Everybody, RpcName, StopAction, string.Empty);
        }
    }

    internal static void HandleRpc(Character character, ZNetView? nview, int action, string payload)
    {
        if (character == null || nview == null || !nview.IsValid() || nview.IsOwner())
        {
            return;
        }

        SweepObserverVisualController? controller = character.GetComponent<SweepObserverVisualController>();
        if (action == StopAction)
        {
            controller?.Stop();
            return;
        }

        if (!TryParsePayload(payload, out SweepObserverVisualPayload parsed))
        {
            return;
        }

        controller ??= character.gameObject.AddComponent<SweepObserverVisualController>();
        bool triggerAnimation = action is StartSpinningSweepAction or StartHarvestSweepAction;
        bool forceLoopSeek = action is RefreshSpinningSweepAction or RefreshHarvestSweepAction;
        controller.BeginOrRefresh(character, parsed, triggerAnimation, forceLoopSeek, Time.time + ObserverTimeoutSeconds);
    }

    private static void Send(Humanoid humanoid, int action, string animation, float loopStart, float loopEnd, float animationSpeed)
    {
        if (humanoid == null ||
            !SecondaryAttackManager.TryGetCharacterZdo(humanoid, out ZNetView? nview, out _) ||
            ZRoutedRpc.instance == null)
        {
            return;
        }

        nview!.InvokeRPC(ZNetView.Everybody, RpcName, action, CreatePayload(animation, loopStart, loopEnd, animationSpeed));
    }

    private static string CreatePayload(string animation, float loopStart, float loopEnd, float animationSpeed)
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        return string.Join(
            "|",
            animation ?? string.Empty,
            Mathf.Clamp01(loopStart).ToString("R", culture),
            Mathf.Clamp01(loopEnd).ToString("R", culture),
            Mathf.Max(0.01f, animationSpeed).ToString("R", culture));
    }

    private static bool TryParsePayload(string payload, out SweepObserverVisualPayload parsed)
    {
        parsed = default;
        string[] parts = payload.Split('|');
        if (parts.Length < 4)
        {
            return false;
        }

        CultureInfo culture = CultureInfo.InvariantCulture;
        if (!float.TryParse(parts[1], NumberStyles.Float, culture, out float loopStart) ||
            !float.TryParse(parts[2], NumberStyles.Float, culture, out float loopEnd) ||
            !float.TryParse(parts[3], NumberStyles.Float, culture, out float animationSpeed))
        {
            return false;
        }

        parsed = new SweepObserverVisualPayload(
            parts[0],
            Mathf.Clamp01(loopStart),
            Mathf.Clamp(Mathf.Max(loopEnd, loopStart + 0.01f), 0.01f, 1.5f),
            Mathf.Max(0.01f, animationSpeed));
        return true;
    }

    private readonly struct SweepObserverVisualPayload
    {
        public SweepObserverVisualPayload(string animation, float loopStart, float loopEnd, float animationSpeed)
        {
            Animation = animation;
            LoopStart = loopStart;
            LoopEnd = loopEnd;
            AnimationSpeed = animationSpeed;
        }

        public string Animation { get; }

        public float LoopStart { get; }

        public float LoopEnd { get; }

        public float AnimationSpeed { get; }
    }

    private sealed class SweepObserverVisualController : MonoBehaviour
    {
        private static readonly int AttackTagHash = ZSyncAnimation.GetHash("attack");

        private Character? _character;
        private Animator? _animator;
        private ZSyncAnimation? _zanim;
        private SweepObserverVisualPayload _payload;
        private float _expiresAt;
        private float _originalAnimatorSpeed = 1f;
        private int _loopStateHash;
        private bool _hasOriginalAnimatorSpeed;
        private bool _loopRearmed = true;

        internal void BeginOrRefresh(Character character, SweepObserverVisualPayload payload, bool triggerAnimation, bool forceLoopSeek, float expiresAt)
        {
            _character = character;
            _payload = payload;
            _expiresAt = expiresAt;
            _animator ??= character.GetComponentInChildren<Animator>();
            _zanim ??= character.GetComponent<ZSyncAnimation>();
            if (_animator == null)
            {
                Stop();
                return;
            }

            if (!_hasOriginalAnimatorSpeed)
            {
                _originalAnimatorSpeed = _animator.speed;
                _hasOriginalAnimatorSpeed = true;
            }

            ApplyAnimationSpeed();
            if (triggerAnimation && !string.IsNullOrWhiteSpace(payload.Animation))
            {
                _animator.SetTrigger(payload.Animation);
            }

            if (forceLoopSeek)
            {
                SeekToLoopStart();
            }

            enabled = true;
        }

        internal void Stop()
        {
            Destroy(this);
        }

        private void Update()
        {
            if (_character == null ||
                _character.IsDead() ||
                SecondaryAttackManager.HasCharacterAuthority(_character) ||
                Time.time > _expiresAt)
            {
                Stop();
                return;
            }

            _animator ??= _character.GetComponentInChildren<Animator>();
            if (_animator == null)
            {
                Stop();
                return;
            }

            ApplyAnimationSpeed();
            AnimatorStateInfo state = GetAttackAnimatorState(_animator);
            if (!IsAttackState(state))
            {
                return;
            }

            if (_loopStateHash == 0 && state.fullPathHash != 0)
            {
                _loopStateHash = state.fullPathHash;
            }

            if (!_loopRearmed)
            {
                if (state.normalizedTime < _payload.LoopEnd)
                {
                    _loopRearmed = true;
                }

                return;
            }

            if (state.normalizedTime >= _payload.LoopEnd)
            {
                SeekToLoopStart();
                _loopRearmed = false;
            }
        }

        private void ApplyAnimationSpeed()
        {
            if (_animator == null)
            {
                return;
            }

            _zanim?.SetSpeed(_payload.AnimationSpeed);
            _animator.speed = _payload.AnimationSpeed;
        }

        private void SeekToLoopStart()
        {
            if (_animator == null)
            {
                return;
            }

            AnimatorStateInfo state = GetAttackAnimatorState(_animator);
            int stateHash = _loopStateHash != 0 ? _loopStateHash : state.fullPathHash;
            if (stateHash == 0)
            {
                return;
            }

            SweepTrailResetSystem.ClearWeaponTrails(_character);
            _animator.Play(stateHash, 0, _payload.LoopStart);
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

        private void OnDestroy()
        {
            if (_hasOriginalAnimatorSpeed && _animator != null)
            {
                _zanim?.SetSpeed(_originalAnimatorSpeed);
                _animator.speed = _originalAnimatorSpeed;
            }
        }
    }
}
