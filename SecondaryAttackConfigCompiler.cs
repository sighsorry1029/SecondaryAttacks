using System;
using System.Collections.Generic;

namespace SecondaryAttacks;

internal static class SecondaryAttackConfigCompiler
{
    public static SecondaryAttackCompiledSnapshot Compile(
        int snapshotId,
        SecondaryAttackParsedYaml parsedYaml)
    {
        return Compile(
            snapshotId,
            parsedYaml.Ranged,
            parsedYaml.Melee,
            parsedYaml.BloodMagic,
            parsedYaml.Effects);
    }

    public static SecondaryAttackCompiledSnapshot Compile(
        int snapshotId,
        IReadOnlyDictionary<string, RangedWeaponConfig> parsedRanged,
        IReadOnlyDictionary<string, MeleeWeaponConfig> parsedMelee,
        IReadOnlyDictionary<string, BloodMagicWeaponConfig> parsedBloodMagic,
        IReadOnlyDictionary<string, EffectBehaviorConfig> parsedEffects)
    {
        SecondaryAttackWeaponNormalizationResult weaponNormalization =
            SecondaryAttackWeaponConfigNormalizer.Normalize(parsedRanged, parsedMelee, parsedBloodMagic);
        Dictionary<string, EffectBehaviorConfig> normalizedEffects = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string effectId, EffectBehaviorConfig effectConfig) in parsedEffects)
        {
            if (!string.IsNullOrWhiteSpace(effectId) && effectConfig != null)
            {
                normalizedEffects[effectId.Trim()] = effectConfig;
            }
        }

        return new SecondaryAttackCompiledSnapshot(
            snapshotId,
            new NormalizedSecondaryAttackConfigFile
            {
                Weapons = weaponNormalization.Weapons,
                GlobalRangedPresets = weaponNormalization.GlobalRangedPresets,
                GlobalBloodMagicPresets = weaponNormalization.GlobalBloodMagicPresets,
                GlobalMeleeFallback = weaponNormalization.GlobalMeleeFallback,
                Effects = normalizedEffects,
                MagicSummons = SecondaryAttackMagicSummonNormalizer.Normalize(parsedBloodMagic)
            });
    }
}
