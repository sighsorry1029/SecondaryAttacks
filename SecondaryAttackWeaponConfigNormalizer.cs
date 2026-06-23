using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace SecondaryAttacks;

internal sealed class SecondaryAttackWeaponNormalizationResult
{
    public Dictionary<string, NormalizedWeaponConfig> Weapons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, NormalizedWeaponConfig> GlobalRangedPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, NormalizedWeaponConfig> GlobalBloodMagicPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public NormalizedWeaponConfig? GlobalMeleeFallback { get; set; }
}

internal static class SecondaryAttackWeaponConfigNormalizer
{
    private const string FixedThrowCarrierPrefab = "SpearFlint";

    private const string FixedImpactBurstVfx = "vfx_archerytarget_bullseye_double";

    private const string GlobalFallbackKey = "Global";

    internal static SecondaryAttackWeaponNormalizationResult Normalize(
        IReadOnlyDictionary<string, RangedWeaponConfig> ranged,
        IReadOnlyDictionary<string, MeleeWeaponConfig> melee,
        IReadOnlyDictionary<string, BloodMagicWeaponConfig> bloodMagic)
    {
        Dictionary<string, NormalizedWeaponConfig> normalizedWeapons = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, NormalizedWeaponConfig> globalRangedPresets = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, NormalizedWeaponConfig> globalBloodMagicPresets = new(StringComparer.OrdinalIgnoreCase);
        RangedWeaponConfig? rawGlobalRangedPresets = null;
        foreach ((string prefabName, RangedWeaponConfig weaponConfig) in ranged)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || weaponConfig == null)
            {
                continue;
            }

