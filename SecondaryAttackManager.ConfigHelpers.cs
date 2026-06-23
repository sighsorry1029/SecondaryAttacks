using System.Collections.Generic;
using UnityEngine;

namespace SecondaryAttacks;

internal static partial class SecondaryAttackManager
{
    internal static float GetConfiguredSummonEmpowerMoveSpeedFactor(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Secondary?.SummonEmpower.MoveSpeedFactor ?? 1f;
    }

    internal static float GetConfiguredSummonEmpowerAttackSpeedFactor(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Secondary?.SummonEmpower.AttackSpeedFactor ?? 1f;
    }

    private static float GetNormalizedResourceMultiplier(NormalizedWeaponConfig weaponConfig)
    {
        if (weaponConfig.HarvestSweep?.Enabled == true)
        {
            return weaponConfig.HarvestSweep.ResourceMultiplier;
        }

        return weaponConfig.Secondary?.ResourceMultiplier ?? 1f;
    }

    private static float GetSelectedMeleeResourceMultiplier(NormalizedWeaponConfig weaponConfig)
    {
        float secondaryMultiplier = GetNormalizedResourceMultiplier(weaponConfig);
        return weaponConfig.MeleePreset switch
        {
            MeleeSpecialPreset.SneakAmbush => weaponConfig.SneakAmbush?.ResourceMultiplier ?? 1f,
            MeleeSpecialPreset.CleavingThrust => weaponConfig.CleavingThrust?.ResourceMultiplier ?? 1f,
            MeleeSpecialPreset.SpearRain => weaponConfig.SpearRain?.ResourceMultiplier ?? 1f,
            MeleeSpecialPreset.ImpactBurst => weaponConfig.ImpactBurst?.ResourceMultiplier ?? 1f,
            MeleeSpecialPreset.Boomerang => weaponConfig.Boomerang?.ResourceMultiplier ?? 1f,
            MeleeSpecialPreset.SpinningSweep => weaponConfig.SpinningSweep?.ResourceMultiplier ?? 1f,
            MeleeSpecialPreset.LaunchSlam => weaponConfig.LaunchSlam?.ResourceMultiplier ?? 1f,
            MeleeSpecialPreset.KnockbackChain => weaponConfig.KnockbackChain?.ResourceMultiplier ?? 1f,
            MeleeSpecialPreset.Aftershock => weaponConfig.Aftershock?.ResourceMultiplier ?? 1f,
            MeleeSpecialPreset.RiftTrail => weaponConfig.RiftTrail?.ResourceMultiplier ?? 1f,
            MeleeSpecialPreset.FractureLine => weaponConfig.FractureLine?.ResourceMultiplier ?? 1f,
            _ => secondaryMultiplier
        };
    }

    private static float GetNormalizedOutputMultiplier(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Secondary?.OutputMultiplier ?? 1f;
    }

    private static float GetNormalizedDurabilityFactor(NormalizedWeaponConfig weaponConfig)
    {
        return weaponConfig.Secondary?.DurabilityFactor ?? 1f;
    }

    private static float GetSelectedMeleeDurabilityFactor(NormalizedWeaponConfig weaponConfig)
    {
        float secondaryFactor = GetNormalizedDurabilityFactor(weaponConfig);
        return weaponConfig.MeleePreset switch
        {
            MeleeSpecialPreset.SneakAmbush => weaponConfig.SneakAmbush?.DurabilityFactor ?? secondaryFactor,
            MeleeSpecialPreset.CleavingThrust => weaponConfig.CleavingThrust?.DurabilityFactor ?? secondaryFactor,
            MeleeSpecialPreset.SpearRain => weaponConfig.SpearRain?.DurabilityFactor ?? secondaryFactor,
            MeleeSpecialPreset.ImpactBurst => weaponConfig.ImpactBurst?.DurabilityFactor ?? secondaryFactor,
            MeleeSpecialPreset.Boomerang => weaponConfig.Boomerang?.DurabilityFactor ?? secondaryFactor,
            MeleeSpecialPreset.SpinningSweep => weaponConfig.SpinningSweep?.DurabilityFactor ?? secondaryFactor,
            MeleeSpecialPreset.LaunchSlam => weaponConfig.LaunchSlam?.DurabilityFactor ?? secondaryFactor,
            MeleeSpecialPreset.KnockbackChain => weaponConfig.KnockbackChain?.DurabilityFactor ?? secondaryFactor,
            MeleeSpecialPreset.Aftershock => weaponConfig.Aftershock?.DurabilityFactor ?? secondaryFactor,
            MeleeSpecialPreset.RiftTrail => weaponConfig.RiftTrail?.DurabilityFactor ?? secondaryFactor,
            MeleeSpecialPreset.FractureLine => weaponConfig.FractureLine?.DurabilityFactor ?? secondaryFactor,
            _ => secondaryFactor
        };
    }

