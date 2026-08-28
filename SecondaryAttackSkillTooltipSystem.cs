using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackSkillTooltipSystem
{
    private const string BloodMagicHeadingToken = "$sa_skill_tooltip_blood_magic_heading";
    private const string BloodMagicCooldownToken = "$sa_skill_tooltip_blood_magic_cooldown";
    private const string BloodMagicLifetimeToken = "$sa_skill_tooltip_blood_magic_lifetime";
    private const string BloodMagicMaxHealthCostToken = "$sa_skill_tooltip_blood_magic_max_health_cost";
    private const string BloodMagicHealthGainToken = "$sa_skill_tooltip_blood_magic_health_gain";

    private const string SneakHeadingToken = "$sa_skill_tooltip_sneak_heading";
    private const string SneakBackstabGainToken = "$sa_skill_tooltip_sneak_backstab_gain";
    private const string SneakVisibilityToken = "$sa_skill_tooltip_sneak_visibility";
    private const string SneakMovementToken = "$sa_skill_tooltip_sneak_movement";
    private const string SneakAmbushToken = "$sa_skill_tooltip_sneak_ambush";

    internal static void AppendSkillTooltips(SkillsDialog dialog, Player player)
    {
        Skills? playerSkills = player.GetSkills();
        if (playerSkills == null)
        {
            return;
        }

        List<Skills.Skill> skills = playerSkills.GetSkillList();
        for (int index = 0; index < skills.Count; index++)
        {
            Skills.Skill? skill = skills[index];
            Skills.SkillDef? skillInfo = skill?.m_info;
            if (skillInfo == null ||
                skillInfo.m_skill is not (Skills.SkillType.BloodMagic or Skills.SkillType.Sneak))
            {
                continue;
            }

            UITooltip? tooltip = FindSkillTooltip(
                dialog,
                index,
                skillInfo.m_description);
            if (tooltip == null)
            {
                continue;
            }

            string text = skillInfo.m_skill == Skills.SkillType.BloodMagic
                ? AppendBloodMagicSection(tooltip.m_text)
                : AppendSneakSection(tooltip.m_text);
            if (!string.Equals(text, tooltip.m_text, StringComparison.Ordinal))
            {
                tooltip.Set(
                    tooltip.m_topic,
                    text,
                    tooltip.m_anchor,
                    tooltip.m_fixedPosition);
            }
        }
    }

    private static string AppendBloodMagicSection(string? original)
    {
        original ??= string.Empty;
        if (original.IndexOf(BloodMagicHeadingToken, StringComparison.Ordinal) >= 0)
        {
            return original;
        }

        StringBuilder section = new(BloodMagicHeadingToken);
        if (HasBloodMagicCooldownSkillScaling())
        {
            section.Append('\n').Append(BloodMagicCooldownToken);
        }

        if (SecondaryAttacksPlugin.BloodMagicSummonLifetimeSeconds.Value > 0 &&
            !Mathf.Approximately(
                Mathf.Max(1f, SecondaryAttacksPlugin.BloodMagicSummonLifetimeSkill100Multiplier.Value),
                1f))
        {
            section.Append('\n').Append(BloodMagicLifetimeToken);
        }

        if (SecondaryAttacksPlugin.BloodMagicHealthCostUsesMaxHealth.Value ==
            SecondaryAttacksPlugin.Toggle.On)
        {
            section.Append('\n').Append(BloodMagicMaxHealthCostToken);
        }

        if (SecondaryAttacksPlugin.BloodMagicHealthCostSkillRaiseFactor.Value > 0f)
        {
            section.Append('\n').Append(BloodMagicHealthGainToken);
        }

        if (section.Length == BloodMagicHeadingToken.Length)
        {
            return original;
        }

        return AppendSection(original, section);
    }

    private static string AppendSneakSection(string? original)
    {
        original ??= string.Empty;
        if (original.IndexOf(SneakHeadingToken, StringComparison.Ordinal) >= 0)
        {
            return original;
        }

        StringBuilder section = new(SneakHeadingToken);
        if (SecondaryAttacksPlugin.BackstabSneakSkillRaiseAmount.Value > 0f)
        {
            section.Append('\n').Append(SneakBackstabGainToken);
        }

        float visibilityFactor = Mathf.Clamp(
            SecondaryAttacksPlugin.SneakVisibilitySkillEffectFactor.Value,
            1f,
            2f);
        if (!Mathf.Approximately(visibilityFactor, 1f))
        {
            section.Append('\n').Append(SneakVisibilityToken);
        }

        float movementFactor = Mathf.Clamp(
            SecondaryAttacksPlugin.SneakMovementSpeedSkillFactor.Value,
            1f,
            2f);
        if (!Mathf.Approximately(movementFactor, 1f))
        {
            section.Append('\n').Append(SneakMovementToken);
        }

        if (HasSneakAmbushSkillScaling())
        {
            section.Append('\n').Append(SneakAmbushToken);
        }

        if (section.Length == SneakHeadingToken.Length)
        {
            return original;
        }

        return AppendSection(original, section);
    }

    private static bool HasBloodMagicCooldownSkillScaling()
    {
        foreach (SecondaryAttackDefinition definition in
                 SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.Values)
        {
            MeleePresetCooldownDefinition? cooldown = definition.Behavior switch
            {
                SummonEmpowerSecondaryBehavior summonEmpower => summonEmpower.PresetCooldown,
                ShieldConvertSecondaryBehavior shieldConvert => shieldConvert.PresetCooldown,
                _ => null
            };
            if (cooldown != null &&
                cooldown.Cooldown > 0f &&
                cooldown.CooldownReductionFactor > 0f &&
                UsesBloodMagicSkill(cooldown.CooldownSkill))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UsesBloodMagicSkill(string? skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            return false;
        }

        string normalized = skillName!.Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);
        return normalized.Equals("bloodmagic", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("blood", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSneakAmbushSkillScaling()
    {
        foreach (SecondaryAttackDefinition definition in
                 SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.Values)
        {
            SneakAmbushDefinition? sneakAmbush = definition.SneakAmbush;
            if (sneakAmbush != null &&
                sneakAmbush.ChargeMaxSeconds > 0f &&
                sneakAmbush.ChargeSkillFactor > 1f)
            {
                return true;
            }
        }

        return false;
    }

    private static string AppendSection(string original, StringBuilder section)
    {
        return original.Length > 0
            ? original + "\n\n" + section
            : section.ToString();
    }

    private static UITooltip? FindSkillTooltip(
        SkillsDialog dialog,
        int skillIndex,
        string skillDescription)
    {
        if (dialog.m_elements != null &&
            skillIndex >= 0 &&
            skillIndex < dialog.m_elements.Count)
        {
            GameObject? indexedElement = dialog.m_elements[skillIndex];
            if (indexedElement != null)
            {
                UITooltip? indexedTooltip = indexedElement
                    .GetComponentInChildren<UITooltip>(true);
                if (indexedTooltip != null &&
                    MatchesSkillDescription(indexedTooltip.m_text, skillDescription))
                {
                    return indexedTooltip;
                }
            }
        }

        InventoryGui? inventory = dialog.GetComponentInParent<InventoryGui>();
        if (inventory == null)
        {
            return null;
        }

        UITooltip[] candidates = inventory.GetComponentsInChildren<UITooltip>(true);
        foreach (UITooltip candidate in candidates)
        {
            if (candidate != null &&
                candidate.gameObject.activeInHierarchy &&
                MatchesSkillDescription(candidate.m_text, skillDescription))
            {
                return candidate;
            }
        }

        foreach (UITooltip candidate in candidates)
        {
            if (candidate != null &&
                MatchesSkillDescription(candidate.m_text, skillDescription))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool MatchesSkillDescription(
        string? tooltipText,
        string? skillDescription)
    {
        if (string.IsNullOrWhiteSpace(tooltipText) ||
            string.IsNullOrWhiteSpace(skillDescription))
        {
            return false;
        }

        if (tooltipText!.IndexOf(skillDescription!, StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        if (Localization.instance == null)
        {
            return false;
        }

        string localizedDescription = Localization.instance.Localize(skillDescription!);
        return !string.IsNullOrWhiteSpace(localizedDescription) &&
               !string.Equals(localizedDescription, skillDescription, StringComparison.Ordinal) &&
               tooltipText.IndexOf(localizedDescription, StringComparison.Ordinal) >= 0;
    }
}

[HarmonyPatch(typeof(SkillsDialog), nameof(SkillsDialog.Setup))]
internal static class SkillsDialogSetupSecondaryAttackTooltipPatch
{
    private static bool _failureLogged;

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("randyknapp.mods.epicloot")]
    private static void Postfix(SkillsDialog __instance, Player player)
    {
        if (__instance == null || player == null)
        {
            return;
        }

        try
        {
            SecondaryAttackSkillTooltipSystem.AppendSkillTooltips(__instance, player);
        }
        catch (Exception exception)
        {
            if (_failureLogged)
            {
                return;
            }

            _failureLogged = true;
            SecondaryAttacksPlugin.ModLogger.LogWarning(
                "Could not extend the Blood Magic and Sneak skill tooltips: " +
                exception.GetBaseException().Message);
        }
    }
}
