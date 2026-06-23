using System;
using System.Collections.Generic;
using System.Linq;

namespace SecondaryAttacks;

internal static class SecondaryAttackMagicSummonNormalizer
{
    private const int MaxSpawnChoiceWeight = 100;

    internal static Dictionary<string, NormalizedMagicSummonOverrideConfig> Normalize(
        IReadOnlyDictionary<string, BloodMagicWeaponConfig> bloodMagic)
    {
        Dictionary<string, NormalizedMagicSummonOverrideConfig> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string entryId, BloodMagicWeaponConfig weaponConfig) in bloodMagic)
        {
            if (string.IsNullOrWhiteSpace(entryId) ||
                entryId.Equals("Global", StringComparison.OrdinalIgnoreCase) ||
                weaponConfig?.Enabled == false ||
                weaponConfig?.Summon == null)
            {
                continue;
            }

            MagicSummonOverrideConfig raw = weaponConfig.Summon;
            string normalizedEntryId = entryId.Trim();
            MagicSummonQualityPreset qualityPreset = NormalizeMagicSummonQualityPreset(raw.QualityPreset, normalizedEntryId);

            List<MagicSummonCloneConfig> rawSpawnChoices = raw.SpawnChoices?
                .Where(summon => summon != null)
                .ToList()
                ?? new List<MagicSummonCloneConfig>();

            List<NormalizedMagicSummonCloneConfig> normalizedSummons = new();
            HashSet<string> clonePrefabNames = new(StringComparer.OrdinalIgnoreCase);
            foreach (MagicSummonCloneConfig rawSummon in rawSpawnChoices)
            {
                int weight = ClampInt(rawSummon.Weight ?? 1, 0, MaxSpawnChoiceWeight);
                if (weight <= 0)
                {
                    continue;
                }

                string sourcePrefab = TrimOrEmpty(rawSummon.SourcePrefab);
                if (string.IsNullOrWhiteSpace(sourcePrefab))
                {
                    SecondaryAttacksPlugin.ModLogger.LogWarning(
                        $"Skipping summon override '{normalizedEntryId}' from {SecondaryAttackYamlDomainRegistry.BloodMagicYamlFileName}: sourcePrefab is required.");
                    continue;
                }

                string clonePrefab = TrimOrEmpty(rawSummon.ClonePrefab);
                if (string.IsNullOrWhiteSpace(clonePrefab))
                {
                    clonePrefab = $"SA_{SanitizeMagicSummonId(normalizedEntryId)}_{SanitizeMagicSummonId(sourcePrefab)}";
                }

                if (!clonePrefabNames.Add(clonePrefab))
                {
                    SecondaryAttacksPlugin.ModLogger.LogWarning(
                        $"Skipping duplicate clonePrefab '{clonePrefab}' in summon override '{normalizedEntryId}'.");
                    continue;
                }

                normalizedSummons.Add(new NormalizedMagicSummonCloneConfig
                {
                    SourcePrefab = sourcePrefab,
                    ClonePrefab = clonePrefab,
                    DisplayName = TrimOrEmpty(rawSummon.DisplayName),
                    Health = rawSummon.Health,
                    Weight = weight
                });
            }

            if (normalizedSummons.Count == 0)
            {
                if (qualityPreset == MagicSummonQualityPreset.None)
                {
                    continue;
                }
            }

            normalized[normalizedEntryId] = new NormalizedMagicSummonOverrideConfig
            {
                EntryId = normalizedEntryId,
                QualityPreset = qualityPreset,
                MaxQuality = ClampInt(raw.MaxQuality ?? 4, 1, 10),
                Summons = normalizedSummons
            };
        }

        return normalized;
    }

    private static string TrimOrEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
    }

    private static MagicSummonQualityPreset NormalizeMagicSummonQualityPreset(string? rawPreset, string entryId)
    {
        string preset = TrimOrEmpty(rawPreset);
        if (string.IsNullOrWhiteSpace(preset) ||
            preset.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            preset.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            preset.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return MagicSummonQualityPreset.None;
        }

        if (preset.Equals("countByQuality", StringComparison.OrdinalIgnoreCase) ||
            preset.Equals("qualityCount", StringComparison.OrdinalIgnoreCase) ||
            preset.Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            return MagicSummonQualityPreset.CountByQuality;
        }

        if (preset.Equals("levelByQuality", StringComparison.OrdinalIgnoreCase) ||
            preset.Equals("qualityLevel", StringComparison.OrdinalIgnoreCase) ||
            preset.Equals("level", StringComparison.OrdinalIgnoreCase))
        {
            return MagicSummonQualityPreset.LevelByQuality;
        }

        SecondaryAttacksPlugin.ModLogger.LogWarning(
            $"Ignoring invalid qualityPreset '{preset}' in summon override '{entryId}' from {SecondaryAttackYamlDomainRegistry.BloodMagicYamlFileName}. Valid values are countByQuality or levelByQuality.");
        return MagicSummonQualityPreset.None;
    }

    private static int ClampInt(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private static string SanitizeMagicSummonId(string value)
    {
        string sanitized = new(value.Select(character => char.IsLetterOrDigit(character) ? character : '_').ToArray());
        sanitized = sanitized.Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "Summon" : sanitized;
    }
}
