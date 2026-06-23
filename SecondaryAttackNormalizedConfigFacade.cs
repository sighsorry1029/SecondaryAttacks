using System;
using System.Collections.Generic;

namespace SecondaryAttacks;

internal static class SecondaryAttackNormalizedConfigFacade
{
    internal static NormalizedSecondaryAttackConfigFile FromParsed(
        IReadOnlyDictionary<string, RangedWeaponConfig> ranged,
        IReadOnlyDictionary<string, MeleeWeaponConfig> melee,
        IReadOnlyDictionary<string, BloodMagicWeaponConfig> bloodMagic,
        IReadOnlyDictionary<string, EffectBehaviorConfig> effects)
    {
        SecondaryAttackWeaponNormalizationResult weaponNormalization =
            SecondaryAttackWeaponConfigNormalizer.Normalize(ranged, melee, bloodMagic);
        return new NormalizedSecondaryAttackConfigFile
        {
            Weapons = weaponNormalization.Weapons,
            GlobalRangedPresets = weaponNormalization.GlobalRangedPresets,
            GlobalBloodMagicPresets = weaponNormalization.GlobalBloodMagicPresets,
            GlobalMeleeFallback = weaponNormalization.GlobalMeleeFallback,
            Effects = NormalizeEffects(effects),
            MagicSummons = SecondaryAttackMagicSummonNormalizer.Normalize(bloodMagic)
        };
    }

    private static Dictionary<string, EffectBehaviorConfig> NormalizeEffects(IReadOnlyDictionary<string, EffectBehaviorConfig> effects)
    {
        Dictionary<string, EffectBehaviorConfig> normalizedEffects = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string effectId, EffectBehaviorConfig effectConfig) in effects)
        {
            if (string.IsNullOrWhiteSpace(effectId) || effectConfig == null)
            {
                continue;
            }

            normalizedEffects[effectId.Trim()] = effectConfig;
        }

        return normalizedEffects;
    }
}
