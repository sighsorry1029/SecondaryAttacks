using System;
using System.Collections.Generic;

namespace SecondaryAttacks;

internal sealed class RangedWeaponConfig
{
    public bool? Enabled { get; set; }

    public RangedWeaponConfig? Barrage { get; set; }

    public RangedWeaponConfig? Volley { get; set; }

    public RangedWeaponConfig? Piercing { get; set; }

    public RangedWeaponConfig? Scatter { get; set; }

    public RangedWeaponConfig? Spiral { get; set; }

    public RangedWeaponConfig? Sentinel { get; set; }

    public RangedWeaponConfig? Meteor { get; set; }

    public RangedWeaponConfig? Burst { get; set; }

    public RangedWeaponConfig? StickyDetonator { get; set; }

    public RangedWeaponConfig? OverchargedBomb { get; set; }

    public string? Animation { get; set; }

    public string? DetonateAnimation { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public float? DamageFactor { get; set; }

    public float? ProjectileSpeedFactor { get; set; }

    public float? ProjectileScaleFactor { get; set; }

    public string? Preset { get; set; }

    public int? Count { get; set; }

    public float? SpreadAngle { get; set; }

    public int? AmmoConsumption { get; set; }

    public float? VolleyRadius { get; set; }

    public float? VolleyArcAngleMin { get; set; }

    public float? VolleyArcAngleMax { get; set; }

    public float? VolleyMaxRange { get; set; }

    public float? Interval { get; set; }

    public float? HoldRepeatInterval { get; set; }

    public float? BarrageSpacing { get; set; }

    public float? MeteorRadius { get; set; }

    public float? PierceDamageDecay { get; set; }

    public float? SplitAngle { get; set; }

    public int? RicochetBounces { get; set; }

    public float? RicochetDecay { get; set; }

    public float? RicochetRoughness { get; set; }

    public float? SpiralRadius { get; set; }

    public float? SpiralTurns { get; set; }

    public float? DetectionRange { get; set; }

    public float? HoverDistance { get; set; }

    public float? HoverHeight { get; set; }

    public float? HoverElevationAngle { get; set; }

    public float? OrbitRadius { get; set; }

    public float? OrbitSpeed { get; set; }

    public float? Lifetime { get; set; }

    public float? AttackDelay { get; set; }

    public float? MeteorSpawnHeight { get; set; }

    public int? MaxCharges { get; set; }

    public float? AoeRadiusFactor { get; set; }
}

internal sealed class MeleeWeaponConfig
{
    public bool? Enabled { get; set; }

    public string Preset { get; set; } = "";

    public string CopyFrom { get; set; } = "";

    public string Animation { get; set; } = "";

    public float ResourceMultiplier { get; set; } = 1f;

    public float OutputMultiplier { get; set; } = 1f;

    public float? DurabilityFactor { get; set; }

    public SneakAmbushConfig? SneakAmbush { get; set; }

    public CleavingThrustConfig? CleavingThrust { get; set; }

    public MeleeOnProjectileHitConfig? SpearRain { get; set; }

    public ImpactBurstConfig? ImpactBurst { get; set; }

    public BoomerangConfig? Boomerang { get; set; }

    public SpinningSweepConfig? SpinningSweep { get; set; }

    public LaunchSlamConfig? LaunchSlam { get; set; }

    public KnockbackChainConfig? KnockbackChain { get; set; }

    public AftershockConfig? Aftershock { get; set; }

    public RiftTrailConfig? RiftTrail { get; set; }

    public FractureLineConfig? FractureLine { get; set; }

    public HarvestSweepConfig? HarvestSweep { get; set; }
}

internal sealed class MeleeOnProjectileHitConfig
{
    public bool? Enabled { get; set; }

