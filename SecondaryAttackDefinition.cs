using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecondaryAttacks;

internal sealed class SecondaryAttackDefinition
{
    private List<ConfiguredWeaponEffectDefinition> _configuredEffects = new();
    private WeaponEffectRuntimeCache? _effectRuntimeCache;

    public string PrefabName { get; set; } = "";

    public bool AppliesSecondaryOverride { get; set; }

    public SecondaryAttackBehavior Behavior { get; set; } = new EffectOnlySecondaryBehavior();

    public SecondaryAttackBehaviorType BehaviorType => Behavior.BehaviorType;

    public string AttackAnimation { get; set; } = "";

    public string GuardAttackAnimation { get; set; } = "";

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

    public List<ConfiguredWeaponEffectDefinition> ConfiguredEffects
    {
        get => _configuredEffects;
        set
        {
            _configuredEffects = value ?? new List<ConfiguredWeaponEffectDefinition>();
            _effectRuntimeCache = null;
        }
    }

    internal WeaponEffectRuntimeCache EffectRuntimeCache =>
        _effectRuntimeCache ??= WeaponEffectRuntimeCache.Build(_configuredEffects);

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

    public string CooldownFallback { get; set; } = ProjectilePresetCooldownFallback.OriginalSecondary;

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

    public string CooldownFallback { get; set; } = ProjectilePresetCooldownFallback.OriginalSecondary;

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

    public float RangeFactor { get; set; } = 2.5f;

    public float Angle { get; set; } = 90f;

    public float DamageFactor { get; set; } = 1f;

    public float PushFactor { get; set; } = 1f;

}

internal sealed class LaunchSlamDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; } = 1f;

    public float LaunchHeight { get; set; } = 4f;

    public float DamageFactor { get; set; } = 1f;

    public float LandingAreaRadiusFactor { get; set; } = 1.5f;

    public float LandingAreaRadiusMax { get; set; } = 4f;

    public string Vfx { get; set; } = "vfx_archerytarget_bullseye";

    public Vector3 VfxRotationOffset { get; set; } = new(90f, 0f, 0f);

    public string Sfx { get; set; } = "sfx_sledge_hit";
}

internal sealed class KnockbackChainDefinition
{
    public MeleePresetCooldownDefinition PresetCooldown { get; set; } = new();

    public float DurabilityFactor { get; set; } = 1f;

    public float PushFactor { get; set; } = 8f;

    public float CollisionRadius { get; set; } = 2f;

    public float ChainDecay { get; set; } = 0.75f;

    public int MaxChainTargets { get; set; } = 5;

    public float Duration { get; set; } = 1.5f;

    public float MinSpeed { get; set; } = 0.5f;

    public float HitCooldown { get; set; } = 0.2f;

    public string InitialHitVfx { get; set; } = "vfx_archerytarget_bullseye";

    public string FirstCollisionVfx { get; set; } = "vfx_HitSparks";

    public string SecondCollisionVfx { get; set; } = "vfx_HitSparks";

    public string LaterCollisionVfx { get; set; } = "vfx_HitSparks";

    public string CollisionSfx { get; set; } = "sfx_club_hit";

    public float CollisionVfxMinDistanceFromPlayer { get; set; }
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


    public string CooldownSkill { get; set; } = "weapon";

    public float CooldownReductionFactor { get; set; } = 0.5f;
}

internal static class ProjectilePresetCooldownFallback
{
    internal const string OriginalSecondary = "originalSecondary";
    internal const string CopiedSecondary = "copiedSecondary";

    internal static string Normalize(string? raw, string fallback = OriginalSecondary)
    {
        string value = raw?.Trim() ?? "";
        if (value.Length == 0)
        {
            return IsSupported(fallback) ? fallback : OriginalSecondary;
        }

        if (value.Equals(OriginalSecondary, System.StringComparison.OrdinalIgnoreCase))
        {
            return OriginalSecondary;
        }

        if (value.Equals(CopiedSecondary, System.StringComparison.OrdinalIgnoreCase))
        {
            return CopiedSecondary;
        }

        return IsSupported(fallback) ? fallback : OriginalSecondary;
    }

