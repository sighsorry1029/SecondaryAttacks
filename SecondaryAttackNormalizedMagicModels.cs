using System.Collections.Generic;

namespace SecondaryAttacks;

internal enum MagicSummonQualityPreset
{
    None = 0,
    CountByQuality = 1,
    LevelByQuality = 2
}

internal sealed class NormalizedMagicSummonOverrideConfig
{
    public string EntryId { get; set; } = "";

    public MagicSummonQualityPreset QualityPreset { get; set; }

    public int MaxQuality { get; set; } = 4;

    public int? LifetimeSeconds { get; set; }

    public bool HasQualityPreset => QualityPreset != MagicSummonQualityPreset.None;

    public List<NormalizedMagicSummonCloneConfig> Summons { get; set; } = new();
}

internal sealed class NormalizedMagicSummonCloneConfig
{
    public string SourcePrefab { get; set; } = "";

    public string ClonePrefab { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public float? Health { get; set; }

    public int Weight { get; set; } = 1;
}

internal sealed class NormalizedSummonEmpowerSecondaryConfig
{
    public float Cooldown { get; set; } = 30f;

    public string CooldownSkill { get; set; } = "bloodMagic";

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float Radius { get; set; } = 10f;

    public float Duration { get; set; } = 15f;

    public float MoveSpeedFactor { get; set; } = 1.3f;

    public float AttackSpeedFactor { get; set; } = 1.5f;
}

internal sealed class NormalizedShieldConvertSecondaryConfig
{
    public float Cooldown { get; set; } = 30f;

    public string CooldownSkill { get; set; } = "bloodMagic";

    public float CooldownReductionFactor { get; set; } = 0.5f;

    public float Radius { get; set; } = 7f;

    public float HealFactor { get; set; }
}
