using System;
using HarmonyLib;

namespace SecondaryAttacks;

internal static class SecondaryAttackNetworkCompat
{
    private static string VersionCheckRpcName => $"{SecondaryAttacksPlugin.ModName}_VersionCheck";

    public static void HandleVersionCheck(ZRpc rpc, ZPackage pkg)
    {
        string? version = pkg.ReadString();
        SecondaryAttacksPlugin.ModLogger.LogInfo($"Version check, local: {SecondaryAttacksPlugin.ModVersion}, remote: {version}");
        if (version != SecondaryAttacksPlugin.ModVersion)
        {
            SecondaryAttacksPlugin.ConnectionError = $"{SecondaryAttacksPlugin.ModName} Installed: {SecondaryAttacksPlugin.ModVersion}\n Needed: {version}";
            if (!ZNet.instance.IsServer())
            {
                return;
            }

            SecondaryAttacksPlugin.ModLogger.LogWarning($"Peer ({rpc.m_socket.GetHostName()}) has incompatible version, disconnecting...");
            rpc.Invoke("Error", 3);
            return;
        }

        if (!ZNet.instance.IsServer())
        {
            SecondaryAttacksPlugin.ModLogger.LogInfo("Received same version from server.");
        }
    }

    [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
    private static class RegisterAndSendVersionCheckPatch
    {
        private static void Prefix(ZNetPeer peer)
        {
            peer.m_rpc.Register(VersionCheckRpcName, new Action<ZRpc, ZPackage>(HandleVersionCheck));

            SecondaryAttacksPlugin.ModLogger.LogInfo("Invoking version check");
            ZPackage zpackage = new();
            zpackage.Write(SecondaryAttacksPlugin.ModVersion);
            peer.m_rpc.Invoke(VersionCheckRpcName, zpackage);
        }
    }

    [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.ShowConnectError))]
    private static class ShowConnectionErrorPatch
    {
        private static void Postfix(FejdStartup __instance)
        {
            if (!__instance.m_connectionFailedPanel.activeSelf)
            {
                return;
            }

            __instance.m_connectionFailedError.fontSizeMax = 25;
            __instance.m_connectionFailedError.fontSizeMin = 15;
            __instance.m_connectionFailedError.text += $"\n{SecondaryAttacksPlugin.ConnectionError}";
        }
    }
}
