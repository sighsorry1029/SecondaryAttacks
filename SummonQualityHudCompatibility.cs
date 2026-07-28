using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SummonQualityHudCompatibility
{
    private const string CreatureManagerLevelContentName = "CreatureManager_LevelContent";
    private const string CreatureManagerBossContentName = "CreatureManager_BossLevelContent";
    private const string CreatureManagerStarGroupName = "CreatureManager_StarGroup";
    private const string CreatureLevelControlLevelPrefix = "level_";
    private const string StarLevelSystemHighLevelName = "SLS_level_n";
    private static readonly string[] StarLevelSystemLevelNames =
    {
        "SLS_level_2",
        "SLS_level_3",
        "SLS_level_4",
        "SLS_level_5",
        "SLS_level_6"
    };
    private static bool _initialized;
    private static bool _creatureManagerLoaded;
    private static bool _creatureLevelControlLoaded;
    private static bool _starLevelSystemLoaded;

    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _creatureManagerLoaded = Chainloader.PluginInfos.ContainsKey(SecondaryAttacksPlugin.CreatureManagerGuid);
        _creatureLevelControlLoaded =
            Chainloader.PluginInfos.ContainsKey(SecondaryAttacksPlugin.CreatureLevelControlGuid);
        _starLevelSystemLoaded =
            Chainloader.PluginInfos.ContainsKey(SecondaryAttacksPlugin.StarLevelSystemGuid);

        List<string> providers = new();
        if (_creatureManagerLoaded)
        {
            providers.Add("CreatureManager");
        }

        if (_creatureLevelControlLoaded)
        {
            providers.Add("Creature Level & Loot Control");
        }

        if (_starLevelSystemLoaded)
        {
            providers.Add("StarLevelSystem");
        }

        if (providers.Count > 0)
        {
            SecondaryAttacksPlugin.ModLogger.LogInfo(
                $"Automatic summon star HUD compatibility enabled for: {string.Join(", ", providers)}.");
        }
    }

    internal static bool HasActiveExternalStarHud(EnemyHud.HudData hudData)
    {
        Initialize();
        if (hudData?.m_gui == null)
        {
            return false;
        }

        Transform hudRoot = hudData.m_gui.transform;
        return _creatureManagerLoaded && HasActiveCreatureManagerStarHud(hudData) ||
               _creatureLevelControlLoaded && HasActiveCreatureLevelControlStarHud(hudRoot) ||
               _starLevelSystemLoaded && HasActiveStarLevelSystemStarHud(hudRoot);
    }

    private static bool HasActiveCreatureManagerStarHud(EnemyHud.HudData hudData)
    {
        Transform parent = GetCreatureManagerHudContentParent(hudData);
        return HasActiveCreatureManagerStarGroup(parent, CreatureManagerLevelContentName) ||
               HasActiveCreatureManagerStarGroup(parent, CreatureManagerBossContentName);
    }

    private static bool HasActiveCreatureManagerStarGroup(Transform parent, string contentName)
    {
        Transform? content = parent.Find(contentName);
        if (content == null || !content.gameObject.activeSelf)
        {
            return false;
        }

        Transform? starGroup = content.Find(CreatureManagerStarGroupName);
        return starGroup != null && starGroup.gameObject.activeSelf;
    }

    private static Transform GetCreatureManagerHudContentParent(EnemyHud.HudData hudData)
    {
        RectTransform? healthBar = hudData.m_healthFast != null
            ? hudData.m_healthFast.transform as RectTransform
            : null;
        if (healthBar?.parent is RectTransform healthRoot &&
            healthRoot.parent is RectTransform healthParent)
        {
            return healthParent;
        }

        if (hudData.m_level2?.parent is RectTransform level2Parent)
        {
            return level2Parent;
        }

        if (hudData.m_level3?.parent is RectTransform level3Parent)
        {
            return level3Parent;
        }

        if (hudData.m_name?.rectTransform.parent is RectTransform nameParent)
        {
            return nameParent;
        }

        return hudData.m_gui.transform;
    }

    private static bool HasActiveCreatureLevelControlStarHud(Transform hudRoot)
    {
        for (int index = 0; index < hudRoot.childCount; index++)
        {
            Transform child = hudRoot.GetChild(index);
            if (child.gameObject.activeSelf &&
                IsCreatureLevelControlLevelGroup(child.name) &&
                child.GetComponent<SummonQualityHudMarker>() == null)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCreatureLevelControlLevelGroup(string name)
    {
        if (!name.StartsWith(CreatureLevelControlLevelPrefix, StringComparison.Ordinal) ||
            name.Length == CreatureLevelControlLevelPrefix.Length)
        {
            return false;
        }

        for (int index = CreatureLevelControlLevelPrefix.Length; index < name.Length; index++)
        {
            if (!char.IsDigit(name[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasActiveStarLevelSystemStarHud(Transform hudRoot)
    {
        if (IsSelfActive(hudRoot.Find(StarLevelSystemHighLevelName)))
        {
            return true;
        }

        foreach (string levelName in StarLevelSystemLevelNames)
        {
            if (IsSelfActive(hudRoot.Find(levelName)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSelfActive(Transform? transform)
    {
        // Provider intent remains in activeSelf while EnemyHud temporarily hides the parent on hover loss.
        return transform != null && transform.gameObject.activeSelf;
    }
}

internal sealed class SummonQualityHudMarker : MonoBehaviour
{
}
