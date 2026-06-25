using System;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackAdminAccessSystem
{
    private const string AdminProbePrefix = "secondaryattacks_admintest_";
    private const float AdminProbeRetrySeconds = 5f;
    private const float AdminProbeTimeoutSeconds = 3f;

    private static ZNet? _adminProbeZNet;
    private static long _adminProbePlayerId;
    private static string _adminProbeToken = "";
    private static bool _adminProbePending;
    private static bool? _adminProbeVerified;
    private static float _adminProbeNextTime;
    private static float _adminProbeDeadline;
    private static int _adminAccessFrame = -1;
    private static bool _adminAccess;

    internal static void Update()
    {
        if (!IsAdminNoPresetCooldownEnabled())
        {
            ResetServerAdminProbe();
            return;
        }

        PrimeServerAdminProbe();
    }

    internal static bool ShouldBypassPresetCooldowns(Character? character)
    {
        if (!IsAdminNoPresetCooldownEnabled() ||
            character is not Player player ||
            player != Player.m_localPlayer)
        {
            return false;
        }

        return HasCachedServerAdminAccess();
    }

    internal static bool HandleServerAdminProbeRemotePrint(string text)
    {
        if (!_adminProbePending)
        {
            return false;
        }

        if (string.Equals(text, $"Unbanning user {_adminProbeToken}", StringComparison.Ordinal))
        {
            MarkServerAdminProbeSuccess(ZNet.instance);
            return true;
        }

        if (string.Equals(text, "You are not admin", StringComparison.Ordinal))
        {
            _adminProbePending = false;
            _adminProbeVerified = false;
            _adminProbeNextTime = Time.realtimeSinceStartup + AdminProbeRetrySeconds;
            return true;
        }

        return false;
    }

    private static bool IsAdminNoPresetCooldownEnabled()
    {
        return SecondaryAttacksPlugin.AdminNoPresetCooldowns.Value == SecondaryAttacksPlugin.Toggle.On;
    }

    private static bool HasCachedServerAdminAccess()
    {
        int frame = Time.frameCount;
        if (_adminAccessFrame == frame)
        {
            return _adminAccess;
        }

        _adminAccessFrame = frame;
        _adminAccess = HasServerAdminAccess();
        return _adminAccess;
    }

    private static bool HasServerAdminAccess()
    {
        if (ZNet.instance == null)
        {
            return true;
        }

        if (ZNet.instance.IsServer() || ZNet.instance.LocalPlayerIsAdminOrHost())
        {
            MarkServerAdminProbeSuccess(ZNet.instance);
            return true;
        }

        UpdateServerAdminProbeState();
        if (_adminProbeVerified == true)
        {
            return true;
        }

        StartServerAdminProbe(force: false);
        return false;
    }

    private static void PrimeServerAdminProbe()
    {
        if (ZNet.instance == null || ZNet.instance.IsServer())
        {
            ResetServerAdminProbe();
            return;
        }

        UpdateServerAdminProbeState();
        if (_adminProbeVerified == null)
        {
            StartServerAdminProbe(force: false);
        }
    }

    private static void UpdateServerAdminProbeState()
    {
        ZNet? znet = ZNet.instance;
        long playerId = GetLocalPlayerId();
        if (!ReferenceEquals(_adminProbeZNet, znet) || _adminProbePlayerId != playerId)
        {
            ResetServerAdminProbe();
            _adminProbeZNet = znet;
            _adminProbePlayerId = playerId;
            _adminProbeToken = playerId > 0 ? AdminProbePrefix + playerId.ToString(CultureInfo.InvariantCulture) : "";
        }

        if (_adminProbePending && Time.realtimeSinceStartup > _adminProbeDeadline)
        {
            _adminProbePending = false;
            _adminProbeVerified = false;
            _adminProbeNextTime = Time.realtimeSinceStartup + AdminProbeRetrySeconds;
        }
    }

    private static void StartServerAdminProbe(bool force)
    {
        ZNet? znet = ZNet.instance;
        if (znet == null || znet.IsServer())
        {
            return;
        }

        UpdateServerAdminProbeState();
        if (_adminProbePending || string.IsNullOrWhiteSpace(_adminProbeToken))
        {
            return;
        }

        float now = Time.realtimeSinceStartup;
        if (!force && now < _adminProbeNextTime)
        {
            return;
        }

        try
        {
            _adminProbePending = true;
            _adminProbeDeadline = now + AdminProbeTimeoutSeconds;
            _adminProbeNextTime = now + AdminProbeRetrySeconds;
            znet.Unban(_adminProbeToken);
        }
        catch (Exception ex)
        {
            _adminProbePending = false;
            _adminProbeVerified = false;
            _adminProbeNextTime = Time.realtimeSinceStartup + AdminProbeRetrySeconds;
            SecondaryAttacksPlugin.ModLogger.LogDebug($"Admin cooldown bypass probe failed: {ex.Message}");
        }
    }

    private static void MarkServerAdminProbeSuccess(ZNet? znet)
    {
        _adminProbeZNet = znet;
        _adminProbePlayerId = GetLocalPlayerId();
        _adminProbePending = false;
        _adminProbeVerified = true;
        _adminProbeNextTime = Time.realtimeSinceStartup + AdminProbeRetrySeconds;
    }

    private static void ResetServerAdminProbe()
    {
        _adminProbeZNet = null;
        _adminProbePlayerId = 0;
        _adminProbeToken = "";
        _adminProbePending = false;
        _adminProbeVerified = null;
        _adminProbeNextTime = 0f;
        _adminProbeDeadline = 0f;
        _adminAccessFrame = -1;
        _adminAccess = false;
    }

    private static long GetLocalPlayerId()
    {
        long playerId = Game.instance?.GetPlayerProfile()?.GetPlayerID() ?? 0L;
        if (playerId != 0L)
        {
            return playerId;
        }

        Player? localPlayer = Player.m_localPlayer;
        return localPlayer != null ? localPlayer.GetPlayerID() : 0L;
    }
}

[HarmonyPatch(typeof(ZNet), "RPC_RemotePrint")]
internal static class SecondaryAttackAdminProbeRemotePrintPatch
{
    private static bool Prefix(string text)
    {
        return !SecondaryAttackAdminAccessSystem.HandleServerAdminProbeRemotePrint(text);
    }
}
