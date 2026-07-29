using System;

namespace SecondaryAttacks;

internal static class SecondaryAttackCooldownGroupResolver
{
    internal static void Apply(
        string prefabName,
        ItemDrop itemDrop,
        SecondaryAttackDefinition definition)
    {
        string automaticFamilyGroup =
            SecondaryAttackWeaponFamilyResolver.ResolveAutomaticCooldownGroup(prefabName, itemDrop);

        Assign(definition.SneakAmbush?.PresetCooldown, automaticFamilyGroup, "sneakAmbush");
        Assign(definition.CleavingThrust?.PresetCooldown, automaticFamilyGroup, "cleavingThrust");
        Assign(definition.LaunchSlam?.PresetCooldown, automaticFamilyGroup, "launchSlam");
        Assign(definition.KnockbackChain?.PresetCooldown, automaticFamilyGroup, "knockbackChain");
        Assign(definition.Aftershock?.PresetCooldown, automaticFamilyGroup, "aftershock");
        Assign(definition.RiftTrail?.PresetCooldown, automaticFamilyGroup, "riftTrail");
        Assign(definition.FractureLine?.PresetCooldown, automaticFamilyGroup, "fractureLine");
        Assign(definition.HarvestSweep?.PresetCooldown, automaticFamilyGroup, "harvestSweep");
        Assign(
            definition.OnProjectileHit?.PresetCooldown,
            automaticFamilyGroup,
            definition.OnProjectileHit?.Preset);
        Assign(definition.Boomerang?.PresetCooldown, automaticFamilyGroup, "boomerang");
        Assign(definition.SpinningSweep?.PresetCooldown, automaticFamilyGroup, "spinningSweep");

        switch (definition.Behavior)
        {
            case ProjectileSecondaryBehavior projectile:
                projectile.CooldownGroup = !string.IsNullOrWhiteSpace(automaticFamilyGroup)
                    ? automaticFamilyGroup
                    : CreatePresetFallback("ranged", ProjectileRuntimeSystem.GetPresetName(projectile.Preset));
                break;
            case SummonEmpowerSecondaryBehavior summonEmpower:
                summonEmpower.PresetCooldown.CooldownGroup =
                    SecondaryAttackWeaponFamilyResolver.BloodSummonCooldownGroup;
                break;
            case ShieldConvertSecondaryBehavior shieldConvert:
                shieldConvert.PresetCooldown.CooldownGroup =
                    SecondaryAttackWeaponFamilyResolver.BloodShieldCooldownGroup;
                break;
        }
    }

    internal static string ResolveMeleeGroup(
        string presetName,
        MeleePresetCooldownDefinition? cooldown)
    {
        return !string.IsNullOrWhiteSpace(cooldown?.CooldownGroup)
            ? cooldown!.CooldownGroup.Trim()
            : CreatePresetFallback("melee", presetName);
    }

    internal static string ResolveRangedGroup(ProjectileSecondaryBehavior? behavior)
    {
        if (behavior == null)
        {
            return "";
        }

        return !string.IsNullOrWhiteSpace(behavior.CooldownGroup)
            ? behavior.CooldownGroup.Trim()
            : CreatePresetFallback("ranged", ProjectileRuntimeSystem.GetPresetName(behavior.Preset));
    }

    private static void Assign(
        MeleePresetCooldownDefinition? cooldown,
        string automaticFamilyGroup,
        string? presetName)
    {
        if (cooldown == null)
        {
            return;
        }

        cooldown.CooldownGroup = !string.IsNullOrWhiteSpace(automaticFamilyGroup)
            ? automaticFamilyGroup
            : CreatePresetFallback("melee", presetName);
    }

    private static string CreatePresetFallback(string domain, string? presetName)
    {
        string normalizedDomain = NormalizeToken(domain);
        string normalizedPreset = NormalizeToken(presetName);
        if (string.IsNullOrEmpty(normalizedDomain) || string.IsNullOrEmpty(normalizedPreset))
        {
            return "";
        }

        return $"preset:{normalizedDomain}:{normalizedPreset}";
    }

    private static string NormalizeToken(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? ""
            : value!.Trim().ToLowerInvariant();
    }
}
