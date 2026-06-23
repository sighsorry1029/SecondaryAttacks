namespace SecondaryAttacks;

internal static partial class SecondaryAttackDefinitionCompiler
{
    private enum DefinitionValidationDisposition
    {
        Continue,
        Skip,
        EffectOnly
    }

    private readonly struct DefinitionValidationResult
    {
        public DefinitionValidationResult(DefinitionValidationDisposition disposition, Attack? primaryAttack = null)
        {
            Disposition = disposition;
            PrimaryAttack = primaryAttack;
        }

        public DefinitionValidationDisposition Disposition { get; }

        public Attack? PrimaryAttack { get; }
    }

    private static DefinitionValidationResult ValidateDefinitionRequest(
        string prefabName,
        ItemDrop.ItemData.SharedData sharedData,
        NormalizedWeaponConfig weaponConfig,
        DefinitionFeatures features)
    {
        if (!features.WantsSecondaryOverride)
        {
            return !features.HasEffectConfig
                ? new DefinitionValidationResult(DefinitionValidationDisposition.Skip)
                : new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly);
        }

        if (!features.HasSecondaryConfig)
        {
            return !features.HasEffectConfig
                ? new DefinitionValidationResult(DefinitionValidationDisposition.Skip)
                : new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly);
        }

        if (string.IsNullOrWhiteSpace(features.SecondaryType))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: a secondary behavior preset is required.");
            return features.HasEffectConfig
                ? new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly)
                : new DefinitionValidationResult(DefinitionValidationDisposition.Skip);
        }

        if (features.HasCustomPayload &&
            (weaponConfig.Secondary?.Projectile == null || string.IsNullOrWhiteSpace(weaponConfig.Secondary.Projectile.Preset)))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: ranged secondary requires preset.");
            return features.HasEffectConfig
                ? new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly)
                : new DefinitionValidationResult(DefinitionValidationDisposition.Skip);
        }

        if (features.UsesSummonEmpower && features.UsesShieldConvert)
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: summon empower and shield convert cannot be used together on the same weapon.");
            return features.HasEffectConfig
                ? new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly)
                : new DefinitionValidationResult(DefinitionValidationDisposition.Skip);
        }

        Attack primaryAttack = sharedData.m_attack;
        if (primaryAttack == null || string.IsNullOrWhiteSpace(primaryAttack.m_attackAnimation))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: primary attack is missing.");
            return features.HasEffectConfig
                ? new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly)
                : new DefinitionValidationResult(DefinitionValidationDisposition.Skip);
        }

        return new DefinitionValidationResult(DefinitionValidationDisposition.Continue, primaryAttack);
    }
}
