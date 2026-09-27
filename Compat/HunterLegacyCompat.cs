using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;

namespace SecondaryAttacks;

// Hunter owns ranged input, attack startup and ammo accounting, not just the
// secondary Attack data. Do not give these weapons an SA runtime definition.
internal static class HunterLegacyCompat
{
    internal const string PluginGuid = "Dreanegade.Hunter_Legacy";

    private static readonly HashSet<string> OffhandPrefabs = new(StringComparer.OrdinalIgnoreCase)
    {
        "OffhandCrossbowBlackforest_DO", "OffhandCrossbowMountain_DO",
        "OffhandCrossbowMistlands_DO", "OffhandCrossbowAshlands_DO"
    };

    // Reviewed 1.1.4 names also protect weapons if its private registry changes.
    private static readonly HashSet<string> KnownRangedPrefabs = new(StringComparer.OrdinalIgnoreCase)
    {
        "BowEvoCoreBlackforest_DO", "BowEvoCoreSwamp_DO", "BowEvoCoreMountain_DO",
        "BowEvoCorePlains_DO", "BowEvoCoreMistlands_DO", "BowEvoCoreAshlandsLightning_DO",
        "BowEvoCoreAshlandsBlood_DO", "BowEvoCoreAshlandsNature_DO", "BowForestSentinel_DO",
        "BowFrostpeakHarrier_DO", "BowNocturneArrow_DO", "BowGoldenStrider_DO",
        "BowSapphireFalcon_DO", "BowAshenExile_DO",
        "CrossbowEvoCoreBlackforest_DO", "CrossbowEvoCoreSwamp_DO", "CrossbowEvoCoreMountain_DO",
        "CrossbowEvoCorePlains_DO", "CrossbowEvoCoreMistlands_DO", "CrossbowEvoCoreAshlandsBlood_DO",
        "CrossbowEvoCoreAshlandsLightning_DO", "CrossbowEvoCoreAshlandsNature_DO",
        "CrossbowEvoRenegadeBlackforest_DO", "CrossbowEvoRenegadeSwamp_DO",
        "CrossbowEvoRenegadeMountain_DO", "CrossbowEvoRenegadePlains_DO",
        "CrossbowEvoRenegadeMistlands_DO", "CrossbowEvoRenegadeAshlandsFrost_DO",
        "CrossbowEvoRenegadeAshlandsSpirit_DO", "CrossbowDemonHunter_DO",
        "SlingshotMeadows_DO", "SlingshotBlackforest_DO", "SlingshotSwamp_DO",
        "SlingshotPlains_DO", "SlingshotAshlands_DO"
    };
    private static readonly HashSet<string> RegisteredPrefabs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> WarnedOverrides = new(StringComparer.OrdinalIgnoreCase);
    private static Type? _pluginType;
    private static bool _prefabsResolved;
    private static FieldInfo? _cloneSourcesField;
    private static Dictionary<Attack, Attack>? _cloneSources;

    internal static void Initialize()
    {
        Dispose();
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out BepInEx.PluginInfo? plugin) ||
            plugin?.Instance == null)
        {
            return;
        }

        _pluginType = plugin.Instance.GetType();
        SecondaryAttacksPlugin.ModLogger.LogInfo(
            "Hunter Legacy compatibility enabled: its ranged weapons keep their own attacks and ignore SA preset overrides. " +
            "Hunter Technique integration with SA projectile presets is not included.");
        try
        {
            // Do not assume later versions retain this private tracking contract.
            if (plugin.Metadata.Version != new System.Version(1, 1, 4))
            {
                throw new NotSupportedException($"Unreviewed Hunter Legacy version {plugin.Metadata.Version}.");
            }

            FieldInfo? field = _pluginType.Assembly.GetType("Hunter_Legacy.AmmoUsageOverride")?
                .GetField("CloneSources", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null || !field.IsPrivate || !field.IsInitOnly ||
                field.FieldType != typeof(Dictionary<Attack, Attack>))
            {
                throw new MissingFieldException("AmmoUsageOverride.CloneSources contract changed.");
            }

            // Read the dictionary only after Clone has run Hunter's own postfix.
            _cloneSourcesField = field;
        }
        catch (Exception exception)
        {
            DisableCloneCleanup(exception);
        }
    }

    internal static void PrepareForApply()
    {
        if (_pluginType == null || _prefabsResolved)
        {
            return;
        }

        try
        {
            _prefabsResolved = NativeSecondaryAttackCompat.DiscoverRegisteredPrefabs(_pluginType, RegisteredPrefabs);
        }
        catch (Exception exception)
        {
            _prefabsResolved = true;
            SecondaryAttacksPlugin.ModLogger.LogWarning(
                $"Hunter Legacy prefab discovery failed; using the built-in 1.1.4 weapon list. {exception.Message}");
        }
    }

    internal static bool ShouldSkipWeapon(string prefabName, ItemDrop itemDrop, bool explicitOverride)
    {
        if (_pluginType == null || string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        ItemDrop.ItemData.SharedData? shared = itemDrop?.m_itemData?.m_shared;
        bool registeredRanged = RegisteredPrefabs.Contains(prefabName) && shared != null &&
            shared.m_itemType != ItemDrop.ItemData.ItemType.Ammo &&
            shared.m_itemType != ItemDrop.ItemData.ItemType.AmmoNonEquipable &&
            (shared.m_itemType == ItemDrop.ItemData.ItemType.Bow ||
             shared.m_skillType == Skills.SkillType.Bows || shared.m_skillType == Skills.SkillType.Crossbows);
        if (!KnownRangedPrefabs.Contains(prefabName) && !OffhandPrefabs.Contains(prefabName) && !registeredRanged)
        {
            return false;
        }

        if (explicitOverride && WarnedOverrides.Add(prefabName))
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning(
                $"Skipping SecondaryAttacks YAML override for {prefabName}: Hunter Legacy owns this weapon's input, attacks and ammo handling.");
        }

        return true;
    }

    internal static bool IsOffhandCrossbow(ItemDrop.ItemData item)
    {
        return _pluginType != null && item?.m_dropPrefab != null && OffhandPrefabs.Contains(item.m_dropPrefab.name);
    }

    internal static void ForgetPreparedClone(Attack source, Attack clone)
    {
        if (_cloneSourcesField == null || source == null || clone == null || ReferenceEquals(source, clone))
        {
            return;
        }

        try
        {
            _cloneSources ??= (Dictionary<Attack, Attack>)_cloneSourcesField.GetValue(null)!;
            if (_cloneSources.TryGetValue(clone, out Attack? trackedSource) && ReferenceEquals(trackedSource, source))
            {
                // Only this SA template. Runtime Clone -> Start tracking and
                // mappings changed by another Clone postfix remain untouched.
                _cloneSources.Remove(clone);
            }
        }
        catch (Exception exception)
        {
            DisableCloneCleanup(exception);
        }
    }

    private static void DisableCloneCleanup(Exception exception)
    {
        _cloneSourcesField = null;
        _cloneSources = null;
        SecondaryAttacksPlugin.ModLogger.LogWarning(
            $"Hunter Legacy prepared-attack tracking cleanup is unavailable; native weapon protection remains active. {exception.Message}");
    }

    internal static void Dispose()
    {
        // Release our references only; Hunter owns its runtime tracking tables.
        _cloneSourcesField = null;
        _cloneSources = null;
        _pluginType = null;
        _prefabsResolved = false;
        RegisteredPrefabs.Clear();
        WarnedOverrides.Clear();
    }
}
