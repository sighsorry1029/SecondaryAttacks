using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackWorldApplySystem
{
    private const string FixedThrowCarrierPrefab = "SpearFlint";
    private static int _nextApplyRevision = 1;

    public static SecondaryAttackAppliedWorldSnapshot Apply(
        ObjectDB objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        if (objectDb == null)
        {
            return SecondaryAttackAppliedWorldSnapshot.Empty;
        }

        SecondaryAttackWorldApplyContributors.BeforeDefinitions(objectDb, compiledSnapshot, emitMissingWarnings);
        SecondaryAttackManager.ResetWorldApplyTransientState();
        SecondaryAttackDefinitionBuildContext buildContext = new(objectDb, compiledSnapshot.Effects, emitMissingWarnings);

        Dictionary<string, SecondaryAttackDefinition> appliedDefinitions = new(StringComparer.OrdinalIgnoreCase);
        int appliedCount = 0;
        int appliedGlobalRangedFallbackCount = 0;
        int appliedGlobalBloodMagicFallbackCount = 0;
        int appliedGlobalMeleeFallbackCount = 0;
        int appliedEffectOnlyCount = 0;
        HashSet<string> seenConfiguredPrefabs = new(StringComparer.OrdinalIgnoreCase);

        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null)
            {
                continue;
            }

            ItemDrop itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop == null)
            {
                continue;
            }

            SecondaryAttackManager.DumpRuntimeWeaponProfile(itemPrefab.name, itemDrop.m_itemData.m_shared);

            bool usesGlobalMeleeFallback = false;
            bool usesGlobalRangedFallback = false;
            bool usesGlobalBloodMagicFallback = false;
            bool usesEffectOnlyFallback = false;
            compiledSnapshot.Weapons.TryGetValue(itemPrefab.name, out NormalizedWeaponConfig? weaponConfig);
            if (weaponConfig != null)
            {
                seenConfiguredPrefabs.Add(itemPrefab.name);
                if (weaponConfig.Enabled && !weaponConfig.UseAutomaticFallback)
                {
                    ApplyDefaultMeleeFallbacksIfNeeded(itemDrop, weaponConfig, compiledSnapshot.GlobalMeleeFallback);
                }
                else
                {
                    weaponConfig = null;
                }
            }

            if (weaponConfig == null)
            {
                if (TryCreateDefaultBloodMagicFallback(itemPrefab.name, itemDrop, compiledSnapshot.GlobalBloodMagicPresets, out NormalizedWeaponConfig? defaultBloodMagicFallback))
                {
                    weaponConfig = defaultBloodMagicFallback!;
                    usesGlobalBloodMagicFallback = true;
                }
                else if (TryCreateDefaultRangedFallback(itemDrop, compiledSnapshot.GlobalRangedPresets, out NormalizedWeaponConfig? defaultRangedFallback))
                {
                    weaponConfig = defaultRangedFallback!;
                    usesGlobalRangedFallback = true;
                }
                else if (TryCreateDefaultMeleeFallback(itemDrop, compiledSnapshot.GlobalMeleeFallback, out NormalizedWeaponConfig? defaultMeleeFallback))
                {
                    weaponConfig = defaultMeleeFallback!;
                    usesGlobalMeleeFallback = true;
                }
                else if (SecondaryAttackDefinitionCompiler.HasConfiguredWeaponEffects(itemPrefab.name, compiledSnapshot.Effects))
                {
                    weaponConfig = new NormalizedWeaponConfig();
                    usesEffectOnlyFallback = true;
                }
                else
                {
                    continue;
                }
            }

            if (!SecondaryAttackDefinitionCompiler.TryCreateDefinition(buildContext, itemPrefab.name, itemDrop, weaponConfig, out SecondaryAttackDefinition? definition))
            {
                continue;
            }

            SecondaryAttackDefinition resolvedDefinition = definition!;
            resolvedDefinition.CooldownFallbackSecondaryAttack = ResolveCooldownFallbackSecondaryAttack(objectDb, itemPrefab.name, itemDrop);
            appliedDefinitions[itemPrefab.name] = resolvedDefinition;
            if (resolvedDefinition.AppliesSecondaryOverride)
            {
                Attack sourceAttack = SecondaryAttackManager.ResolveSourceAttack(objectDb, itemDrop, resolvedDefinition);
                Attack configuredSecondaryAttack = SecondaryAttackManager.BuildSecondaryAttack(sourceAttack, resolvedDefinition);
                SecondaryAttackManager.NormalizeCopiedProjectileAim(configuredSecondaryAttack, resolvedDefinition);
                resolvedDefinition.ConfiguredSecondaryAttack = SecondaryAttackManager.CloneAttack(configuredSecondaryAttack);
                if (!ProjectilePresetCooldownFallback.UsesDynamicOriginalSecondary(resolvedDefinition))
                {
                    itemDrop.m_itemData.m_shared.m_secondaryAttack = configuredSecondaryAttack;
                }
            }

            appliedCount++;
            if (usesGlobalRangedFallback)
            {
                appliedGlobalRangedFallbackCount++;
            }

            if (usesGlobalBloodMagicFallback)
            {
                appliedGlobalBloodMagicFallbackCount++;
            }

            if (usesGlobalMeleeFallback)
            {
                appliedGlobalMeleeFallbackCount++;
            }

            if (usesEffectOnlyFallback)
            {
                appliedEffectOnlyCount++;
            }
        }

        SecondaryAttackAppliedWorldSnapshot appliedWorldSnapshot = new(compiledSnapshot, appliedDefinitions, _nextApplyRevision++);

        foreach (string configuredPrefabName in compiledSnapshot.Weapons.Keys.Where(key => !seenConfiguredPrefabs.Contains(key)))
        {
            if (!emitMissingWarnings)
            {
                continue;
            }

            string warningKey = $"missing_objectdb_prefab:{configuredPrefabName}";
            if (SecondaryAttackManager.TryMarkCompatibilityWarningReported(warningKey))
            {
                SecondaryAttacksPlugin.ModLogger.LogWarning($"Configured prefab '{configuredPrefabName}' was not found in ObjectDB.");
            }
        }

        SecondaryAttackWorldApplyContributors.AfterDefinitions(objectDb, appliedWorldSnapshot, emitMissingWarnings);
        SecondaryAttacksPlugin.ModLogger.LogInfo($"Applied {appliedCount} secondary attack definition(s), including {appliedGlobalRangedFallbackCount} global ranged fallback definition(s), {appliedGlobalBloodMagicFallbackCount} global blood magic fallback definition(s), {appliedGlobalMeleeFallbackCount} global melee fallback definition(s), and {appliedEffectOnlyCount} effect-only definition(s).");
        return appliedWorldSnapshot;
    }

    private static bool TryCreateDefaultBloodMagicFallback(
        string prefabName,
        ItemDrop itemDrop,
        IReadOnlyDictionary<string, NormalizedWeaponConfig> globalBloodMagicPresets,
        out NormalizedWeaponConfig? fallback)
    {
        fallback = null;
        if (globalBloodMagicPresets.Count == 0)
        {
            return false;
        }

        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        Attack? primaryAttack = sharedData?.m_attack;
        if (sharedData == null || primaryAttack == null || sharedData.m_skillType != Skills.SkillType.BloodMagic)
        {
            return false;
        }

        if (IsDefaultShieldConvertWeapon(prefabName) &&
            globalBloodMagicPresets.TryGetValue("shieldConvert", out NormalizedWeaponConfig? shieldConvertFallback))
        {
            fallback = shieldConvertFallback;
            return true;
        }

        if (IsDefaultSummonEmpowerWeapon(primaryAttack) &&
            globalBloodMagicPresets.TryGetValue("summonEmpower", out NormalizedWeaponConfig? summonEmpowerFallback))
        {
            fallback = summonEmpowerFallback;
            return true;
        }

        return false;
    }

    private static bool IsDefaultShieldConvertWeapon(string prefabName)
    {
        return string.Equals(prefabName, "StaffShield", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultSummonEmpowerWeapon(Attack primaryAttack)
    {
        if (primaryAttack.m_attackProjectile == null)
        {
            return false;
        }

        SpawnAbility? spawnAbility = primaryAttack.m_attackProjectile.GetComponent<SpawnAbility>();
        return spawnAbility?.m_spawnPrefab != null &&
               spawnAbility.m_spawnPrefab.Any(spawnPrefab => spawnPrefab != null);
    }

    private static bool TryCreateDefaultRangedFallback(
        ItemDrop itemDrop,
        IReadOnlyDictionary<string, NormalizedWeaponConfig> globalRangedPresets,
        out NormalizedWeaponConfig? fallback)
    {
        fallback = null;
        if (!TryResolveRangedPresetName(itemDrop, out string presetName))
        {
            return false;
        }

        if (globalRangedPresets.TryGetValue(presetName, out NormalizedWeaponConfig? configuredFallback))
        {
            fallback = configuredFallback;
            return true;
        }

        fallback = SecondaryAttackWeaponConfigNormalizer.FromRangedRaw(new RangedWeaponConfig
        {
            Preset = presetName
        });
        return true;
    }

    private static bool TryResolveRangedPresetName(ItemDrop itemDrop, out string presetName)
    {
        presetName = "";
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        Attack? primaryAttack = sharedData?.m_attack;
        if (sharedData == null || primaryAttack == null)
        {
            return false;
        }

        if (IsAmmoItemType(sharedData.m_itemType) ||
            string.IsNullOrWhiteSpace(primaryAttack.m_attackAnimation))
        {
            return false;
        }

        if (TryResolveBombPresetName(primaryAttack, out presetName))
        {
            return !string.IsNullOrWhiteSpace(presetName);
        }

        if (sharedData.m_skillType == Skills.SkillType.ElementalMagic)
        {
            string animation = primaryAttack.m_attackAnimation ?? "";
            if (string.Equals(animation, "staff_fireball", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetRangedPresetName(SecondaryAttacksPlugin.FireballStaffPreset.Value, out presetName);
            }

            if (string.Equals(animation, "staff_rapidfire", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetRangedPresetName(SecondaryAttacksPlugin.RapidStaffPreset.Value, out presetName);
            }

            if (string.Equals(animation, "staff_lightningshot", StringComparison.OrdinalIgnoreCase))
            {
                return TryGetRangedPresetName(SecondaryAttacksPlugin.LightningStaffPreset.Value, out presetName);
            }
        }

        if (sharedData.m_skillType == Skills.SkillType.Crossbows ||
            (primaryAttack.m_requiresReload &&
             primaryAttack.m_attackType == Attack.AttackType.Projectile &&
             !string.IsNullOrWhiteSpace(sharedData.m_ammoType)))
        {
            return TryGetRangedPresetName(SecondaryAttacksPlugin.CrossbowPreset.Value, out presetName);
        }

        if (sharedData.m_itemType == ItemDrop.ItemData.ItemType.Bow ||
            sharedData.m_skillType == Skills.SkillType.Bows)
        {
            return TryGetRangedPresetName(SecondaryAttacksPlugin.BowPreset.Value, out presetName);
        }

        return false;
    }

    private static bool IsAmmoItemType(ItemDrop.ItemData.ItemType itemType)
    {
        return itemType is ItemDrop.ItemData.ItemType.Ammo or ItemDrop.ItemData.ItemType.AmmoNonEquipable;
    }

    private static bool TryResolveBombPresetName(
        Attack primaryAttack,
        out string presetName)
    {
        presetName = "";
        if (!IsBombProjectileAttack(primaryAttack))
        {
            return false;
        }

        SecondaryAttacksPlugin.BombPresetSelection configuredPreset = SecondaryAttacksPlugin.BombPreset.Value;
        presetName = configuredPreset switch
        {
            SecondaryAttacksPlugin.BombPresetSelection.StickyDetonator => ProjectileRuntimeSystem.GetPresetName(SecondaryAttackPreset.StickyDetonator),
            SecondaryAttacksPlugin.BombPresetSelection.OverchargedBomb => ProjectileRuntimeSystem.GetPresetName(SecondaryAttackPreset.OverchargedBomb),
            _ => BombProjectileSpawnsAoe(primaryAttack.m_attackProjectile)
                ? ProjectileRuntimeSystem.GetPresetName(SecondaryAttackPreset.OverchargedBomb)
                : ProjectileRuntimeSystem.GetPresetName(SecondaryAttackPreset.StickyDetonator)
        };
        return true;
    }

    private static bool IsBombProjectileAttack(Attack primaryAttack)
    {
        return primaryAttack.m_attackProjectile != null &&
               primaryAttack.m_attackProjectile.GetComponent<Projectile>() != null &&
               (primaryAttack.m_attackType == Attack.AttackType.Projectile || primaryAttack.m_attackProjectile.GetComponent<IProjectile>() != null) &&
               string.Equals(primaryAttack.m_attackAnimation, "throw_bomb", StringComparison.OrdinalIgnoreCase);
    }

    private static bool BombProjectileSpawnsAoe(GameObject? projectilePrefab)
    {
        if (projectilePrefab == null)
        {
            return false;
        }

        Projectile? projectile = projectilePrefab.GetComponent<Projectile>();
        if (projectile == null)
        {
            return false;
        }

        if (projectile.m_aoe > 0f)
        {
            return true;
        }

        if (PrefabContainsAoe(projectile.m_spawnOnHit))
        {
            return true;
        }

        List<GameObject>? randomSpawnOnHit = projectile.m_randomSpawnOnHit;
        if (randomSpawnOnHit == null)
        {
            return false;
        }

        foreach (GameObject spawnPrefab in randomSpawnOnHit)
        {
            if (PrefabContainsAoe(spawnPrefab))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PrefabContainsAoe(GameObject? prefab)
    {
        return PrefabContainsAoe(prefab, new HashSet<GameObject>());
    }

    private static bool PrefabContainsAoe(GameObject? prefab, HashSet<GameObject> visitedPrefabs)
    {
        if (prefab == null || !visitedPrefabs.Add(prefab))
        {
            return false;
        }

        if (prefab.GetComponentInChildren<Aoe>(true) != null)
        {
            return true;
        }

        foreach (SpawnAbility spawnAbility in prefab.GetComponentsInChildren<SpawnAbility>(true))
        {
            foreach (GameObject spawnPrefab in spawnAbility.m_spawnPrefab ?? Array.Empty<GameObject>())
            {
                if (PrefabContainsAoe(spawnPrefab, visitedPrefabs))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetRangedPresetName(SecondaryAttacksPlugin.RangedPresetSelection selection, out string presetName)
    {
        presetName = "";
        if (!Enum.TryParse(selection.ToString(), out SecondaryAttackPreset preset))
        {
            return false;
        }

        presetName = ProjectileRuntimeSystem.GetPresetName(preset);
        return true;
    }

    private static bool ShouldApplyGlobalMeleeFallback(ItemDrop itemDrop, NormalizedWeaponConfig? globalMeleeFallback)
    {
        return TryCreateDefaultMeleeFallback(itemDrop, globalMeleeFallback, out _);
    }

    private static bool IsDefaultSneakAmbushWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Knives;
    }

    private static bool IsDefaultCleavingThrustWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Swords &&
               sharedData.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon;
    }

    private static bool IsDefaultRiftTrailWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Swords &&
               sharedData.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon &&
               sharedData.m_secondaryAttack != null &&
               string.Equals(sharedData.m_secondaryAttack.m_attackAnimation, "sword_secondary", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultLaunchSlamWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Clubs &&
               sharedData.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon;
    }

    private static bool IsDefaultKnockbackChainWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Unarmed &&
               sharedData.m_secondaryAttack != null &&
               string.Equals(sharedData.m_secondaryAttack.m_attackAnimation, "unarmed_kick", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultAftershockWeapon(ItemDrop itemDrop)
    {
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        string prefabName = itemDrop.name ?? "";
        return sharedData?.m_skillType == Skills.SkillType.Clubs &&
               sharedData.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon &&
               prefabName.Contains("Sledge", StringComparison.OrdinalIgnoreCase) &&
               (sharedData.m_attack?.m_attackType == Attack.AttackType.Area ||
                sharedData.m_secondaryAttack?.m_attackType == Attack.AttackType.Area);
    }

    private static bool IsDefaultSpinningSweepWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Polearms &&
               sharedData.m_secondaryAttack != null &&
               string.Equals(sharedData.m_secondaryAttack.m_attackAnimation, "atgeir_secondary", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultHarvestSweepWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Farming;
    }

    private static bool IsDefaultFractureLineWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Pickaxes;
    }

    private static bool IsDefaultSpearRainWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        Attack? secondaryAttack = sharedData?.m_secondaryAttack;
        return sharedData?.m_skillType == Skills.SkillType.Spears &&
               secondaryAttack != null &&
               !string.IsNullOrWhiteSpace(secondaryAttack.m_attackAnimation);
    }

    private static bool IsDefaultImpactBurstWeapon(ItemDrop itemDrop)
    {
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        if (sharedData?.m_skillType != Skills.SkillType.Axes ||
            sharedData.m_itemType != ItemDrop.ItemData.ItemType.TwoHandedWeapon)
        {
            return false;
        }

        string prefabName = itemDrop.name ?? "";
        if (prefabName.Contains("DualAxe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string secondaryAnimation = sharedData.m_secondaryAttack?.m_attackAnimation ?? "";
        if (!string.Equals(secondaryAnimation, "battleaxe_secondary", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (prefabName.Contains("Battleaxe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string primaryAnimation = sharedData.m_attack?.m_attackAnimation ?? "";
        return (primaryAnimation.Contains("battleaxe", StringComparison.OrdinalIgnoreCase) ||
                secondaryAnimation.Contains("battleaxe", StringComparison.OrdinalIgnoreCase)) &&
               !primaryAnimation.Contains("dualaxe", StringComparison.OrdinalIgnoreCase) &&
               !secondaryAnimation.Contains("dualaxe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDefaultBoomerangWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Axes &&
               sharedData.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon;
    }

    private static bool TryCreateDefaultMeleeFallback(
        ItemDrop itemDrop,
        NormalizedWeaponConfig? globalMeleeFallback,
        out NormalizedWeaponConfig? defaultMeleeFallback)
    {
        defaultMeleeFallback = null;
        if (globalMeleeFallback == null || !globalMeleeFallback.Enabled)
        {
            return false;
        }

        NormalizedSneakAmbushConfig? sneakAmbush = globalMeleeFallback.SneakAmbush?.Enabled == true &&
                                               IsDefaultSneakAmbushWeapon(itemDrop.m_itemData?.m_shared)
            ? globalMeleeFallback.SneakAmbush.Clone()
            : null;
        NormalizedCleavingThrustConfig? cleavingThrust = globalMeleeFallback.CleavingThrust?.Enabled == true &&
                                                     IsDefaultCleavingThrustWeapon(itemDrop.m_itemData?.m_shared)
            ? globalMeleeFallback.CleavingThrust.Clone()
            : null;
        NormalizedRiftTrailConfig? riftTrail = globalMeleeFallback.RiftTrail?.Enabled == true &&
                                               IsDefaultRiftTrailWeapon(itemDrop.m_itemData?.m_shared)
            ? globalMeleeFallback.RiftTrail.Clone()
            : null;
        NormalizedLaunchSlamConfig? launchSlam = globalMeleeFallback.LaunchSlam?.Enabled == true &&
                                                 IsDefaultLaunchSlamWeapon(itemDrop.m_itemData?.m_shared)
            ? globalMeleeFallback.LaunchSlam.Clone()
            : null;
        NormalizedKnockbackChainConfig? knockbackChain = globalMeleeFallback.KnockbackChain?.Enabled == true &&
                                                         IsDefaultKnockbackChainWeapon(itemDrop.m_itemData?.m_shared)
            ? globalMeleeFallback.KnockbackChain.Clone()
            : null;
        NormalizedAftershockConfig? aftershock = globalMeleeFallback.Aftershock?.Enabled == true &&
                                                 IsDefaultAftershockWeapon(itemDrop)
            ? globalMeleeFallback.Aftershock.Clone()
            : null;
        NormalizedHarvestSweepConfig? harvestSweep = globalMeleeFallback.HarvestSweep?.Enabled == true &&
                                                     IsDefaultHarvestSweepWeapon(itemDrop.m_itemData?.m_shared)
            ? globalMeleeFallback.HarvestSweep.Clone()
            : null;
        NormalizedSpinningSweepConfig? spinningSweep = globalMeleeFallback.SpinningSweep?.Enabled == true &&
                                                       IsDefaultSpinningSweepWeapon(itemDrop.m_itemData?.m_shared)
            ? globalMeleeFallback.SpinningSweep.Clone()
            : null;
        NormalizedFractureLineConfig? fractureLine = globalMeleeFallback.FractureLine?.Enabled == true &&
                                                     IsDefaultFractureLineWeapon(itemDrop.m_itemData?.m_shared)
            ? globalMeleeFallback.FractureLine.Clone()
            : null;
        NormalizedMeleeOnProjectileHitConfig? spearRain = globalMeleeFallback.SpearRain?.Enabled == true &&
                                                          IsDefaultSpearRainWeapon(itemDrop.m_itemData?.m_shared)
            ? globalMeleeFallback.SpearRain.Clone()
            : null;
        NormalizedImpactBurstConfig? impactBurst = globalMeleeFallback.ImpactBurst?.Enabled == true &&
                                                   IsDefaultImpactBurstWeapon(itemDrop)
            ? globalMeleeFallback.ImpactBurst.Clone()
            : null;
        NormalizedBoomerangConfig? boomerang = globalMeleeFallback.Boomerang?.Enabled == true &&
                                               IsDefaultBoomerangWeapon(itemDrop.m_itemData?.m_shared)
            ? globalMeleeFallback.Boomerang.Clone()
            : null;
        if (sneakAmbush == null && cleavingThrust == null && riftTrail == null && launchSlam == null && knockbackChain == null && aftershock == null && harvestSweep == null && fractureLine == null && spearRain == null && impactBurst == null && boomerang == null && spinningSweep == null)
        {
            return false;
        }

        defaultMeleeFallback = new NormalizedWeaponConfig
        {
            Secondary = CreateDefaultMeleeSecondary(
                globalMeleeFallback.Secondary,
                aftershock,
                fractureLine,
                spearRain,
                impactBurst,
                boomerang,
                spinningSweep,
                harvestSweep),
            SneakAmbush = sneakAmbush,
            CleavingThrust = cleavingThrust,
            RiftTrail = riftTrail,
            LaunchSlam = launchSlam,
            KnockbackChain = knockbackChain,
            Aftershock = aftershock,
            HarvestSweep = harvestSweep,
            FractureLine = fractureLine,
            SpearRain = spearRain,
            ImpactBurst = impactBurst,
            Boomerang = boomerang,
            SpinningSweep = spinningSweep,
            MeleePreset = ResolveDefaultMeleePreset(sneakAmbush, cleavingThrust, riftTrail, launchSlam, knockbackChain, aftershock, fractureLine, spearRain, impactBurst, boomerang, spinningSweep),
            HasExplicitMeleePreset = false
        };
        return true;
    }

    private static NormalizedSecondaryModeConfig? CreateDefaultMeleeSecondary(
        NormalizedSecondaryModeConfig? source,
        NormalizedAftershockConfig? aftershock,
        NormalizedFractureLineConfig? fractureLine,
        NormalizedMeleeOnProjectileHitConfig? spearRain,
        NormalizedImpactBurstConfig? impactBurst,
        NormalizedBoomerangConfig? boomerang,
        NormalizedSpinningSweepConfig? spinningSweep,
        NormalizedHarvestSweepConfig? harvestSweep)
    {
        if (aftershock != null)
        {
            return CloneAftershockSecondary(source);
        }

        if (fractureLine != null)
        {
            return CreateFractureLineSecondary(source);
        }

        if (spearRain != null)
        {
            return CreateSpearRainSecondary(source, spearRain);
        }

        if (impactBurst != null)
        {
            return CreateImpactBurstSecondary(impactBurst);
        }

        if (boomerang != null)
        {
            return CreateBoomerangSecondary(boomerang);
        }

        if (spinningSweep != null)
        {
            return CreateSpinningSweepSecondary(spinningSweep);
        }

        return harvestSweep != null ? CreateHarvestSweepSecondary(harvestSweep) : null;
    }

    private static Attack ResolveCooldownFallbackSecondaryAttack(
        ObjectDB objectDb,
        string prefabName,
        ItemDrop itemDrop)
    {
        if (SecondaryAttackObjectDbStateStore.TryGetOriginalSecondaryAttack(
                objectDb,
                prefabName,
                out Attack? originalSecondaryAttack) &&
            originalSecondaryAttack != null &&
            !string.IsNullOrWhiteSpace(originalSecondaryAttack.m_attackAnimation))
        {
            return SecondaryAttackManager.CloneAttack(originalSecondaryAttack);
        }

        return SecondaryAttackManager.CloneAttack(itemDrop.m_itemData.m_shared.m_secondaryAttack);
    }

    private static NormalizedSecondaryModeConfig CloneAftershockSecondary(NormalizedSecondaryModeConfig? source)
    {
        return new NormalizedSecondaryModeConfig
        {
            Type = "aftershock",
            Animation = source?.Animation ?? "",
            ResourceMultiplier = 1f,
            OutputMultiplier = source?.OutputMultiplier ?? 1f,
            DurabilityFactor = source?.DurabilityFactor ?? 1f,
            CopyFrom = source?.CopyFrom ?? "",
            Projectile = new NormalizedProjectileSecondaryConfig(),
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }

    private static NormalizedSecondaryModeConfig CreateFractureLineSecondary(NormalizedSecondaryModeConfig? source)
    {
        return new NormalizedSecondaryModeConfig
        {
            Type = "fractureLine",
            Animation = source?.Animation ?? "",
            ResourceMultiplier = 1f,
            OutputMultiplier = source?.OutputMultiplier ?? 1f,
            DurabilityFactor = source?.DurabilityFactor ?? 1f,
            CopyFrom = source?.CopyFrom ?? "",
            Projectile = new NormalizedProjectileSecondaryConfig(),
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }

    private static NormalizedSecondaryModeConfig CreateSpearRainSecondary(
        NormalizedSecondaryModeConfig? source,
        NormalizedMeleeOnProjectileHitConfig spearRain)
    {
        return new NormalizedSecondaryModeConfig
        {
            Type = "copy",
            Animation = source?.Animation ?? "",
            ResourceMultiplier = spearRain.ResourceMultiplier,
            OutputMultiplier = source?.OutputMultiplier ?? 1f,
            DurabilityFactor = source?.DurabilityFactor ?? spearRain.DurabilityFactor,
            CopyFrom = source?.CopyFrom ?? "",
            OnProjectileHit = spearRain.Clone(),
            Projectile = new NormalizedProjectileSecondaryConfig(),
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }

    private static NormalizedSecondaryModeConfig CreateImpactBurstSecondary(NormalizedImpactBurstConfig impactBurst)
    {
        return new NormalizedSecondaryModeConfig
        {
            Type = "copy",
            Animation = impactBurst.Animation,
            ResourceMultiplier = impactBurst.ResourceMultiplier,
            OutputMultiplier = 1f,
            DurabilityFactor = impactBurst.DurabilityFactor,
            CopyFrom = FixedThrowCarrierPrefab,
            OnProjectileHit = new NormalizedMeleeOnProjectileHitConfig
            {
                Preset = "impactBurst",
                Cooldown = impactBurst.Cooldown,
                CooldownReductionFactor = impactBurst.CooldownReductionFactor,
                CooldownFallback = ProjectilePresetCooldownFallback.OriginalSecondary,
                ResourceMultiplier = impactBurst.ResourceMultiplier,
                DurabilityFactor = impactBurst.DurabilityFactor,
                ProjectileSpinAxis = impactBurst.ProjectileSpinAxis,
                ProjectileVisualRotationOffset = impactBurst.ProjectileVisualRotationOffset,
                Vfx = impactBurst.Vfx,
                DamageFactor = impactBurst.DamageFactor,
                PushFactor = impactBurst.PushFactor,
                Radius = impactBurst.Radius,
                IncludeDirectTarget = false,
                IncludeDestructibles = true,
                TriggerOnCharactersOnly = false
            },
            Projectile = new NormalizedProjectileSecondaryConfig(),
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }

    private static NormalizedSecondaryModeConfig CreateBoomerangSecondary(NormalizedBoomerangConfig boomerang)
    {
        return new NormalizedSecondaryModeConfig
        {
            Type = "copy",
            Animation = boomerang.Animation,
            ResourceMultiplier = boomerang.ResourceMultiplier,
            OutputMultiplier = 1f,
            DurabilityFactor = boomerang.DurabilityFactor,
            CopyFrom = FixedThrowCarrierPrefab,
            Projectile = new NormalizedProjectileSecondaryConfig(),
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }

    private static NormalizedSecondaryModeConfig CreateSpinningSweepSecondary(NormalizedSpinningSweepConfig spinningSweep)
    {
        return new NormalizedSecondaryModeConfig
        {
            Type = "copy",
            Animation = string.IsNullOrWhiteSpace(spinningSweep.Animation)
                ? "atgeir_secondary"
                : spinningSweep.Animation,
            ResourceMultiplier = spinningSweep.ResourceMultiplier,
            OutputMultiplier = 1f,
            DurabilityFactor = spinningSweep.DurabilityFactor,
            CopyFrom = "",
            Projectile = new NormalizedProjectileSecondaryConfig(),
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }


    private static NormalizedSecondaryModeConfig CreateHarvestSweepSecondary(NormalizedHarvestSweepConfig? harvestSweep)
    {
        return new NormalizedSecondaryModeConfig
        {
            Type = "copy",
            Animation = ResolveHarvestSweepAnimation(harvestSweep),
            ResourceMultiplier = harvestSweep?.ResourceMultiplier ?? 1f,
            OutputMultiplier = 1f,
            DurabilityFactor = harvestSweep?.DurabilityFactor ?? 1f,
            CopyFrom = "AtgeirIron",
            Projectile = new NormalizedProjectileSecondaryConfig(),
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }

    private static string ResolveHarvestSweepAnimation(NormalizedHarvestSweepConfig? harvestSweep)
    {
        string? animation = harvestSweep?.Animation;
        return !string.IsNullOrWhiteSpace(animation)
            ? animation!
            : "atgeir_secondary";
    }

    private static MeleeSpecialPreset ResolveDefaultMeleePreset(
        NormalizedSneakAmbushConfig? sneakAmbush,
        NormalizedCleavingThrustConfig? cleavingThrust,
        NormalizedRiftTrailConfig? riftTrail,
        NormalizedLaunchSlamConfig? launchSlam,
        NormalizedKnockbackChainConfig? knockbackChain,
        NormalizedAftershockConfig? aftershock,
        NormalizedFractureLineConfig? fractureLine,
        NormalizedMeleeOnProjectileHitConfig? spearRain,
        NormalizedImpactBurstConfig? impactBurst,
        NormalizedBoomerangConfig? boomerang,
        NormalizedSpinningSweepConfig? spinningSweep)
    {
        if (spearRain?.Enabled == true)
        {
            return MeleeSpecialPreset.SpearRain;
        }

        if (impactBurst != null)
        {
            return MeleeSpecialPreset.ImpactBurst;
        }

        if (boomerang != null)
        {
            return MeleeSpecialPreset.Boomerang;
        }

        if (spinningSweep != null)
        {
            return MeleeSpecialPreset.SpinningSweep;
        }

        if (cleavingThrust != null)
        {
            return MeleeSpecialPreset.CleavingThrust;
        }

        if (riftTrail != null)
        {
            return MeleeSpecialPreset.RiftTrail;
        }

        if (launchSlam != null)
        {
            return MeleeSpecialPreset.LaunchSlam;
        }

        if (knockbackChain != null)
        {
            return MeleeSpecialPreset.KnockbackChain;
        }

        if (aftershock != null)
        {
            return MeleeSpecialPreset.Aftershock;
        }

        if (fractureLine != null)
        {
            return MeleeSpecialPreset.FractureLine;
        }

        return sneakAmbush != null ? MeleeSpecialPreset.SneakAmbush : MeleeSpecialPreset.None;
    }

    private static void ApplyDefaultMeleeFallbacksIfNeeded(
        ItemDrop itemDrop,
        NormalizedWeaponConfig weaponConfig,
        NormalizedWeaponConfig? globalMeleeFallback)
    {
        if (!weaponConfig.Enabled)
        {
            return;
        }

        if (!TryCreateDefaultMeleeFallback(itemDrop, globalMeleeFallback, out NormalizedWeaponConfig? defaultMeleeFallback) ||
            defaultMeleeFallback == null)
        {
            return;
        }

        bool applyHarvestSweep = weaponConfig.HarvestSweep == null && defaultMeleeFallback.HarvestSweep != null;
        weaponConfig.HarvestSweep ??= defaultMeleeFallback.HarvestSweep;
        if ((applyHarvestSweep || IsDefaultHarvestSweepWeapon(itemDrop.m_itemData?.m_shared)) &&
            weaponConfig.HarvestSweep?.Enabled == true &&
            weaponConfig.Secondary == null)
        {
            weaponConfig.Secondary = CreateHarvestSweepSecondary(weaponConfig.HarvestSweep);
        }

        if (weaponConfig.HasExplicitMeleePreset || weaponConfig.MeleePreset != MeleeSpecialPreset.None)
        {
            return;
        }

        bool applySneakAmbush = weaponConfig.SneakAmbush == null && defaultMeleeFallback.SneakAmbush != null;
        bool applyCleavingThrust = weaponConfig.CleavingThrust == null && defaultMeleeFallback.CleavingThrust != null;
        bool applyRiftTrail = weaponConfig.RiftTrail == null && defaultMeleeFallback.RiftTrail != null;
        bool applyLaunchSlam = weaponConfig.LaunchSlam == null && defaultMeleeFallback.LaunchSlam != null;
        bool applyKnockbackChain = weaponConfig.KnockbackChain == null && defaultMeleeFallback.KnockbackChain != null;
        bool applyAftershock = weaponConfig.Aftershock == null && defaultMeleeFallback.Aftershock != null;
        bool applyFractureLine = weaponConfig.FractureLine == null && defaultMeleeFallback.FractureLine != null;
        bool applySpearRain = weaponConfig.SpearRain == null && defaultMeleeFallback.SpearRain?.Enabled == true;
        bool applyImpactBurst = weaponConfig.ImpactBurst == null && defaultMeleeFallback.ImpactBurst != null;
        bool applyBoomerang = weaponConfig.Boomerang == null && defaultMeleeFallback.Boomerang != null;
        bool applySpinningSweep = weaponConfig.SpinningSweep == null && defaultMeleeFallback.SpinningSweep != null;
        weaponConfig.SneakAmbush ??= defaultMeleeFallback.SneakAmbush;
        weaponConfig.CleavingThrust ??= defaultMeleeFallback.CleavingThrust;
        weaponConfig.RiftTrail ??= defaultMeleeFallback.RiftTrail;
        weaponConfig.LaunchSlam ??= defaultMeleeFallback.LaunchSlam;
        weaponConfig.KnockbackChain ??= defaultMeleeFallback.KnockbackChain;
        weaponConfig.Aftershock ??= defaultMeleeFallback.Aftershock;
        weaponConfig.FractureLine ??= defaultMeleeFallback.FractureLine;
        weaponConfig.SpearRain ??= defaultMeleeFallback.SpearRain;
        weaponConfig.ImpactBurst ??= defaultMeleeFallback.ImpactBurst;
        weaponConfig.Boomerang ??= defaultMeleeFallback.Boomerang;
        weaponConfig.SpinningSweep ??= defaultMeleeFallback.SpinningSweep;
        weaponConfig.Secondary ??= CreateDefaultMeleeSecondary(
            defaultMeleeFallback.Secondary,
            applyAftershock ? defaultMeleeFallback.Aftershock : null,
            applyFractureLine ? defaultMeleeFallback.FractureLine : null,
            applySpearRain ? defaultMeleeFallback.SpearRain : null,
            applyImpactBurst ? defaultMeleeFallback.ImpactBurst : null,
            applyBoomerang ? defaultMeleeFallback.Boomerang : null,
            applySpinningSweep ? defaultMeleeFallback.SpinningSweep : null,
            harvestSweep: null);

        if (applySneakAmbush || applyCleavingThrust || applyRiftTrail || applyLaunchSlam || applyKnockbackChain || applyAftershock || applyFractureLine || applySpearRain || applyImpactBurst || applyBoomerang || applySpinningSweep || applyHarvestSweep)
        {
            weaponConfig.MeleePreset = defaultMeleeFallback.MeleePreset;
        }
    }

}