    private static string GetNormalizedAttackAnimation(NormalizedWeaponConfig weaponConfig)
    {
        string? secondaryAnimation = weaponConfig.Secondary?.Animation;
        return !string.IsNullOrWhiteSpace(secondaryAnimation)
            ? secondaryAnimation!.Trim()
            : "";
    }

    private static MeleePresetCooldownDefinition CreatePresetCooldown(
        float cooldown,
        float cooldownReductionFactor,
        string cooldownSkill = "weapon")
    {
        return new MeleePresetCooldownDefinition
        {
            Cooldown = Mathf.Max(0f, cooldown),
            CooldownSkill = string.IsNullOrWhiteSpace(cooldownSkill) ? "weapon" : cooldownSkill.Trim(),
            CooldownReductionFactor = Mathf.Clamp01(cooldownReductionFactor)
        };
    }

    private static SneakAmbushDefinition? CreateSneakAmbushDefinition(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedSneakAmbushConfig? sneakAmbush = weaponConfig.SneakAmbush;
        if (sneakAmbush == null || !sneakAmbush.Enabled)
        {
            return null;
        }

        return new SneakAmbushDefinition
        {
            PresetCooldown = CreatePresetCooldown(
                sneakAmbush.Cooldown,
                sneakAmbush.CooldownReductionFactor),
            DurabilityFactor = Mathf.Max(0f, sneakAmbush.DurabilityFactor),
            ChargeMaxSeconds = Mathf.Max(0f, sneakAmbush.ChargeMaxSeconds),
            ChargeSkillFactor = Mathf.Max(0f, sneakAmbush.ChargeSkillFactor),
            AggroResetRangePerChargeSecond = sneakAmbush.AggroResetRangePerChargeSecond,
            SenseBlockDurationPerChargeSecond = sneakAmbush.SenseBlockDurationPerChargeSecond,
            BackstabResetSecondsPerChargeSecond = sneakAmbush.BackstabResetSecondsPerChargeSecond
        };
    }

    private static CleavingThrustDefinition? CreateCleavingThrustDefinition(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedCleavingThrustConfig? cleavingThrust = weaponConfig.CleavingThrust;
        if (cleavingThrust == null || !cleavingThrust.Enabled)
        {
            return null;
        }

        return new CleavingThrustDefinition
        {
            PresetCooldown = CreatePresetCooldown(
                cleavingThrust.Cooldown,
                cleavingThrust.CooldownReductionFactor),
            DurabilityFactor = Mathf.Max(0f, cleavingThrust.DurabilityFactor),
            RangeFactor = Mathf.Max(0.1f, cleavingThrust.RangeFactor),
            Angle = Mathf.Clamp(cleavingThrust.Angle, 1f, 360f),
            DamageFactor = Mathf.Max(0f, cleavingThrust.DamageFactor),
            PushFactor = Mathf.Max(0f, cleavingThrust.PushFactor)
        };
    }

    private static LaunchSlamDefinition? CreateLaunchSlamDefinition(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedLaunchSlamConfig? launchSlam = weaponConfig.LaunchSlam;
        if (launchSlam == null || !launchSlam.Enabled)
        {
            return null;
        }

        return new LaunchSlamDefinition
        {
            PresetCooldown = CreatePresetCooldown(
                launchSlam.Cooldown,
                launchSlam.CooldownReductionFactor),
            DurabilityFactor = Mathf.Max(0f, launchSlam.DurabilityFactor),
            LaunchHeight = Mathf.Max(0f, launchSlam.LaunchHeight),
            DamageFactor = Mathf.Max(0f, launchSlam.DamageFactor),
            Vfx = launchSlam.Vfx.Trim(),
            VfxRotationOffset = launchSlam.VfxRotationOffset,
            Sfx = launchSlam.Sfx.Trim()
        };
    }

