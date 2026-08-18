using UnityEngine;

namespace SecondaryAttacks;

internal sealed class SecondaryAttackDefinition
{
    public string PrefabName { get; set; } = "";

    public bool AppliesSecondaryOverride { get; set; }

    public SecondaryAttackBehavior Behavior { get; set; } = new EffectOnlySecondaryBehavior();

    public SecondaryAttackBehaviorType BehaviorType => Behavior.BehaviorType;

    public string AttackAnimation { get; set; } = "";

    public bool HasCustomAttackAnimation { get; set; }

    public Attack? CooldownFallbackSecondaryAttack { get; set; }

    public Attack? ConfiguredSecondaryAttack { get; set; }

    public float ResourceMultiplier { get; set; } = 1f;

    public float OutputMultiplier { get; set; } = 1f;

    public float DurabilityFactor { get; set; } = 1f;

    public float RawAttackHealth { get; set; }

    public float RawAttackHealthPercentage { get; set; }

    public float RawAttackStamina { get; set; }

    public float RawAttackEitr { get; set; }

    public float RawDrawStamina { get; set; }

    public float RawDrawEitr { get; set; }

    public float RawReloadStamina { get; set; }

    public float RawReloadEitr { get; set; }

    public SneakAmbushDefinition? SneakAmbush { get; set; }

    public CleavingThrustDefinition? CleavingThrust { get; set; }

    public LaunchSlamDefinition? LaunchSlam { get; set; }

    public KnockbackChainDefinition? KnockbackChain { get; set; }

    public AftershockDefinition? Aftershock { get; set; }

    public RiftTrailDefinition? RiftTrail { get; set; }

    public FractureLineDefinition? FractureLine { get; set; }

    public HarvestSweepDefinition? HarvestSweep { get; set; }

    public MeleeOnProjectileHitDefinition? OnProjectileHit { get; set; }

    public BoomerangDefinition? Boomerang { get; set; }

    public SpinningSweepDefinition? SpinningSweep { get; set; }
}

internal sealed class MeleeOnProjectileHitDefinition
{
    public MeleeSpecialPreset Preset { get; set; }

    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; }

    public string ProjectileSpinAxis { get; set; } = "none";

    public Vector3 ProjectileVisualRotationOffset { get; set; } = Vector3.zero;

    public string Vfx { get; set; } = "";

    public int Count { get; set; }

    public float SpawnHeight { get; set; }

    public float SpawnRadius { get; set; }

    public float FlightTime { get; set; }

    public float DamageFactor { get; set; }

    public float PushFactor { get; set; }

    public float Radius { get; set; }

    public bool IncludeDirectTarget { get; set; }

    public bool IncludeDestructibles { get; set; }

    public bool TriggerOnCharactersOnly { get; set; }
}

internal sealed class BoomerangDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public string Side { get; set; } = "right";

    public string ProjectileSpinAxis { get; set; } = "horizontal";

    public Vector3 ProjectileVisualRotationOffset { get; set; } = Vector3.zero;

    public float MaxDistance { get; set; }

    public float CurveFactor { get; set; }

    public float DespawnDistance { get; set; }

    public float CatchRadius { get; set; }

    public float CatchDelay { get; set; }

    public bool AutoEquipOnCatch { get; set; }

    public float DamageFactor { get; set; }

    public float PushFactor { get; set; }

    public float HitDamageDecay { get; set; }

    public bool IncludeDestructibles { get; set; }

}

internal sealed class SneakAmbushDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float ChargeMaxSeconds { get; set; }

    public float ChargeSkillFactor { get; set; }

    public float AggroResetRangePerChargeSecond { get; set; }

    public float SenseBlockDurationPerChargeSecond { get; set; }

    public float BackstabResetSecondsPerChargeSecond { get; set; }

    public float DurabilityFactor { get; set; }
}

internal sealed class SpinningSweepDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; }

    public float DamageFactor { get; set; }

    public float PushFactor { get; set; }

    public float LoopStart { get; set; }

    public float LoopEnd { get; set; }

    public float AnimationSpeed { get; set; }

    public float MoveSpeedFactor { get; set; }

    public float SkillRaiseFactor { get; set; }

}

internal sealed class CleavingThrustDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; }

    public float RangeFactor { get; set; }

    public float Angle { get; set; }

    public float DamageFactor { get; set; }

    public float PushFactor { get; set; }

}

internal sealed class LaunchSlamDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; }

    public float LaunchHeight { get; set; }

    public float DamageFactor { get; set; }
}

internal sealed class KnockbackChainDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; }

    public float PushFactor { get; set; }

    public float ChainDecay { get; set; }
}

internal sealed class AftershockDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public int Waves { get; set; }

    public float Interval { get; set; }

    public float WaveDecay { get; set; }

    public float ForwardStep { get; set; }

    public float DurabilityFactor { get; set; }

}

internal sealed class RiftTrailDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float Duration { get; set; }

    public float TickInterval { get; set; }

    public float DamageFactor { get; set; }

    public float PushFactor { get; set; }

    public float Range { get; set; }

    public float Angle { get; set; }

    public float Width { get; set; }

    public float DurabilityFactor { get; set; }

    public float VisualScaleFactor { get; set; }

    public float VisualForwardOffset { get; set; }

    public string VisualTint { get; set; } = "#ffffff";

    public float VisualAlphaFactor { get; set; }
}

internal sealed class FractureLineDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float Range { get; set; }

    public float HitSpacing { get; set; }

    public float Duration { get; set; }

    public float TickInterval { get; set; }

    public float DamageFactor { get; set; }

    public float DurabilityFactor { get; set; }
}

internal sealed class MeleePresetCooldownDefinition
{
    public float Cooldown { get; set; }

    public string CooldownGroup { get; set; } = "";

    public string CooldownSkill { get; set; } = "weapon";

    public float CooldownReductionFactor { get; set; } = 0.5f;
}

internal static class ProjectilePresetCooldownPolicy
{
    internal static bool UsesDynamicOriginalSecondary(SecondaryAttackDefinition? definition)
    {
        if (definition?.AppliesSecondaryOverride != true ||
            definition.Behavior is not CopiedSecondaryBehavior)
        {
            return false;
        }

        return definition.Boomerang != null || definition.OnProjectileHit != null;
    }
}

internal sealed class HarvestSweepDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; }

    public float LoopStart { get; set; }

    public float LoopEnd { get; set; }

    public float AnimationSpeed { get; set; }

    public float MoveSpeedFactor { get; set; }

    public float SkillRaiseFactor { get; set; }
}