            string normalizedPrefabName = prefabName.Trim();
            if (normalizedPrefabName.Equals(GlobalFallbackKey, StringComparison.OrdinalIgnoreCase))
            {
                rawGlobalRangedPresets = weaponConfig;
                continue;
            }
        }

        AddGlobalRangedPreset(globalRangedPresets, "barrage", rawGlobalRangedPresets?.Barrage);
        AddGlobalRangedPreset(globalRangedPresets, "volley", rawGlobalRangedPresets?.Volley);
        AddGlobalRangedPreset(globalRangedPresets, "piercing", rawGlobalRangedPresets?.Piercing);
        AddGlobalRangedPreset(globalRangedPresets, "scatter", rawGlobalRangedPresets?.Scatter);
        AddGlobalRangedPreset(globalRangedPresets, "spiral", rawGlobalRangedPresets?.Spiral);
        AddGlobalRangedPreset(globalRangedPresets, "sentinel", rawGlobalRangedPresets?.Sentinel);
        AddGlobalRangedPreset(globalRangedPresets, "meteor", rawGlobalRangedPresets?.Meteor);
        AddGlobalRangedPreset(globalRangedPresets, "burst", rawGlobalRangedPresets?.Burst);
        AddGlobalRangedPreset(globalRangedPresets, "stickyDetonator", rawGlobalRangedPresets?.StickyDetonator);
        AddGlobalRangedPreset(globalRangedPresets, "overchargedBomb", rawGlobalRangedPresets?.OverchargedBomb);

        foreach ((string prefabName, RangedWeaponConfig weaponConfig) in ranged)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || weaponConfig == null)
            {
                continue;
            }

            string normalizedPrefabName = prefabName.Trim();
            if (normalizedPrefabName.Equals(GlobalFallbackKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            NormalizedWeaponConfig? fallback = ResolveRangedPresetFallback(weaponConfig, globalRangedPresets);
            AddNormalizedWeapon(normalizedWeapons, normalizedPrefabName, FromRangedRaw(weaponConfig, fallback), SecondaryAttackYamlDomainRegistry.RangedYamlFileName);
        }

        MeleeWeaponConfig? rawGlobalMeleeFallback = null;
        foreach ((string prefabName, MeleeWeaponConfig weaponConfig) in melee)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || weaponConfig == null)
            {
                continue;
            }

            if (prefabName.Trim().Equals(GlobalFallbackKey, StringComparison.OrdinalIgnoreCase))
            {
                rawGlobalMeleeFallback = weaponConfig;
                break;
            }
        }

        NormalizedWeaponConfig? globalMeleeFallback = rawGlobalMeleeFallback != null
            ? CreateGlobalMeleeFallback(rawGlobalMeleeFallback)
            : null;
        foreach ((string prefabName, MeleeWeaponConfig weaponConfig) in melee)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || weaponConfig == null)
            {
                continue;
            }

            string normalizedPrefabName = prefabName.Trim();
            if (normalizedPrefabName.Equals(GlobalFallbackKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            AddNormalizedWeapon(normalizedWeapons, normalizedPrefabName, FromMeleeRaw(normalizedPrefabName, weaponConfig, globalMeleeFallback), SecondaryAttackYamlDomainRegistry.MeleeYamlFileName);
        }

        BloodMagicWeaponConfig? rawGlobalBloodMagicPresets = null;
        foreach ((string prefabName, BloodMagicWeaponConfig weaponConfig) in bloodMagic)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || weaponConfig == null)
            {
                continue;
            }

            string normalizedPrefabName = prefabName.Trim();
            if (normalizedPrefabName.Equals(GlobalFallbackKey, StringComparison.OrdinalIgnoreCase))
            {
                rawGlobalBloodMagicPresets = weaponConfig;
                continue;
            }
        }

        AddGlobalBloodMagicPreset(globalBloodMagicPresets, "summonEmpower", rawGlobalBloodMagicPresets?.SummonEmpower);
        AddGlobalBloodMagicPreset(globalBloodMagicPresets, "shieldConvert", rawGlobalBloodMagicPresets?.ShieldConvert);

        foreach ((string prefabName, BloodMagicWeaponConfig weaponConfig) in bloodMagic)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || weaponConfig == null)
            {
                continue;
            }

            string normalizedPrefabName = prefabName.Trim();
            if (normalizedPrefabName.Equals(GlobalFallbackKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            NormalizedWeaponConfig? fallback = ResolveBloodMagicPresetFallback(weaponConfig, globalBloodMagicPresets);
            AddNormalizedWeapon(normalizedWeapons, normalizedPrefabName, FromBloodMagicRaw(weaponConfig, fallback), SecondaryAttackYamlDomainRegistry.BloodMagicYamlFileName);
        }

        return new SecondaryAttackWeaponNormalizationResult
        {
            Weapons = normalizedWeapons,
            GlobalRangedPresets = globalRangedPresets,
            GlobalBloodMagicPresets = globalBloodMagicPresets,
            GlobalMeleeFallback = globalMeleeFallback
        };
    }

    private static void AddGlobalRangedPreset(
        Dictionary<string, NormalizedWeaponConfig> globalRangedPresets,
        string presetName,
        RangedWeaponConfig? rawConfig)
    {
        if (rawConfig == null)
        {
            return;
        }

        rawConfig.Preset = presetName;
        globalRangedPresets[presetName] = FromRangedRaw(rawConfig);
    }

    private static void AddGlobalBloodMagicPreset(
        Dictionary<string, NormalizedWeaponConfig> globalBloodMagicPresets,
        string presetName,
        BloodMagicWeaponConfig? rawConfig)
    {
        if (rawConfig == null)
        {
            return;
        }

        rawConfig.Preset = presetName;
        globalBloodMagicPresets[presetName] = FromBloodMagicRaw(rawConfig);
    }

    private static NormalizedWeaponConfig? ResolveRangedPresetFallback(
        RangedWeaponConfig rawConfig,
        IReadOnlyDictionary<string, NormalizedWeaponConfig> globalRangedPresets)
    {
        string preset = rawConfig.Preset?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(preset) ||
            preset.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return globalRangedPresets.TryGetValue(preset, out NormalizedWeaponConfig? fallback)
            ? fallback
            : null;
    }

    private static NormalizedWeaponConfig? ResolveBloodMagicPresetFallback(
        BloodMagicWeaponConfig rawConfig,
        IReadOnlyDictionary<string, NormalizedWeaponConfig> globalBloodMagicPresets)
    {
        string preset = rawConfig.Preset?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(preset))
        {
            return null;
        }

        return globalBloodMagicPresets.TryGetValue(preset, out NormalizedWeaponConfig? fallback)
            ? fallback
            : null;
    }

    private static void AddNormalizedWeapon(
        Dictionary<string, NormalizedWeaponConfig> normalizedWeapons,
        string prefabName,
        NormalizedWeaponConfig weaponConfig,
        string sourceFileName)
    {
        string normalizedPrefabName = prefabName.Trim();
        if (normalizedWeapons.ContainsKey(normalizedPrefabName))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning(
                $"Skipping duplicate '{normalizedPrefabName}' entry from {sourceFileName}: the prefab is already configured by another SecondaryAttacks YAML file.");
            return;
        }

        normalizedWeapons[normalizedPrefabName] = weaponConfig;
    }

    public static NormalizedWeaponConfig FromRangedRaw(RangedWeaponConfig raw, NormalizedWeaponConfig? fallback = null)
    {
        return new NormalizedWeaponConfig
        {
            Enabled = raw.Enabled ?? true,
            UseAutomaticFallback = fallback == null && string.IsNullOrWhiteSpace(raw.Preset),
            Secondary = NormalizeRanged(raw, fallback?.Secondary)
        };
    }

    public static NormalizedWeaponConfig CreateGlobalMeleeFallback(MeleeWeaponConfig raw)
    {
        return new NormalizedWeaponConfig
        {
            Enabled = raw.Enabled ?? true,
            SneakAmbush = NormalizeSneakAmbush(raw.SneakAmbush, null),
            CleavingThrust = NormalizeCleavingThrust(raw.CleavingThrust, null),
            LaunchSlam = NormalizeLaunchSlam(raw.LaunchSlam, null),
            KnockbackChain = NormalizeKnockbackChain(raw.KnockbackChain, null),
            Aftershock = NormalizeAftershock(raw.Aftershock, null),
            RiftTrail = NormalizeRiftTrail(raw.RiftTrail, null),
            FractureLine = NormalizeFractureLine(raw.FractureLine, null),
            ImpactBurst = NormalizeImpactBurst(raw.ImpactBurst, null),
            Boomerang = NormalizeBoomerang(raw.Boomerang, null),
            SpinningSweep = NormalizeSpinningSweep(raw.SpinningSweep, null),
            HarvestSweep = NormalizeHarvestSweep(raw.HarvestSweep, null),
            SpearRain = NormalizeMeleeOnProjectileHit(raw.SpearRain, forceSpearRainPreset: true, fallback: null),
            Secondary = raw.Aftershock != null && raw.Aftershock.Enabled != false
                ? NormalizeMelee(raw, null, forceSpearRainPreset: false, fallbackOnProjectileHit: null, secondaryType: "aftershock")
                : null,
            MeleePreset = MeleeSpecialPreset.None,
            HasExplicitMeleePreset = false
        };
    }

    public static NormalizedWeaponConfig FromMeleeRaw(
        string prefabName,
        MeleeWeaponConfig raw,
        NormalizedWeaponConfig? fallback = null)
    {
        bool hasExplicitPreset = TryParseExplicitMeleePreset(prefabName, raw.Preset, out MeleeSpecialPreset selectedPreset);
        if (!hasExplicitPreset)
        {
            selectedPreset = ResolveImplicitMeleePreset(prefabName, raw);
        }

        SneakAmbushConfig? rawSneakAmbush = raw.SneakAmbush;
        MeleeOnProjectileHitConfig? rawSpearRain = raw.SpearRain;
        NormalizedImpactBurstConfig? impactBurst = selectedPreset == MeleeSpecialPreset.ImpactBurst
            ? NormalizeSelectedImpactBurst(raw.ImpactBurst, fallback?.ImpactBurst)
            : raw.ImpactBurst?.Enabled == false
                ? NormalizeImpactBurst(raw.ImpactBurst, fallback?.ImpactBurst)
                : null;
        NormalizedBoomerangConfig? boomerang = selectedPreset == MeleeSpecialPreset.Boomerang
            ? NormalizeSelectedBoomerang(raw.Boomerang, fallback?.Boomerang)
            : raw.Boomerang?.Enabled == false
                ? NormalizeBoomerang(raw.Boomerang, fallback?.Boomerang)
                : null;
        NormalizedSpinningSweepConfig? spinningSweep = selectedPreset == MeleeSpecialPreset.SpinningSweep
            ? NormalizeSelectedSpinningSweep(raw.SpinningSweep, fallback?.SpinningSweep)
            : raw.SpinningSweep?.Enabled == false
                ? NormalizeSpinningSweep(raw.SpinningSweep, fallback?.SpinningSweep)
                : null;
        if (hasExplicitPreset)
        {
            LogIgnoredExplicitMeleePresetBlocks(prefabName, raw, selectedPreset);
        }

        MeleeOnProjectileHitConfig? selectedProjectileHit = selectedPreset == MeleeSpecialPreset.SpearRain
            ? rawSpearRain ?? new MeleeOnProjectileHitConfig()
            : null;
        NormalizedMeleeOnProjectileHitConfig? fallbackProjectileHit = selectedPreset == MeleeSpecialPreset.SpearRain
            ? fallback?.SpearRain
            : null;
        NormalizedSecondaryModeConfig? secondary = selectedPreset == MeleeSpecialPreset.ImpactBurst && impactBurst != null
            ? CreateImpactBurstSecondary(impactBurst)
            : selectedPreset == MeleeSpecialPreset.Boomerang && boomerang != null
                ? CreateBoomerangSecondary(boomerang)
                : selectedPreset == MeleeSpecialPreset.SpinningSweep && spinningSweep != null
                    ? CreateSpinningSweepSecondary(spinningSweep)
                    : HasMeleeSecondaryConfig(raw, selectedPreset, selectedProjectileHit)
                        ? NormalizeMelee(
                            raw,
                            selectedProjectileHit,
                            selectedPreset == MeleeSpecialPreset.SpearRain,
                            fallbackProjectileHit,
                            ResolveMeleeSecondaryType(raw, selectedPreset))
                        : null;

        return new NormalizedWeaponConfig
        {
            Enabled = raw.Enabled ?? true,
            Secondary = secondary,
            SneakAmbush = selectedPreset == MeleeSpecialPreset.SneakAmbush
                ? NormalizeSelectedSneakAmbush(rawSneakAmbush, fallback?.SneakAmbush)
                : rawSneakAmbush?.Enabled == false
                    ? NormalizeSneakAmbush(rawSneakAmbush, fallback?.SneakAmbush)
                    : null,
            CleavingThrust = selectedPreset == MeleeSpecialPreset.CleavingThrust
                ? NormalizeSelectedCleavingThrust(raw.CleavingThrust, fallback?.CleavingThrust)
                : raw.CleavingThrust?.Enabled == false
                    ? NormalizeCleavingThrust(raw.CleavingThrust, fallback?.CleavingThrust)
                    : null,
            LaunchSlam = selectedPreset == MeleeSpecialPreset.LaunchSlam
                ? NormalizeSelectedLaunchSlam(raw.LaunchSlam, fallback?.LaunchSlam)
                : raw.LaunchSlam?.Enabled == false
                    ? NormalizeLaunchSlam(raw.LaunchSlam, fallback?.LaunchSlam)
                    : null,
            KnockbackChain = selectedPreset == MeleeSpecialPreset.KnockbackChain
                ? NormalizeSelectedKnockbackChain(raw.KnockbackChain, fallback?.KnockbackChain)
                : raw.KnockbackChain?.Enabled == false
                    ? NormalizeKnockbackChain(raw.KnockbackChain, fallback?.KnockbackChain)
                    : null,
            Aftershock = selectedPreset == MeleeSpecialPreset.Aftershock
                ? NormalizeSelectedAftershock(raw.Aftershock, fallback?.Aftershock)
                : raw.Aftershock?.Enabled == false
                    ? NormalizeAftershock(raw.Aftershock, fallback?.Aftershock)
                    : null,
            RiftTrail = selectedPreset == MeleeSpecialPreset.RiftTrail
                ? NormalizeSelectedRiftTrail(raw.RiftTrail, fallback?.RiftTrail)
                : raw.RiftTrail?.Enabled == false
                    ? NormalizeRiftTrail(raw.RiftTrail, fallback?.RiftTrail)
                    : null,
            FractureLine = selectedPreset == MeleeSpecialPreset.FractureLine
                ? NormalizeSelectedFractureLine(raw.FractureLine, fallback?.FractureLine)
                : raw.FractureLine?.Enabled == false
                    ? NormalizeFractureLine(raw.FractureLine, fallback?.FractureLine)
                    : null,
            ImpactBurst = impactBurst,
            Boomerang = boomerang,
            SpinningSweep = spinningSweep,
            HarvestSweep = raw.HarvestSweep != null
                ? NormalizeHarvestSweep(raw.HarvestSweep, fallback?.HarvestSweep)
                : null,
            MeleePreset = selectedPreset,
            HasExplicitMeleePreset = hasExplicitPreset
        };
    }

    public static NormalizedWeaponConfig FromBloodMagicRaw(BloodMagicWeaponConfig raw, NormalizedWeaponConfig? fallback = null)
    {
        return new NormalizedWeaponConfig
        {
            Enabled = raw.Enabled ?? true,
            UseAutomaticFallback = fallback == null && string.IsNullOrWhiteSpace(raw.Preset),
            Secondary = NormalizeBloodMagic(raw, fallback?.Secondary)
        };
    }

    private static bool TryParseExplicitMeleePreset(string prefabName, string? rawPreset, out MeleeSpecialPreset preset)
    {
        preset = MeleeSpecialPreset.None;
        if (string.IsNullOrWhiteSpace(rawPreset))
        {
            return false;
        }

        string trimmedPreset = rawPreset!.Trim();
        if (TryParseMeleePreset(trimmedPreset, out preset))
        {
            return true;
        }

        SecondaryAttacksPlugin.ModLogger.LogWarning(
            $"Unknown melee preset '{trimmedPreset}' on {prefabName}; no melee special preset will be applied. Valid values: none, sneakAmbush, cleavingThrust, spearRain, impactBurst, boomerang, spinningSweep, launchSlam, knockbackChain, aftershock, riftTrail, fractureLine.");
        preset = MeleeSpecialPreset.None;
        return true;
    }

    private static bool TryParseMeleePreset(string rawPreset, out MeleeSpecialPreset preset)
    {
        string normalizedPreset = rawPreset.Trim();
        if (normalizedPreset.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalizedPreset.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            normalizedPreset.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.None;
            return true;
        }

        if (normalizedPreset.Equals("sneakAmbush", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.SneakAmbush;
            return true;
        }

        if (normalizedPreset.Equals("cleavingThrust", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.CleavingThrust;
            return true;
        }

        if (normalizedPreset.Equals("spearRain", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.SpearRain;
            return true;
        }

        if (normalizedPreset.Equals("impactBurst", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.ImpactBurst;
            return true;
        }

        if (normalizedPreset.Equals("boomerang", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.Boomerang;
            return true;
        }

        if (normalizedPreset.Equals("spinningSweep", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.SpinningSweep;
            return true;
        }

        if (normalizedPreset.Equals("launchSlam", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.LaunchSlam;
            return true;
        }

        if (normalizedPreset.Equals("knockbackChain", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.KnockbackChain;
            return true;
        }

        if (normalizedPreset.Equals("aftershock", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.Aftershock;
            return true;
        }

        if (normalizedPreset.Equals("riftTrail", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.RiftTrail;
            return true;
        }

        if (normalizedPreset.Equals("fractureLine", StringComparison.OrdinalIgnoreCase))
        {
            preset = MeleeSpecialPreset.FractureLine;
            return true;
        }

        preset = MeleeSpecialPreset.None;
        return false;
    }

    private static MeleeSpecialPreset ResolveImplicitMeleePreset(string prefabName, MeleeWeaponConfig raw)
    {
        List<MeleeSpecialPreset> candidates = new();
        SneakAmbushConfig? rawSneakAmbush = raw.SneakAmbush;
        if (rawSneakAmbush != null && rawSneakAmbush.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.SneakAmbush);
        }

        if (raw.CleavingThrust != null && raw.CleavingThrust.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.CleavingThrust);
        }

        if (raw.SpearRain != null && raw.SpearRain.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.SpearRain);
        }

        if (raw.ImpactBurst != null && raw.ImpactBurst.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.ImpactBurst);
        }

        if (raw.Boomerang != null && raw.Boomerang.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.Boomerang);
        }

        if (raw.SpinningSweep != null && raw.SpinningSweep.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.SpinningSweep);
        }

        if (raw.LaunchSlam != null && raw.LaunchSlam.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.LaunchSlam);
        }

        if (raw.KnockbackChain != null && raw.KnockbackChain.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.KnockbackChain);
        }

        if (raw.Aftershock != null && raw.Aftershock.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.Aftershock);
        }

        if (raw.RiftTrail != null && raw.RiftTrail.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.RiftTrail);
        }

        if (raw.FractureLine != null && raw.FractureLine.Enabled != false)
        {
            candidates.Add(MeleeSpecialPreset.FractureLine);
        }

        if (candidates.Count == 0)
        {
            return MeleeSpecialPreset.None;
        }

        MeleeSpecialPreset selectedPreset = SelectImplicitMeleePreset(candidates);
        if (candidates.Count > 1)
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning(
                $"Multiple melee preset blocks configured for {prefabName}; using {FormatMeleePreset(selectedPreset)} and ignoring {FormatIgnoredMeleePresets(candidates, selectedPreset)}. Add 'preset: {FormatMeleePreset(selectedPreset)}' to make this explicit.");
        }

        return selectedPreset;
    }

    private static MeleeSpecialPreset SelectImplicitMeleePreset(List<MeleeSpecialPreset> candidates)
    {
        if (candidates.Contains(MeleeSpecialPreset.SpearRain))
        {
            return MeleeSpecialPreset.SpearRain;
        }

        if (candidates.Contains(MeleeSpecialPreset.ImpactBurst))
        {
            return MeleeSpecialPreset.ImpactBurst;
        }

        if (candidates.Contains(MeleeSpecialPreset.Boomerang))
        {
            return MeleeSpecialPreset.Boomerang;
        }

        if (candidates.Contains(MeleeSpecialPreset.SpinningSweep))
        {
            return MeleeSpecialPreset.SpinningSweep;
        }

        if (candidates.Contains(MeleeSpecialPreset.CleavingThrust))
        {
            return MeleeSpecialPreset.CleavingThrust;
        }

        if (candidates.Contains(MeleeSpecialPreset.RiftTrail))
        {
            return MeleeSpecialPreset.RiftTrail;
        }

        if (candidates.Contains(MeleeSpecialPreset.FractureLine))
        {
            return MeleeSpecialPreset.FractureLine;
        }

        if (candidates.Contains(MeleeSpecialPreset.LaunchSlam))
        {
            return MeleeSpecialPreset.LaunchSlam;
        }

        if (candidates.Contains(MeleeSpecialPreset.KnockbackChain))
        {
            return MeleeSpecialPreset.KnockbackChain;
        }

        if (candidates.Contains(MeleeSpecialPreset.Aftershock))
        {
            return MeleeSpecialPreset.Aftershock;
        }

        return candidates.Contains(MeleeSpecialPreset.SneakAmbush)
            ? MeleeSpecialPreset.SneakAmbush
            : MeleeSpecialPreset.None;
    }

    private static string FormatIgnoredMeleePresets(List<MeleeSpecialPreset> candidates, MeleeSpecialPreset selectedPreset)
    {
        List<string> ignored = new();
        foreach (MeleeSpecialPreset candidate in candidates)
        {
            if (candidate != selectedPreset)
            {
                ignored.Add(FormatMeleePreset(candidate));
            }
        }

        return string.Join(", ", ignored);
    }

    private static string FormatMeleePreset(MeleeSpecialPreset preset)
    {
        return preset switch
        {
            MeleeSpecialPreset.SneakAmbush => "sneakAmbush",
            MeleeSpecialPreset.CleavingThrust => "cleavingThrust",
            MeleeSpecialPreset.SpearRain => "spearRain",
            MeleeSpecialPreset.ImpactBurst => "impactBurst",
            MeleeSpecialPreset.Boomerang => "boomerang",
            MeleeSpecialPreset.SpinningSweep => "spinningSweep",
            MeleeSpecialPreset.LaunchSlam => "launchSlam",
            MeleeSpecialPreset.KnockbackChain => "knockbackChain",
            MeleeSpecialPreset.Aftershock => "aftershock",
            MeleeSpecialPreset.RiftTrail => "riftTrail",
            MeleeSpecialPreset.FractureLine => "fractureLine",
            _ => "none"
        };
    }

    private static void LogIgnoredExplicitMeleePresetBlocks(
        string prefabName,
        MeleeWeaponConfig raw,
        MeleeSpecialPreset selectedPreset)
    {
        List<string> ignoredBlocks = new();
        if (selectedPreset != MeleeSpecialPreset.SneakAmbush && raw.SneakAmbush != null)
        {
            ignoredBlocks.Add("sneakAmbush");
        }

        if (selectedPreset != MeleeSpecialPreset.CleavingThrust && raw.CleavingThrust != null)
        {
            ignoredBlocks.Add("cleavingThrust");
        }

        if (selectedPreset != MeleeSpecialPreset.SpearRain && raw.SpearRain != null)
        {
            ignoredBlocks.Add("spearRain");
        }

        if (selectedPreset != MeleeSpecialPreset.ImpactBurst && raw.ImpactBurst != null)
        {
            ignoredBlocks.Add("impactBurst");
        }

        if (selectedPreset != MeleeSpecialPreset.Boomerang && raw.Boomerang != null)
        {
            ignoredBlocks.Add("boomerang");
        }

        if (selectedPreset != MeleeSpecialPreset.SpinningSweep && raw.SpinningSweep != null)
        {
            ignoredBlocks.Add("spinningSweep");
        }

        if (selectedPreset != MeleeSpecialPreset.LaunchSlam && raw.LaunchSlam != null)
        {
            ignoredBlocks.Add("launchSlam");
        }

        if (selectedPreset != MeleeSpecialPreset.KnockbackChain && raw.KnockbackChain != null)
        {
            ignoredBlocks.Add("knockbackChain");
        }

        if (selectedPreset != MeleeSpecialPreset.Aftershock && raw.Aftershock != null)
        {
            ignoredBlocks.Add("aftershock");
        }

        if (selectedPreset != MeleeSpecialPreset.RiftTrail && raw.RiftTrail != null)
        {
            ignoredBlocks.Add("riftTrail");
        }

        if (selectedPreset != MeleeSpecialPreset.FractureLine && raw.FractureLine != null)
        {
            ignoredBlocks.Add("fractureLine");
        }

        if (ignoredBlocks.Count == 0)
        {
            return;
        }

        SecondaryAttacksPlugin.ModLogger.LogWarning(
            $"Ignoring melee preset block(s) {string.Join(", ", ignoredBlocks)} on {prefabName} because preset is {FormatMeleePreset(selectedPreset)}.");
    }

    private static NormalizedSneakAmbushConfig? NormalizeSelectedSneakAmbush(
        SneakAmbushConfig? rawSneakAmbush,
        NormalizedSneakAmbushConfig? fallback)
    {
        return NormalizeSneakAmbush(rawSneakAmbush ?? new SneakAmbushConfig(), fallback);
    }

    private static NormalizedCleavingThrustConfig? NormalizeSelectedCleavingThrust(
        CleavingThrustConfig? rawCleavingThrust,
        NormalizedCleavingThrustConfig? fallback)
    {
        return NormalizeCleavingThrust(rawCleavingThrust ?? new CleavingThrustConfig(), fallback);
    }

    private static NormalizedLaunchSlamConfig? NormalizeSelectedLaunchSlam(
        LaunchSlamConfig? rawLaunchSlam,
        NormalizedLaunchSlamConfig? fallback)
    {
        return NormalizeLaunchSlam(rawLaunchSlam ?? new LaunchSlamConfig(), fallback);
    }

    private static NormalizedKnockbackChainConfig? NormalizeSelectedKnockbackChain(
        KnockbackChainConfig? rawKnockbackChain,
        NormalizedKnockbackChainConfig? fallback)
    {
        return NormalizeKnockbackChain(rawKnockbackChain ?? new KnockbackChainConfig(), fallback);
    }

    private static NormalizedAftershockConfig? NormalizeSelectedAftershock(
        AftershockConfig? rawAftershock,
        NormalizedAftershockConfig? fallback)
    {
        return NormalizeAftershock(rawAftershock ?? new AftershockConfig(), fallback);
    }

    private static NormalizedRiftTrailConfig? NormalizeSelectedRiftTrail(
        RiftTrailConfig? rawRiftTrail,
        NormalizedRiftTrailConfig? fallback)
    {
        return NormalizeRiftTrail(rawRiftTrail ?? new RiftTrailConfig(), fallback);
    }

    private static NormalizedFractureLineConfig? NormalizeSelectedFractureLine(
        FractureLineConfig? rawFractureLine,
        NormalizedFractureLineConfig? fallback)
    {
        return NormalizeFractureLine(rawFractureLine ?? new FractureLineConfig(), fallback);
    }

    private static NormalizedImpactBurstConfig? NormalizeSelectedImpactBurst(
        ImpactBurstConfig? rawImpactBurst,
        NormalizedImpactBurstConfig? fallback)
    {
        return NormalizeImpactBurst(rawImpactBurst ?? new ImpactBurstConfig(), fallback);
    }

    private static NormalizedBoomerangConfig? NormalizeSelectedBoomerang(
        BoomerangConfig? rawBoomerang,
        NormalizedBoomerangConfig? fallback)
    {
        return NormalizeBoomerang(rawBoomerang ?? new BoomerangConfig(), fallback);
    }

    private static NormalizedSpinningSweepConfig? NormalizeSelectedSpinningSweep(
        SpinningSweepConfig? rawSpinningSweep,
        NormalizedSpinningSweepConfig? fallback)
    {
        return NormalizeSpinningSweep(rawSpinningSweep ?? new SpinningSweepConfig(), fallback);
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
            Projectile = new NormalizedProjectileSecondaryConfig(),
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
            Projectile = new NormalizedProjectileSecondaryConfig(),
            CopyFrom = FixedThrowCarrierPrefab,
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }

    private static NormalizedSecondaryModeConfig CreateSpinningSweepSecondary(NormalizedSpinningSweepConfig spinningSweep)
    {
        return new NormalizedSecondaryModeConfig
        {
            Type = "copy",
            Animation = spinningSweep.Animation,
            ResourceMultiplier = spinningSweep.ResourceMultiplier,
            OutputMultiplier = 1f,
            DurabilityFactor = spinningSweep.DurabilityFactor,
            Projectile = new NormalizedProjectileSecondaryConfig(),
            CopyFrom = "",
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }

    private static NormalizedSecondaryModeConfig NormalizeRanged(
        RangedWeaponConfig rawRanged,
        NormalizedSecondaryModeConfig? fallback)
    {
        NormalizedSecondaryModeConfig baseConfig = fallback ?? new NormalizedSecondaryModeConfig();
        string preset = rawRanged.Preset?.Trim() ?? baseConfig.Projectile.Preset;
        bool isBombPreset = IsBombRangedPreset(preset);
        return new NormalizedSecondaryModeConfig
        {
            Type = "projectile",
            Animation = isBombPreset
                ? ""
                : rawRanged.Animation != null
                    ? rawRanged.Animation.Trim()
                    : baseConfig.Animation,
            ResourceMultiplier = isBombPreset ? 1f : rawRanged.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
            DurabilityFactor = isBombPreset ? 1f : rawRanged.DurabilityFactor ?? baseConfig.DurabilityFactor,
            OutputMultiplier = 1f,
            Projectile = NormalizeRangedProjectile(rawRanged, baseConfig.Projectile, preset),
            CopyFrom = "",
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }

    private static NormalizedSecondaryModeConfig NormalizeMelee(
        MeleeWeaponConfig rawMelee,
        MeleeOnProjectileHitConfig? rawOnProjectileHit,
        bool forceSpearRainPreset,
        NormalizedMeleeOnProjectileHitConfig? fallbackOnProjectileHit,
        string secondaryType = "copy")
    {
        return new NormalizedSecondaryModeConfig
        {
            Type = secondaryType,
            Animation = rawMelee.Animation?.Trim() ?? "",
            ResourceMultiplier = rawMelee.ResourceMultiplier,
            OutputMultiplier = rawMelee.OutputMultiplier,
            DurabilityFactor = rawMelee.DurabilityFactor ?? 1f,
            Projectile = new NormalizedProjectileSecondaryConfig(),
            CopyFrom = rawMelee.CopyFrom?.Trim() ?? "",
            OnProjectileHit = NormalizeMeleeOnProjectileHit(rawOnProjectileHit, forceSpearRainPreset, fallbackOnProjectileHit),
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig(),
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig()
        };
    }

    private static bool HasMeleeSecondaryConfig(
        MeleeWeaponConfig rawMelee,
        MeleeSpecialPreset selectedPreset,
        MeleeOnProjectileHitConfig? selectedProjectileHit)
    {
        return !string.IsNullOrWhiteSpace(rawMelee.CopyFrom) ||
               !string.IsNullOrWhiteSpace(rawMelee.Animation) ||
               selectedProjectileHit != null ||
               selectedPreset == MeleeSpecialPreset.SpearRain ||
               selectedPreset == MeleeSpecialPreset.ImpactBurst ||
               selectedPreset == MeleeSpecialPreset.Boomerang ||
               selectedPreset == MeleeSpecialPreset.SpinningSweep ||
               selectedPreset == MeleeSpecialPreset.Aftershock ||
               selectedPreset == MeleeSpecialPreset.RiftTrail ||
               selectedPreset == MeleeSpecialPreset.FractureLine ||
               rawMelee.DurabilityFactor.HasValue ||
               Math.Abs(rawMelee.ResourceMultiplier - 1f) > 0.0001f ||
               Math.Abs(rawMelee.OutputMultiplier - 1f) > 0.0001f;
    }

    private static string ResolveMeleeSecondaryType(MeleeWeaponConfig rawMelee, MeleeSpecialPreset selectedPreset)
    {
        return selectedPreset switch
        {
            MeleeSpecialPreset.Aftershock => "aftershock",
            MeleeSpecialPreset.FractureLine => "fractureLine",
            _ => "copy"
        };
    }

    private static TNormalized? NormalizeOptional<TSource, TNormalized>(
        TSource? raw,
        TNormalized? fallback,
        Func<TNormalized> createDefault,
        Func<TNormalized, TNormalized> clone,
        Func<TSource, TNormalized, TNormalized> merge,
        bool inheritFallbackWhenRawMissing = true)
        where TSource : class
        where TNormalized : class
    {
        if (raw == null)
        {
            return inheritFallbackWhenRawMissing && fallback != null ? clone(fallback) : null;
        }

        return merge(raw, fallback ?? createDefault());
    }

    private static NormalizedMeleeOnProjectileHitConfig? NormalizeMeleeOnProjectileHit(
        MeleeOnProjectileHitConfig? rawOnHit,
        bool forceSpearRainPreset,
        NormalizedMeleeOnProjectileHitConfig? fallback)
    {
        return NormalizeOptional(
            rawOnHit,
            fallback,
            NormalizedMeleeOnProjectileHitConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) =>
            {
                string preset = forceSpearRainPreset ? "spearRain" : (raw.Preset?.Trim() ?? "");
                if (string.IsNullOrWhiteSpace(preset))
                {
                    preset = fallback != null
                        ? baseConfig.Preset
                        : "";
                }

                bool isSpearRainPreset = preset.Equals("spearRain", StringComparison.OrdinalIgnoreCase);
                return new NormalizedMeleeOnProjectileHitConfig
                {
                    Enabled = raw.Enabled ?? baseConfig.Enabled,
                    Preset = preset,
                    Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                    CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                    CooldownFallback = ProjectilePresetCooldownFallback.OriginalSecondary,
                    ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                    DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor,
                    ProjectileSpinAxis = isSpearRainPreset
                        ? "none"
                        : NormalizeProjectileSpinAxis(
                            raw.ProjectileSpinAxis,
                            baseConfig.ProjectileSpinAxis),
                    ProjectileVisualRotationOffset = isSpearRainPreset
                        ? Vector3.zero
                        : NormalizeProjectileVisualRotationOffset(
                            raw.ProjectileVisualRotationOffset,
                            baseConfig.ProjectileVisualRotationOffset),
                    Vfx = raw.Vfx != null ? raw.Vfx.Trim() : baseConfig.Vfx,
                    Count = raw.Count ?? baseConfig.Count,
                    SpawnHeight = raw.SpawnHeight ?? baseConfig.SpawnHeight,
                    SpawnRadius = raw.SpawnRadius ?? baseConfig.SpawnRadius,
                    FlightTime = raw.FlightTime ?? baseConfig.FlightTime,
                    DamageFactor = raw.DamageFactor ?? baseConfig.DamageFactor,
                    PushFactor = raw.PushFactor ?? baseConfig.PushFactor,
                    Radius = raw.Radius ?? baseConfig.Radius,
                    IncludeDirectTarget = raw.IncludeDirectTarget ?? baseConfig.IncludeDirectTarget,
                    IncludeDestructibles = raw.IncludeDestructibles ?? baseConfig.IncludeDestructibles,
                    TriggerOnCharactersOnly = true
                };
            });
    }

    private static NormalizedImpactBurstConfig? NormalizeImpactBurst(
        ImpactBurstConfig? rawImpactBurst,
        NormalizedImpactBurstConfig? fallback)
    {
        return NormalizeOptional(
            rawImpactBurst,
            fallback,
            NormalizedImpactBurstConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedImpactBurstConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Animation = !string.IsNullOrWhiteSpace(raw.Animation)
                    ? raw.Animation.Trim()
                    : baseConfig.Animation,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                CooldownFallback = ProjectilePresetCooldownFallback.OriginalSecondary,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor,
                ProjectileSpinAxis = NormalizeProjectileSpinAxis(
                    raw.ProjectileSpinAxis,
                    baseConfig.ProjectileSpinAxis),
                ProjectileVisualRotationOffset = NormalizeProjectileVisualRotationOffset(
                    raw.ProjectileVisualRotationOffset,
                    baseConfig.ProjectileVisualRotationOffset),
                Vfx = FixedImpactBurstVfx,
                Radius = raw.Radius ?? baseConfig.Radius,
                DamageFactor = raw.DamageFactor ?? baseConfig.DamageFactor,
                PushFactor = raw.PushFactor ?? baseConfig.PushFactor
            });
    }

    private static NormalizedBoomerangConfig? NormalizeBoomerang(
        BoomerangConfig? rawBoomerang,
        NormalizedBoomerangConfig? fallback)
    {
        return NormalizeOptional(
            rawBoomerang,
            fallback,
            NormalizedBoomerangConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedBoomerangConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Animation = !string.IsNullOrWhiteSpace(raw.Animation)
                    ? raw.Animation.Trim()
                    : baseConfig.Animation,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                CooldownFallback = ProjectilePresetCooldownFallback.OriginalSecondary,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor,
                ProjectileSpinAxis = NormalizeProjectileSpinAxis(
                    raw.ProjectileSpinAxis,
                    baseConfig.ProjectileSpinAxis),
                ProjectileVisualRotationOffset = NormalizeProjectileVisualRotationOffset(
                    raw.ProjectileVisualRotationOffset,
                    baseConfig.ProjectileVisualRotationOffset),
                MaxDistance = raw.MaxDistance ?? baseConfig.MaxDistance,
                CurveFactor = raw.CurveFactor ?? baseConfig.CurveFactor,
                DamageFactor = raw.DamageFactor ?? baseConfig.DamageFactor,
                PushFactor = raw.PushFactor ?? baseConfig.PushFactor,
                HitDamageDecay = raw.HitDamageDecay ?? baseConfig.HitDamageDecay,
                IncludeDestructibles = true
            });
    }

    private static NormalizedSpinningSweepConfig? NormalizeSpinningSweep(
        SpinningSweepConfig? rawSpinningSweep,
        NormalizedSpinningSweepConfig? fallback)
    {
        return NormalizeOptional(
            rawSpinningSweep,
            fallback,
            NormalizedSpinningSweepConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedSpinningSweepConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Animation = !string.IsNullOrWhiteSpace(raw.Animation)
                    ? raw.Animation.Trim()
                    : baseConfig.Animation,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor,
                LoopStart = raw.LoopStart ?? baseConfig.LoopStart,
                LoopEnd = raw.LoopEnd ?? baseConfig.LoopEnd,
                AnimationSpeed = raw.AnimationSpeed ?? baseConfig.AnimationSpeed,
                MoveSpeedFactor = raw.MoveSpeedFactor ?? baseConfig.MoveSpeedFactor,
                SkillRaiseFactor = raw.SkillRaiseFactor ?? baseConfig.SkillRaiseFactor,
            });
    }

    private static string NormalizeProjectileSpinAxis(string? rawSpinAxis, string fallback)
    {
        if (rawSpinAxis == null)
        {
            return fallback;
        }

        string normalized = ProjectileSpinAxis.Normalize(rawSpinAxis);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        string value = rawSpinAxis.Trim();
        if (value.Length > 0)
        {
            SecondaryAttackWarningLog.WarnOnce(
                $"projectile_spin_axis:{value}",
                $"Invalid projectileSpinAxis '{value}'. Expected 'none', 'horizontal', or 'vertical'. Falling back to '{fallback}'.");
        }

        return fallback;
    }

    private static Vector3 NormalizeProjectileVisualRotationOffset(string? rawOffset, Vector3 fallback)
    {
        if (string.IsNullOrWhiteSpace(rawOffset))
        {
            return fallback;
        }

        string[] parts = rawOffset!.Split(',');
        if (parts.Length != 3)
        {
            return fallback;
        }

        return float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
               float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) &&
               float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
            ? new Vector3(x, y, z)
            : fallback;
    }

    private static NormalizedSneakAmbushConfig? NormalizeSneakAmbush(
        SneakAmbushConfig? rawSneakAmbush,
        NormalizedSneakAmbushConfig? fallback)
    {
        return NormalizeOptional(
            rawSneakAmbush,
            fallback,
            NormalizedSneakAmbushConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedSneakAmbushConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor,
                ChargeMaxSeconds = raw.ChargeMaxSeconds ?? baseConfig.ChargeMaxSeconds,
                ChargeSkillFactor = raw.ChargeSkillFactor ?? baseConfig.ChargeSkillFactor,
                AggroResetRangePerChargeSecond = raw.AggroResetRangePerChargeSecond ?? baseConfig.AggroResetRangePerChargeSecond,
                SenseBlockDurationPerChargeSecond = raw.SenseBlockDurationPerChargeSecond ?? baseConfig.SenseBlockDurationPerChargeSecond,
                BackstabResetSecondsPerChargeSecond = raw.BackstabResetSecondsPerChargeSecond ?? baseConfig.BackstabResetSecondsPerChargeSecond
            });
    }

    private static NormalizedCleavingThrustConfig? NormalizeCleavingThrust(
        CleavingThrustConfig? rawCleavingThrust,
        NormalizedCleavingThrustConfig? fallback)
    {
        return NormalizeOptional(
            rawCleavingThrust,
            fallback,
            NormalizedCleavingThrustConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedCleavingThrustConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor,
                RangeFactor = raw.RangeFactor ?? baseConfig.RangeFactor,
                Angle = raw.Angle ?? baseConfig.Angle,
                DamageFactor = raw.DamageFactor ?? baseConfig.DamageFactor,
                PushFactor = raw.PushFactor ?? baseConfig.PushFactor
            });
    }

    private static NormalizedLaunchSlamConfig? NormalizeLaunchSlam(
        LaunchSlamConfig? rawLaunchSlam,
        NormalizedLaunchSlamConfig? fallback)
    {
        return NormalizeOptional(
            rawLaunchSlam,
            fallback,
            NormalizedLaunchSlamConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedLaunchSlamConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor,
                LaunchHeight = raw.LaunchHeight ?? baseConfig.LaunchHeight,
                DamageFactor = raw.DamageFactor ?? baseConfig.DamageFactor,
                Vfx = baseConfig.Vfx,
                VfxRotationOffset = baseConfig.VfxRotationOffset,
                Sfx = baseConfig.Sfx
            });
    }

    private static NormalizedKnockbackChainConfig? NormalizeKnockbackChain(
        KnockbackChainConfig? rawKnockbackChain,
        NormalizedKnockbackChainConfig? fallback)
    {
        return NormalizeOptional(
            rawKnockbackChain,
            fallback,
            NormalizedKnockbackChainConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedKnockbackChainConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor,
                PushFactor = raw.PushFactor ?? baseConfig.PushFactor,
                ChainDecay = raw.ChainDecay ?? baseConfig.ChainDecay
            });
    }

    private static NormalizedAftershockConfig? NormalizeAftershock(
        AftershockConfig? rawAftershock,
        NormalizedAftershockConfig? fallback)
    {
        return NormalizeOptional(
            rawAftershock,
            fallback,
            NormalizedAftershockConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedAftershockConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                Waves = raw.Waves ?? baseConfig.Waves,
                Interval = raw.Interval ?? baseConfig.Interval,
                WaveDecay = raw.WaveDecay ?? baseConfig.WaveDecay,
                ForwardStep = raw.ForwardStep ?? baseConfig.ForwardStep,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor
            });
    }

    private static NormalizedRiftTrailConfig? NormalizeRiftTrail(
        RiftTrailConfig? rawRiftTrail,
        NormalizedRiftTrailConfig? fallback)
    {
        return NormalizeOptional(
            rawRiftTrail,
            fallback,
            NormalizedRiftTrailConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedRiftTrailConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                Duration = raw.Duration ?? baseConfig.Duration,
                TickInterval = raw.TickInterval ?? baseConfig.TickInterval,
                DamageFactor = raw.DamageFactor ?? baseConfig.DamageFactor,
                PushFactor = raw.PushFactor ?? baseConfig.PushFactor,
                Range = raw.Range ?? baseConfig.Range,
                Angle = raw.Angle ?? baseConfig.Angle,
                Width = raw.Width ?? baseConfig.Width,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor,
                VisualScaleFactor = baseConfig.VisualScaleFactor,
                VisualForwardOffset = baseConfig.VisualForwardOffset,
                VisualTint = baseConfig.VisualTint,
                VisualAlphaFactor = baseConfig.VisualAlphaFactor
            });
    }

    private static NormalizedFractureLineConfig? NormalizeFractureLine(
        FractureLineConfig? rawFractureLine,
        NormalizedFractureLineConfig? fallback)
    {
        return NormalizeOptional(
            rawFractureLine,
            fallback,
            NormalizedFractureLineConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedFractureLineConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                Range = raw.Range ?? baseConfig.Range,
                HitSpacing = raw.HitSpacing ?? baseConfig.HitSpacing,
                Duration = raw.Duration ?? baseConfig.Duration,
                TickInterval = raw.TickInterval ?? baseConfig.TickInterval,
                DamageFactor = raw.DamageFactor ?? baseConfig.DamageFactor,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor
            });
    }

    private static NormalizedHarvestSweepConfig? NormalizeHarvestSweep(
        HarvestSweepConfig? rawHarvestSweep,
        NormalizedHarvestSweepConfig? fallback)
    {
        return NormalizeOptional(
            rawHarvestSweep,
            fallback,
            NormalizedHarvestSweepConfig.CreateDefault,
            config => config.Clone(),
            (raw, baseConfig) => new NormalizedHarvestSweepConfig
            {
                Enabled = raw.Enabled ?? baseConfig.Enabled,
                Cooldown = raw.Cooldown ?? baseConfig.Cooldown,
                CooldownReductionFactor = raw.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
                ResourceMultiplier = raw.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
                DurabilityFactor = raw.DurabilityFactor ?? baseConfig.DurabilityFactor,
                Animation = !string.IsNullOrWhiteSpace(raw.Animation)
                    ? raw.Animation!.Trim()
                    : baseConfig.Animation,
                LoopStart = raw.LoopStart ?? baseConfig.LoopStart,
                LoopEnd = raw.LoopEnd ?? baseConfig.LoopEnd,
                AnimationSpeed = raw.AnimationSpeed ?? baseConfig.AnimationSpeed,
                MoveSpeedFactor = raw.MoveSpeedFactor ?? baseConfig.MoveSpeedFactor,
                SkillRaiseFactor = raw.SkillRaiseFactor ?? baseConfig.SkillRaiseFactor
            },
            inheritFallbackWhenRawMissing: false);
    }

    private static NormalizedSecondaryModeConfig NormalizeBloodMagic(
        BloodMagicWeaponConfig rawBloodMagic,
        NormalizedSecondaryModeConfig? fallback)
    {
        NormalizedSecondaryModeConfig baseConfig = fallback ?? new NormalizedSecondaryModeConfig();
        NormalizedSummonEmpowerSecondaryConfig summonEmpowerBase = fallback?.SummonEmpower ?? new NormalizedSummonEmpowerSecondaryConfig();
        NormalizedShieldConvertSecondaryConfig shieldConvertBase = fallback?.ShieldConvert ?? new NormalizedShieldConvertSecondaryConfig();
        string preset = rawBloodMagic.Preset?.Trim() ?? baseConfig.Type;
        float summonEmpowerRadius = rawBloodMagic.Radius ?? summonEmpowerBase.Radius;
        float shieldConvertRadius = rawBloodMagic.Radius ?? shieldConvertBase.Radius;
        return new NormalizedSecondaryModeConfig
        {
            Type = preset,
            Animation = rawBloodMagic.Animation != null
                ? rawBloodMagic.Animation.Trim()
                : baseConfig.Animation,
            ResourceMultiplier = rawBloodMagic.ResourceMultiplier ?? baseConfig.ResourceMultiplier,
            DurabilityFactor = rawBloodMagic.DurabilityFactor ?? baseConfig.DurabilityFactor,
            Projectile = new NormalizedProjectileSecondaryConfig(),
            CopyFrom = "",
            SummonEmpower = new NormalizedSummonEmpowerSecondaryConfig
            {
                PresetCooldown = new MeleePresetCooldownDefinition
                {
                    Cooldown = rawBloodMagic.Cooldown ?? summonEmpowerBase.PresetCooldown.Cooldown,
                    CooldownSkill = string.IsNullOrWhiteSpace(summonEmpowerBase.PresetCooldown.CooldownSkill)
                        ? "bloodMagic"
                        : summonEmpowerBase.PresetCooldown.CooldownSkill.Trim(),
                    CooldownReductionFactor = rawBloodMagic.CooldownReductionFactor ?? summonEmpowerBase.PresetCooldown.CooldownReductionFactor
                },
                Radius = summonEmpowerRadius,
                Duration = rawBloodMagic.Duration ?? summonEmpowerBase.Duration,
                MoveSpeedFactor = rawBloodMagic.MoveSpeedFactor ?? summonEmpowerBase.MoveSpeedFactor,
                AttackSpeedFactor = rawBloodMagic.AttackSpeedFactor ?? summonEmpowerBase.AttackSpeedFactor
            },
            ShieldConvert = new NormalizedShieldConvertSecondaryConfig
            {
                PresetCooldown = new MeleePresetCooldownDefinition
                {
                    Cooldown = rawBloodMagic.Cooldown ?? shieldConvertBase.PresetCooldown.Cooldown,
                    CooldownSkill = string.IsNullOrWhiteSpace(shieldConvertBase.PresetCooldown.CooldownSkill)
                        ? "bloodMagic"
                        : shieldConvertBase.PresetCooldown.CooldownSkill.Trim(),
                    CooldownReductionFactor = rawBloodMagic.CooldownReductionFactor ?? shieldConvertBase.PresetCooldown.CooldownReductionFactor
                },
                Radius = shieldConvertRadius,
                HealFactor = rawBloodMagic.HealFactor ?? shieldConvertBase.HealFactor
            }
        };
    }

    private static NormalizedProjectileSecondaryConfig NormalizeRangedProjectile(
        RangedWeaponConfig rawRanged,
        NormalizedProjectileSecondaryConfig baseConfig,
        string preset)
    {
        bool isStickyDetonatorPreset = IsStickyDetonatorRangedPreset(preset);
        bool isOverchargedBombPreset = IsOverchargedBombRangedPreset(preset);
        bool isBombPreset = isStickyDetonatorPreset || isOverchargedBombPreset;
        NormalizedProjectileSecondaryConfig defaultConfig = new();
        return new NormalizedProjectileSecondaryConfig
        {
            Preset = preset,
            Cooldown = isBombPreset ? 0f : rawRanged.Cooldown ?? baseConfig.Cooldown,
            CooldownReductionFactor = isBombPreset ? 0f : rawRanged.CooldownReductionFactor ?? baseConfig.CooldownReductionFactor,
            DamageFactor = isStickyDetonatorPreset ? 1f : rawRanged.DamageFactor ?? baseConfig.DamageFactor,
            ProjectileSpeedFactor = rawRanged.ProjectileSpeedFactor ?? baseConfig.ProjectileSpeedFactor,
            ProjectileScaleFactor = rawRanged.ProjectileScaleFactor ?? baseConfig.ProjectileScaleFactor,
            DurabilityFactor = isBombPreset ? 1f : rawRanged.DurabilityFactor ?? baseConfig.DurabilityFactor,
            Count = isBombPreset ? 1 : rawRanged.Count ?? baseConfig.Count,
            SpreadAngle = isBombPreset ? 0f : rawRanged.SpreadAngle ?? baseConfig.SpreadAngle,
            AmmoConsumption = rawRanged.AmmoConsumption ?? baseConfig.AmmoConsumption,
            VolleyRadius = rawRanged.VolleyRadius ?? baseConfig.VolleyRadius,
            VolleyArcAngleMin = rawRanged.VolleyArcAngleMin ?? baseConfig.VolleyArcAngleMin,
            VolleyArcAngleMax = rawRanged.VolleyArcAngleMax ?? baseConfig.VolleyArcAngleMax,
            VolleyMaxRange = rawRanged.VolleyMaxRange ?? baseConfig.VolleyMaxRange,
            Interval = rawRanged.Interval ?? baseConfig.Interval,
            HoldRepeatInterval = rawRanged.HoldRepeatInterval ?? baseConfig.HoldRepeatInterval,
            BarrageSpacing = rawRanged.BarrageSpacing ?? baseConfig.BarrageSpacing,
            MeteorRadius = rawRanged.MeteorRadius ?? baseConfig.MeteorRadius,
            PierceDamageDecay = rawRanged.PierceDamageDecay ?? baseConfig.PierceDamageDecay,
            SplitAngle = rawRanged.SplitAngle ?? baseConfig.SplitAngle,
            RicochetBounces = rawRanged.RicochetBounces ?? baseConfig.RicochetBounces,
            RicochetDecay = rawRanged.RicochetDecay ?? baseConfig.RicochetDecay,
            RicochetRoughness = rawRanged.RicochetRoughness ?? baseConfig.RicochetRoughness,
            SpiralRadius = rawRanged.SpiralRadius ?? baseConfig.SpiralRadius,
            SpiralTurns = rawRanged.SpiralTurns ?? baseConfig.SpiralTurns,
            SentinelDetectionRange = rawRanged.DetectionRange ?? baseConfig.SentinelDetectionRange,
            SentinelHoverDistance = rawRanged.HoverDistance ?? baseConfig.SentinelHoverDistance,
            SentinelHoverHeight = rawRanged.HoverHeight ?? baseConfig.SentinelHoverHeight,
            SentinelHoverElevationAngle = rawRanged.HoverElevationAngle ?? baseConfig.SentinelHoverElevationAngle,
            SentinelOrbitRadius = rawRanged.OrbitRadius ?? baseConfig.SentinelOrbitRadius,
            SentinelOrbitSpeed = rawRanged.OrbitSpeed ?? baseConfig.SentinelOrbitSpeed,
            SentinelLifetime = rawRanged.Lifetime ?? baseConfig.SentinelLifetime,
            SentinelAttackDelay = rawRanged.AttackDelay ?? baseConfig.SentinelAttackDelay,
            MeteorSpawnHeight = rawRanged.MeteorSpawnHeight ?? baseConfig.MeteorSpawnHeight,
            MaxCharges = isStickyDetonatorPreset ? rawRanged.MaxCharges ?? baseConfig.MaxCharges : defaultConfig.MaxCharges,
            DetonateAnimation = isStickyDetonatorPreset
                ? rawRanged.DetonateAnimation != null
                    ? rawRanged.DetonateAnimation.Trim()
                    : baseConfig.DetonateAnimation
                : "",
            AoeRadiusFactor = isStickyDetonatorPreset ? 1f : rawRanged.AoeRadiusFactor ?? baseConfig.AoeRadiusFactor
        };
    }

    private static bool IsBombRangedPreset(string? preset)
    {
        return IsStickyDetonatorRangedPreset(preset) || IsOverchargedBombRangedPreset(preset);
    }

    private static bool IsStickyDetonatorRangedPreset(string? preset)
    {
        return preset != null &&
               preset.Trim().Equals("stickyDetonator", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOverchargedBombRangedPreset(string? preset)
    {
        return preset != null &&
               preset.Trim().Equals("overchargedBomb", StringComparison.OrdinalIgnoreCase);
    }

}
