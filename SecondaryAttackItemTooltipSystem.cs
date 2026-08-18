using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;

namespace SecondaryAttacks;

internal static class SecondaryAttackItemTooltipSystem
{
    private const string HeadingColor = "#FFD27A";
    private const string DescriptionColor = "#D6D6D6";

    internal static void AppendPresetDescriptions(ItemDrop.ItemData item, ref string tooltip)
    {
        bool hasAttackDefinition =
            SecondaryAttackRuntimeFacade.TryGetDefinition(item, out SecondaryAttackDefinition definition);
        bool hasSummonQualityPreset =
            MagicSummonQualityPresetSystem.TryGetItemQualityPreset(
                item,
                out MagicSummonQualityPreset qualityPreset);
        if (!hasAttackDefinition && !hasSummonQualityPreset)
        {
            return;
        }

        int expectedPresetCount = (hasAttackDefinition ? 3 : 0) + (hasSummonQualityPreset ? 1 : 0);
        List<string> presetKeys = new(expectedPresetCount);
        HashSet<string> seenPresetKeys = new(StringComparer.OrdinalIgnoreCase);

        if (hasAttackDefinition)
        {
            AddPresetKey(
                presetKeys,
                seenPresetKeys,
                SecondaryAttackPresetCatalog.GetKey(definition.Behavior));
            AddCompiledMeleePresetKeys(definition, presetKeys, seenPresetKeys);
            if (definition.HarvestSweep != null &&
                definition.SpinningSweep == null)
            {
                AddPresetKey(presetKeys, seenPresetKeys, "harvestSweep");
            }
        }

        if (hasSummonQualityPreset)
        {
            AddPresetKey(
                presetKeys,
                seenPresetKeys,
                qualityPreset == MagicSummonQualityPreset.CountByQuality
                    ? "countByQuality"
                    : "levelByQuality");
        }

        string presetTooltip = BuildPresetTooltip(presetKeys);
        if (string.IsNullOrEmpty(presetTooltip))
        {
            return;
        }

        if (!string.IsNullOrEmpty(tooltip) &&
            tooltip.IndexOf(presetTooltip, StringComparison.Ordinal) >= 0)
        {
            return;
        }

        string separator = string.IsNullOrEmpty(tooltip)
            ? ""
            : tooltip.EndsWith("\n\n", StringComparison.Ordinal)
                ? ""
                : tooltip.EndsWith("\n", StringComparison.Ordinal)
                    ? "\n"
                    : "\n\n";
        tooltip = string.IsNullOrEmpty(tooltip)
            ? presetTooltip
            : $"{tooltip}{separator}{presetTooltip}";
    }

    private static void AddCompiledMeleePresetKeys(
        SecondaryAttackDefinition definition,
        ICollection<string> presetKeys,
        ISet<string> seenPresetKeys)
    {
        if (definition.SneakAmbush != null)
        {
            AddPresetKey(presetKeys, seenPresetKeys, "sneakAmbush");
        }

        if (definition.CleavingThrust != null)
        {
            AddPresetKey(presetKeys, seenPresetKeys, "cleavingThrust");
        }

        if (definition.LaunchSlam != null)
        {
            AddPresetKey(presetKeys, seenPresetKeys, "launchSlam");
        }

        if (definition.KnockbackChain != null)
        {
            AddPresetKey(presetKeys, seenPresetKeys, "knockbackChain");
        }

        if (definition.Aftershock != null)
        {
            AddPresetKey(presetKeys, seenPresetKeys, "aftershock");
        }

        if (definition.RiftTrail != null)
        {
            AddPresetKey(presetKeys, seenPresetKeys, "riftTrail");
        }

        if (definition.FractureLine != null)
        {
            AddPresetKey(presetKeys, seenPresetKeys, "fractureLine");
        }

        if (definition.BehaviorType == SecondaryAttackBehaviorType.CopiedSecondary)
        {
            AddPresetKey(
                presetKeys,
                seenPresetKeys,
                definition.OnProjectileHit == null
                    ? null
                    : SecondaryAttackPresetCatalog.GetKey(definition.OnProjectileHit.Preset));
            if (definition.Boomerang != null)
            {
                AddPresetKey(presetKeys, seenPresetKeys, "boomerang");
            }
        }

        if (definition.SpinningSweep != null)
        {
            AddPresetKey(presetKeys, seenPresetKeys, "spinningSweep");
        }
    }

    private static void AddPresetKey(
        ICollection<string> presetKeys,
        ISet<string> seenPresetKeys,
        string? presetKey)
    {
        if (string.IsNullOrWhiteSpace(presetKey))
        {
            return;
        }

        string resolvedPresetKey = presetKey!.Trim();
        if (seenPresetKeys.Add(resolvedPresetKey))
        {
            presetKeys.Add(resolvedPresetKey);
        }
    }

    private static string BuildPresetTooltip(IReadOnlyList<string> presetKeys)
    {
        StringBuilder builder = new();
        foreach (string presetKey in presetKeys)
        {
            if (!SecondaryAttackPresetCatalog.TryGet(presetKey, out SecondaryAttackPresetInfo presetInfo))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("\n\n");
            }

            bool summonQuality = presetInfo.Group == SecondaryAttackPresetGroup.SummonQuality;
            string heading = summonQuality
                ? SecondaryAttackLocalization.Localize(
                    SecondaryAttackLocalization.ItemTooltipSummonQuality,
                    "Summon Quality")
                : SecondaryAttackLocalization.Localize(
                    SecondaryAttackLocalization.ItemTooltipSecondaryAttack,
                    "Secondary Attack");
            string presetName = SecondaryAttackLocalization.Localize(
                presetInfo.NameToken,
                presetInfo.FallbackName);
            string presetDescription = SecondaryAttackLocalization.Localize(
                presetInfo.DescriptionToken,
                presetInfo.FallbackDescription);

            builder
                .Append("<color=")
                .Append(HeadingColor)
                .Append("><b>")
                .Append(heading)
                .Append(": ")
                .Append(presetName)
                .Append("</b></color>\n<color=")
                .Append(DescriptionColor)
                .Append('>')
                .Append(presetDescription)
                .Append("</color>");
        }

        return builder.ToString();
    }
}

[HarmonyPatch(
    typeof(ItemDrop.ItemData),
    nameof(ItemDrop.ItemData.GetTooltip),
    new[]
    {
        typeof(ItemDrop.ItemData),
        typeof(int),
        typeof(bool),
        typeof(float),
        typeof(int)
    })]
[HarmonyAfter(
    "randyknapp.mods.epicloot",
    "org.bepinex.plugins.jewelcrafting",
    "MidnightsFX.EpicJewels")]
internal static class ItemDataGetTooltipSecondaryAttackPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(
        ItemDrop.ItemData item,
        bool crafting,
        ref string __result)
    {
        if (crafting ||
            item == null ||
            SecondaryAttacksPlugin.SecondaryAttackTooltipsEnabled.Value != SecondaryAttacksPlugin.Toggle.On)
        {
            return;
        }

        SecondaryAttackItemTooltipSystem.AppendPresetDescriptions(item, ref __result);
    }
}
