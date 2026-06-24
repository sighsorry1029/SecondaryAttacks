using UnityEngine;

namespace SecondaryAttacks;

internal sealed class NormalizedMeleeOnProjectileHitConfig
{
    public bool Enabled { get; set; } = true;

    public string Preset { get; set; } = "";

    public float Cooldown { get; set; } = 20f;

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public string CooldownFallback { get; set; } = ProjectilePresetCooldownFallback.OriginalSecondary;

    public float ResourceMultiplier { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public string ProjectileSpinAxis { get; set; } = "none";

    public Vector3 ProjectileVisualRotationOffset { get; set; } = Vector3.zero;

    public string Vfx { get; set; } = "";

    public int Count { get; set; } = 6;

    public float SpawnHeight { get; set; } = 10f;

    public float SpawnRadius { get; set; } = 10f;

    public float FlightTime { get; set; } = 1f;

    public float DamageFactor { get; set; } = 0.25f;

    public float PushFactor { get; set; } = 2f;

    public float Radius { get; set; } = 4f;

    public bool IncludeDirectTarget { get; set; }

    public bool IncludeDestructibles { get; set; }

    public bool TriggerOnCharactersOnly { get; set; } = true;

    public static NormalizedMeleeOnProjectileHitConfig CreateDefault()
    {
        return new NormalizedMeleeOnProjectileHitConfig
        {
            Preset = "spearRain"
        };
    }

    public NormalizedMeleeOnProjectileHitConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedBoomerangConfig
{
    public bool Enabled { get; set; } = true;

    public string Animation { get; set; } = "swing_axe2";

    public float Cooldown { get; set; } = 16f;

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public string CooldownFallback { get; set; } = ProjectilePresetCooldownFallback.OriginalSecondary;

    public float ResourceMultiplier { get; set; } = 1.5f;

    public float DurabilityFactor { get; set; } = 1f;

    public string ProjectileSpinAxis { get; set; } = "vertical";

    public Vector3 ProjectileVisualRotationOffset { get; set; } = Vector3.zero;

    public float MaxDistance { get; set; } = 12f;

    public float CurveFactor { get; set; } = 0.5f;

    public float DamageFactor { get; set; } = 1f;

    public float PushFactor { get; set; } = 1f;

    public float HitDamageDecay { get; set; } = 0.2f;

    public bool IncludeDestructibles { get; set; } = true;

    public static NormalizedBoomerangConfig CreateDefault()
    {
        return new NormalizedBoomerangConfig();
    }

    public NormalizedBoomerangConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedImpactBurstConfig
{
    public bool Enabled { get; set; } = true;

    public string Animation { get; set; } = "battleaxe_attack1";

    public float Cooldown { get; set; } = 16f;

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public string CooldownFallback { get; set; } = ProjectilePresetCooldownFallback.OriginalSecondary;

    public float ResourceMultiplier { get; set; } = 1.5f;

    public float DurabilityFactor { get; set; } = 1f;

    public string ProjectileSpinAxis { get; set; } = "vertical";

    public Vector3 ProjectileVisualRotationOffset { get; set; } = Vector3.zero;

    public string Vfx { get; set; } = "vfx_archerytarget_bullseye_double";

    public float Radius { get; set; } = 4f;

    public float DamageFactor { get; set; } = 0.75f;

    public float PushFactor { get; set; } = 4f;

    public static NormalizedImpactBurstConfig CreateDefault()
    {
        return new NormalizedImpactBurstConfig();
    }

    public NormalizedImpactBurstConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}

internal sealed class NormalizedProjectileSecondaryConfig
{
    public string Preset { get; set; } = "";

    public float Cooldown { get; set; } = 8f;

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float DamageFactor { get; set; } = 1f;

    public float ProjectileSpeedFactor { get; set; } = 1f;

    public float ProjectileScaleFactor { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public int Count { get; set; } = 1;

    public float SpreadAngle { get; set; } = 24f;

    public int AmmoConsumption { get; set; } = -1;

    public float VolleyRadius { get; set; } = 4f;

    public float VolleyArcAngleMin { get; set; } = 35f;

    public float VolleyArcAngleMax { get; set; } = 65f;

    public float VolleyMaxRange { get; set; } = 64f;

    public float Interval { get; set; } = 0f;

    public float HoldRepeatInterval { get; set; } = 0.2f;

    public float BarrageSpacing { get; set; } = 0.8f;

    public float MeteorRadius { get; set; } = 0f;

    public float PierceDamageDecay { get; set; } = 0.25f;

    public float SplitAngle { get; set; } = 30f;

    public int RicochetBounces { get; set; } = 3;

    public float RicochetDecay { get; set; } = 0.2f;

    public float RicochetRoughness { get; set; } = 0.2f;

    public float SpiralRadius { get; set; } = 0.35f;

    public float SpiralTurns { get; set; } = 1.5f;

    public float SentinelDetectionRange { get; set; } = 25f;

    public float SentinelHoverDistance { get; set; } = 1.75f;

    public float SentinelHoverHeight { get; set; } = 1.6f;

    public float SentinelHoverElevationAngle { get; set; }

    public float SentinelOrbitRadius { get; set; } = 0.35f;

    public float SentinelOrbitSpeed { get; set; } = 120f;

    public float SentinelLifetime { get; set; } = 12f;

    public float SentinelAttackDelay { get; set; }

    public float MeteorSpawnHeight { get; set; } = 12f;

    public int MaxCharges { get; set; } = 6;

    public string DetonateAnimation { get; set; } = "emote_blowkiss";

    public float AoeRadiusFactor { get; set; } = 1f;

    public static NormalizedProjectileSecondaryConfig CreateDefault()
    {
        return new NormalizedProjectileSecondaryConfig();
    }

    public NormalizedProjectileSecondaryConfig Clone()
    {
        return SecondaryAttackConfigClone.Shallow(this);
    }
}
