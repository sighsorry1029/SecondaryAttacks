using System;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackDefinitionCompiler
{
    internal static bool TryCreateDefinition(
        SecondaryAttackDefinitionBuildContext buildContext,
        string prefabName,
        ItemDrop itemDrop,
        NormalizedWeaponConfig weaponConfig,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        if (sharedData == null)
        {
            return false;
        }

        if (!weaponConfig.Enabled)
        {
            return false;
        }

        if (IsPresetOptOut(weaponConfig))
        {
            return false;
        }

        DefinitionFeatures features = AnalyzeDefinitionFeatures(weaponConfig);
        DefinitionValidationResult validation = ValidateDefinitionRequest(prefabName, sharedData, weaponConfig, features);
        switch (validation.Disposition)
        {
            case DefinitionValidationDisposition.EffectOnly:
                definition = SecondaryAttackManager.CreateEffectOnlyDefinition(prefabName, weaponConfig);
                return true;
            case DefinitionValidationDisposition.Skip:
                return false;
            default:
                return TryCreateValidatedDefinition(buildContext, prefabName, sharedData, validation.PrimaryAttack!, weaponConfig, features, out definition);
        }
    }

    private static bool IsPresetOptOut(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedSecondaryModeConfig? secondary = weaponConfig.Secondary;
        if (secondary == null)
        {
            return false;
        }

        if (string.Equals(secondary.Type, "none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(secondary.Type, "projectile", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(secondary.Projectile?.Preset, "none", StringComparison.OrdinalIgnoreCase);
    }

    private readonly struct DefinitionFeatures
    {
        public DefinitionFeatures(
            bool hasMeleeFeatureConfig,
            bool hasSecondaryConfig,
            string secondaryType,
            bool usesSummonEmpower,
            bool usesShieldConvert,
            bool usesAftershock,
            bool usesFractureLine,
            bool hasCustomPayload,
            bool hasCopiedSecondary)
        {
            HasMeleeFeatureConfig = hasMeleeFeatureConfig;
            HasSecondaryConfig = hasSecondaryConfig;
            SecondaryType = secondaryType;
            UsesSummonEmpower = usesSummonEmpower;
            UsesShieldConvert = usesShieldConvert;
            UsesAftershock = usesAftershock;
            UsesFractureLine = usesFractureLine;
            HasCustomPayload = hasCustomPayload;
            HasCopiedSecondary = hasCopiedSecondary;
        }

        public bool HasMeleeFeatureConfig { get; }

        public bool HasSecondaryConfig { get; }

        public string SecondaryType { get; }

        public bool UsesSummonEmpower { get; }

        public bool UsesShieldConvert { get; }

        public bool UsesAftershock { get; }

        public bool UsesFractureLine { get; }

        public bool HasCustomPayload { get; }

        public bool HasCopiedSecondary { get; }
    }

    private static DefinitionFeatures AnalyzeDefinitionFeatures(NormalizedWeaponConfig weaponConfig)
    {
        bool hasMeleeFeatureConfig = weaponConfig.HasEnabledMeleeFeatureConfig;
        bool hasSecondaryConfig = weaponConfig.Secondary != null;
        string secondaryType = weaponConfig.Secondary?.Type?.Trim() ?? "";
        bool usesSummonEmpower = secondaryType == "summonEmpower";
        bool usesShieldConvert = secondaryType == "shieldConvert";
        bool usesAftershock = secondaryType == "aftershock";
        bool usesFractureLine = secondaryType == "fractureLine";
        bool hasCustomPayload = secondaryType == "projectile";
        bool hasCopiedSecondary = secondaryType == "copy";
        return new DefinitionFeatures(
            hasMeleeFeatureConfig,
            hasSecondaryConfig,
            secondaryType,
            usesSummonEmpower,
            usesShieldConvert,
            usesAftershock,
            usesFractureLine,
            hasCustomPayload,
            hasCopiedSecondary);
    }

    private static bool TryCreateValidatedDefinition(
        SecondaryAttackDefinitionBuildContext buildContext,
        string prefabName,
        ItemDrop.ItemData.SharedData sharedData,
        Attack primaryAttack,
        NormalizedWeaponConfig weaponConfig,
        DefinitionFeatures features,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;

        if (features.UsesSummonEmpower)
        {
            return SecondaryAttackManager.TryCreateSummonEmpowerDefinition(prefabName, sharedData, primaryAttack, weaponConfig, out definition);
        }

        if (features.UsesShieldConvert)
        {
            return SecondaryAttackManager.TryCreateShieldConvertDefinition(prefabName, sharedData, primaryAttack, weaponConfig, out definition);
        }

        if (features.HasCustomPayload)
        {
            if (primaryAttack.m_attackType != Attack.AttackType.Projectile)
            {
                SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: primary attack is not projectile-based.");
                if (features.HasMeleeFeatureConfig)
                {
                    definition = SecondaryAttackManager.CreateEffectOnlyDefinition(prefabName, weaponConfig);
                    return true;
                }

                return false;
            }

            return SecondaryAttackManager.TryCreateCustomPayloadDefinition(prefabName, sharedData, primaryAttack, weaponConfig, out definition);
        }

        if (features.UsesAftershock)
        {
            return SecondaryAttackManager.TryCreateAftershockDefinition(buildContext, prefabName, sharedData, primaryAttack, weaponConfig, out definition);
        }

        if (features.UsesFractureLine)
        {
            return SecondaryAttackManager.TryCreateFractureLineDefinition(buildContext, prefabName, primaryAttack, weaponConfig, out definition);
        }

        string sourcePrefabName = string.IsNullOrWhiteSpace(weaponConfig.Secondary?.CopyFrom)
            ? prefabName
            : weaponConfig.Secondary!.CopyFrom.Trim();
        if (features.HasCopiedSecondary)
        {
            if (!SecondaryAttackManager.TryResolveSecondarySourceAttack(buildContext.ObjectDb, sourcePrefabName, out Attack? sourceSecondaryAttack, out string reason))
            {
                if (buildContext.EmitMissingWarnings)
                {
                    SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: {reason}");
                }

                if (features.HasMeleeFeatureConfig)
                {
                    definition = SecondaryAttackManager.CreateEffectOnlyDefinition(prefabName, weaponConfig);
                    return true;
                }

                return false;
            }

            return SecondaryAttackManager.TryCreateSecondaryOverrideDefinition(prefabName, sourcePrefabName, primaryAttack, sourceSecondaryAttack!, weaponConfig, out definition);
        }

        if (features.HasMeleeFeatureConfig)
        {
            definition = SecondaryAttackManager.CreateEffectOnlyDefinition(prefabName, weaponConfig);
            return true;
        }

        SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: unsupported secondary.type '{features.SecondaryType}'.");
        return false;
    }

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
        if (!features.HasSecondaryConfig)
        {
            return !features.HasMeleeFeatureConfig
                ? new DefinitionValidationResult(DefinitionValidationDisposition.Skip)
                : new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly);
        }

        if (string.IsNullOrWhiteSpace(features.SecondaryType))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: a secondary behavior preset is required.");
            return features.HasMeleeFeatureConfig
                ? new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly)
                : new DefinitionValidationResult(DefinitionValidationDisposition.Skip);
        }

        if (features.HasCustomPayload &&
            (weaponConfig.Secondary?.Projectile == null || string.IsNullOrWhiteSpace(weaponConfig.Secondary.Projectile.Preset)))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: ranged secondary requires preset.");
            return features.HasMeleeFeatureConfig
                ? new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly)
                : new DefinitionValidationResult(DefinitionValidationDisposition.Skip);
        }

        Attack primaryAttack = sharedData.m_attack;
        if (primaryAttack == null || string.IsNullOrWhiteSpace(primaryAttack.m_attackAnimation))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: primary attack is missing.");
            return features.HasMeleeFeatureConfig
                ? new DefinitionValidationResult(DefinitionValidationDisposition.EffectOnly)
                : new DefinitionValidationResult(DefinitionValidationDisposition.Skip);
        }

        return new DefinitionValidationResult(DefinitionValidationDisposition.Continue, primaryAttack);
    }
}

internal readonly struct SecondaryAttackDefinitionBuildContext
{
    public SecondaryAttackDefinitionBuildContext(
        ObjectDB objectDb,
        bool emitMissingWarnings)
    {
        ObjectDb = objectDb;
        EmitMissingWarnings = emitMissingWarnings;
    }

    public ObjectDB ObjectDb { get; }

    public bool EmitMissingWarnings { get; }
}
