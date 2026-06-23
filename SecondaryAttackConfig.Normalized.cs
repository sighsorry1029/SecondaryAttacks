using System;
using System.Collections.Generic;

namespace SecondaryAttacks;

internal sealed class NormalizedSecondaryAttackConfigFile
{
    public Dictionary<string, EffectBehaviorConfig> Effects { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, NormalizedMagicSummonOverrideConfig> MagicSummons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, NormalizedWeaponConfig> Weapons { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, NormalizedWeaponConfig> GlobalRangedPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, NormalizedWeaponConfig> GlobalBloodMagicPresets { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public NormalizedWeaponConfig? GlobalMeleeFallback { get; set; }
}