    private static KnockbackChainDefinition? CreateKnockbackChainDefinition(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedKnockbackChainConfig? knockbackChain = weaponConfig.KnockbackChain;
        if (knockbackChain == null || !knockbackChain.Enabled)
        {
            return null;
        }

        return new KnockbackChainDefinition
        {
            PresetCooldown = CreatePresetCooldown(
                knockbackChain.Cooldown,
                knockbackChain.CooldownReductionFactor),
            DurabilityFactor = Mathf.Max(0f, knockbackChain.DurabilityFactor),
            PushFactor = Mathf.Max(0f, knockbackChain.PushFactor),
            ChainDecay = Mathf.Clamp01(knockbackChain.ChainDecay)
        };
    }

    private static AftershockDefinition? CreateAftershockDefinition(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedAftershockConfig? aftershock = weaponConfig.Aftershock;
        if (aftershock == null || !aftershock.Enabled)
        {
            return null;
        }

        return new AftershockDefinition
        {
            PresetCooldown = CreatePresetCooldown(
                aftershock.Cooldown,
                aftershock.CooldownReductionFactor),
            Waves = Mathf.Max(0, aftershock.Waves),
            Interval = Mathf.Max(0f, aftershock.Interval),
            WaveDecay = Mathf.Clamp01(aftershock.WaveDecay),
            ForwardStep = aftershock.ForwardStep,
            DurabilityFactor = Mathf.Max(0f, aftershock.DurabilityFactor)
        };
    }

    private static RiftTrailDefinition? CreateRiftTrailDefinition(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedRiftTrailConfig? riftTrail = weaponConfig.RiftTrail;
        if (riftTrail == null || !riftTrail.Enabled)
        {
            return null;
        }

        return new RiftTrailDefinition
        {
            PresetCooldown = CreatePresetCooldown(
                riftTrail.Cooldown,
                riftTrail.CooldownReductionFactor),
            Duration = Mathf.Max(0.01f, riftTrail.Duration),
            TickInterval = Mathf.Max(0.01f, riftTrail.TickInterval),
            DamageFactor = Mathf.Max(0f, riftTrail.DamageFactor),
            PushFactor = Mathf.Max(0f, riftTrail.PushFactor),
            Range = Mathf.Max(0f, riftTrail.Range),
            Angle = riftTrail.Angle <= 0f ? 0f : Mathf.Clamp(riftTrail.Angle, 1f, 360f),
            Width = Mathf.Max(0f, riftTrail.Width),
            DurabilityFactor = Mathf.Max(0f, riftTrail.DurabilityFactor),
            VisualScaleFactor = Mathf.Max(0f, riftTrail.VisualScaleFactor),
            VisualForwardOffset = riftTrail.VisualForwardOffset,
            VisualTint = riftTrail.VisualTint,
            VisualAlphaFactor = Mathf.Max(0f, riftTrail.VisualAlphaFactor)
        };
    }

    private static FractureLineDefinition? CreateFractureLineDefinition(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedFractureLineConfig? fractureLine = weaponConfig.FractureLine;
        if (fractureLine == null || !fractureLine.Enabled)
        {
            return null;
        }

        return new FractureLineDefinition
        {
            PresetCooldown = CreatePresetCooldown(
                fractureLine.Cooldown,
                fractureLine.CooldownReductionFactor,
                "Pickaxes"),
            Range = Mathf.Max(0.1f, fractureLine.Range),
            HitSpacing = Mathf.Max(0.1f, fractureLine.HitSpacing),
            Duration = Mathf.Max(0.01f, fractureLine.Duration),
            TickInterval = Mathf.Max(0.01f, fractureLine.TickInterval),
            DamageFactor = Mathf.Max(0f, fractureLine.DamageFactor),
            DurabilityFactor = Mathf.Max(0f, fractureLine.DurabilityFactor)
        };
    }

    private static HarvestSweepDefinition? CreateHarvestSweepDefinition(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedHarvestSweepConfig? harvestSweep = weaponConfig.HarvestSweep;
        if (harvestSweep == null || !harvestSweep.Enabled)
        {
            return null;
        }

        float loopStart = Mathf.Clamp(harvestSweep.LoopStart, 0f, 0.93f);
        float loopEnd = Mathf.Clamp(harvestSweep.LoopEnd, loopStart + 0.05f, 0.98f);

        return new HarvestSweepDefinition
        {
            PresetCooldown = CreatePresetCooldown(
                harvestSweep.Cooldown,
                harvestSweep.CooldownReductionFactor,
                "Farming"),
            DurabilityFactor = Mathf.Max(0f, harvestSweep.DurabilityFactor),
            LoopStart = loopStart,
            LoopEnd = loopEnd,
            AnimationSpeed = Mathf.Clamp(harvestSweep.AnimationSpeed, 0.1f, 5f),
            MoveSpeedFactor = Mathf.Max(0f, harvestSweep.MoveSpeedFactor),
            SkillRaiseFactor = Mathf.Max(0f, harvestSweep.SkillRaiseFactor)
        };
    }

