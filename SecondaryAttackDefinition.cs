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
    public string Preset { get; set; } = "";

    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; } = 1f;

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

    public bool TriggerOnCharactersOnly { get; set; } = true;
}

internal sealed class BoomerangDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new()
    {
        Cooldown = 6f,
        CooldownSkill = "weapon",
        CooldownReductionFactor = 0.5f
    };

    public string Side { get; set; } = "right";

    public string ProjectileSpinAxis { get; set; } = "horizontal";

    public Vector3 ProjectileVisualRotationOffset { get; set; } = Vector3.zero;

    public float MaxDistance { get; set; } = 20f;

    public float CurveFactor { get; set; } = 0.5f;

    public float DespawnDistance { get; set; } = 0.8f;

    public float CatchRadius { get; set; } = 1.2f;

    public float CatchDelay { get; set; } = 0.25f;

    public bool AutoEquipOnCatch { get; set; } = true;

    public float DamageFactor { get; set; } = 1f;

    public float PushFactor { get; set; } = 1f;

    public float HitDamageDecay { get; set; } = 0.2f;

    public bool IncludeDestructibles { get; set; }

}

internal sealed class SneakAmbushDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new()
    {
        Cooldown = 30f,
        CooldownSkill = "weapon",
        CooldownReductionFactor = 0.5f
    };

    public float ChargeMaxSeconds { get; set; } = 8f;

    public float ChargeSkillFactor { get; set; } = 2f;

    public float AggroResetRangePerChargeSecond { get; set; } = 1f;

    public float SenseBlockDurationPerChargeSecond { get; set; } = 0.25f;

    public float BackstabResetSecondsPerChargeSecond { get; set; } = 35f;

    public float DurabilityFactor { get; set; } = 1f;
}

internal sealed class SpinningSweepDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new()
    {
        Cooldown = 8f,
        CooldownSkill = "weapon",
        CooldownReductionFactor = 0.5f
    };

    public float DurabilityFactor { get; set; } = 1f;

    public float DamageFactor { get; set; } = 0.75f;

    public float PushFactor { get; set; } = 0.5f;

    public float LoopStart { get; set; } = 0.4f;

    public float LoopEnd { get; set; } = 0.6f;

    public float AnimationSpeed { get; set; } = 1f;

    public float MoveSpeedFactor { get; set; } = 0.75f;

    public float SkillRaiseFactor { get; set; } = 0.25f;

}

internal sealed class CleavingThrustDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; } = 1f;

    public float RangeFactor { get; set; } = 3f;

    public float Angle { get; set; } = 90f;

    public float DamageFactor { get; set; } = 1f;

    public float PushFactor { get; set; } = 6f;

}

internal sealed class LaunchSlamDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; } = 1f;

    public float LaunchHeight { get; set; } = 4f;

    public float DamageFactor { get; set; } = 1f;
}

internal sealed class KnockbackChainDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; } = 1f;

    public float PushFactor { get; set; } = 8f;

    public float ChainDecay { get; set; } = 0.75f;
}

internal sealed class AftershockDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public int Waves { get; set; } = 3;

    public float Interval { get; set; } = 0.5f;

    public float WaveDecay { get; set; } = 0.2f;

    public float ForwardStep { get; set; } = 3f;

    public float DurabilityFactor { get; set; } = 1f;

}

internal sealed class RiftTrailDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

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
}

internal sealed class FractureLineDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float Range { get; set; } = 5f;

    public float HitSpacing { get; set; } = 0.75f;

    public float Duration { get; set; } = 1.2f;

    public float TickInterval { get; set; } = 0.3f;

    public float DamageFactor { get; set; } = 0.35f;

    public float DurabilityFactor { get; set; } = 1f;
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

    public float DurabilityFactor { get; set; } = 1f;

    public float LoopStart { get; set; } = 0.4f;

    public float LoopEnd { get; set; } = 0.6f;

    public float AnimationSpeed { get; set; } = 1f;

    public float MoveSpeedFactor { get; set; } = 0.75f;

    public float SkillRaiseFactor { get; set; } = 0.25f;
}
