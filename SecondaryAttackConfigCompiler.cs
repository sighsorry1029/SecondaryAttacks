namespace SecondaryAttacks;

internal static class SecondaryAttackConfigCompiler
{
    public static SecondaryAttackCompiledSnapshot Compile(
        int snapshotId,
        SecondaryAttackParsedYaml parsedYaml)
    {
        SecondaryAttackWeaponNormalizationResult weaponNormalization =
            SecondaryAttackWeaponConfigNormalizer.Normalize(
                parsedYaml.Ranged,
                parsedYaml.Melee,
                parsedYaml.BloodMagic);
        return new SecondaryAttackCompiledSnapshot(
            snapshotId,
            weaponNormalization.Weapons,
            weaponNormalization.GlobalRangedPresets,
            weaponNormalization.GlobalBloodMagicPresets,
            weaponNormalization.GlobalMeleeFallback,
            SecondaryAttackMagicSummonNormalizer.Normalize(parsedYaml.BloodMagic));
    }
}
