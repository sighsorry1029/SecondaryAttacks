using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecondaryAttacks;

internal static partial class SecondaryAttackManager
{
    internal static bool TryCreateCustomPayloadDefinition(
        string prefabName,
        ItemDrop.ItemData.SharedData sharedData,
        Attack primaryAttack,
        NormalizedWeaponConfig weaponConfig,
        List<ConfiguredWeaponEffectDefinition> configuredEffects,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;
        NormalizedProjectileSecondaryConfig projectileConfig = weaponConfig.Secondary?.Projectile ?? new NormalizedProjectileSecondaryConfig();
        if (!TryParsePreset(projectileConfig.Preset, out SecondaryAttackPreset preset))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: unknown projectile preset '{projectileConfig.Preset}'.");
            return false;
        }

        bool usesAmmo = !string.IsNullOrWhiteSpace(sharedData.m_ammoType);
        if (!ProjectileRuntimeSystem.TryValidateConfiguredPayload(prefabName, primaryAttack, preset, usesAmmo, out string compatibilityReason))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: {compatibilityReason}");
            return false;
        }

        int projectileCount = Mathf.Max(1, projectileConfig.Count);
        int ammoConsumption = ResolveAmmoConsumption(projectileConfig.AmmoConsumption, usesAmmo, preset, projectileCount);
        string resolvedAttackAnimation = GetNormalizedAttackAnimation(weaponConfig);
        bool hasCustomAttackAnimation = !string.IsNullOrWhiteSpace(resolvedAttackAnimation);
        string attackAnimation = hasCustomAttackAnimation
            ? resolvedAttackAnimation.Trim()
            : primaryAttack.m_attackAnimation.Trim();
        bool isBombPreset = IsBombProjectilePreset(preset);
        float resourceMultiplier = isBombPreset ? 1f : Mathf.Max(0f, GetNormalizedResourceMultiplier(weaponConfig));
        float durabilityFactor = isBombPreset ? 1f : Mathf.Max(0f, projectileConfig.DurabilityFactor);
        float spreadAngle = isBombPreset ? 0f : Mathf.Max(0f, projectileConfig.SpreadAngle);
        float rewardFactor = ResolveProjectileRewardFactor(preset, projectileCount);
        float volleyArcAngleMin = Mathf.Clamp(projectileConfig.VolleyArcAngleMin, 1f, 89f);
        float volleyArcAngleMax = Mathf.Clamp(projectileConfig.VolleyArcAngleMax, 1f, 89f);
        if (volleyArcAngleMax < volleyArcAngleMin)
        {
            (volleyArcAngleMin, volleyArcAngleMax) = (volleyArcAngleMax, volleyArcAngleMin);
        }
        float interval = projectileConfig.Interval;
        if (preset != SecondaryAttackPreset.Barrage &&
            preset != SecondaryAttackPreset.Volley &&
            preset != SecondaryAttackPreset.Meteor &&
            preset != SecondaryAttackPreset.Spiral &&
            interval <= 0f)
        {
            interval = 0.12f;
        }

        definition = new SecondaryAttackDefinition
        {
            PrefabName = prefabName,
            AppliesSecondaryOverride = true,
            Behavior = new ProjectileSecondaryBehavior
            {
                Preset = preset,
                Cooldown = isBombPreset ? 0f : Mathf.Max(0f, projectileConfig.Cooldown),
                CooldownReductionFactor = isBombPreset ? 0f : Mathf.Clamp01(projectileConfig.CooldownReductionFactor),
                DamageFactor = Mathf.Max(0f, projectileConfig.DamageFactor),
                SkillRaiseFactor = rewardFactor,
                AdrenalineFactor = rewardFactor,
                ProjectileSpeedFactor = Mathf.Max(0.01f, projectileConfig.ProjectileSpeedFactor),
                ProjectileScaleFactor = Mathf.Max(0.01f, projectileConfig.ProjectileScaleFactor),
                DurabilityFactor = durabilityFactor,
                ProjectileCount = projectileCount,
                SpreadAngle = spreadAngle,
                AmmoConsumption = Mathf.Max(0, ammoConsumption),
                VolleyRadius = Mathf.Max(0f, projectileConfig.VolleyRadius),
                VolleyArcAngleMin = volleyArcAngleMin,
                VolleyArcAngleMax = volleyArcAngleMax,
                VolleyMaxRange = Mathf.Max(1f, projectileConfig.VolleyMaxRange),
                Interval = preset is SecondaryAttackPreset.Barrage or SecondaryAttackPreset.Volley or SecondaryAttackPreset.Meteor or SecondaryAttackPreset.Spiral ? Mathf.Max(0f, interval) : Mathf.Max(0.01f, interval),
                HoldRepeatInterval = Mathf.Max(0.01f, projectileConfig.HoldRepeatInterval),
                BarrageSpacing = Mathf.Max(0f, projectileConfig.BarrageSpacing),
                MeteorRadius = Mathf.Max(0f, projectileConfig.MeteorRadius),
                PierceDamageDecay = Mathf.Clamp01(projectileConfig.PierceDamageDecay),
                SplitAngle = Mathf.Max(0f, projectileConfig.SplitAngle),
                RicochetBounces = Mathf.Max(0, projectileConfig.RicochetBounces),
                RicochetDecay = Mathf.Clamp01(projectileConfig.RicochetDecay),
                RicochetRoughness = Mathf.Clamp01(projectileConfig.RicochetRoughness),
                SpiralRadius = Mathf.Max(0f, projectileConfig.SpiralRadius),
                SpiralTurns = Mathf.Max(0f, projectileConfig.SpiralTurns),
                SentinelDetectionRange = Mathf.Max(1f, projectileConfig.SentinelDetectionRange),
                SentinelHoverDistance = Mathf.Max(0.5f, projectileConfig.SentinelHoverDistance),
                SentinelHoverHeight = Mathf.Max(0.5f, projectileConfig.SentinelHoverHeight),
                SentinelHoverElevationAngle = Mathf.Clamp(projectileConfig.SentinelHoverElevationAngle, 0f, 90f),
                SentinelOrbitRadius = Mathf.Max(0f, projectileConfig.SentinelOrbitRadius),
                SentinelOrbitSpeed = Mathf.Max(0f, projectileConfig.SentinelOrbitSpeed),
                SentinelLifetime = Mathf.Max(0.5f, projectileConfig.SentinelLifetime),
                SentinelAttackDelay = Mathf.Max(0f, projectileConfig.SentinelAttackDelay),
                MeteorSpawnHeight = Mathf.Max(1f, projectileConfig.MeteorSpawnHeight),
                MaxCharges = Mathf.Max(1, projectileConfig.MaxCharges),
                DetonateAnimation = projectileConfig.DetonateAnimation.Trim(),
                AoeRadiusFactor = Mathf.Max(0.01f, projectileConfig.AoeRadiusFactor)
            },
            AttackAnimation = attackAnimation,
            HasCustomAttackAnimation = hasCustomAttackAnimation,
            ResourceMultiplier = resourceMultiplier,
            OutputMultiplier = Mathf.Max(0f, GetNormalizedOutputMultiplier(weaponConfig)),
            DurabilityFactor = durabilityFactor,
            SneakAmbush = CreateSneakAmbushDefinition(weaponConfig),
            CleavingThrust = CreateCleavingThrustDefinition(weaponConfig),
            LaunchSlam = CreateLaunchSlamDefinition(weaponConfig),
            KnockbackChain = CreateKnockbackChainDefinition(weaponConfig),
            Aftershock = CreateAftershockDefinition(weaponConfig),
            RiftTrail = CreateRiftTrailDefinition(weaponConfig),
            FractureLine = CreateFractureLineDefinition(weaponConfig),
            HarvestSweep = CreateHarvestSweepDefinition(weaponConfig),
            OnProjectileHit = CreateMeleeOnProjectileHitDefinition(prefabName, weaponConfig.Secondary?.OnProjectileHit),
            Boomerang = CreateBoomerangDefinition(weaponConfig),
            SpinningSweep = CreateSpinningSweepDefinition(weaponConfig),
            ConfiguredEffects = configuredEffects
        };
        ApplyAttackResourceScaling(definition, primaryAttack, resourceMultiplier);
        return true;
    }

    private static bool IsBombProjectilePreset(SecondaryAttackPreset preset)
    {
        return preset is SecondaryAttackPreset.StickyDetonator or SecondaryAttackPreset.OverchargedBomb;
    }

    private static float ResolveProjectileRewardFactor(SecondaryAttackPreset preset, int projectileCount)
    {
        return preset switch
        {
            SecondaryAttackPreset.StickyDetonator or SecondaryAttackPreset.OverchargedBomb => 0f,
            SecondaryAttackPreset.Piercing => 1f,
            _ => 1f / Mathf.Max(1, projectileCount)
        };
    }

    internal static bool TryCreateSecondaryOverrideDefinition(
        string prefabName,
        string sourcePrefabName,
        Attack primaryAttack,
        Attack sourceSecondaryAttack,
        NormalizedWeaponConfig weaponConfig,
        List<ConfiguredWeaponEffectDefinition> configuredEffects,
        out SecondaryAttackDefinition? definition)
    {
        string resolvedAttackAnimation = GetNormalizedAttackAnimation(weaponConfig);
        bool hasCustomAttackAnimation = !string.IsNullOrWhiteSpace(resolvedAttackAnimation);
        string attackAnimation = hasCustomAttackAnimation
            ? resolvedAttackAnimation.Trim()
            : sourceSecondaryAttack.m_attackAnimation.Trim();

        definition = new SecondaryAttackDefinition
        {
            PrefabName = prefabName,
            AppliesSecondaryOverride = true,
            Behavior = new CopiedSecondaryBehavior
            {
                SourcePrefabName = sourcePrefabName
            },
            AttackAnimation = attackAnimation,
            HasCustomAttackAnimation = hasCustomAttackAnimation,
            ResourceMultiplier = Mathf.Max(0f, GetSelectedMeleeResourceMultiplier(weaponConfig)),
            OutputMultiplier = Mathf.Max(0f, GetNormalizedOutputMultiplier(weaponConfig)),
            DurabilityFactor = Mathf.Max(0f, GetSelectedMeleeDurabilityFactor(weaponConfig)),
            SneakAmbush = CreateSneakAmbushDefinition(weaponConfig),
            CleavingThrust = CreateCleavingThrustDefinition(weaponConfig),
            LaunchSlam = CreateLaunchSlamDefinition(weaponConfig),
            KnockbackChain = CreateKnockbackChainDefinition(weaponConfig),
            Aftershock = CreateAftershockDefinition(weaponConfig),
            RiftTrail = CreateRiftTrailDefinition(weaponConfig),
            FractureLine = CreateFractureLineDefinition(weaponConfig),
            HarvestSweep = CreateHarvestSweepDefinition(weaponConfig),
            OnProjectileHit = CreateMeleeOnProjectileHitDefinition(prefabName, weaponConfig.Secondary?.OnProjectileHit),
            Boomerang = CreateBoomerangDefinition(weaponConfig),
            SpinningSweep = CreateSpinningSweepDefinition(weaponConfig),
            ConfiguredEffects = configuredEffects
        };
        ApplyAttackResourceScaling(definition, primaryAttack, GetSelectedMeleeResourceMultiplier(weaponConfig));
        return true;
    }

    internal static bool TryCreateAftershockDefinition(
        SecondaryAttackDefinitionBuildContext buildContext,
        string prefabName,
        ItemDrop.ItemData.SharedData sharedData,
        Attack primaryAttack,
        NormalizedWeaponConfig weaponConfig,
        List<ConfiguredWeaponEffectDefinition> configuredEffects,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;
        string sourcePrefabName = string.IsNullOrWhiteSpace(weaponConfig.Secondary?.CopyFrom)
            ? prefabName
            : weaponConfig.Secondary!.CopyFrom.Trim();
        if (!TryResolveAftershockSourceAttack(buildContext.ObjectDb, sourcePrefabName, out Attack? sourceAttack, out string reason))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: {reason}");
            if (weaponConfig.SneakAmbush?.Enabled == true ||
                weaponConfig.CleavingThrust?.Enabled == true ||
                weaponConfig.LaunchSlam?.Enabled == true ||
                weaponConfig.KnockbackChain?.Enabled == true ||
                weaponConfig.RiftTrail?.Enabled == true ||
                weaponConfig.FractureLine?.Enabled == true ||
                weaponConfig.HarvestSweep?.Enabled == true ||
                weaponConfig.SpinningSweep?.Enabled == true ||
                configuredEffects.Count > 0)
            {
                definition = CreateEffectOnlyDefinition(prefabName, weaponConfig, configuredEffects);
                return true;
            }

            return false;
        }

        string resolvedAttackAnimation = GetNormalizedAttackAnimation(weaponConfig);
        bool hasCustomAttackAnimation = !string.IsNullOrWhiteSpace(resolvedAttackAnimation);
        string attackAnimation = hasCustomAttackAnimation
            ? resolvedAttackAnimation.Trim()
            : sourceAttack!.m_attackAnimation.Trim();

        definition = new SecondaryAttackDefinition
        {
            PrefabName = prefabName,
            AppliesSecondaryOverride = true,
            Behavior = new AftershockSecondaryBehavior
            {
                SourcePrefabName = sourcePrefabName
            },
            AttackAnimation = attackAnimation,
            HasCustomAttackAnimation = hasCustomAttackAnimation,
            ResourceMultiplier = Mathf.Max(0f, GetSelectedMeleeResourceMultiplier(weaponConfig)),
            OutputMultiplier = Mathf.Max(0f, GetNormalizedOutputMultiplier(weaponConfig)),
            DurabilityFactor = Mathf.Max(0f, GetSelectedMeleeDurabilityFactor(weaponConfig)),
            SneakAmbush = CreateSneakAmbushDefinition(weaponConfig),
            CleavingThrust = CreateCleavingThrustDefinition(weaponConfig),
            LaunchSlam = CreateLaunchSlamDefinition(weaponConfig),
            KnockbackChain = CreateKnockbackChainDefinition(weaponConfig),
            Aftershock = CreateAftershockDefinition(weaponConfig),
            RiftTrail = CreateRiftTrailDefinition(weaponConfig),
            FractureLine = CreateFractureLineDefinition(weaponConfig),
            HarvestSweep = CreateHarvestSweepDefinition(weaponConfig),
            OnProjectileHit = CreateMeleeOnProjectileHitDefinition(prefabName, weaponConfig.Secondary?.OnProjectileHit),
            Boomerang = CreateBoomerangDefinition(weaponConfig),
            SpinningSweep = CreateSpinningSweepDefinition(weaponConfig),
            ConfiguredEffects = configuredEffects
        };
        ApplyAttackResourceScaling(definition, primaryAttack, GetSelectedMeleeResourceMultiplier(weaponConfig));
        return true;
    }

    internal static bool TryCreateFractureLineDefinition(
        SecondaryAttackDefinitionBuildContext buildContext,
        string prefabName,
        Attack primaryAttack,
        NormalizedWeaponConfig weaponConfig,
        List<ConfiguredWeaponEffectDefinition> configuredEffects,
        out SecondaryAttackDefinition? definition)
    {
        definition = null;
        string sourcePrefabName = string.IsNullOrWhiteSpace(weaponConfig.Secondary?.CopyFrom)
            ? prefabName
            : weaponConfig.Secondary!.CopyFrom.Trim();
        if (!TryResolveFractureLineSourceAttack(buildContext.ObjectDb, sourcePrefabName, out Attack? sourceAttack, out string reason))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName}: {reason}");
            if (weaponConfig.SneakAmbush?.Enabled == true ||
                weaponConfig.CleavingThrust?.Enabled == true ||
                weaponConfig.LaunchSlam?.Enabled == true ||
                weaponConfig.KnockbackChain?.Enabled == true ||
                weaponConfig.RiftTrail?.Enabled == true ||
                weaponConfig.HarvestSweep?.Enabled == true ||
                weaponConfig.SpinningSweep?.Enabled == true ||
                configuredEffects.Count > 0)
            {
                definition = CreateEffectOnlyDefinition(prefabName, weaponConfig, configuredEffects);
                return true;
            }

            return false;
        }

        string resolvedAttackAnimation = GetNormalizedAttackAnimation(weaponConfig);
        bool hasCustomAttackAnimation = !string.IsNullOrWhiteSpace(resolvedAttackAnimation);
        string attackAnimation = hasCustomAttackAnimation
            ? resolvedAttackAnimation.Trim()
            : sourceAttack!.m_attackAnimation.Trim();

        definition = new SecondaryAttackDefinition
        {
            PrefabName = prefabName,
            AppliesSecondaryOverride = true,
            Behavior = new FractureLineSecondaryBehavior
            {
                SourcePrefabName = sourcePrefabName
            },
            AttackAnimation = attackAnimation,
            HasCustomAttackAnimation = hasCustomAttackAnimation,
            ResourceMultiplier = Mathf.Max(0f, GetSelectedMeleeResourceMultiplier(weaponConfig)),
            OutputMultiplier = Mathf.Max(0f, GetNormalizedOutputMultiplier(weaponConfig)),
            DurabilityFactor = Mathf.Max(0f, GetSelectedMeleeDurabilityFactor(weaponConfig)),
            SneakAmbush = CreateSneakAmbushDefinition(weaponConfig),
            CleavingThrust = CreateCleavingThrustDefinition(weaponConfig),
            LaunchSlam = CreateLaunchSlamDefinition(weaponConfig),
            KnockbackChain = CreateKnockbackChainDefinition(weaponConfig),
            Aftershock = CreateAftershockDefinition(weaponConfig),
            RiftTrail = CreateRiftTrailDefinition(weaponConfig),
            FractureLine = CreateFractureLineDefinition(weaponConfig),
            HarvestSweep = CreateHarvestSweepDefinition(weaponConfig),
            OnProjectileHit = CreateMeleeOnProjectileHitDefinition(prefabName, weaponConfig.Secondary?.OnProjectileHit),
            Boomerang = CreateBoomerangDefinition(weaponConfig),
            SpinningSweep = CreateSpinningSweepDefinition(weaponConfig),
            ConfiguredEffects = configuredEffects
        };
        ApplyAttackResourceScaling(definition, primaryAttack, GetSelectedMeleeResourceMultiplier(weaponConfig));
        return true;
    }

    private static MeleeOnProjectileHitDefinition? CreateMeleeOnProjectileHitDefinition(string prefabName, NormalizedMeleeOnProjectileHitConfig? config)
    {
        if (config == null || !config.Enabled || string.IsNullOrWhiteSpace(config.Preset))
        {
            return null;
        }

        string preset = config.Preset.Trim();
        if (!preset.Equals("spearRain", StringComparison.OrdinalIgnoreCase) &&
            !preset.Equals("impactBurst", StringComparison.OrdinalIgnoreCase))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {prefabName} onProjectileHit: unknown preset '{config.Preset}'.");
            return null;
        }

        return new MeleeOnProjectileHitDefinition
        {
            Preset = preset.Equals("impactBurst", StringComparison.OrdinalIgnoreCase) ? "impactBurst" : "spearRain",
            PresetCooldown = CreatePresetCooldown(
                config.Cooldown,
                config.CooldownReductionFactor),
            CooldownFallback = config.CooldownFallback,
            DurabilityFactor = Mathf.Max(0f, config.DurabilityFactor),
            ProjectileSpinAxis = config.ProjectileSpinAxis,
            ProjectileVisualRotationOffset = config.ProjectileVisualRotationOffset,
            Vfx = config.Vfx,
            Count = Mathf.Max(1, config.Count),
            SpawnHeight = Mathf.Max(0.1f, config.SpawnHeight),
            SpawnRadius = Mathf.Max(0f, config.SpawnRadius),
            FlightTime = Mathf.Max(0.1f, config.FlightTime),
            DamageFactor = Mathf.Max(0f, config.DamageFactor),
            PushFactor = Mathf.Max(0f, config.PushFactor),
            Radius = Mathf.Max(0.1f, config.Radius),
            IncludeDirectTarget = config.IncludeDirectTarget,
            IncludeDestructibles = config.IncludeDestructibles,
            TriggerOnCharactersOnly = config.TriggerOnCharactersOnly
        };
    }
}
