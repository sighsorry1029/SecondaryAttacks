using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using UnityEngine;

namespace SecondaryAttacks;

internal sealed class NativeSecondaryAttackCompat
{
    internal const string MagicSupremacyGuid = "Dreanegade.Magic_Supremacy";
    internal const string WizardryGuid = "Therzie.Wizardry";
    internal const string VikingsMagicGuid = "radamanto.Vikings_Magic";

    // Vikings Magic 1.1.8. Include First Oath's empty secondary so an automatic
    // override is restored before checking native ownership in another world.
    // Wands use the generic one-handed rule; books and other RDM_ items do not.
    private static readonly HashSet<string> VikingsMagicStaffNames = new(StringComparer.Ordinal)
    {
        "RDM_staff_begin",
        "RDM_staff_meadows",
        "RDM_staff_bforest",
        "RDM_staff_swamp",
        "RDM_staff_mountain",
        "RDM_staff_plains",
        "RDM_staff_mistlands",
        "RDM_staff_ashlands"
    };

    // Only retain originals while our override is applied. Unmodified native
    // attacks stay owned by their mod, including its live configuration updates.
    private static readonly ConditionalWeakTable<ItemDrop.ItemData.SharedData, Attack> MagicWeaponOverrides = new();

    // Magic Supremacy 3.1.1 weapon prefabs. Asset-bundle discovery below also
    // picks up later additions without creating a compile-time dependency.
    private static readonly NativeSecondaryAttackCompat MagicSupremacy = new(
        MagicSupremacyGuid, "Magic Supremacy", "3.1.1", new[]
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
    });

    // Wizardry 1.2.0 staves, including bases and summon staves with no secondary.
    // Its own ItemManager registry discovers additional items without matching
    // the shared _TW suffix used by other Therzie mods.
    private static readonly NativeSecondaryAttackCompat Wizardry = new(
        WizardryGuid, "Wizardry", "1.2.0", new[]
    {
        "StaffBlackforest_TW",
        "StaffSwamp_TW",
        "StaffMountain_TW",
        "StaffPlains_TW",
        "StaffMistlands_TW",
        "StaffSurtling_TW",
        "StaffGolem_TW",
        "Staffbase_BlackForest_TW",
        "Staffbase_Swamp_TW",
        "Staffbase_Mountain_TW",
        "Staffbase_Plains_TW",
        "Staffbase_Mistlands_TW"
    });

    private readonly string _pluginGuid;
    private readonly string _modName;
    private readonly string _fallbackVersion;
    private readonly HashSet<string> _ownedPrefabNames;
    private readonly Dictionary<string, Attack> _nativeSecondaryAttacks =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _pluginPresenceResolved;
    private bool _pluginLoaded;
    private bool _assetNamesResolved;

    private NativeSecondaryAttackCompat(string pluginGuid, string modName, string fallbackVersion, string[] prefabNames)
    {
        _pluginGuid = pluginGuid;
        _modName = modName;
        _fallbackVersion = fallbackVersion;
        _ownedPrefabNames = new HashSet<string>(prefabNames, StringComparer.OrdinalIgnoreCase);
    }

    internal static void PrepareForApply(ObjectDB objectDb)
    {
        MagicSupremacy.RestoreNativeAttacks(objectDb);
        Wizardry.RestoreNativeAttacks(objectDb);

        if (objectDb == null)
        {
            return;
        }

        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null)
            {
                continue;
            }

            ItemDrop.ItemData.SharedData? sharedData = itemPrefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared;
            if (sharedData == null)
            {
                continue;
            }

            if (MagicWeaponOverrides.TryGetValue(sharedData, out Attack? original))
            {
                sharedData.m_secondaryAttack = SecondaryAttackManager.CloneAttack(original);
                MagicWeaponOverrides.Remove(sharedData);
            }

            MagicPluginCompat.RefreshNativeSecondaryCost(itemPrefab.name, sharedData.m_secondaryAttack);
        }
    }

    private static bool PreservesNativeMagicAttack(string prefabName, ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData != null &&
               (SecondaryAttackWeaponFamilyResolver.IsOneHandedElementalProjectileWeapon(sharedData) ||
                SecondaryAttackWeaponFamilyResolver.IsOffensiveBloodMagicStaff(sharedData) ||
                (VikingsMagicStaffNames.Contains(prefabName) && Chainloader.PluginInfos.ContainsKey(VikingsMagicGuid)));
    }

    internal static void CaptureMagicWeaponOverride(string prefabName, ItemDrop itemDrop)
    {
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        if (PreservesNativeMagicAttack(prefabName, sharedData))
        {
            // Keep this independent of ObjectDB: worlds can reuse the same prefab.
            MagicWeaponOverrides.GetValue(sharedData!, shared => SecondaryAttackManager.CloneAttack(shared.m_secondaryAttack));
        }
    }

    internal static bool ShouldPreserveNativeSecondary(string prefabName, ItemDrop itemDrop)
    {
        if (itemDrop == null || string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        return (PreservesNativeMagicAttack(prefabName, itemDrop.m_itemData?.m_shared) &&
                HasUsableSecondaryAttack(itemDrop.m_itemData?.m_shared?.m_secondaryAttack)) ||
               MagicSupremacy.HasNativeSecondary(prefabName, itemDrop) ||
               Wizardry.HasNativeSecondary(prefabName, itemDrop);
    }

    private void RestoreNativeAttacks(ObjectDB objectDb)
    {
        if (objectDb == null || !IsPluginLoaded())
        {
            return;
        }

        ResolveOwnedPrefabNames();
        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null || !_ownedPrefabNames.Contains(itemPrefab.name))
            {
                continue;
            }

            ItemDrop? itemDrop = itemPrefab.GetComponent<ItemDrop>();
            ItemDrop.ItemData.SharedData? sharedData = itemDrop?.m_itemData?.m_shared;
            if (sharedData == null)
            {
                continue;
            }

            if (_nativeSecondaryAttacks.TryGetValue(itemPrefab.name, out Attack? nativeSecondaryAttack))
            {
                sharedData.m_secondaryAttack = SecondaryAttackManager.CloneAttack(nativeSecondaryAttack);
                continue;
            }

            // ObjectDBs can share the same prefab across world changes. Capture
            // even an empty attack, so our fallback cannot later become "native".
            _nativeSecondaryAttacks[itemPrefab.name] =
                SecondaryAttackManager.CloneAttack(sharedData.m_secondaryAttack);
        }
    }

    private bool HasNativeSecondary(string prefabName, ItemDrop itemDrop)
    {
        if (!IsPluginLoaded() ||
            string.IsNullOrWhiteSpace(prefabName) ||
            !_ownedPrefabNames.Contains(prefabName))
        {
            return false;
        }

        if (_nativeSecondaryAttacks.TryGetValue(prefabName, out Attack? nativeSecondaryAttack))
        {
            return HasUsableSecondaryAttack(nativeSecondaryAttack);
        }

        return HasUsableSecondaryAttack(itemDrop?.m_itemData?.m_shared?.m_secondaryAttack);
    }

    private static bool HasUsableSecondaryAttack(Attack? attack)
    {
        return attack != null && !string.IsNullOrWhiteSpace(attack.m_attackAnimation);
    }

    private bool IsPluginLoaded()
    {
        if (!_pluginPresenceResolved)
        {
            _pluginLoaded = Chainloader.PluginInfos.ContainsKey(_pluginGuid);
            _pluginPresenceResolved = true;
        }

        return _pluginLoaded;
    }

    private void ResolveOwnedPrefabNames()
    {
        if (_assetNamesResolved ||
            !Chainloader.PluginInfos.TryGetValue(_pluginGuid, out BepInEx.PluginInfo? pluginInfo) ||
            pluginInfo == null)
        {
            return;
        }

        try
        {
            Type pluginType = pluginInfo.Instance.GetType();
            if (_pluginGuid == WizardryGuid)
            {
                if (!DiscoverRegisteredPrefabs(pluginType, _ownedPrefabNames))
                {
                    return;
                }
            }
            else
            {
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
                    if (fileName.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    {
                        _ownedPrefabNames.Add(fileName.Substring(0, fileName.Length - ".prefab".Length));
                    }
                }
            }

            _assetNamesResolved = true;
            SecondaryAttacksPlugin.ModLogger.LogInfo(
                $"{_modName} compatibility enabled: native secondary attacks are preserved unless a prefab has an explicit SecondaryAttacks YAML override.");
        }
        catch (Exception exception)
        {
            _assetNamesResolved = true;
            SecondaryAttacksPlugin.ModLogger.LogWarning(
                $"{_modName} prefab discovery failed; using the built-in {_fallbackVersion} weapon list. {exception.Message}");
        }
    }

    internal static bool DiscoverRegisteredPrefabs(Type pluginType, ISet<string> ownedPrefabNames)
    {
        // This private registry belongs to the selected plugin assembly,
        // not another mod's bundled ItemManager. Only inspect it after plugin Awake.
        Type? prefabManagerType = pluginType.Assembly.GetType("ItemManager.PrefabManager");
        FieldInfo? prefabsField = prefabManagerType?.GetField("prefabs", BindingFlags.Static | BindingFlags.NonPublic);
        if (prefabsField == null)
        {
            throw new MissingFieldException($"{pluginType.FullName}: ItemManager.PrefabManager.prefabs was not found.");
        }

        if (prefabsField.GetValue(null) is not IEnumerable<GameObject> prefabs)
        {
            throw new InvalidOperationException($"{pluginType.FullName}: registered prefab list has an unexpected type.");
        }

        bool foundPrefab = false;
        foreach (GameObject prefab in prefabs)
        {
            if (prefab != null)
            {
                ownedPrefabNames.Add(prefab.name);
                foundPrefab = true;
            }
        }

        return foundPrefab;
    }
}
