using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SecondaryAttacks;

internal static partial class SecondaryAttackManager
{
    internal static bool TryCreateSummonEmpowerDefinition(
        string prefabName,
        ItemDrop.ItemData.SharedData sharedData,
        Attack primaryAttack,
        NormalizedWeaponConfig weaponConfig,
        List<ConfiguredWeaponEffectDefinition> configuredEffects,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;
        LogStaffDebug(
            $"TryCreateSummonEmpowerDefinition '{prefabName}': primaryAnimation='{primaryAttack.m_attackAnimation}', primaryProjectile='{primaryAttack.m_attackProjectile?.name ?? "<null>"}'.");
        if (!TryResolveSummonSourcePrefabs(primaryAttack, out List<string> summonSourcePrefabs))
        {
            LogStaffDebug($"TryCreateSummonEmpowerDefinition '{prefabName}' failed: could not resolve summon source prefabs.");
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: summon empower requires a summon projectile with a SpawnAbility payload.");
            return false;
        }

        LogStaffDebug($"TryCreateSummonEmpowerDefinition '{prefabName}' resolved summon prefabs: {string.Join(", ", summonSourcePrefabs)}.");

        string resolvedAttackAnimation = GetNormalizedAttackAnimation(weaponConfig);
        bool hasCustomAttackAnimation = !string.IsNullOrWhiteSpace(resolvedAttackAnimation);
        string attackAnimation = hasCustomAttackAnimation
            ? resolvedAttackAnimation.Trim()
            : primaryAttack.m_attackAnimation.Trim();
        NormalizedSummonEmpowerSecondaryConfig summonEmpowerConfig = weaponConfig.Secondary?.SummonEmpower ?? new NormalizedSummonEmpowerSecondaryConfig();
        float summonEmpowerMoveSpeedFactor = summonEmpowerConfig.MoveSpeedFactor;
        float summonEmpowerAttackSpeedFactor = summonEmpowerConfig.AttackSpeedFactor;

        definition = new SecondaryAttackDefinition
        {
            PrefabName = prefabName,
            AppliesSecondaryOverride = true,
            Behavior = new SummonEmpowerSecondaryBehavior
            {
                SummonSourcePrefabs = summonSourcePrefabs,
                PresetCooldown = CreatePresetCooldown(
                    summonEmpowerConfig.PresetCooldown.Cooldown,
                    summonEmpowerConfig.PresetCooldown.CooldownReductionFactor,
                    ResolveBloodMagicCooldownSkill(summonEmpowerConfig.PresetCooldown)),
                Radius = Mathf.Max(0f, summonEmpowerConfig.Radius),
                Duration = Mathf.Max(0.1f, summonEmpowerConfig.Duration),
                MoveSpeedFactor = Mathf.Max(0.05f, summonEmpowerMoveSpeedFactor),
                AttackSpeedFactor = Mathf.Max(0.05f, summonEmpowerAttackSpeedFactor)
            },
            AttackAnimation = attackAnimation,
            HasCustomAttackAnimation = hasCustomAttackAnimation,
            ResourceMultiplier = Mathf.Max(0f, GetNormalizedResourceMultiplier(weaponConfig)),
            DurabilityFactor = Mathf.Max(0f, GetNormalizedDurabilityFactor(weaponConfig)),
            SneakAmbush = CreateSneakAmbushDefinition(weaponConfig),
            CleavingThrust = CreateCleavingThrustDefinition(weaponConfig),
            LaunchSlam = CreateLaunchSlamDefinition(weaponConfig),
            KnockbackChain = CreateKnockbackChainDefinition(weaponConfig),
            Aftershock = CreateAftershockDefinition(weaponConfig),
            ConfiguredEffects = configuredEffects
        };
        ApplyAttackResourceScaling(definition, primaryAttack, GetNormalizedResourceMultiplier(weaponConfig));
        return true;
    }

