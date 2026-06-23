using UnityEngine;

namespace SecondaryAttacks;

internal sealed class NormalizedSneakAmbushConfig
{
    public bool Enabled { get; set; } = true;

    public float Cooldown { get; set; } = 30f;

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float ResourceMultiplier { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public float ChargeMaxSeconds { get; set; } = 8f;

    public float ChargeSkillFactor { get; set; } = 2f;

    public float AggroResetRangePerChargeSecond { get; set; } = 1f;

    public float SenseBlockDurationPerChargeSecond { get; set; } = 0.25f;

    public float BackstabResetSecondsPerChargeSecond { get; set; } = 35f;

    public static NormalizedSneakAmbushConfig CreateDefault()
    {
        return new NormalizedSneakAmbushConfig();
    }

    public NormalizedSneakAmbushConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedCleavingThrustConfig
{
    public bool Enabled { get; set; } = true;

    public float Cooldown { get; set; }

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float ResourceMultiplier { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public float RangeFactor { get; set; } = 2.5f;

    public float Angle { get; set; } = 90f;

    public float DamageFactor { get; set; } = 1f;

    public float PushFactor { get; set; } = 1f;

    public static NormalizedCleavingThrustConfig CreateDefault()
    {
        return new NormalizedCleavingThrustConfig();
    }

    public NormalizedCleavingThrustConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedLaunchSlamConfig
{
    public bool Enabled { get; set; } = true;

    public float Cooldown { get; set; }

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float ResourceMultiplier { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public float LaunchHeight { get; set; } = 4f;

    public float DamageFactor { get; set; } = 1f;

    public string Vfx { get; set; } = "vfx_archerytarget_bullseye";

    public Vector3 VfxRotationOffset { get; set; } = new(90f, 0f, 0f);

    public string Sfx { get; set; } = "sfx_sledge_hit";

    public static NormalizedLaunchSlamConfig CreateDefault()
    {
        return new NormalizedLaunchSlamConfig();
    }

    public NormalizedLaunchSlamConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedKnockbackChainConfig
{
    public bool Enabled { get; set; } = true;

    public float Cooldown { get; set; }

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float ResourceMultiplier { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public float PushFactor { get; set; } = 8f;

    public float ChainDecay { get; set; } = 0.75f;

    public static NormalizedKnockbackChainConfig CreateDefault()
    {
        return new NormalizedKnockbackChainConfig();
    }

    public NormalizedKnockbackChainConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedAftershockConfig
{
    public bool Enabled { get; set; } = true;

    public float Cooldown { get; set; }

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float ResourceMultiplier { get; set; } = 1f;

    public int Waves { get; set; } = 3;

    public float Interval { get; set; } = 0.5f;

    public float WaveDecay { get; set; } = 0.2f;

    public float ForwardStep { get; set; } = 3f;

    public float DurabilityFactor { get; set; } = 1f;

    public static NormalizedAftershockConfig CreateDefault()
    {
        return new NormalizedAftershockConfig();
    }

    public NormalizedAftershockConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedRiftTrailConfig
{
    public bool Enabled { get; set; } = true;

    public float Cooldown { get; set; }

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float ResourceMultiplier { get; set; } = 1f;

    public float Duration { get; set; } = 2f;

    public float TickInterval { get; set; } = 0.5f;

    public float DamageFactor { get; set; } = 0.25f;

    public float PushFactor { get; set; }

    public float Range { get; set; }

    public float Angle { get; set; }

    public float Width { get; set; }

    public float DurabilityFactor { get; set; } = 1f;

    public float VisualScaleFactor { get; set; } = 1.5f;

    public float VisualForwardOffset { get; set; } = 1.5f;

    public string VisualTint { get; set; } = "#ffffff";

    public float VisualAlphaFactor { get; set; } = 1f;

    public static NormalizedRiftTrailConfig CreateDefault()
    {
        return new NormalizedRiftTrailConfig();
    }

    public NormalizedRiftTrailConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedFractureLineConfig
{
    public bool Enabled { get; set; } = true;

    public float Cooldown { get; set; } = 6f;

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float ResourceMultiplier { get; set; } = 1f;

    public float Range { get; set; } = 5f;

    public float HitSpacing { get; set; } = 0.75f;

    public float Duration { get; set; } = 1.5f;

    public float TickInterval { get; set; } = 0.3f;

    public float DamageFactor { get; set; } = 0.35f;

    public float DurabilityFactor { get; set; } = 1f;

    public static NormalizedFractureLineConfig CreateDefault()
    {
        return new NormalizedFractureLineConfig();
    }

    public NormalizedFractureLineConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedSpinningSweepConfig
{
    public bool Enabled { get; set; } = true;

    public string Animation { get; set; } = "atgeir_secondary";

    public float Cooldown { get; set; } = 8f;

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float ResourceMultiplier { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public float LoopStart { get; set; } = 0.4f;

    public float LoopEnd { get; set; } = 0.6f;

    public float AnimationSpeed { get; set; } = 1f;

    public float MoveSpeedFactor { get; set; } = 0.75f;

    public float SkillRaiseFactor { get; set; } = 0.25f;

    public static NormalizedSpinningSweepConfig CreateDefault()
    {
        return new NormalizedSpinningSweepConfig();
    }

    public NormalizedSpinningSweepConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedHarvestSweepConfig
{
    public bool Enabled { get; set; } = true;

    public float Cooldown { get; set; } = 16f;


    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float ResourceMultiplier { get; set; } = 1.5f;

    public float DurabilityFactor { get; set; } = 1.5f;

    public string Animation { get; set; } = "atgeir_secondary";

    public float LoopStart { get; set; } = 0.4f;

    public float LoopEnd { get; set; } = 0.6f;

    public float AnimationSpeed { get; set; } = 1f;

    public float MoveSpeedFactor { get; set; } = 0.75f;

    public float SkillRaiseFactor { get; set; } = 0.25f;

    public static NormalizedHarvestSweepConfig CreateDefault()
    {
        return new NormalizedHarvestSweepConfig();
    }

    public NormalizedHarvestSweepConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}
