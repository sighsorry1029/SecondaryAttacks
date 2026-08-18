using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class CleavingThrustTrailVisualSystem
{
    internal const string CleavingThrustVisualSessionRpcName = "SecondaryAttacks_CleavingThrustVisualSessionV2";
    private const double CleavingThrustVisualSessionDuration = 1.25d;
    private const float MaxObserverRangeScale = 64f;
    private const double MinVisualSessionDuration = 0.1d;
    private const double MaxVisualSessionDuration = 3d;
    private const double MaxVisualSessionFutureSkew = 0.5d;
    private const double VisualSessionStaleGrace = 1d;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Transform, OriginalTrailTipState> OriginalTrailTipLocalPositions = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Attack, List<Transform>> ScaledTrailTipsByAttack = new();
    private static readonly Dictionary<MeleeWeaponTrail, CleavingThrustObserverTrailScaleController> ObserverTrailScaleControllers = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Attack, object> StartedVisualSessions = new();
    private static uint _nextCleavingThrustVisualSequence;

    internal static void BeginCleavingThrustVisualSession(Attack attack, SecondaryAttackDefinition definition)
    {
        if (attack?.m_character == null ||
            !SecondaryAttackManager.HasCharacterAuthority(attack.m_character) ||
            !TryGetReadyCleavingThrustTrailScale(attack, definition, out float rangeScale))
        {
            return;
        }

        if (StartedVisualSessions.TryGetValue(attack, out _))
        {
            return;
        }

        StartedVisualSessions.Add(attack, new object());

        ApplyTrailScale(attack, rangeScale);
        SendCleavingThrustVisualSession(attack, rangeScale);
    }

    internal static void HandleCleavingThrustVisualSessionRpc(
        Character character,
        ZNetView? nview,
        long sender,
        uint sequence,
        float rangeScale,
        double startedAt,
        double expiresAt)
    {
        if (ZNet.instance != null && ZNet.instance.IsDedicated())
        {
            return;
        }

        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        ZDO? zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
        double sourceDuration = expiresAt - startedAt;
        double sourceAge = now - startedAt;
        if (character == null ||
            nview == null ||
            !nview.IsValid() ||
            nview.IsOwner() ||
            zdo == null ||
            zdo.GetOwner() != sender ||
            sequence == 0U ||
            rangeScale <= 1.0001f ||
            float.IsNaN(rangeScale) ||
            float.IsInfinity(rangeScale) ||
            double.IsNaN(startedAt) ||
            double.IsInfinity(startedAt) ||
            double.IsNaN(expiresAt) ||
            double.IsInfinity(expiresAt) ||
            sourceDuration < MinVisualSessionDuration ||
            sourceDuration > MaxVisualSessionDuration ||
            sourceAge < -MaxVisualSessionFutureSkew ||
            sourceAge > sourceDuration + VisualSessionStaleGrace)
        {
            return;
        }

        rangeScale = Mathf.Clamp(rangeScale, 1f, MaxObserverRangeScale);
        double localExpiresAt = now + sourceDuration;

        CleavingThrustObserverTrailScaleController controller =
            character.GetComponent<CleavingThrustObserverTrailScaleController>() ??
            character.gameObject.AddComponent<CleavingThrustObserverTrailScaleController>();
        controller.Begin(character, sequence, rangeScale, now, localExpiresAt);
    }

    private static void ApplyTrailScale(Attack attack, float rangeScale)
    {
        List<Transform> scaledTips = new();
        foreach (MeleeWeaponTrail trail in WeaponTrailAccess.GetTrails(attack))
        {
            if (!WeaponTrailAccess.TryGetEndpoints(
                    trail,
                    out Transform baseTransform,
                    out Transform tipTransform))
            {
                continue;
            }

            if (!OriginalTrailTipLocalPositions.TryGetValue(tipTransform, out OriginalTrailTipState? originalTipState))
            {
                originalTipState = new OriginalTrailTipState(tipTransform.localPosition);
                OriginalTrailTipLocalPositions.Add(tipTransform, originalTipState);
            }

            Vector3 baseLocalPosition = tipTransform.parent != null
                ? tipTransform.parent.InverseTransformPoint(baseTransform.position)
                : baseTransform.position;
            Vector3 originalDelta = originalTipState.LocalPosition - baseLocalPosition;
            tipTransform.localPosition = baseLocalPosition + originalDelta * rangeScale;
            scaledTips.Add(tipTransform);
        }

        if (scaledTips.Count > 0)
        {
            ScaledTrailTipsByAttack.Remove(attack);
            ScaledTrailTipsByAttack.Add(attack, scaledTips);
        }
    }

    internal static void RestoreTrailScaleForAttack(Attack attack)
    {
        StartedVisualSessions.Remove(attack);
        if (!ScaledTrailTipsByAttack.TryGetValue(attack, out List<Transform>? scaledTips))
        {
            return;
        }

        ScaledTrailTipsByAttack.Remove(attack);
        RestoreTrailTips(scaledTips);
    }

    private static bool TryGetReadyCleavingThrustTrailScale(Attack attack, SecondaryAttackDefinition definition, out float rangeScale)
    {
        rangeScale = 1f;
        CleavingThrustDefinition? cleavingThrust = definition.CleavingThrust;
        if (cleavingThrust == null ||
            !CleavingThrustSystem.CanHandle(attack) ||
            !MeleePresetCooldownSystem.IsReady(
                attack.m_character,
                "cleavingThrust",
                cleavingThrust.PresetCooldown))
        {
            return false;
        }

        rangeScale = CleavingThrustSystem.ResolveVisualRangeScale(attack, definition);
        return rangeScale > 1.0001f;
    }

    private static void SendCleavingThrustVisualSession(Attack attack, float rangeScale)
    {
        if (rangeScale <= 1.0001f ||
            attack?.m_character == null ||
            !SecondaryAttackManager.TryGetCharacterZdo(attack.m_character, out ZNetView? nview, out _) ||
            ZRoutedRpc.instance == null)
        {
            return;
        }

        uint sequence = NextCleavingThrustVisualSequence();
        double startedAt = SecondaryAttackManager.GetNetworkTimeSeconds();
        double expiresAt = startedAt + CleavingThrustVisualSessionDuration;

        nview!.InvokeRPC(
            ZNetView.Everybody,
            CleavingThrustVisualSessionRpcName,
            sequence,
            rangeScale,
            startedAt,
            expiresAt);
    }

    private static uint NextCleavingThrustVisualSequence()
    {
        unchecked
        {
            _nextCleavingThrustVisualSequence++;
            if (_nextCleavingThrustVisualSequence == 0U)
            {
                _nextCleavingThrustVisualSequence = 1U;
            }

            return _nextCleavingThrustVisualSequence;
        }
    }

    internal static ObserverTrailSampleScope BeginObserverTrailSample(MeleeWeaponTrail trail)
    {
        if (trail == null ||
            !ObserverTrailScaleControllers.TryGetValue(trail, out CleavingThrustObserverTrailScaleController? controller))
        {
            return default;
        }

        return controller.BeginTrailSample(trail);
    }

    internal static void EndObserverTrailSample(ObserverTrailSampleScope scope)
    {
        scope.Restore();
    }

    internal static void EndObserverTrailSample(ref ObserverTrailSampleScope scope)
    {
        EndObserverTrailSample(scope);
        scope = default;
    }

    private static void RestoreTrailTips(List<Transform> scaledTips)
    {
        foreach (Transform tipTransform in scaledTips)
        {
            if (tipTransform != null &&
                OriginalTrailTipLocalPositions.TryGetValue(tipTransform, out OriginalTrailTipState? originalTipState))
            {
                tipTransform.localPosition = originalTipState.LocalPosition;
            }
        }
    }

    private sealed class OriginalTrailTipState
    {
        internal OriginalTrailTipState(Vector3 localPosition)
        {
            LocalPosition = localPosition;
        }

        internal Vector3 LocalPosition { get; }
    }

    private sealed class CleavingThrustObserverTrailScaleController : MonoBehaviour
    {
        private readonly List<MeleeWeaponTrail> _registeredTrails = new();
        private Character? _character;
        private GameObject? _rightItemInstance;
        private uint _sequence;
        private bool _hasSequence;
        private float _rangeScale;
        private double _startedAt;
        private double _expiresAt;

        internal void Begin(Character character, uint sequence, float rangeScale, double startedAt, double expiresAt)
        {
            if (_hasSequence && !IsNewerSequence(sequence, _sequence))
            {
                return;
            }

            _character = character;
            _sequence = sequence;
            _hasSequence = true;
            _rangeScale = Mathf.Max(1f, rangeScale);
            _startedAt = startedAt;
            _expiresAt = expiresAt;
            RefreshRegistrations();
            enabled = true;
        }

        private static bool IsNewerSequence(uint candidate, uint current)
        {
            // Serial-number arithmetic keeps duplicate and reverse delivery safe across uint wrap.
            return candidate != current && unchecked((int)(candidate - current)) > 0;
        }

        private void Update()
        {
            if (_character == null ||
                _character.IsDead() ||
                SecondaryAttackManager.HasCharacterAuthority(_character) ||
                SecondaryAttackManager.GetNetworkTimeSeconds() >= _expiresAt)
            {
                Destroy(this);
                return;
            }

            GameObject? currentRightItemInstance = WeaponTrailAccess.GetRightItemInstance(_character);
            if (_rightItemInstance != currentRightItemInstance ||
                _registeredTrails.Count == 0 ||
                HasDestroyedRegisteredTrail())
            {
                RefreshRegistrations(currentRightItemInstance);
            }
        }

        internal ObserverTrailSampleScope BeginTrailSample(MeleeWeaponTrail trail)
        {
            double now = SecondaryAttackManager.GetNetworkTimeSeconds();
            if (_character == null ||
                now < _startedAt ||
                now >= _expiresAt ||
                SecondaryAttackManager.HasCharacterAuthority(_character))
            {
                return default;
            }

            if (!WeaponTrailAccess.TryGetEndpoints(
                    trail,
                    out Transform baseTransform,
                    out Transform tipTransform))
            {
                return default;
            }

            Vector3 basePosition = baseTransform.position;
            Vector3 tipDelta = tipTransform.position - basePosition;
            if (tipDelta.sqrMagnitude <= 0.000001f)
            {
                return default;
            }

            Vector3 originalLocalPosition = tipTransform.localPosition;
            tipTransform.position = basePosition + tipDelta * _rangeScale;
            return new ObserverTrailSampleScope(tipTransform, originalLocalPosition);
        }

        private void RefreshRegistrations(GameObject? rightItemInstance = null)
        {
            if (_character == null)
            {
                return;
            }

            rightItemInstance ??= WeaponTrailAccess.GetRightItemInstance(_character);
            UnregisterTrails();
            _rightItemInstance = rightItemInstance;
            if (rightItemInstance == null)
            {
                return;
            }

            foreach (MeleeWeaponTrail trail in WeaponTrailAccess.GetTrails(rightItemInstance))
            {
                if (trail == null)
                {
                    continue;
                }

                ObserverTrailScaleControllers[trail] = this;
                _registeredTrails.Add(trail);
            }
        }

        private bool HasDestroyedRegisteredTrail()
        {
            for (int index = 0; index < _registeredTrails.Count; index++)
            {
                if (_registeredTrails[index] == null)
                {
                    return true;
                }
            }

            return false;
        }

        private void UnregisterTrails()
        {
            for (int index = 0; index < _registeredTrails.Count; index++)
            {
                MeleeWeaponTrail trail = _registeredTrails[index];
                if (!ReferenceEquals(trail, null) &&
                    ObserverTrailScaleControllers.TryGetValue(trail, out CleavingThrustObserverTrailScaleController? controller) &&
                    ReferenceEquals(controller, this))
                {
                    ObserverTrailScaleControllers.Remove(trail);
                }
            }

            _registeredTrails.Clear();
            _rightItemInstance = null;
        }

        private void OnDestroy()
        {
            UnregisterTrails();
        }
    }

    internal readonly struct ObserverTrailSampleScope
    {
        private readonly Transform? _tipTransform;
        private readonly Vector3 _originalLocalPosition;

        internal ObserverTrailSampleScope(Transform tipTransform, Vector3 originalLocalPosition)
        {
            _tipTransform = tipTransform;
            _originalLocalPosition = originalLocalPosition;
        }

        internal void Restore()
        {
            if (_tipTransform != null)
            {
                _tipTransform.localPosition = _originalLocalPosition;
            }
        }
    }

}

