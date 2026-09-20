using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace SecondaryAttacks;

internal static class MagicPluginCompat
{
    // These three native attacks have live costs owned by MagicPlugin 2.2.0.
    // Read current entries after restoring an original, not values cached before
    // a YAML override. Calling StatsSetup again would duplicate its subscriptions.
    private static readonly Dictionary<string, SecondaryCost> Costs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BMP_FlameScepter"] = new("flame"),
        ["BMP_IceScepter"] = new("ice"),
        ["BMP_LightningScepter"] = new("lightning")
    };

    internal static void RefreshNativeSecondaryCost(string prefabName, Attack? attack)
    {
        if (attack == null || !Costs.TryGetValue(prefabName, out SecondaryCost? cost) ||
            !Chainloader.PluginInfos.TryGetValue(SecondaryAttacksPlugin.MagicPluginGuid, out BepInEx.PluginInfo? pluginInfo) ||
            pluginInfo == null)
        {
            return;
        }

        cost.Apply(pluginInfo.Instance.GetType().Assembly, attack);
    }

    private sealed class SecondaryCost
    {
        private readonly string _prefix;
        private ConfigEntryBase? _source;
        private ConfigEntry<float>? _drain;
        private bool _resolved;

        internal SecondaryCost(string prefix) => _prefix = prefix;

        internal void Apply(Assembly pluginAssembly, Attack attack)
        {
            if (!_resolved)
            {
                _resolved = true;
                try
                {
                    Type? configType = pluginAssembly.GetType("MagicPlugin.Functions.ConfigSetup");
                    const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                    _source = configType?.GetField($"_{_prefix}ScepterSource", flags)?.GetValue(null) as ConfigEntryBase;
                    _drain = configType?.GetField($"_{_prefix}ScepterMagicSourceSecondary", flags)?.GetValue(null) as ConfigEntry<float>;
                    if (_source == null || _drain == null)
                    {
                        throw new MissingFieldException($"MagicPlugin {_prefix} Scepter cost entries were not found.");
                    }
                }
                catch (Exception exception)
                {
                    _source = null;
                    _drain = null;
                    SecondaryAttacksPlugin.ModLogger.LogWarning(
                        $"MagicPlugin native secondary cost refresh unavailable: {exception.Message}");
                }
            }

            if (_source == null || _drain == null)
            {
                return;
            }

            // Match MagicPlugin's enum policy: Both pays the full drain twice;
            // unrecognized values leave the original costs unchanged.
            switch (_source.BoxedValue?.ToString())
            {
                case "Stamina":
                    attack.m_attackStamina = _drain.Value;
                    attack.m_attackEitr = 0f;
                    break;
                case "Eitr":
                    attack.m_attackStamina = 0f;
                    attack.m_attackEitr = _drain.Value;
                    break;
                case "Both":
                    attack.m_attackStamina = _drain.Value;
                    attack.m_attackEitr = _drain.Value;
                    break;
            }
        }
    }
}
