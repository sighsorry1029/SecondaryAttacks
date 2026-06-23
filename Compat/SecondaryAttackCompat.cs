using HarmonyLib;

namespace SecondaryAttacks;

internal static class SecondaryAttackCompat
{
    internal const string MagicPluginGuid = MagicPluginCompat.MagicPluginGuid;

    internal static void TryInstallStartupHooks(Harmony harmony)
    {
        MagicPluginCompat.TryInstall(harmony);
    }

    internal static void TryInstallWorldApplyHooks()
    {
    }
}
