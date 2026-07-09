using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class GreatSwordSkillScalingSystem
{
    internal const string CleavingThrustTrailScaleRpcName = "SecondaryAttacks_CleavingThrustTrailScale";
    private const float CleavingThrustObserverTrailScaleDuration = 1.25f;
    private static readonly FieldInfo AttackVisEquipmentField = AccessTools.Field(typeof(Attack), "m_visEquipment")!;
    private static readonly FieldInfo VisRightItemInstanceField = AccessTools.Field(typeof(VisEquipment), "m_rightItemInstance")!;
    private static readonly FieldInfo TrailBaseField = AccessTools.Field(typeof(MeleeWeaponTrail), "_base")!;
    private static readonly FieldInfo TrailTipField = AccessTools.Field(typeof(MeleeWeaponTrail), "_tip")!;
    private static readonly Dictionary<Transform, Vector3> OriginalTrailTipLocalPositions = new();
    private static readonly Dictionary<Attack, List<Transform>> ScaledTrailTipsByAttack = new();
    private static readonly Dictionary<MeleeWeaponTrail, CleavingThrustObserverTrailScaleController> ObserverTrailScaleControllers = new();

    internal static AttackRangeScope BeginAttackRangeScope(Attack attack)
    {
        return AttackRangeScope.Inactive;
    }

    internal static void EndAttackRangeScope(Attack attack, AttackRangeScope scope)
    {
        if (!scope.Active)
        {
            return;
        }

        attack.m_attackRange = scope.OriginalRange;
        attack.m_attackRayWidth = scope.OriginalRayWidth;
        attack.m_attackRayWidthCharExtra = scope.OriginalRayWidthCharExtra;
    }

    internal static void ApplyTrailScaleForAttack(Attack attack)
    {
        if (!TryGetConfiguredTrailScale(attack, out float rangeScale))
        {
            return;
        }

        ApplyTrailScale(attack, rangeScale);
        SendCleavingThrustTrailScale(attack, rangeScale);
    }

    internal static void HandleCleavingThrustTrailScaleRpc(Character character, ZNetView? nview, float rangeScale, float duration)
    {
        if (character == null ||
            nview == null ||
            !nview.IsValid() ||
            nview.IsOwner() ||
            rangeScale <= 1.0001f)
        {
            return;
        }

        CleavingThrustObserverTrailScaleController controller =
            character.GetComponent<CleavingThrustObserverTrailScaleController>() ??
            character.gameObject.AddComponent<CleavingThrustObserverTrailScaleController>();
        controller.Begin(character, rangeScale, Mathf.Clamp(duration, 0.1f, 3f));
    }

    internal static void ApplyTrailScaleForActiveDefinition(Attack attack, SecondaryAttackDefinition definition)
    {
        if (!TryGetConfiguredTrailScale(attack, definition, requireSecondaryAttack: false, out float rangeScale))
        {
            return;
        }

        ApplyTrailScale(attack, rangeScale);
    }

    internal static void ApplyCleavingThrustTrailScaleForTriggeredAttack(Attack attack, SecondaryAttackDefinition definition)
    {
        if (!TryGetCleavingThrustTrailScale(attack, definition, out float rangeScale))
        {
            return;
        }

        ApplyTrailScale(attack, rangeScale);
        SendCleavingThrustTrailScale(attack, rangeScale);
    }

    private static void ApplyTrailScale(Attack attack, float rangeScale)
    {
        GameObject? rightItemInstance = GetRightItemInstance(attack);
        if (rightItemInstance == null)
        {
            return;
        }

        List<Transform> scaledTips = new();
        foreach (MeleeWeaponTrail trail in rightItemInstance.GetComponentsInChildren<MeleeWeaponTrail>(includeInactive: true))
        {
            Transform? baseTransform = TrailBaseField.GetValue(trail) as Transform;
            Transform? tipTransform = TrailTipField.GetValue(trail) as Transform;
            if (baseTransform == null || tipTransform == null)
            {
                continue;
            }

            if (!OriginalTrailTipLocalPositions.TryGetValue(tipTransform, out Vector3 originalTipLocalPosition))
            {
                originalTipLocalPosition = tipTransform.localPosition;
                OriginalTrailTipLocalPositions[tipTransform] = originalTipLocalPosition;
            }

            Vector3 baseLocalPosition = tipTransform.parent != null
                ? tipTransform.parent.InverseTransformPoint(baseTransform.position)
                : baseTransform.position;
            Vector3 originalDelta = originalTipLocalPosition - baseLocalPosition;
            tipTransform.localPosition = baseLocalPosition + originalDelta * rangeScale;
            scaledTips.Add(tipTransform);
        }

        if (scaledTips.Count > 0)
        {
            ScaledTrailTipsByAttack[attack] = scaledTips;
        }
    }

    internal static void RestoreTrailScaleForAttack(Attack attack)
    {
        if (!ScaledTrailTipsByAttack.TryGetValue(attack, out List<Transform>? scaledTips))
        {
            return;
        }

        ScaledTrailTipsByAttack.Remove(attack);
        RestoreTrailTips(scaledTips);
    }

    private static bool TryGetConfiguredTrailScale(Attack attack, out float rangeScale)
    {
        rangeScale = 1f;
        if (attack?.m_character == null ||
            attack.m_weapon?.m_shared == null ||
            !IsSecondaryAttack(attack) ||
            !SecondaryAttackRuntimeFacade.TryGetDefinition(attack.m_weapon, out SecondaryAttackDefinition definition))
        {
            return false;
        }

        return TryGetConfiguredTrailScale(attack, definition, requireSecondaryAttack: false, out rangeScale);
    }

    private static bool TryGetConfiguredTrailScale(
        Attack attack,
        SecondaryAttackDefinition definition,
        bool requireSecondaryAttack,
        out float rangeScale)
    {
        rangeScale = 1f;
        if (attack?.m_character == null ||
            attack.m_weapon?.m_shared == null ||
            (requireSecondaryAttack && !IsSecondaryAttack(attack)))
        {
            return false;
        }

        if (TryGetReadyCleavingThrustTrailScale(attack, definition, out float cleavingThrustScale))
        {
            rangeScale = Mathf.Max(rangeScale, cleavingThrustScale);
        }

        return rangeScale > 1.0001f;
    }

    private static bool TryGetCleavingThrustTrailScale(Attack attack, SecondaryAttackDefinition definition, out float rangeScale)
    {
        rangeScale = 1f;
        if (definition.CleavingThrust == null)
        {
            return false;
        }

        rangeScale = CleavingThrustSystem.ResolveVisualRangeScale(attack, definition);
        return rangeScale > 1.0001f;
    }

    private static bool TryGetReadyCleavingThrustTrailScale(Attack attack, SecondaryAttackDefinition definition, out float rangeScale)
    {
        rangeScale = 1f;
        CleavingThrustDefinition? cleavingThrust = definition.CleavingThrust;
        if (cleavingThrust == null ||
            !CleavingThrustSystem.CanHandle(attack) ||
            !MeleePresetCooldownSystem.IsReady(attack.m_character, attack.m_weapon, "cleavingThrust", cleavingThrust.PresetCooldown))
        {
            return false;
        }

        rangeScale = CleavingThrustSystem.ResolveVisualRangeScale(attack, definition);
        return rangeScale > 1.0001f;
    }

    private static bool IsSecondaryAttack(Attack attack)
    {
        if (attack.m_character is Humanoid humanoid && humanoid.m_currentAttack == attack)
        {
            return humanoid.m_currentAttackIsSecondary;
        }

        return attack.m_weapon?.m_shared?.m_secondaryAttack == attack;
    }

    private static GameObject? GetRightItemInstance(Attack attack)
    {
        VisEquipment? visEquipment = AttackVisEquipmentField.GetValue(attack) as VisEquipment;
        GameObject? rightItemInstance = visEquipment != null
            ? VisRightItemInstanceField.GetValue(visEquipment) as GameObject
            : null;
        if (rightItemInstance != null)
        {
            return rightItemInstance;
        }

        return GetRightItemInstance(attack.m_character);
    }

    private static GameObject? GetRightItemInstance(Character? character)
    {
        VisEquipment? visEquipment = character != null ? character.GetComponent<VisEquipment>() : null;
        return visEquipment != null ? VisRightItemInstanceField.GetValue(visEquipment) as GameObject : null;
    }

    private static void SendCleavingThrustTrailScale(Attack attack, float rangeScale)
    {
        if (rangeScale <= 1.0001f ||
            attack?.m_character == null ||
            !SecondaryAttackManager.TryGetCharacterZdo(attack.m_character, out ZNetView? nview, out _) ||
            ZRoutedRpc.instance == null)
        {
            return;
        }

        nview!.InvokeRPC(
            ZNetView.Everybody,
            CleavingThrustTrailScaleRpcName,
            rangeScale,
            CleavingThrustObserverTrailScaleDuration);
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

    private static void RestoreTrailTips(List<Transform> scaledTips)
    {
        foreach (Transform tipTransform in scaledTips)
        {
            if (tipTransform != null && OriginalTrailTipLocalPositions.TryGetValue(tipTransform, out Vector3 originalTipLocalPosition))
            {
                tipTransform.localPosition = originalTipLocalPosition;
            }
        }
    }

    private sealed class CleavingThrustObserverTrailScaleController : MonoBehaviour
    {
        private readonly List<MeleeWeaponTrail> _registeredTrails = new();
        private Character? _character;
        private GameObject? _rightItemInstance;
        private float _rangeScale;
        private float _expiresAt;

        internal void Begin(Character character, float rangeScale, float duration)
        {
            UnregisterTrails();
            _character = character;
            _rangeScale = Mathf.Max(1f, rangeScale);
            _expiresAt = Time.time + Mathf.Max(0.1f, duration);
            _rightItemInstance = null;
            RefreshRegistrations();
            enabled = true;
        }

        private void Update()
        {
            if (_character == null ||
                _character.IsDead() ||
                SecondaryAttackManager.HasCharacterAuthority(_character) ||
                Time.time >= _expiresAt)
            {
                Destroy(this);
                return;
            }

            GameObject? currentRightItemInstance = GetRightItemInstance(_character);
            if (_rightItemInstance != currentRightItemInstance ||
                _registeredTrails.Count == 0 ||
                HasDestroyedRegisteredTrail())
            {
                RefreshRegistrations(currentRightItemInstance);
            }
        }

        internal ObserverTrailSampleScope BeginTrailSample(MeleeWeaponTrail trail)
        {
            if (_character == null ||
                Time.time >= _expiresAt ||
                SecondaryAttackManager.HasCharacterAuthority(_character))
            {
                return default;
            }

            Transform? baseTransform = TrailBaseField.GetValue(trail) as Transform;
            Transform? tipTransform = TrailTipField.GetValue(trail) as Transform;
            if (baseTransform == null || tipTransform == null)
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

            rightItemInstance ??= GetRightItemInstance(_character);
            UnregisterTrails();
            _rightItemInstance = rightItemInstance;
            if (rightItemInstance == null)
            {
                return;
            }

            foreach (MeleeWeaponTrail trail in rightItemInstance.GetComponentsInChildren<MeleeWeaponTrail>(includeInactive: true))
            {
                if (trail == null)
                {
                    continue;
                }

                ObserverTrailScaleControllers[trail] = this;
                _registeredTrails.Add(trail);
            }

            if (_registeredTrails.Count > 0)
            {
                SweepTrailResetSystem.ClearWeaponTrails(_character);
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
                if (trail != null &&
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

    internal readonly struct AttackRangeScope
    {
        public static readonly AttackRangeScope Inactive = new(false, 0f, 0f, 0f);

        public AttackRangeScope(bool active, float originalRange, float originalRayWidth, float originalRayWidthCharExtra)
        {
            Active = active;
            OriginalRange = originalRange;
            OriginalRayWidth = originalRayWidth;
            OriginalRayWidthCharExtra = originalRayWidthCharExtra;
        }

        public bool Active { get; }

        public float OriginalRange { get; }

        public float OriginalRayWidth { get; }

        public float OriginalRayWidthCharExtra { get; }
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
internal static class AttackStartGreatSwordSkillScalingPatch
{
    private static void Postfix(Attack __instance, bool __result)
    {
        if (__result)
        {
            GreatSwordSkillScalingSystem.ApplyTrailScaleForAttack(__instance);
        }
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.Stop))]
internal static class AttackStopGreatSwordSkillScalingPatch
{
    private static void Postfix(Attack __instance)
    {
        GreatSwordSkillScalingSystem.RestoreTrailScaleForAttack(__instance);
    }
}

[HarmonyPatch(typeof(MeleeWeaponTrail), nameof(MeleeWeaponTrail.CustomFixedUpdate))]
internal static class MeleeWeaponTrailCleavingThrustObserverScalePatch
{
    private static void Prefix(
        MeleeWeaponTrail __instance,
        out GreatSwordSkillScalingSystem.ObserverTrailSampleScope __state)
    {
        __state = GreatSwordSkillScalingSystem.BeginObserverTrailSample(__instance);
    }

    private static void Postfix(GreatSwordSkillScalingSystem.ObserverTrailSampleScope __state)
    {
        GreatSwordSkillScalingSystem.EndObserverTrailSample(__state);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
internal static class AttackDoMeleeAttackGreatSwordSkillScalingPatch
{
    private static void Prefix(Attack __instance, out GreatSwordSkillScalingSystem.AttackRangeScope __state)
    {
        __state = GreatSwordSkillScalingSystem.BeginAttackRangeScope(__instance);
    }

    private static void Postfix(Attack __instance, GreatSwordSkillScalingSystem.AttackRangeScope __state)
    {
        GreatSwordSkillScalingSystem.EndAttackRangeScope(__instance, __state);
    }
}
