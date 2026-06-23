namespace SecondaryAttacks;

internal static class SecondaryAttackWorldApplyContributors
{
    internal static void BeforeDefinitions(
        ObjectDB objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        CaptureOriginalObjectDbState(objectDb);
        RestoreOriginalObjectDbState(objectDb);
        ApplyObjectDbPreDefinitionSystems(objectDb, compiledSnapshot);
    }

    internal static void AfterDefinitions(
        ObjectDB objectDb,
        SecondaryAttackAppliedWorldSnapshot appliedWorldSnapshot,
        bool emitMissingWarnings)
    {
        WeaponEffectManager.ApplyToObjectDb(
            objectDb,
            appliedWorldSnapshot.DefinitionsByPrefabName,
            appliedWorldSnapshot.Effects);
        MagicSummonQualityPresetSystem.ApplyToObjectDb(
            objectDb,
            appliedWorldSnapshot.CompiledSnapshot.MagicSummons);
    }

    internal static void ApplyToZNetScene(
        ZNetScene scene,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        SummonPrefabOverrideSystem.RestoreScene(scene);
        SummonPrefabOverrideSystem.Apply(
            scene,
            compiledSnapshot.MagicSummons,
            compiledSnapshot.SnapshotId,
            emitMissingWarnings);
        MagicSummonQualityPresetSystem.ApplyToZNetScene(scene, compiledSnapshot.MagicSummons);
    }

    private static void CaptureOriginalObjectDbState(ObjectDB objectDb)
    {
        SecondaryAttackObjectDbStateStore.Capture(objectDb);
    }

    private static void RestoreOriginalObjectDbState(ObjectDB objectDb)
    {
        SecondaryAttackObjectDbStateStore.Restore(objectDb);
        MagicSummonQualityPresetSystem.RestoreObjectDb(objectDb);
    }

    private static void ApplyObjectDbPreDefinitionSystems(
        ObjectDB objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot)
    {
        SecondaryAttackCompat.TryInstallWorldApplyHooks();
    }
}
