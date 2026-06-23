using System;
using System.Linq;
using UnityEngine;

namespace SecondaryAttacks;

internal static class WeaponEffectDefinitionCompiler
{
    internal static bool HasPrefabAssignment(EffectBehaviorConfig effectConfig, string prefabName)
    {
        if (effectConfig.Prefabs == null || string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        return effectConfig.Prefabs.Keys.Any(configuredPrefabName =>
            string.Equals(configuredPrefabName?.Trim(), prefabName, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryResolveForPrefab(
        string effectId,
        EffectBehaviorConfig effectConfig,
        string prefabName,
        out ConfiguredWeaponEffectDefinition? definition,
        out string reason)
    {
        definition = null;
        reason = "";
        if (!TryGetPrefabOverride(effectConfig, prefabName, out EffectBehaviorOverrideConfig? prefabOverride))
        {
            return false;
        }

        bool hasOverrideValues = HasOverrideValues(prefabOverride);
        string runtimeEffectId = hasOverrideValues ? $"{effectId}_{prefabName}" : effectId;
        EffectBehaviorConfig resolvedConfig = hasOverrideValues
            ? MergePrefabOverride(effectConfig, prefabOverride!)
            : effectConfig;

        if (!TryResolve(runtimeEffectId, resolvedConfig, out definition, out reason))
        {
            return false;
        }

        definition!.Id = effectId;
        return true;
    }

    internal static bool TryResolve(
        string effectId,
        EffectBehaviorConfig effectConfig,
        out ConfiguredWeaponEffectDefinition? definition,
        out string reason)
    {
        definition = null;
        reason = "";
        string effectTypeText = string.IsNullOrWhiteSpace(effectConfig.Type) ? effectId : effectConfig.Type;
        if (!TryParseWeaponEffectType(effectTypeText, out WeaponEffectType effectType))
        {
            reason = $"unknown effect type '{effectTypeText}'.";
            return false;
        }

        if (!TryParseWeaponEffectTrigger(effectConfig.Trigger, out WeaponEffectTrigger trigger))
        {
            reason = $"unknown trigger '{effectConfig.Trigger}'.";
            return false;
        }

        string damageTypeText = string.IsNullOrWhiteSpace(effectConfig.DamageType)
            ? GetDefaultDamageType(effectType)
            : effectConfig.DamageType;
        if (!TryParseHitDamageType(damageTypeText, out HitData.DamageType damageType))
        {
            reason = $"unknown damageType '{damageTypeText}'.";
            return false;
        }

        if (!TryParseDamageModifier(effectConfig.Modifier, out HitData.DamageModifier modifier))
        {
            reason = $"unknown modifier '{effectConfig.Modifier}'.";
            return false;
        }

        if (!TryParseScalarValueMode(effectConfig.Damage.Mode, out ScalarValueMode damageMode))
        {
            reason = $"unknown damage mode '{effectConfig.Damage.Mode}'.";
            return false;
        }

        if (!TryParseScalarValueMode(effectConfig.Heal.Mode, out ScalarValueMode healMode))
        {
            reason = $"unknown heal mode '{effectConfig.Heal.Mode}'.";
            return false;
        }

        if (!TryParseScalarValueMode(effectConfig.StaminaRestore.Mode, out ScalarValueMode staminaMode))
        {
            reason = $"unknown staminaRestore mode '{effectConfig.StaminaRestore.Mode}'.";
            return false;
        }

        definition = new ConfiguredWeaponEffectDefinition
        {
            Id = effectId,
            StatusEffectName = UsesRuntimeStatusEffect(effectType) ? GetRuntimeEffectStatusName(effectId, effectType) : "",
            Type = effectType,
            Trigger = trigger,
            StacksRequired = Mathf.Max(1, effectConfig.StacksRequired ?? GetDefaultStacksRequired(effectType)),
            StackWindow = ResolveStackWindow(effectConfig, effectType),
            Duration = ResolveDuration(effectConfig, effectType),
            TickInterval = Mathf.Max(0.01f, effectConfig.TickInterval ?? 1f),
            ProcChance = Mathf.Clamp(effectConfig.ProcChance, 0f, 100f),
            DamageType = damageType,
            Modifier = modifier,
            DamageMode = damageMode,
            DamageValue = Mathf.Max(0f, effectConfig.Damage.Value),
            ValuePercent = Mathf.Max(0f, effectConfig.Value.HasValue ? effectConfig.Value.Value : GetDefaultValuePercent(effectType)),
            HealMode = healMode,
            HealValue = Mathf.Max(0f, effectConfig.Heal.Value),
            StaminaRestoreMode = staminaMode,
            StaminaRestoreValue = Mathf.Max(0f, effectConfig.StaminaRestore.Value),
            MoveSpeedMultiplier = ResolveMoveSpeedMultiplier(effectConfig, effectType),
            HealthThresholdPercent = Mathf.Clamp(effectConfig.HealthThresholdPercent, 0f, 100f),
            DamageMultiplier = Mathf.Max(0f, effectConfig.DamageMultiplier),
            ConsumeOnModify = effectConfig.ConsumeOnModify
        };
        return true;
    }

    private static bool TryGetPrefabOverride(
        EffectBehaviorConfig effectConfig,
        string prefabName,
        out EffectBehaviorOverrideConfig? prefabOverride)
    {
        prefabOverride = null;
        if (effectConfig.Prefabs == null)
        {
            return false;
        }

        foreach ((string configuredPrefabName, EffectBehaviorOverrideConfig? configuredOverride) in effectConfig.Prefabs)
        {
            if (string.Equals(configuredPrefabName?.Trim(), prefabName, StringComparison.OrdinalIgnoreCase))
            {
                prefabOverride = configuredOverride ?? new EffectBehaviorOverrideConfig();
                return true;
            }
        }

        return false;
    }

    private static bool HasOverrideValues(EffectBehaviorOverrideConfig? prefabOverride)
    {
        return prefabOverride != null &&
               (!string.IsNullOrWhiteSpace(prefabOverride.Type) ||
                prefabOverride.Value.HasValue ||
                !string.IsNullOrWhiteSpace(prefabOverride.Trigger) ||
                prefabOverride.StacksRequired.HasValue ||
                prefabOverride.StackWindow.HasValue ||
                prefabOverride.Duration.HasValue ||
                prefabOverride.TickInterval.HasValue ||
                prefabOverride.DamageFactor.HasValue ||
                prefabOverride.ProcChance.HasValue ||
                !string.IsNullOrWhiteSpace(prefabOverride.DamageType) ||
                !string.IsNullOrWhiteSpace(prefabOverride.Modifier) ||
                HasScalarOverrideValues(prefabOverride.Damage) ||
                HasScalarOverrideValues(prefabOverride.Heal) ||
                HasScalarOverrideValues(prefabOverride.StaminaRestore) ||
                prefabOverride.MoveSpeedMultiplier.HasValue ||
                prefabOverride.HealthThresholdPercent.HasValue ||
                prefabOverride.DamageMultiplier.HasValue ||
                prefabOverride.ConsumeOnModify.HasValue);
    }

    private static bool HasScalarOverrideValues(ScalarValueOverrideConfig? overrideValue)
    {
        return overrideValue != null &&
               (!string.IsNullOrWhiteSpace(overrideValue.Mode) ||
                overrideValue.Value.HasValue);
    }

    private static EffectBehaviorConfig MergePrefabOverride(
        EffectBehaviorConfig baseConfig,
        EffectBehaviorOverrideConfig prefabOverride)
    {
        return new EffectBehaviorConfig
        {
            Type = !string.IsNullOrWhiteSpace(prefabOverride.Type) ? prefabOverride.Type! : baseConfig.Type,
            Value = prefabOverride.Value ?? baseConfig.Value,
            Trigger = !string.IsNullOrWhiteSpace(prefabOverride.Trigger) ? prefabOverride.Trigger! : baseConfig.Trigger,
            StacksRequired = prefabOverride.StacksRequired ?? baseConfig.StacksRequired,
            StackWindow = prefabOverride.StackWindow ?? baseConfig.StackWindow,
            Duration = prefabOverride.Duration ?? baseConfig.Duration,
            TickInterval = prefabOverride.TickInterval ?? baseConfig.TickInterval,
            DamageFactor = prefabOverride.DamageFactor ?? baseConfig.DamageFactor,
            ProcChance = prefabOverride.ProcChance ?? baseConfig.ProcChance,
            DamageType = !string.IsNullOrWhiteSpace(prefabOverride.DamageType) ? prefabOverride.DamageType! : baseConfig.DamageType,
            Modifier = !string.IsNullOrWhiteSpace(prefabOverride.Modifier) ? prefabOverride.Modifier! : baseConfig.Modifier,
            Damage = MergeScalarValue(baseConfig.Damage, prefabOverride.Damage),
            Heal = MergeScalarValue(baseConfig.Heal, prefabOverride.Heal),
            StaminaRestore = MergeScalarValue(baseConfig.StaminaRestore, prefabOverride.StaminaRestore),
            MoveSpeedMultiplier = prefabOverride.MoveSpeedMultiplier ?? baseConfig.MoveSpeedMultiplier,
            HealthThresholdPercent = prefabOverride.HealthThresholdPercent ?? baseConfig.HealthThresholdPercent,
            DamageMultiplier = prefabOverride.DamageMultiplier ?? baseConfig.DamageMultiplier,
            ConsumeOnModify = prefabOverride.ConsumeOnModify ?? baseConfig.ConsumeOnModify
        };
    }

    private static ScalarValueConfig MergeScalarValue(
        ScalarValueConfig baseValue,
        ScalarValueOverrideConfig? overrideValue)
    {
        if (overrideValue == null)
        {
            return new ScalarValueConfig
            {
                Mode = baseValue.Mode,
                Value = baseValue.Value
            };
        }

        return new ScalarValueConfig
        {
            Mode = !string.IsNullOrWhiteSpace(overrideValue.Mode) ? overrideValue.Mode! : baseValue.Mode,
            Value = overrideValue.Value ?? baseValue.Value
        };
    }

    private static bool UsesRuntimeStatusEffect(WeaponEffectType effectType)
    {
        return effectType is WeaponEffectType.Dot
            or WeaponEffectType.ResistanceShred
            or WeaponEffectType.Haste;
    }

    private static string GetRuntimeEffectStatusName(string effectId, WeaponEffectType effectType)
    {
        return $"SecondaryAttacks_{SanitizeInternalId(effectId)}_{SanitizeInternalId(effectType.ToString())}";
    }

    private static bool TryParseWeaponEffectType(string effectTypeText, out WeaponEffectType effectType)
    {
        switch (effectTypeText.Trim().ToLowerInvariant())
        {
            case "dot":
                effectType = WeaponEffectType.Dot;
                return true;
            case "resistanceShred":
            case "resistanceshred":
                effectType = WeaponEffectType.ResistanceShred;
                return true;
            case "adrenaline":
                effectType = WeaponEffectType.Adrenaline;
                return true;
            case "haste":
                effectType = WeaponEffectType.Haste;
                return true;
            case "vampirism":
                effectType = WeaponEffectType.Vampirism;
                return true;
            case "execute":
                effectType = WeaponEffectType.Execute;
                return true;
            case "executioner":
                effectType = WeaponEffectType.Executioner;
                return true;
            case "staggerChance":
            case "staggerchance":
                effectType = WeaponEffectType.StaggerChance;
                return true;
            case "burstDamage":
            case "burstdamage":
                effectType = WeaponEffectType.BurstDamage;
                return true;
            case "bleeding":
                effectType = WeaponEffectType.Bleeding;
                return true;
            case "bash":
                effectType = WeaponEffectType.Bash;
                return true;
            case "piercing":
                effectType = WeaponEffectType.Piercing;
                return true;
            case "decapitator":
                effectType = WeaponEffectType.Decapitator;
                return true;
            case "smasher":
                effectType = WeaponEffectType.Smasher;
                return true;
            case "juggernaut":
                effectType = WeaponEffectType.Juggernaut;
                return true;
            default:
                effectType = default;
                return false;
        }
    }

    private static int GetDefaultStacksRequired(WeaponEffectType effectType)
    {
        return effectType switch
        {
            WeaponEffectType.Adrenaline => 5,
            WeaponEffectType.Haste => 6,
            WeaponEffectType.Vampirism => 5,
            WeaponEffectType.Bleeding => 4,
            WeaponEffectType.Bash => 4,
            WeaponEffectType.Piercing => 4,
            WeaponEffectType.Decapitator => 4,
            WeaponEffectType.Smasher => 4,
            WeaponEffectType.Juggernaut => 4,
            _ => 1
        };
    }

    private static float ResolveStackWindow(EffectBehaviorConfig effectConfig, WeaponEffectType effectType)
    {
        float configuredWindow = Mathf.Max(0f, effectConfig.StackWindow);
        if (configuredWindow > 0f)
        {
            return configuredWindow;
        }

        return GetDefaultStacksRequired(effectType) > 1 ? 10f : 0f;
    }

    private static float ResolveDuration(EffectBehaviorConfig effectConfig, WeaponEffectType effectType)
    {
        if (effectConfig.Duration.HasValue)
        {
            return Mathf.Max(0f, effectConfig.Duration.Value);
        }

        return effectType switch
        {
            WeaponEffectType.Haste => 6f,
            WeaponEffectType.Bleeding => 4f,
            _ => 0f
        };
    }

    private static float GetDefaultValuePercent(WeaponEffectType effectType)
    {
        return effectType switch
        {
            WeaponEffectType.Adrenaline => 10f,
            WeaponEffectType.Haste => 20f,
            WeaponEffectType.Vampirism => 10f,
            WeaponEffectType.Bleeding => 50f,
            WeaponEffectType.Bash => 50f,
            WeaponEffectType.Piercing => 50f,
            WeaponEffectType.Executioner => 50f,
            _ => 0f
        };
    }

    private static float ResolveMoveSpeedMultiplier(EffectBehaviorConfig effectConfig, WeaponEffectType effectType)
    {
        if (effectType == WeaponEffectType.Haste &&
            Mathf.Approximately(effectConfig.MoveSpeedMultiplier, 1f))
        {
            float valuePercent = Mathf.Max(0f, effectConfig.Value.HasValue ? effectConfig.Value.Value : GetDefaultValuePercent(effectType));
            return 1f + valuePercent * 0.01f;
        }

        return Mathf.Max(0f, effectConfig.MoveSpeedMultiplier);
    }

    private static string GetDefaultDamageType(WeaponEffectType effectType)
    {
        return effectType switch
        {
            WeaponEffectType.Bleeding => "slash",
            WeaponEffectType.Piercing => "pierce",
            WeaponEffectType.Decapitator => "slash",
            WeaponEffectType.Smasher => "blunt",
            WeaponEffectType.Juggernaut => "pierce",
            _ => ""
        };
    }

    private static bool TryParseWeaponEffectTrigger(string triggerText, out WeaponEffectTrigger trigger)
    {
        switch (triggerText.Trim())
        {
            case "anyHit":
                trigger = WeaponEffectTrigger.AnyHit;
                return true;
            case "mainHit":
                trigger = WeaponEffectTrigger.MainHit;
                return true;
            case "secondaryHit":
                trigger = WeaponEffectTrigger.SecondaryHit;
                return true;
            default:
                trigger = default;
                return false;
        }
    }

    private static bool TryParseScalarValueMode(string modeText, out ScalarValueMode mode)
    {
        switch (modeText.Trim().ToLowerInvariant())
        {
            case "fixed":
                mode = ScalarValueMode.Fixed;
                return true;
            case "targetmaxhealthpercent":
                mode = ScalarValueMode.TargetMaxHealthPercent;
                return true;
            case "selfmaxhealthpercent":
                mode = ScalarValueMode.SelfMaxHealthPercent;
                return true;
            case "selfmaxstaminapercent":
                mode = ScalarValueMode.SelfMaxStaminaPercent;
                return true;
            default:
                mode = default;
                return false;
        }
    }

    private static bool TryParseHitDamageType(string damageTypeText, out HitData.DamageType damageType)
    {
        switch (damageTypeText.Trim())
        {
            case "":
            case "damage":
                damageType = HitData.DamageType.Damage;
                return true;
            case "blunt":
                damageType = HitData.DamageType.Blunt;
                return true;
            case "slash":
                damageType = HitData.DamageType.Slash;
                return true;
            case "pierce":
                damageType = HitData.DamageType.Pierce;
                return true;
            case "chop":
                damageType = HitData.DamageType.Chop;
                return true;
            case "pickaxe":
                damageType = HitData.DamageType.Pickaxe;
                return true;
            case "fire":
                damageType = HitData.DamageType.Fire;
                return true;
            case "frost":
                damageType = HitData.DamageType.Frost;
                return true;
            case "lightning":
                damageType = HitData.DamageType.Lightning;
                return true;
            case "poison":
                damageType = HitData.DamageType.Poison;
                return true;
            case "spirit":
                damageType = HitData.DamageType.Spirit;
                return true;
            default:
                damageType = default;
                return false;
        }
    }

    private static bool TryParseDamageModifier(string modifierText, out HitData.DamageModifier modifier)
    {
        switch (modifierText.Trim())
        {
            case "normal":
                modifier = HitData.DamageModifier.Normal;
                return true;
            case "slightlyresistant":
                modifier = HitData.DamageModifier.SlightlyResistant;
                return true;
            case "resistant":
                modifier = HitData.DamageModifier.Resistant;
                return true;
            case "veryresistant":
                modifier = HitData.DamageModifier.VeryResistant;
                return true;
            case "slightlyweak":
                modifier = HitData.DamageModifier.SlightlyWeak;
                return true;
            case "weak":
                modifier = HitData.DamageModifier.Weak;
                return true;
            case "veryweak":
                modifier = HitData.DamageModifier.VeryWeak;
                return true;
            case "immune":
                modifier = HitData.DamageModifier.Immune;
                return true;
            case "ignore":
                modifier = HitData.DamageModifier.Ignore;
                return true;
            default:
                modifier = default;
                return false;
        }
    }

    private static string SanitizeInternalId(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }
}
