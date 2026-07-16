using System;
using System.Globalization;
using LocalizationManager;

namespace SecondaryAttacks;

internal static class SecondaryAttackLocalization
{
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
