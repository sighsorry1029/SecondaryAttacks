using System;
using System.Collections.Generic;

namespace SecondaryAttacks;

internal enum SecondaryAttackPreset
{
    Barrage,
    Volley,
    Piercing,
    Scatter,
    Spiral,
    Sentinel,
    Meteor,
    Burst,
    StickyDetonator,
    OverchargedBomb
}

internal enum MeleeSpecialPreset
{
    None,
    SneakAmbush,
    CleavingThrust,
    SpearRain,
    ImpactBurst,
    Boomerang,
    SpinningSweep,
    LaunchSlam,
    KnockbackChain,
    Aftershock,
    RiftTrail,
    FractureLine
}

internal enum WeaponEffectType
{
    Dot,
    ResistanceShred,
    Adrenaline,
    Haste,
    Vampirism,
    Execute,
    StaggerChance,
    BurstDamage,
    Bleeding,
    Bash,
    Piercing,
    Executioner,
    Decapitator,
    Smasher,
    Juggernaut
}

internal enum WeaponEffectTrigger
{
    AnyHit,
    MainHit,
    SecondaryHit
}

internal enum ScalarValueMode
{
    Fixed,
    TargetMaxHealthPercent,
    SelfMaxHealthPercent,
    SelfMaxStaminaPercent
}

internal enum SecondaryAttackBehaviorType
{
    EffectOnly,
    Projectile,
    SummonEmpower,
    ShieldConvert,
    CopiedSecondary,
    Aftershock,
    FractureLine
}

internal abstract class SecondaryAttackBehavior
{
    public abstract SecondaryAttackBehaviorType BehaviorType { get; }
}

internal sealed class AftershockSecondaryBehavior : SecondaryAttackBehavior
{
    public override SecondaryAttackBehaviorType BehaviorType => SecondaryAttackBehaviorType.Aftershock;

    public string SourcePrefabName { get; set; } = "";
}

internal sealed class FractureLineSecondaryBehavior : SecondaryAttackBehavior
{
    public override SecondaryAttackBehaviorType BehaviorType => SecondaryAttackBehaviorType.FractureLine;

    public string SourcePrefabName { get; set; } = "";
}

internal sealed class EffectOnlySecondaryBehavior : SecondaryAttackBehavior
{
    public override SecondaryAttackBehaviorType BehaviorType => SecondaryAttackBehaviorType.EffectOnly;
}

internal sealed class ProjectileSecondaryBehavior : SecondaryAttackBehavior
{
    public override SecondaryAttackBehaviorType BehaviorType => SecondaryAttackBehaviorType.Projectile;

    public SecondaryAttackPreset Preset { get; set; }

    public float Cooldown { get; set; } = 8f;

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float DamageFactor { get; set; } = 1f;

    public float SkillRaiseFactor { get; set; } = 1f;

    public float AdrenalineFactor { get; set; } = 1f;

    public float ProjectileSpeedFactor { get; set; } = 1f;

    public float ProjectileScaleFactor { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public int ProjectileCount { get; set; }

    public float SpreadAngle { get; set; }

    public int AmmoConsumption { get; set; }

    public float VolleyRadius { get; set; }

    public float VolleyArcAngleMin { get; set; }

    public float VolleyArcAngleMax { get; set; }

    public float VolleyMaxRange { get; set; }

    public float Interval { get; set; }

    public float HoldRepeatInterval { get; set; }

    public float BarrageSpacing { get; set; }

    public float MeteorRadius { get; set; }

    public float PierceDamageDecay { get; set; } = 0.25f;

    public float SplitAngle { get; set; }

    public int RicochetBounces { get; set; }

    public float RicochetDecay { get; set; }

    public float RicochetRoughness { get; set; }

    public float SpiralRadius { get; set; }

    public float SpiralTurns { get; set; }

    public float SentinelDetectionRange { get; set; }

    public float SentinelHoverDistance { get; set; }

    public float SentinelHoverHeight { get; set; }

    public float SentinelHoverElevationAngle { get; set; }

    public float SentinelOrbitRadius { get; set; }

    public float SentinelOrbitSpeed { get; set; }

    public float SentinelLifetime { get; set; }

    public float SentinelAttackDelay { get; set; }

    public float MeteorSpawnHeight { get; set; }

    public int MaxCharges { get; set; } = 6;

    public string DetonateAnimation { get; set; } = "";

    public float AoeRadiusFactor { get; set; } = 1f;
}

internal sealed class SummonEmpowerSecondaryBehavior : SecondaryAttackBehavior
{
    public override SecondaryAttackBehaviorType BehaviorType => SecondaryAttackBehaviorType.SummonEmpower;

    public List<string> SummonSourcePrefabs { get; set; } = new();

    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new()
    {
        CooldownSkill = "bloodMagic"
    };

    public float Radius { get; set; }

    public float Duration { get; set; }

    public float MoveSpeedFactor { get; set; } = 1f;

    public float AttackSpeedFactor { get; set; } = 1f;
}

internal sealed class ShieldConvertSecondaryBehavior : SecondaryAttackBehavior
{
    public override SecondaryAttackBehaviorType BehaviorType => SecondaryAttackBehaviorType.ShieldConvert;

    public int ShieldStatusEffectHash { get; set; }

    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new()
    {
        CooldownSkill = "bloodMagic"
    };

    public float Radius { get; set; }

    public float HealFactor { get; set; }
}

internal sealed class CopiedSecondaryBehavior : SecondaryAttackBehavior
{
    public override SecondaryAttackBehaviorType BehaviorType => SecondaryAttackBehaviorType.CopiedSecondary;

    public string SourcePrefabName { get; set; } = "";
}