    public string Preset { get; set; } = "";

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public string? CooldownFallback { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public string? ProjectileSpinAxis { get; set; }

    public string? ProjectileVisualRotationOffset { get; set; }

    public string? Vfx { get; set; }

    public int? Count { get; set; }

    public float? SpawnHeight { get; set; }

    public float? SpawnRadius { get; set; }

    public float? FlightTime { get; set; }

    public float? DamageFactor { get; set; }

    public float? PushFactor { get; set; }

    public float? Radius { get; set; }

    public bool? IncludeDirectTarget { get; set; }

    public bool? IncludeDestructibles { get; set; }

    public bool? TriggerOnCharactersOnly { get; set; }
}

internal sealed class ImpactBurstConfig
{
    public bool? Enabled { get; set; }

    public string Animation { get; set; } = "";

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public string? CooldownFallback { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public string? ProjectileSpinAxis { get; set; }

    public string? ProjectileVisualRotationOffset { get; set; }

    public float? Radius { get; set; }

    public float? DamageFactor { get; set; }

    public float? PushFactor { get; set; }

}

internal sealed class BoomerangConfig
{
    public bool? Enabled { get; set; }

    public string Animation { get; set; } = "";

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public string? CooldownFallback { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public string? ProjectileSpinAxis { get; set; }

    public string? ProjectileVisualRotationOffset { get; set; }

    public float? MaxDistance { get; set; }

    public float? CurveFactor { get; set; }

    public float? DamageFactor { get; set; }

    public float? PushFactor { get; set; }

    public float? HitDamageDecay { get; set; }

}

internal sealed class SpinningSweepConfig
{
    public bool? Enabled { get; set; }

    public string Animation { get; set; } = "";

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public float? LoopStart { get; set; }

    public float? LoopEnd { get; set; }

    public float? AnimationSpeed { get; set; }

    public float? MoveSpeedFactor { get; set; }

    public float? SkillRaiseFactor { get; set; }

}

internal sealed class SneakAmbushConfig
{
    public bool? Enabled { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public float? ChargeMaxSeconds { get; set; }

    public float? ChargeSkillFactor { get; set; }

    public float? AggroResetRangePerChargeSecond { get; set; }

    public float? SenseBlockDurationPerChargeSecond { get; set; }

    public float? BackstabResetSecondsPerChargeSecond { get; set; }
}

internal sealed class CleavingThrustConfig
{
    public bool? Enabled { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public float? RangeFactor { get; set; }

    public float? TrailScaleFactor { get; set; }

    public float? Angle { get; set; }

    public float? DamageFactor { get; set; }

    public float? PushFactor { get; set; }

    public bool? HitThroughWalls { get; set; }

}

internal sealed class LaunchSlamConfig
{
    public bool? Enabled { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public float? LaunchHeight { get; set; }

    public float? DamageFactor { get; set; }

}

internal sealed class KnockbackChainConfig
{
    public bool? Enabled { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public float? PushFactor { get; set; }

    public float? ChainDecay { get; set; }
}

internal sealed class AftershockConfig
{
    public bool? Enabled { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? ResourceMultiplier { get; set; }

    public int? Waves { get; set; }

    public float? Interval { get; set; }

    public float? WaveDecay { get; set; }

    public float? ForwardStep { get; set; }

    public float? DurabilityFactor { get; set; }

}

internal sealed class RiftTrailConfig
{
    public bool? Enabled { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? Duration { get; set; }

    public float? TickInterval { get; set; }

    public float? DamageFactor { get; set; }

    public float? PushFactor { get; set; }

    public float? Range { get; set; }

    public float? Angle { get; set; }

    public float? Width { get; set; }

    public bool? HitThroughWalls { get; set; }

    public bool? IncludeDestructibles { get; set; }

    public float? DurabilityFactor { get; set; }

}

internal sealed class FractureLineConfig
{
    public bool? Enabled { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? Range { get; set; }

    public float? HitSpacing { get; set; }

    public float? Duration { get; set; }

    public float? TickInterval { get; set; }

    public float? DamageFactor { get; set; }

    public float? DurabilityFactor { get; set; }
}

internal sealed class HarvestSweepConfig
{
    public bool? Enabled { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public string? Animation { get; set; }

    public float? LoopStart { get; set; }

    public float? LoopEnd { get; set; }

    public float? AnimationSpeed { get; set; }

    public float? MoveSpeedFactor { get; set; }

    public float? SkillRaiseFactor { get; set; }
}

internal sealed class BloodMagicWeaponConfig
{
    public bool? Enabled { get; set; }