    internal static bool IsCopiedSecondary(string? value)
    {
        return Normalize(value).Equals(CopiedSecondary, System.StringComparison.Ordinal);
    }

    internal static bool IsOriginalSecondary(string? value)
    {
        return Normalize(value).Equals(OriginalSecondary, System.StringComparison.Ordinal);
    }

    internal static bool UsesDynamicOriginalSecondary(SecondaryAttackDefinition? definition)
    {
        if (definition?.AppliesSecondaryOverride != true ||
            definition.Behavior is not CopiedSecondaryBehavior)
        {
            return false;
        }

        return definition.Boomerang != null || definition.OnProjectileHit != null;
    }

    private static bool IsSupported(string? value)
    {
        return value != null &&
               (value.Equals(OriginalSecondary, System.StringComparison.Ordinal) ||
                value.Equals(CopiedSecondary, System.StringComparison.Ordinal));
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

internal sealed class ConfiguredWeaponEffectDefinition
{
    public string Id { get; set; } = "";

    public string StatusEffectName { get; set; } = "";

    public WeaponEffectType Type { get; set; }

    public WeaponEffectTrigger Trigger { get; set; }

    public int StacksRequired { get; set; } = 1;

    public float StackWindow { get; set; }

    public float Duration { get; set; }

    public float TickInterval { get; set; } = 1f;

    public float ProcChance { get; set; } = 100f;

    public HitData.DamageType DamageType { get; set; } = HitData.DamageType.Damage;

    public HitData.DamageModifier Modifier { get; set; } = HitData.DamageModifier.Normal;

    public ScalarValueMode DamageMode { get; set; } = ScalarValueMode.Fixed;

    public float DamageValue { get; set; }

    public float ValuePercent { get; set; }

    public ScalarValueMode HealMode { get; set; } = ScalarValueMode.Fixed;

    public float HealValue { get; set; }

    public ScalarValueMode StaminaRestoreMode { get; set; } = ScalarValueMode.Fixed;

    public float StaminaRestoreValue { get; set; }

    public float MoveSpeedMultiplier { get; set; } = 1f;

    public float HealthThresholdPercent { get; set; } = 25f;

    public float DamageMultiplier { get; set; } = 1f;

    public bool ConsumeOnModify { get; set; }

    internal bool UsesActualDamagePostfix =>
        Type is WeaponEffectType.Adrenaline
            or WeaponEffectType.Haste
            or WeaponEffectType.Vampirism
            or WeaponEffectType.Bleeding
            or WeaponEffectType.Bash
            or WeaponEffectType.Piercing
            or WeaponEffectType.Executioner
            or WeaponEffectType.Decapitator
            or WeaponEffectType.Smasher
            or WeaponEffectType.Juggernaut;
}

internal sealed class WeaponEffectRuntimeCache
{
    internal static readonly WeaponEffectRuntimeCache Empty = new(
        Array.Empty<ConfiguredWeaponEffectDefinition>(),
        Array.Empty<ConfiguredWeaponEffectDefinition>(),
        Array.Empty<ConfiguredWeaponEffectDefinition>(),
        Array.Empty<ConfiguredWeaponEffectDefinition>());

    private WeaponEffectRuntimeCache(
        ConfiguredWeaponEffectDefinition[] primaryImmediateEffects,
        ConfiguredWeaponEffectDefinition[] primaryPostDamageEffects,
        ConfiguredWeaponEffectDefinition[] secondaryImmediateEffects,
        ConfiguredWeaponEffectDefinition[] secondaryPostDamageEffects)
    {
        PrimaryImmediateEffects = primaryImmediateEffects;
        PrimaryPostDamageEffects = primaryPostDamageEffects;
        SecondaryImmediateEffects = secondaryImmediateEffects;
        SecondaryPostDamageEffects = secondaryPostDamageEffects;
    }

    internal ConfiguredWeaponEffectDefinition[] PrimaryImmediateEffects { get; }

    internal ConfiguredWeaponEffectDefinition[] PrimaryPostDamageEffects { get; }

    internal ConfiguredWeaponEffectDefinition[] SecondaryImmediateEffects { get; }

    internal ConfiguredWeaponEffectDefinition[] SecondaryPostDamageEffects { get; }

    internal bool HasEffects =>
        PrimaryImmediateEffects.Length > 0 ||
        PrimaryPostDamageEffects.Length > 0 ||
        SecondaryImmediateEffects.Length > 0 ||
        SecondaryPostDamageEffects.Length > 0;

    internal ConfiguredWeaponEffectDefinition[] GetImmediateEffects(bool secondaryAttack) =>
        secondaryAttack ? SecondaryImmediateEffects : PrimaryImmediateEffects;

    internal ConfiguredWeaponEffectDefinition[] GetPostDamageEffects(bool secondaryAttack) =>
        secondaryAttack ? SecondaryPostDamageEffects : PrimaryPostDamageEffects;

    internal static WeaponEffectRuntimeCache Build(IReadOnlyList<ConfiguredWeaponEffectDefinition> effects)
    {
        if (effects.Count == 0)
        {
            return Empty;
        }

        List<ConfiguredWeaponEffectDefinition> primaryImmediate = new();
        List<ConfiguredWeaponEffectDefinition> primaryPostDamage = new();
        List<ConfiguredWeaponEffectDefinition> secondaryImmediate = new();
        List<ConfiguredWeaponEffectDefinition> secondaryPostDamage = new();
        AddTriggerEffects(effects, secondaryAttack: false, primaryImmediate, primaryPostDamage);
        AddTriggerEffects(effects, secondaryAttack: true, secondaryImmediate, secondaryPostDamage);
        if (primaryImmediate.Count == 0 &&
            primaryPostDamage.Count == 0 &&
            secondaryImmediate.Count == 0 &&
            secondaryPostDamage.Count == 0)
        {
            return Empty;
        }

        return new WeaponEffectRuntimeCache(
            primaryImmediate.Count > 0 ? primaryImmediate.ToArray() : Array.Empty<ConfiguredWeaponEffectDefinition>(),
            primaryPostDamage.Count > 0 ? primaryPostDamage.ToArray() : Array.Empty<ConfiguredWeaponEffectDefinition>(),
            secondaryImmediate.Count > 0 ? secondaryImmediate.ToArray() : Array.Empty<ConfiguredWeaponEffectDefinition>(),
            secondaryPostDamage.Count > 0 ? secondaryPostDamage.ToArray() : Array.Empty<ConfiguredWeaponEffectDefinition>());
    }

    private static void AddTriggerEffects(
        IReadOnlyList<ConfiguredWeaponEffectDefinition> effects,
        bool secondaryAttack,
        List<ConfiguredWeaponEffectDefinition> immediateEffects,
        List<ConfiguredWeaponEffectDefinition> postDamageEffects)
    {
        HashSet<string> appliedEffects = new(StringComparer.OrdinalIgnoreCase);
        foreach (ConfiguredWeaponEffectDefinition effect in effects)
        {
            if (!appliedEffects.Add(effect.Id) || !MatchesTrigger(effect.Trigger, secondaryAttack))
            {
                continue;
            }

            if (effect.UsesActualDamagePostfix)
            {
                postDamageEffects.Add(effect);
            }
            else
            {
                immediateEffects.Add(effect);
            }
        }
    }

    private static bool MatchesTrigger(WeaponEffectTrigger trigger, bool secondaryAttack)
    {
        return trigger switch
        {
            WeaponEffectTrigger.AnyHit => true,
            WeaponEffectTrigger.MainHit => !secondaryAttack,
            WeaponEffectTrigger.SecondaryHit => secondaryAttack,
            _ => false
        };
    }
}