    internal static bool TryCreateShieldConvertDefinition(
        string prefabName,
        ItemDrop.ItemData.SharedData sharedData,
        Attack primaryAttack,
        NormalizedWeaponConfig weaponConfig,
        List<ConfiguredWeaponEffectDefinition> configuredEffects,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;
        NormalizedShieldConvertSecondaryConfig shieldConvertConfig = weaponConfig.Secondary?.ShieldConvert ?? new NormalizedShieldConvertSecondaryConfig();
        int shieldStatusEffectHash = sharedData.m_attackStatusEffect ? sharedData.m_attackStatusEffect.NameHash() : 0;
        if (shieldStatusEffectHash == 0)
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: shield convert requires a primary attack shield status effect.");
            return false;
        }

        string resolvedAttackAnimation = GetNormalizedAttackAnimation(weaponConfig);
        bool hasCustomAttackAnimation = !string.IsNullOrWhiteSpace(resolvedAttackAnimation);
        string attackAnimation = hasCustomAttackAnimation
            ? resolvedAttackAnimation.Trim()
            : primaryAttack.m_attackAnimation.Trim();

        definition = new SecondaryAttackDefinition
        {
            PrefabName = prefabName,
            AppliesSecondaryOverride = true,
            Behavior = new ShieldConvertSecondaryBehavior
            {
                ShieldStatusEffectHash = shieldStatusEffectHash,
                PresetCooldown = CreatePresetCooldown(
                    shieldConvertConfig.PresetCooldown.Cooldown,
                    shieldConvertConfig.PresetCooldown.CooldownReductionFactor,
                    ResolveBloodMagicCooldownSkill(shieldConvertConfig.PresetCooldown)),
                Radius = Mathf.Max(0f, shieldConvertConfig.Radius),
                HealFactor = Mathf.Max(0f, shieldConvertConfig.HealFactor)
            },
            AttackAnimation = attackAnimation,
            HasCustomAttackAnimation = hasCustomAttackAnimation,
            ResourceMultiplier = Mathf.Max(0f, GetNormalizedResourceMultiplier(weaponConfig)),
            DurabilityFactor = Mathf.Max(0f, GetNormalizedDurabilityFactor(weaponConfig)),
            SneakAmbush = CreateSneakAmbushDefinition(weaponConfig),
            CleavingThrust = CreateCleavingThrustDefinition(weaponConfig),
            LaunchSlam = CreateLaunchSlamDefinition(weaponConfig),
            KnockbackChain = CreateKnockbackChainDefinition(weaponConfig),
            Aftershock = CreateAftershockDefinition(weaponConfig),
            ConfiguredEffects = configuredEffects
        };
        ApplyAttackResourceScaling(definition, primaryAttack, GetNormalizedResourceMultiplier(weaponConfig));
        return true;
    }

    private static string ResolveBloodMagicCooldownSkill(MeleePresetCooldownDefinition presetCooldown)
    {
        return string.IsNullOrWhiteSpace(presetCooldown.CooldownSkill)
            ? "bloodMagic"
            : presetCooldown.CooldownSkill.Trim();
    }

    private static bool TryResolveSummonSourcePrefabs(Attack primaryAttack, out List<string> summonSourcePrefabs)
    {
        summonSourcePrefabs = new List<string>();
        if (primaryAttack?.m_attackProjectile == null)
        {
            LogStaffDebug("TryResolveSummonSourcePrefabs: primary projectile is <null>.");
            return false;
        }

        SpawnAbility spawnAbility = primaryAttack.m_attackProjectile.GetComponent<SpawnAbility>();
        if (spawnAbility == null || spawnAbility.m_spawnPrefab == null || spawnAbility.m_spawnPrefab.Length == 0)
        {
            LogStaffDebug(
                $"TryResolveSummonSourcePrefabs: projectile '{primaryAttack.m_attackProjectile.name}' missing SpawnAbility or spawn prefabs. Components={DescribeComponents(primaryAttack.m_attackProjectile)}.");
            return false;
        }

        foreach (GameObject spawnPrefab in spawnAbility.m_spawnPrefab)
        {
            if (spawnPrefab == null)
            {
                continue;
            }

            string prefabName = Utils.GetPrefabName(spawnPrefab);
            if (!string.IsNullOrWhiteSpace(prefabName) && !summonSourcePrefabs.Any(existing => string.Equals(existing, prefabName, StringComparison.OrdinalIgnoreCase)))
            {
                summonSourcePrefabs.Add(prefabName);
            }
        }

        return summonSourcePrefabs.Count > 0;
    }
}
