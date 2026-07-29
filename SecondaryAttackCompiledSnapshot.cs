using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace SecondaryAttacks;

internal sealed class SecondaryAttackCompiledSnapshot
{
    public static readonly SecondaryAttackCompiledSnapshot Empty = new(
        0,
        new Dictionary<string, NormalizedWeaponConfig>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, NormalizedWeaponConfig>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, NormalizedWeaponConfig>(StringComparer.OrdinalIgnoreCase),
        null,
        new Dictionary<string, NormalizedMagicSummonOverrideConfig>(StringComparer.OrdinalIgnoreCase));

    public SecondaryAttackCompiledSnapshot(
        int snapshotId,
        IReadOnlyDictionary<string, NormalizedWeaponConfig> weapons,
        IReadOnlyDictionary<string, NormalizedWeaponConfig> globalRangedPresets,
        IReadOnlyDictionary<string, NormalizedWeaponConfig> globalBloodMagicPresets,
        NormalizedWeaponConfig? globalMeleeFallback,
        IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> magicSummons)
    {
        SnapshotId = snapshotId;
        Weapons = CopyAsReadOnly(weapons);
        GlobalRangedPresets = CopyAsReadOnly(globalRangedPresets);
        GlobalBloodMagicPresets = CopyAsReadOnly(globalBloodMagicPresets);
        GlobalMeleeFallback = globalMeleeFallback?.Clone();
        MagicSummons = CopyAsReadOnly(magicSummons);
    }

    public int SnapshotId { get; }

    public IReadOnlyDictionary<string, NormalizedWeaponConfig> Weapons { get; }

    public IReadOnlyDictionary<string, NormalizedWeaponConfig> GlobalRangedPresets { get; }

    public IReadOnlyDictionary<string, NormalizedWeaponConfig> GlobalBloodMagicPresets { get; }

    public NormalizedWeaponConfig? GlobalMeleeFallback { get; }

    public IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> MagicSummons { get; }

    private static IReadOnlyDictionary<string, TValue> CopyAsReadOnly<TValue>(
        IReadOnlyDictionary<string, TValue> source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        Dictionary<string, TValue> copy = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, TValue value) in source)
        {
            copy[key] = value;
        }

        return new ReadOnlyDictionary<string, TValue>(copy);
    }
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

}