    public string? Preset { get; set; }

    public BloodMagicWeaponConfig? SummonEmpower { get; set; }

    public BloodMagicWeaponConfig? ShieldConvert { get; set; }

    public string? Animation { get; set; }

    public float? ResourceMultiplier { get; set; }

    public float? DurabilityFactor { get; set; }

    public float? Cooldown { get; set; }

    public float? CooldownReductionFactor { get; set; }

    public float? Radius { get; set; }

    public float? Duration { get; set; }

    public float? MoveSpeedFactor { get; set; }

    public float? AttackSpeedFactor { get; set; }

    public float? HealFactor { get; set; }

    public MagicSummonOverrideConfig? Summon { get; set; }
}

internal sealed class MagicSummonOverrideConfig
{
    public string QualityPreset { get; set; } = "";

    public int? MaxQuality { get; set; }

    public List<MagicSummonCloneConfig>? SpawnChoices { get; set; }
}

internal sealed class MagicSummonCloneConfig
{
    public string SourcePrefab { get; set; } = "";

    public string ClonePrefab { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public float? Health { get; set; }

    public int? Weight { get; set; }
}

internal sealed class EffectBehaviorConfig
{
    public string Type { get; set; } = "";

    public int? Value { get; set; }

    public string Prefab { get; set; } = "";

    public string Trigger { get; set; } = "anyHit";

    public int? StacksRequired { get; set; }

    public float StackWindow { get; set; } = 0f;

    public float? Duration { get; set; }

    public float? TickInterval { get; set; }

    public float? DamageFactor { get; set; }

    public float? LightningDamage { get; set; }

    public float? Radius { get; set; }

    public float? Ttl { get; set; }

    public float? HitInterval { get; set; }

    public float ProcChance { get; set; } = 100f;

    public string DamageType { get; set; } = "";

    public string Modifier { get; set; } = "normal";

    public ScalarValueConfig Damage { get; set; } = new();

    public ScalarValueConfig Heal { get; set; } = new();

    public ScalarValueConfig StaminaRestore { get; set; } = new();

    public float MoveSpeedMultiplier { get; set; } = 1f;

    public float HealthThresholdPercent { get; set; } = 25f;

    public float DamageMultiplier { get; set; } = 1f;

    public bool ConsumeOnModify { get; set; } = false;

    public Dictionary<string, EffectBehaviorOverrideConfig>? Prefabs { get; set; }
}

internal sealed class EffectBehaviorOverrideConfig
{
    public string? Type { get; set; }

    public int? Value { get; set; }

    public string? Prefab { get; set; }

    public string? Trigger { get; set; }

    public int? StacksRequired { get; set; }

    public float? StackWindow { get; set; }

    public float? Duration { get; set; }

    public float? TickInterval { get; set; }

    public float? DamageFactor { get; set; }

    public float? LightningDamage { get; set; }

    public float? Radius { get; set; }

    public float? Ttl { get; set; }

    public float? HitInterval { get; set; }

    public float? ProcChance { get; set; }

    public string? DamageType { get; set; }

    public string? Modifier { get; set; }

    public ScalarValueOverrideConfig? Damage { get; set; }

    public ScalarValueOverrideConfig? Heal { get; set; }

    public ScalarValueOverrideConfig? StaminaRestore { get; set; }

    public float? MoveSpeedMultiplier { get; set; }

    public float? HealthThresholdPercent { get; set; }

    public float? DamageMultiplier { get; set; }

    public bool? ConsumeOnModify { get; set; }
}

internal sealed class ScalarValueConfig
{
    public string Mode { get; set; } = "fixed";

    public float Value { get; set; }
}

internal sealed class ScalarValueOverrideConfig
{
    public string? Mode { get; set; }

    public float? Value { get; set; }
}
