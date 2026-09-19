using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using UnityEngine;

namespace SecondaryAttacks;

internal static class MagicSupremacyCompat
{
    internal const string PluginGuid = "Dreanegade.Magic_Supremacy";

    // Magic Supremacy 3.1.1 weapon prefabs. Asset-bundle discovery below also
    // picks up later additions without creating a compile-time dependency.
    private static readonly HashSet<string> OwnedPrefabNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "BowFrostcallerIce_DO",
        "StaffBloodcallerHeal_DO",
        "StaffBloodcallerShotgun_DO",
        "StaffDeathcallerBlaster_DO",
        "StaffFirecallerBlaster_DO",
        "StaffFirecallerRing_DO",
        "StaffFrostcallerShotgun_DO",
        "StaffLightcallerHeal_DO",
        "StaffLightcallerSphere_DO",
        "StaffMushroomcallerMushroom_DO",
        "StaffStonecallerBurst_DO",
        "StaffStormcallerShocker_DO",
        "StaffStormcallerSphere_DO",
        "StaffTotemcallerBoomerang_DO",
        "StaffTotemcallerTotem_DO",
        "StaffWindcallerBuff_DO"
    };

    private static readonly Dictionary<string, Attack> NativeSecondaryAttacks =
        new(StringComparer.OrdinalIgnoreCase);

    private static bool _pluginPresenceResolved;
    private static bool _pluginLoaded;
    private static bool _assetNamesResolved;
    private static bool _compatibilityLogged;
    private static bool _assetDiscoveryWarningLogged;

    internal static void PrepareForApply(ObjectDB objectDb)
    {
        if (objectDb == null || !IsPluginLoaded())
        {
            return;
        }

        ResolveOwnedPrefabNames();
        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null || !OwnedPrefabNames.Contains(itemPrefab.name))
            {
                continue;
            }

            ItemDrop? itemDrop = itemPrefab.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData? sharedData = itemDrop?.m_itemData?.m_shared;
            if (sharedData == null)
            {
                continue;
            }

            if (NativeSecondaryAttacks.TryGetValue(itemPrefab.name, out Attack nativeSecondaryAttack))
            {
                sharedData.m_secondaryAttack = SecondaryAttackManager.CloneAttack(nativeSecondaryAttack);
                continue;
            }

            if (HasUsableSecondaryAttack(sharedData.m_secondaryAttack))
            {
                NativeSecondaryAttacks[itemPrefab.name] =
                    SecondaryAttackManager.CloneAttack(sharedData.m_secondaryAttack);
            }
        }
    }

    internal static bool ShouldPreserveNativeSecondary(string prefabName, ItemDrop itemDrop)
    {
        if (!IsPluginLoaded() ||
            string.IsNullOrWhiteSpace(prefabName) ||
            !OwnedPrefabNames.Contains(prefabName))
        {
            return false;
        }

        if (NativeSecondaryAttacks.TryGetValue(prefabName, out Attack nativeSecondaryAttack))
        {
            return HasUsableSecondaryAttack(nativeSecondaryAttack);
        }

        return HasUsableSecondaryAttack(itemDrop?.m_itemData?.m_shared?.m_secondaryAttack);
    }

    private static bool HasUsableSecondaryAttack(Attack? attack)
    {
        return attack != null && !string.IsNullOrWhiteSpace(attack.m_attackAnimation);
    }

    private static bool IsPluginLoaded()
    {
        if (!_pluginPresenceResolved)
        {
            _pluginLoaded = Chainloader.PluginInfos.ContainsKey(PluginGuid);
            _pluginPresenceResolved = true;
        }

        return _pluginLoaded;
    }

    private static void ResolveOwnedPrefabNames()
    {
        if (_assetNamesResolved ||
            !Chainloader.PluginInfos.TryGetValue(PluginGuid, out BepInEx.PluginInfo pluginInfo))
        {
            return;
        }

        try
        {
            Type pluginType = pluginInfo.Instance.GetType();
            FieldInfo? assetBundleField = pluginType.GetField(
                "MainAssetBundle",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (assetBundleField?.GetValue(null) is not AssetBundle assetBundle)
            {
                return;
            }

            foreach (string assetName in assetBundle.GetAllAssetNames())
            {
                string normalizedAssetName = assetName.Replace('\\', '/');
                int slashIndex = normalizedAssetName.LastIndexOf('/');
                string fileName = slashIndex >= 0
                    ? normalizedAssetName.Substring(slashIndex + 1)
                    : normalizedAssetName;
                if (!fileName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                OwnedPrefabNames.Add(fileName.Substring(0, fileName.Length - ".prefab".Length));
            }

            _assetNamesResolved = true;
            if (!_compatibilityLogged)
            {
                _compatibilityLogged = true;
                SecondaryAttacksPlugin.ModLogger.LogInfo(
                    "Magic Supremacy compatibility enabled: native secondary attacks are preserved unless a prefab has an explicit SecondaryAttacks YAML override.");
            }
        }
        catch (Exception exception)
        {
            _assetNamesResolved = true;
            if (!_assetDiscoveryWarningLogged)
            {
                _assetDiscoveryWarningLogged = true;
                SecondaryAttacksPlugin.ModLogger.LogWarning(
                    $"Magic Supremacy asset discovery failed; using the built-in 3.1.1 weapon list. {exception.Message}");
            }
        }
    }
}
