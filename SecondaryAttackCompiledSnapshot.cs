using System;
using System.Collections.Generic;

namespace SecondaryAttacks;

internal sealed class SecondaryAttackCompiledSnapshot
{
    public static readonly SecondaryAttackCompiledSnapshot Empty = new(0, new NormalizedSecondaryAttackConfigFile());

    public SecondaryAttackCompiledSnapshot(int snapshotId, NormalizedSecondaryAttackConfigFile config)
    {
        SnapshotId = snapshotId;
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public int SnapshotId { get; }

    public NormalizedSecondaryAttackConfigFile Config { get; }

    public IReadOnlyDictionary<string, NormalizedWeaponConfig> Weapons => Config.Weapons;

    public IReadOnlyDictionary<string, NormalizedWeaponConfig> GlobalRangedPresets => Config.GlobalRangedPresets;

    public IReadOnlyDictionary<string, NormalizedWeaponConfig> GlobalBloodMagicPresets => Config.GlobalBloodMagicPresets;

    public NormalizedWeaponConfig? GlobalMeleeFallback => Config.GlobalMeleeFallback;

    public IReadOnlyDictionary<string, EffectBehaviorConfig> Effects => Config.Effects;

    public IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> MagicSummons => Config.MagicSummons;
}

internal sealed class SecondaryAttackAppliedWorldSnapshot
{
    public static readonly SecondaryAttackAppliedWorldSnapshot Empty =
        new(SecondaryAttackCompiledSnapshot.Empty, new Dictionary<string, SecondaryAttackDefinition>(StringComparer.OrdinalIgnoreCase), 0);

    public SecondaryAttackAppliedWorldSnapshot(
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        IReadOnlyDictionary<string, SecondaryAttackDefinition> definitionsByPrefabName,
        int applyRevision)
    {
        CompiledSnapshot = compiledSnapshot ?? throw new ArgumentNullException(nameof(compiledSnapshot));
        DefinitionsByPrefabName = definitionsByPrefabName ?? throw new ArgumentNullException(nameof(definitionsByPrefabName));
        ApplyRevision = applyRevision;
    }

    public SecondaryAttackCompiledSnapshot CompiledSnapshot { get; }

    public int SnapshotId => CompiledSnapshot.SnapshotId;

    public int ApplyRevision { get; }

    public IReadOnlyDictionary<string, SecondaryAttackDefinition> DefinitionsByPrefabName { get; }

    public IReadOnlyDictionary<string, EffectBehaviorConfig> Effects => CompiledSnapshot.Effects;
}