[HarmonyPatch(typeof(Attack), nameof(Attack.Stop))]
internal static class AttackStopCleavingThrustTrailVisualPatch
{
    private static void Postfix(Attack __instance)
    {
        CleavingThrustTrailVisualSystem.RestoreTrailScaleForAttack(__instance);
    }

    private static void Finalizer(Attack __instance)
    {
        CleavingThrustTrailVisualSystem.RestoreTrailScaleForAttack(__instance);
    }
}

[HarmonyPatch(typeof(MeleeWeaponTrail), nameof(MeleeWeaponTrail.CustomFixedUpdate))]
internal static class MeleeWeaponTrailCleavingThrustObserverScalePatch
{
    private static void Prefix(
        MeleeWeaponTrail __instance,
        out CleavingThrustTrailVisualSystem.ObserverTrailSampleScope __state)
    {
        __state = CleavingThrustTrailVisualSystem.BeginObserverTrailSample(__instance);
    }

    private static void Postfix(ref CleavingThrustTrailVisualSystem.ObserverTrailSampleScope __state)
    {
        CleavingThrustTrailVisualSystem.EndObserverTrailSample(ref __state);
    }

    private static void Finalizer(ref CleavingThrustTrailVisualSystem.ObserverTrailSampleScope __state)
    {
        CleavingThrustTrailVisualSystem.EndObserverTrailSample(ref __state);
    }
}
