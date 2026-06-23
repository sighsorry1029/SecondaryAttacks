using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class GreatSwordSkillScalingSystem
{
    private static readonly FieldInfo AttackVisEquipmentField = AccessTools.Field(typeof(Attack), "m_visEquipment")!;
    private static readonly FieldInfo VisRightItemInstanceField = AccessTools.Field(typeof(VisEquipment), "m_rightItemInstance")!;
    private static readonly FieldInfo TrailBaseField = AccessTools.Field(typeof(MeleeWeaponTrail), "_base")!;
    private static readonly FieldInfo TrailTipField = AccessTools.Field(typeof(MeleeWeaponTrail), "_tip")!;
    private static readonly Dictionary<Transform, Vector3> OriginalTrailTipLocalPositions = new();
    private static readonly Dictionary<Attack, List<Transform>> ScaledTrailTipsByAttack = new();

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
        foreach (Transform tipTransform in scaledTips)
        {
            if (tipTransform != null && OriginalTrailTipLocalPositions.TryGetValue(tipTransform, out Vector3 originalTipLocalPosition))
            {
                tipTransform.localPosition = originalTipLocalPosition;
            }
        }
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

        visEquipment = attack.m_character != null ? attack.m_character.GetComponent<VisEquipment>() : null;
        return visEquipment != null ? VisRightItemInstanceField.GetValue(visEquipment) as GameObject : null;
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
