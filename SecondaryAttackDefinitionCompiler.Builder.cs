using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecondaryAttacks;

internal static partial class SecondaryAttackDefinitionCompiler
{
    private readonly struct DefinitionFeatures
    {
        public DefinitionFeatures(
            bool hasEffectConfig,
            bool hasSecondaryConfig,
            string secondaryType,
            bool usesSummonEmpower,
            bool usesShieldConvert,
            bool usesAftershock,
            bool usesFractureLine,
            bool hasCustomPayload,
            bool hasCopiedSecondary,
            bool wantsSecondaryOverride,
            float summonEmpowerMoveSpeedFactor,
            float summonEmpowerAttackSpeedFactor)
        {
            HasEffectConfig = hasEffectConfig;
            HasSecondaryConfig = hasSecondaryConfig;
            SecondaryType = secondaryType;
            UsesSummonEmpower = usesSummonEmpower;
            UsesShieldConvert = usesShieldConvert;
            UsesAftershock = usesAftershock;
            UsesFractureLine = usesFractureLine;
            HasCustomPayload = hasCustomPayload;
            HasCopiedSecondary = hasCopiedSecondary;
            WantsSecondaryOverride = wantsSecondaryOverride;
            SummonEmpowerMoveSpeedFactor = summonEmpowerMoveSpeedFactor;
            SummonEmpowerAttackSpeedFactor = summonEmpowerAttackSpeedFactor;
        }

        public bool HasEffectConfig { get; }

        public bool HasSecondaryConfig { get; }

        public string SecondaryType { get; }

        public bool UsesSummonEmpower { get; }

        public bool UsesShieldConvert { get; }

        public bool UsesAftershock { get; }

        public bool UsesFractureLine { get; }

        public bool HasCustomPayload { get; }

        public bool HasCopiedSecondary { get; }

        public bool WantsSecondaryOverride { get; }

        public float SummonEmpowerMoveSpeedFactor { get; }

        public float SummonEmpowerAttackSpeedFactor { get; }
    }

    private static DefinitionFeatures AnalyzeDefinitionFeatures(
        NormalizedWeaponConfig weaponConfig,
        List<ConfiguredWeaponEffectDefinition> configuredEffects)
    {
        bool hasEffectConfig = configuredEffects.Count > 0 ||
                               weaponConfig.SneakAmbush?.Enabled == true ||
                               weaponConfig.CleavingThrust?.Enabled == true ||
                               weaponConfig.LaunchSlam?.Enabled == true ||
                               weaponConfig.KnockbackChain?.Enabled == true ||
                               weaponConfig.Aftershock?.Enabled == true ||
                               weaponConfig.RiftTrail?.Enabled == true ||
                               weaponConfig.FractureLine?.Enabled == true ||
                               weaponConfig.ImpactBurst?.Enabled == true ||
                               weaponConfig.Boomerang?.Enabled == true ||
                               weaponConfig.SpinningSweep?.Enabled == true ||
                               weaponConfig.HarvestSweep?.Enabled == true;
        bool hasSecondaryConfig = weaponConfig.Secondary != null;
        string secondaryType = weaponConfig.Secondary?.Type?.Trim() ?? "";
        bool usesSummonEmpower = secondaryType == "summonEmpower";
        bool usesShieldConvert = secondaryType == "shieldConvert";
        bool usesAftershock = secondaryType == "aftershock";
        bool usesFractureLine = secondaryType == "fractureLine";
        bool hasCustomPayload = secondaryType == "projectile";
        bool hasCopiedSecondary = secondaryType == "copy";
        bool wantsSecondaryOverride = hasSecondaryConfig;
        float summonEmpowerMoveSpeedFactor = SecondaryAttackManager.GetConfiguredSummonEmpowerMoveSpeedFactor(weaponConfig);
        float summonEmpowerAttackSpeedFactor = SecondaryAttackManager.GetConfiguredSummonEmpowerAttackSpeedFactor(weaponConfig);

        return new DefinitionFeatures(
            hasEffectConfig,
            hasSecondaryConfig,
            secondaryType,
            usesSummonEmpower,
            usesShieldConvert,
            usesAftershock,
            usesFractureLine,
            hasCustomPayload,
            hasCopiedSecondary,
            wantsSecondaryOverride,
            summonEmpowerMoveSpeedFactor,
            summonEmpowerAttackSpeedFactor);
    }

