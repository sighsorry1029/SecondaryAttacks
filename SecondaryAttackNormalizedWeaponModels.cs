using System.Collections.Generic;

namespace SecondaryAttacks;

internal sealed class NormalizedWeaponConfig
{
    public bool Enabled { get; set; } = true;

    public bool UseAutomaticFallback { get; set; }

    public NormalizedSecondaryModeConfig? Secondary { get; set; }

    public NormalizedSneakAmbushConfig? SneakAmbush { get; set; }

    public NormalizedCleavingThrustConfig? CleavingThrust { get; set; }

    public NormalizedLaunchSlamConfig? LaunchSlam { get; set; }

    public NormalizedKnockbackChainConfig? KnockbackChain { get; set; }

    public NormalizedAftershockConfig? Aftershock { get; set; }

    public NormalizedRiftTrailConfig? RiftTrail { get; set; }

    public NormalizedFractureLineConfig? FractureLine { get; set; }

    public NormalizedHarvestSweepConfig? HarvestSweep { get; set; }

    public NormalizedMeleeOnProjectileHitConfig? SpearRain { get; set; }

    public NormalizedImpactBurstConfig? ImpactBurst { get; set; }

    public NormalizedBoomerangConfig? Boomerang { get; set; }

    public NormalizedSpinningSweepConfig? SpinningSweep { get; set; }

    public MeleeSpecialPreset MeleePreset { get; set; } = MeleeSpecialPreset.None;

    public bool HasExplicitMeleePreset { get; set; }
}

internal sealed class NormalizedProjectileBehaviorConfig
{
    public NormalizedProjectileSecondaryConfig? Ranged { get; set; }

    public NormalizedMeleeOnProjectileHitConfig? OnProjectileHit { get; set; }

    public NormalizedBoomerangConfig? Boomerang { get; set; }
}

internal sealed class NormalizedSecondaryModeConfig
{
    public string Type { get; set; } = "";

    public string Animation { get; set; } = "";

    public float ResourceMultiplier { get; set; } = 1f;

    public float OutputMultiplier { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public NormalizedProjectileSecondaryConfig Projectile { get; set; } = new();

    public string CopyFrom { get; set; } = "";

    public NormalizedMeleeOnProjectileHitConfig? OnProjectileHit { get; set; }

    public NormalizedSummonEmpowerSecondaryConfig SummonEmpower { get; set; } = new();

    public NormalizedShieldConvertSecondaryConfig ShieldConvert { get; set; } = new();
}
