using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackWorldApplySystem
{
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

        SecondaryAttackObjectDbStateStore.Restore(objectDb);
        MagicSummonQualityPresetSystem.RestoreObjectDb(objectDb);
        SecondaryAttackDefinitionBuildContext buildContext = new(objectDb, emitMissingWarnings);

        Dictionary<string, SecondaryAttackDefinition> appliedDefinitions = new(StringComparer.OrdinalIgnoreCase);
        int appliedCount = 0;
        int appliedGlobalRangedFallbackCount = 0;
        int appliedGlobalBloodMagicFallbackCount = 0;
        int appliedGlobalMeleeFallbackCount = 0;
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

            bool usesGlobalMeleeFallback = false;
            bool usesGlobalRangedFallback = false;
            bool usesGlobalBloodMagicFallback = false;
            compiledSnapshot.Weapons.TryGetValue(itemPrefab.name, out NormalizedWeaponConfig? weaponConfig);
            if (weaponConfig != null)
            {
                seenConfiguredPrefabs.Add(itemPrefab.name);
                if (weaponConfig.Enabled && !weaponConfig.UseAutomaticFallback)
                {
                    weaponConfig = ResolveDefaultMeleeFallbacks(itemDrop, weaponConfig, compiledSnapshot.GlobalMeleeFallback);
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
            SecondaryAttackCooldownGroupResolver.Apply(itemPrefab.name, itemDrop, resolvedDefinition);
            resolvedDefinition.CooldownFallbackSecondaryAttack = ResolveCooldownFallbackSecondaryAttack(objectDb, itemPrefab.name, itemDrop);
            appliedDefinitions[itemPrefab.name] = resolvedDefinition;
            if (resolvedDefinition.AppliesSecondaryOverride)
            {
                Attack sourceAttack = SecondaryAttackManager.ResolveSourceAttack(objectDb, itemDrop, resolvedDefinition);
                Attack configuredSecondaryAttack = SecondaryAttackManager.BuildSecondaryAttack(sourceAttack, resolvedDefinition);
                SecondaryAttackManager.NormalizeCopiedProjectileAim(configuredSecondaryAttack, resolvedDefinition);
                resolvedDefinition.ConfiguredSecondaryAttack = SecondaryAttackManager.CloneAttack(configuredSecondaryAttack);
                if (!ProjectilePresetCooldownPolicy.UsesDynamicOriginalSecondary(resolvedDefinition))
                {
                    SecondaryAttackObjectDbStateStore.CaptureSecondaryAttack(
                        objectDb,
                        itemPrefab.name,
                        itemDrop.m_itemData.m_shared.m_secondaryAttack);
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

        }

        SecondaryAttackAppliedWorldSnapshot appliedWorldSnapshot = new(compiledSnapshot, appliedDefinitions, _nextApplyRevision++);

        foreach (string configuredPrefabName in compiledSnapshot.Weapons.Keys.Where(key => !seenConfiguredPrefabs.Contains(key)))
        {
            if (!emitMissingWarnings)
            {
                continue;
            }

            string warningKey = $"missing_objectdb_prefab:{configuredPrefabName}";
            if (SecondaryAttackWarningLog.TryMarkWarning(warningKey))
            {
                SecondaryAttacksPlugin.ModLogger.LogWarning($"Configured prefab '{configuredPrefabName}' was not found in ObjectDB.");
            }
        }

        MagicSummonQualityPresetSystem.ApplyToObjectDb(
            objectDb,
            appliedWorldSnapshot.CompiledSnapshot.MagicSummons);
        SecondaryAttacksPlugin.ModLogger.LogInfo($"Applied {appliedCount} secondary attack definition(s), including {appliedGlobalRangedFallbackCount} global ranged fallback definition(s), {appliedGlobalBloodMagicFallbackCount} global blood magic fallback definition(s), and {appliedGlobalMeleeFallbackCount} global melee fallback definition(s).");
        return appliedWorldSnapshot;
    }

    internal static void ApplyToZNetScene(
        ZNetScene scene,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        SummonPrefabOverrideSystem.Apply(
            scene,
            compiledSnapshot.MagicSummons,
            emitMissingWarnings);
        MagicSummonQualityPresetSystem.ApplyToZNetScene(scene, compiledSnapshot.MagicSummons);
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

        BloodMagicAutomaticWeaponFamily family =
            SecondaryAttackWeaponFamilyResolver.ResolveBloodMagicFamily(prefabName, itemDrop);
        if (family == BloodMagicAutomaticWeaponFamily.Shield &&
            globalBloodMagicPresets.TryGetValue("shieldConvert", out NormalizedWeaponConfig? shieldConvertFallback))
        {
            fallback = shieldConvertFallback;
            return true;
        }

        if (family == BloodMagicAutomaticWeaponFamily.Summon &&
            globalBloodMagicPresets.TryGetValue("summonEmpower", out NormalizedWeaponConfig? summonEmpowerFallback))
        {
            fallback = summonEmpowerFallback;
            return true;
        }

        return false;
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
        RangedAutomaticWeaponFamily family =
            SecondaryAttackWeaponFamilyResolver.ResolveRangedFamily(itemDrop);
        return family switch
        {
            RangedAutomaticWeaponFamily.Bomb =>
                TryResolveBombPresetName(itemDrop.m_itemData.m_shared.m_attack, out presetName),
            RangedAutomaticWeaponFamily.FireballStaff =>
                TryGetRangedPresetName(SecondaryAttacksPlugin.FireballStaffPreset.Value, out presetName),
            RangedAutomaticWeaponFamily.RapidStaff =>
                TryGetRangedPresetName(SecondaryAttacksPlugin.RapidStaffPreset.Value, out presetName),
            RangedAutomaticWeaponFamily.ReloadStaff =>
                TryGetRangedPresetName(SecondaryAttacksPlugin.LightningStaffPreset.Value, out presetName),
            RangedAutomaticWeaponFamily.Crossbow =>
                TryGetRangedPresetName(SecondaryAttacksPlugin.CrossbowPreset.Value, out presetName),
            RangedAutomaticWeaponFamily.Bow =>
                TryGetRangedPresetName(SecondaryAttacksPlugin.BowPreset.Value, out presetName),
            _ => false
        };
    }

    private static bool TryResolveBombPresetName(
        Attack primaryAttack,
        out string presetName)
    {
        presetName = "";
        if (!SecondaryAttackWeaponFamilyResolver.IsBombProjectileAttack(primaryAttack))
        {
            return false;
        }

        SecondaryAttacksPlugin.BombPresetSelection configuredPreset = SecondaryAttacksPlugin.BombPreset.Value;
        if (configuredPreset == SecondaryAttacksPlugin.BombPresetSelection.Off)
        {
            return false;
        }

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
        if (selection == SecondaryAttacksPlugin.RangedPresetSelection.Off)
        {
            return false;
        }

        if (!Enum.TryParse(selection.ToString(), out SecondaryAttackPreset preset))
        {
            return false;
        }

        presetName = ProjectileRuntimeSystem.GetPresetName(preset);
        return true;
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

        NormalizedWeaponConfig fallback = new();
        switch (SecondaryAttackWeaponFamilyResolver.ResolveMeleeFamily(itemDrop))
        {
            case MeleeAutomaticWeaponFamily.Knives when globalMeleeFallback.SneakAmbush?.Enabled == true:
                fallback.SneakAmbush = globalMeleeFallback.SneakAmbush.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.SneakAmbush;
                break;
            case MeleeAutomaticWeaponFamily.TwoHandedSword when globalMeleeFallback.CleavingThrust?.Enabled == true:
                fallback.CleavingThrust = globalMeleeFallback.CleavingThrust.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.CleavingThrust;
                break;
            case MeleeAutomaticWeaponFamily.OneHandedSword when globalMeleeFallback.RiftTrail?.Enabled == true:
                fallback.RiftTrail = globalMeleeFallback.RiftTrail.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.RiftTrail;
                break;
            case MeleeAutomaticWeaponFamily.OneHandedClub when globalMeleeFallback.LaunchSlam?.Enabled == true:
                fallback.LaunchSlam = globalMeleeFallback.LaunchSlam.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.LaunchSlam;
                break;
            case MeleeAutomaticWeaponFamily.Unarmed when globalMeleeFallback.KnockbackChain?.Enabled == true:
                fallback.KnockbackChain = globalMeleeFallback.KnockbackChain.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.KnockbackChain;
                break;
            case MeleeAutomaticWeaponFamily.Sledge when globalMeleeFallback.Aftershock?.Enabled == true:
                fallback.Aftershock = globalMeleeFallback.Aftershock.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.Aftershock;
                break;
            case MeleeAutomaticWeaponFamily.Polearm when globalMeleeFallback.SpinningSweep?.Enabled == true:
                fallback.SpinningSweep = globalMeleeFallback.SpinningSweep.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.SpinningSweep;
                break;
            case MeleeAutomaticWeaponFamily.Farming when globalMeleeFallback.HarvestSweep?.Enabled == true:
                fallback.HarvestSweep = globalMeleeFallback.HarvestSweep.Clone();
                break;
            case MeleeAutomaticWeaponFamily.Pickaxe when globalMeleeFallback.FractureLine?.Enabled == true:
                fallback.FractureLine = globalMeleeFallback.FractureLine.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.FractureLine;
                break;
            case MeleeAutomaticWeaponFamily.Spear when globalMeleeFallback.SpearRain?.Enabled == true:
                fallback.SpearRain = globalMeleeFallback.SpearRain.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.SpearRain;
                break;
            case MeleeAutomaticWeaponFamily.Battleaxe when globalMeleeFallback.ImpactBurst?.Enabled == true:
                fallback.ImpactBurst = globalMeleeFallback.ImpactBurst.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.ImpactBurst;
                break;
            case MeleeAutomaticWeaponFamily.OneHandedAxe when globalMeleeFallback.Boomerang?.Enabled == true:
                fallback.Boomerang = globalMeleeFallback.Boomerang.Clone();
                fallback.MeleePreset = MeleeSpecialPreset.Boomerang;
                break;
            default:
                return false;
        }

        fallback.Secondary = CreateDefaultMeleeSecondary(globalMeleeFallback.Secondary, fallback);
        defaultMeleeFallback = fallback;
        return true;
    }

    private static NormalizedSecondaryModeConfig? CreateDefaultMeleeSecondary(
        NormalizedSecondaryModeConfig? source,
        NormalizedWeaponConfig fallback)
    {
        if (fallback.Aftershock != null)
        {
            return CloneAftershockSecondary(source);
        }

        if (fallback.FractureLine != null)
        {
            return CreateFractureLineSecondary(source);
        }

        if (fallback.SpearRain != null)
        {
            return CreateSpearRainSecondary(source, fallback.SpearRain);
        }

        if (fallback.ImpactBurst != null)
        {
            return SecondaryAttackWeaponConfigNormalizer.CreateImpactBurstSecondary(fallback.ImpactBurst);
        }

        if (fallback.Boomerang != null)
        {
            return SecondaryAttackWeaponConfigNormalizer.CreateBoomerangSecondary(fallback.Boomerang);
        }

        if (fallback.SpinningSweep != null)
        {
            return SecondaryAttackWeaponConfigNormalizer.CreateSpinningSweepSecondary(fallback.SpinningSweep);
        }

        return fallback.HarvestSweep != null
            ? CreateHarvestSweepSecondary(fallback.HarvestSweep)
            : null;
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
            CopyFrom = source?.CopyFrom ?? ""
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
            CopyFrom = source?.CopyFrom ?? ""
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
            OnProjectileHit = spearRain.Clone()
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
            CopyFrom = "AtgeirIron"
        };
    }

    private static string ResolveHarvestSweepAnimation(NormalizedHarvestSweepConfig? harvestSweep)
    {
        string? animation = harvestSweep?.Animation;
        return !string.IsNullOrWhiteSpace(animation)
            ? animation!
            : "atgeir_secondary";
    }

    private static NormalizedWeaponConfig ResolveDefaultMeleeFallbacks(
        ItemDrop itemDrop,
        NormalizedWeaponConfig weaponConfig,
        NormalizedWeaponConfig? globalMeleeFallback)
    {
        if (!weaponConfig.Enabled)
        {
            return weaponConfig;
        }

        if (!TryCreateDefaultMeleeFallback(itemDrop, globalMeleeFallback, out NormalizedWeaponConfig? defaultMeleeFallback) ||
            defaultMeleeFallback == null)
        {
            return weaponConfig;
        }

        NormalizedWeaponConfig resolvedWeaponConfig = weaponConfig.Clone();
        bool applyHarvestSweep = resolvedWeaponConfig.HarvestSweep == null && defaultMeleeFallback.HarvestSweep != null;
        resolvedWeaponConfig.HarvestSweep ??= defaultMeleeFallback.HarvestSweep;
        if (defaultMeleeFallback.HarvestSweep != null &&
            resolvedWeaponConfig.HarvestSweep?.Enabled == true &&
            resolvedWeaponConfig.Secondary == null)
        {
            resolvedWeaponConfig.Secondary = CreateHarvestSweepSecondary(resolvedWeaponConfig.HarvestSweep);
        }

        if (resolvedWeaponConfig.HasExplicitMeleePreset || resolvedWeaponConfig.MeleePreset != MeleeSpecialPreset.None)
        {
            return resolvedWeaponConfig;
        }

        if (TryApplyDefaultMeleePreset(resolvedWeaponConfig, defaultMeleeFallback))
        {
            resolvedWeaponConfig.Secondary ??= defaultMeleeFallback.Secondary;
            resolvedWeaponConfig.MeleePreset = defaultMeleeFallback.MeleePreset;
        }
        else if (applyHarvestSweep)
        {
            resolvedWeaponConfig.MeleePreset = MeleeSpecialPreset.None;
        }

        return resolvedWeaponConfig;
    }

    private static bool TryApplyDefaultMeleePreset(
        NormalizedWeaponConfig target,
        NormalizedWeaponConfig fallback)
    {
        switch (fallback.MeleePreset)
        {
            case MeleeSpecialPreset.SneakAmbush when target.SneakAmbush == null:
                target.SneakAmbush = fallback.SneakAmbush;
                return target.SneakAmbush != null;
            case MeleeSpecialPreset.CleavingThrust when target.CleavingThrust == null:
                target.CleavingThrust = fallback.CleavingThrust;
                return target.CleavingThrust != null;
            case MeleeSpecialPreset.SpearRain when target.SpearRain == null:
                target.SpearRain = fallback.SpearRain;
                return target.SpearRain != null;
            case MeleeSpecialPreset.ImpactBurst when target.ImpactBurst == null:
                target.ImpactBurst = fallback.ImpactBurst;
                return target.ImpactBurst != null;
            case MeleeSpecialPreset.Boomerang when target.Boomerang == null:
                target.Boomerang = fallback.Boomerang;
                return target.Boomerang != null;
            case MeleeSpecialPreset.SpinningSweep when target.SpinningSweep == null:
                target.SpinningSweep = fallback.SpinningSweep;
                return target.SpinningSweep != null;
            case MeleeSpecialPreset.LaunchSlam when target.LaunchSlam == null:
                target.LaunchSlam = fallback.LaunchSlam;
                return target.LaunchSlam != null;
            case MeleeSpecialPreset.KnockbackChain when target.KnockbackChain == null:
                target.KnockbackChain = fallback.KnockbackChain;
                return target.KnockbackChain != null;
            case MeleeSpecialPreset.Aftershock when target.Aftershock == null:
                target.Aftershock = fallback.Aftershock;
                return target.Aftershock != null;
            case MeleeSpecialPreset.RiftTrail when target.RiftTrail == null:
                target.RiftTrail = fallback.RiftTrail;
                return target.RiftTrail != null;
            case MeleeSpecialPreset.FractureLine when target.FractureLine == null:
                target.FractureLine = fallback.FractureLine;
                return target.FractureLine != null;
            default:
                return false;
        }
    }

}