    private static BoomerangDefinition? CreateBoomerangDefinition(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedBoomerangConfig? boomerang = weaponConfig.Boomerang;
        if (boomerang?.Enabled != true)
        {
            return null;
        }

        return new BoomerangDefinition
        {
            PresetCooldown = CreatePresetCooldown(
                boomerang.Cooldown,
                boomerang.CooldownReductionFactor),
            CooldownFallback = ProjectilePresetCooldownFallback.OriginalSecondary,
            Side = "right",
            ProjectileSpinAxis = boomerang.ProjectileSpinAxis,
            ProjectileVisualRotationOffset = boomerang.ProjectileVisualRotationOffset,
            MaxDistance = Mathf.Max(0.5f, boomerang.MaxDistance),
            CurveFactor = Mathf.Max(0f, boomerang.CurveFactor),
            DespawnDistance = 0.8f,
            CatchRadius = 1.2f,
            CatchDelay = 0.25f,
            AutoEquipOnCatch = true,
            DamageFactor = Mathf.Max(0f, boomerang.DamageFactor),
            PushFactor = Mathf.Max(0f, boomerang.PushFactor),
            HitDamageDecay = Mathf.Clamp01(boomerang.HitDamageDecay),
            IncludeDestructibles = boomerang.IncludeDestructibles,
        };
    }

    private static SpinningSweepDefinition? CreateSpinningSweepDefinition(NormalizedWeaponConfig weaponConfig)
    {
        NormalizedSpinningSweepConfig? spinningSweep = weaponConfig.SpinningSweep;
        if (spinningSweep?.Enabled != true)
        {
            return null;
        }

        float loopStart = Mathf.Clamp(spinningSweep.LoopStart, 0f, 0.93f);
        float loopEnd = Mathf.Clamp(spinningSweep.LoopEnd, loopStart + 0.05f, 0.98f);

        return new SpinningSweepDefinition
        {
            PresetCooldown = CreatePresetCooldown(
                spinningSweep.Cooldown,
                spinningSweep.CooldownReductionFactor),
            DurabilityFactor = Mathf.Max(0f, spinningSweep.DurabilityFactor),
            LoopStart = loopStart,
            LoopEnd = loopEnd,
            AnimationSpeed = Mathf.Clamp(spinningSweep.AnimationSpeed, 0.1f, 5f),
            MoveSpeedFactor = Mathf.Max(0f, spinningSweep.MoveSpeedFactor),
            SkillRaiseFactor = Mathf.Max(0f, spinningSweep.SkillRaiseFactor)
        };
    }

    private static int ResolveAmmoConsumption(int configuredValue, bool usesAmmo, SecondaryAttackPreset preset, int projectileCount)
    {
        if (preset == SecondaryAttackPreset.OverchargedBomb)
        {
            return configuredValue < 0 ? 1 : Mathf.Max(1, configuredValue);
        }

        if (preset == SecondaryAttackPreset.StickyDetonator)
        {
            return 0;
        }

        if (!usesAmmo)
        {
            return 0;
        }

        if (preset == SecondaryAttackPreset.Burst && configuredValue < 0)
        {
            return Mathf.Max(1, projectileCount);
        }

        return configuredValue < 0 ? 1 : Mathf.Max(0, configuredValue);
    }

    internal static SecondaryAttackDefinition CreateEffectOnlyDefinition(
        string prefabName,
        NormalizedWeaponConfig weaponConfig,
        List<ConfiguredWeaponEffectDefinition> configuredEffects)
    {
        return new SecondaryAttackDefinition
        {
            PrefabName = prefabName,
            AppliesSecondaryOverride = false,
            Behavior = new EffectOnlySecondaryBehavior(),
            AttackAnimation = "",
            HasCustomAttackAnimation = false,
            ResourceMultiplier = Mathf.Max(0f, GetNormalizedResourceMultiplier(weaponConfig)),
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
            Boomerang = CreateBoomerangDefinition(weaponConfig),
            SpinningSweep = CreateSpinningSweepDefinition(weaponConfig),
            ConfiguredEffects = configuredEffects
        };
    }

}