    private static void LogStaffDefinitionCreation(string prefabName, ItemDrop.ItemData.SharedData sharedData, DefinitionFeatures features)
    {
        if (!string.Equals(prefabName, "StaffSkeleton", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefabName, "StaffRedTroll", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefabName, "StaffShield", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SecondaryAttackManager.LogStaffDebug(
            $"CreateDefinition '{prefabName}': wantsSecondaryOverride={features.WantsSecondaryOverride}, secondaryType='{features.SecondaryType}', usesSummonEmpower={features.UsesSummonEmpower}, usesShieldConvert={features.UsesShieldConvert}, primaryAnimation='{sharedData.m_attack?.m_attackAnimation ?? "<null>"}', primaryProjectile='{sharedData.m_attack?.m_attackProjectile?.name ?? "<null>"}', summonMoveSpeedFactor={features.SummonEmpowerMoveSpeedFactor:0.###}, summonAttackSpeedFactor={features.SummonEmpowerAttackSpeedFactor:0.###}.");
    }

    private static bool TryCreateValidatedDefinition(
        SecondaryAttackDefinitionBuildContext buildContext,
        string prefabName,
        ItemDrop.ItemData.SharedData sharedData,
        Attack primaryAttack,
        NormalizedWeaponConfig weaponConfig,
        List<ConfiguredWeaponEffectDefinition> configuredEffects,
        DefinitionFeatures features,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;

        if (features.UsesSummonEmpower)
        {
            return SecondaryAttackManager.TryCreateSummonEmpowerDefinition(prefabName, sharedData, primaryAttack, weaponConfig, configuredEffects, out definition);
        }

        if (features.UsesShieldConvert)
        {
            return SecondaryAttackManager.TryCreateShieldConvertDefinition(prefabName, sharedData, primaryAttack, weaponConfig, configuredEffects, out definition);
        }

        if (features.HasCustomPayload)
        {
            if (primaryAttack.m_attackType != Attack.AttackType.Projectile)
            {
                SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: primary attack is not projectile-based.");
                if (features.HasEffectConfig)
                {
                    definition = SecondaryAttackManager.CreateEffectOnlyDefinition(prefabName, weaponConfig, configuredEffects);
                    return true;
                }

                return false;
            }

            return SecondaryAttackManager.TryCreateCustomPayloadDefinition(prefabName, sharedData, primaryAttack, weaponConfig, configuredEffects, out definition);
        }

        if (features.UsesAftershock)
        {
            return SecondaryAttackManager.TryCreateAftershockDefinition(buildContext, prefabName, sharedData, primaryAttack, weaponConfig, configuredEffects, out definition);
        }

        if (features.UsesFractureLine)
        {
            return SecondaryAttackManager.TryCreateFractureLineDefinition(buildContext, prefabName, primaryAttack, weaponConfig, configuredEffects, out definition);
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

                if (features.HasEffectConfig)
                {
                    definition = SecondaryAttackManager.CreateEffectOnlyDefinition(prefabName, weaponConfig, configuredEffects);
                    return true;
                }

                return false;
            }

            return SecondaryAttackManager.TryCreateSecondaryOverrideDefinition(prefabName, sourcePrefabName, primaryAttack, sourceSecondaryAttack!, weaponConfig, configuredEffects, out definition);
        }

        if (features.HasEffectConfig)
        {
            definition = SecondaryAttackManager.CreateEffectOnlyDefinition(prefabName, weaponConfig, configuredEffects);
            return true;
        }

        SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: unsupported secondary.type '{features.SecondaryType}'.");
        return false;
    }
}
