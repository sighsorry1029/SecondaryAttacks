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
        return new SecondaryAttackCompiledSnapshot(
            snapshotId,
            SecondaryAttackNormalizedConfigFacade.FromParsed(
                parsedRanged,
                parsedMelee,
                parsedBloodMagic,
                parsedEffects));
    }
}
