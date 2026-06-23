using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecondaryAttacks;

internal static partial class SecondaryAttackDefinitionCompiler
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

        List<ConfiguredWeaponEffectDefinition> configuredEffects = ResolveConfiguredWeaponEffects(prefabName, buildContext.EffectConfigs);
        DefinitionFeatures requestedFeatures = AnalyzeDefinitionFeatures(weaponConfig, configuredEffects);
        if (ShouldIgnoreConfiguredEffectsForProjectilePrimary(sharedData.m_attack, requestedFeatures))
        {
            if (configuredEffects.Count > 0 &&
                SecondaryAttackManager.TryMarkCompatibilityIssueReported($"ignored_projectile_primary_effects:{prefabName}"))
            {
                SecondaryAttacksPlugin.ModLogger.LogWarning(
                    $"Ignoring configured effects on {prefabName}: projectile-primary secondary weapons do not support effect-only assignments together with secondary overrides.");
            }

            configuredEffects = new List<ConfiguredWeaponEffectDefinition>();
        }

        DefinitionFeatures features = AnalyzeDefinitionFeatures(weaponConfig, configuredEffects);
        LogStaffDefinitionCreation(prefabName, sharedData, features);
        DefinitionValidationResult validation = ValidateDefinitionRequest(prefabName, sharedData, weaponConfig, features);
        switch (validation.Disposition)
        {
            case DefinitionValidationDisposition.EffectOnly:
                definition = SecondaryAttackManager.CreateEffectOnlyDefinition(prefabName, weaponConfig, configuredEffects);
                return true;
            case DefinitionValidationDisposition.Skip:
                return false;
            default:
                return TryCreateValidatedDefinition(buildContext, prefabName, sharedData, validation.PrimaryAttack!, weaponConfig, configuredEffects, features, out definition);
        }
    }

    internal static bool HasConfiguredWeaponEffects(
        string prefabName,
        IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs)
    {
        return false;
    }

    private static bool ShouldIgnoreConfiguredEffectsForProjectilePrimary(Attack? primaryAttack, DefinitionFeatures features)
    {
        return features.WantsSecondaryOverride &&
               primaryAttack != null &&
               (primaryAttack.m_attackType == Attack.AttackType.Projectile || primaryAttack.m_attackProjectile != null);
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
               string.Equals(secondary.Projectile.Preset, "none", StringComparison.OrdinalIgnoreCase);
    }

    private static List<ConfiguredWeaponEffectDefinition> ResolveConfiguredWeaponEffects(
        string prefabName,
        IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs)
    {
        return new List<ConfiguredWeaponEffectDefinition>();
    }
}
