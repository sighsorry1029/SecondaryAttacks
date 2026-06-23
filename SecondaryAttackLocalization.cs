using System;
using System.Globalization;
using LocalizationManager;

namespace SecondaryAttacks;

internal static class SecondaryAttackLocalization
{
    internal const string StatusSecondaryCooldown = "$sa_status_secondary_cooldown";
    internal const string StatusSneakAmbushCooldown = "$sa_status_sneak_ambush_cooldown";
    internal const string StatusCleavingThrustCooldown = "$sa_status_cleaving_thrust_cooldown";
    internal const string StatusImpactBurstCooldown = "$sa_status_impact_burst_cooldown";
    internal const string StatusBoomerangCooldown = "$sa_status_boomerang_cooldown";
    internal const string StatusSpinningSweepCooldown = "$sa_status_spinning_sweep_cooldown";
    internal const string StatusLaunchSlamCooldown = "$sa_status_launch_slam_cooldown";
    internal const string StatusKnockbackChainCooldown = "$sa_status_knockback_chain_cooldown";
    internal const string StatusAftershockCooldown = "$sa_status_aftershock_cooldown";
    internal const string StatusRiftTrailCooldown = "$sa_status_rift_trail_cooldown";
    internal const string StatusFractureLineCooldown = "$sa_status_fracture_line_cooldown";
    internal const string StatusHarvestSweepCooldown = "$sa_status_harvest_sweep_cooldown";
    internal const string StatusSpearRainCooldown = "$sa_status_spear_rain_cooldown";
    internal const string StatusSummonEmpowerCooldown = "$sa_status_summon_empower_cooldown";
    internal const string StatusShieldConvertCooldown = "$sa_status_shield_convert_cooldown";
    internal const string StatusSneakAmbushCharge = "$sa_status_sneak_ambush_charge";

    internal const string TooltipSecondaryRecharging = "$sa_tooltip_secondary_recharging";
    internal const string TooltipSneakAmbushCharge = "$sa_tooltip_sneak_ambush_charge";
    internal const string TooltipSneakAmbushChargeProgress = "$sa_tooltip_sneak_ambush_charge_progress";

    internal const string HintDetonate = "$sa_hint_detonate";
    internal const string HudEmpower = "$sa_hud_empower";
    internal const string SummonNameFormat = "$sa_summon_name_format";

    internal static void Load()
    {
        Localizer.Load();
    }

    internal static string Localize(string token, string fallback)
    {
        if (Localization.instance == null)
        {
            return fallback;
        }

        string localized = Localization.instance.Localize(token);
        return string.IsNullOrWhiteSpace(localized) || string.Equals(localized, token, StringComparison.Ordinal)
            ? fallback
            : localized;
    }

    internal static string Format(string token, string fallback, params object[] args)
    {
        string format = Localize(token, fallback);
        try
        {
            return string.Format(CultureInfo.InvariantCulture, format, args);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.InvariantCulture, fallback, args);
        }
    }
}
