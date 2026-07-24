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
            parsedYaml.BloodMagic);
    }

    public static SecondaryAttackCompiledSnapshot Compile(
        int snapshotId,
        IReadOnlyDictionary<string, RangedWeaponConfig> parsedRanged,
        IReadOnlyDictionary<string, MeleeWeaponConfig> parsedMelee,
        IReadOnlyDictionary<string, BloodMagicWeaponConfig> parsedBloodMagic)
    {
        SecondaryAttackWeaponNormalizationResult weaponNormalization =
            SecondaryAttackWeaponConfigNormalizer.Normalize(parsedRanged, parsedMelee, parsedBloodMagic);
        return new SecondaryAttackCompiledSnapshot(
            snapshotId,
            new NormalizedSecondaryAttackConfigFile
            {
                Weapons = weaponNormalization.Weapons,
                GlobalRangedPresets = weaponNormalization.GlobalRangedPresets,
                GlobalBloodMagicPresets = weaponNormalization.GlobalBloodMagicPresets,
                GlobalMeleeFallback = weaponNormalization.GlobalMeleeFallback,
                MagicSummons = SecondaryAttackMagicSummonNormalizer.Normalize(parsedBloodMagic)
            });
    }
}
