using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class SummonQualityHudSystem
{
    private const int MaxExtendedStars = 9;
    private const float StarSpacing = 16f;
    private const int MissingTagRefreshFrames = 60;
    private static readonly Dictionary<int, HudLevelGroup> ActiveGroups = new();
    private static readonly Dictionary<int, CachedTag> TagCache = new();
    private static readonly HashSet<int> RemoveBuffer = new();
    private static readonly HashSet<int> VisibleCharacters = new();
    private static readonly List<int> TagRemoveBuffer = new();

    internal static void Update(EnemyHud enemyHud)
    {
        if (enemyHud?.m_huds == null)
        {
            HideAllOwnedGroups();
            return;
        }

        RemoveBuffer.Clear();
        VisibleCharacters.Clear();
        foreach (int instanceId in ActiveGroups.Keys)
        {
            RemoveBuffer.Add(instanceId);
        }

        foreach ((Character character, EnemyHud.HudData hudData) in enemyHud.m_huds)
        {
            if (character == null || hudData?.m_gui == null)
            {
                continue;
            }

            int instanceId = character.GetInstanceID();
            VisibleCharacters.Add(instanceId);
            RemoveBuffer.Remove(instanceId);
            UpdateHud(character, hudData, instanceId);
        }

        foreach (int instanceId in RemoveBuffer)
        {
            DestroyOwnedGroup(instanceId);
        }

        PruneTagCache();
    }

    private static void UpdateHud(Character character, EnemyHud.HudData hudData, int instanceId)
    {
        SummonQualityPresetTag? tag = ResolveCachedTag(character, instanceId);
        int level = tag != null && tag.UsesLevelByQuality
            ? Mathf.Max(character.GetLevel(), tag.SummonLevel)
            : character.GetLevel();
        int starCount = Mathf.Clamp(level - 1, 0, MaxExtendedStars);
        RectTransform? level3 = hudData.m_level3;
        if (tag == null || !tag.UsesLevelByQuality || starCount <= 2 || level3 == null)
        {
            DestroyOwnedGroup(instanceId);
            return;
        }

        GameObject? group = GetOrCreateLevelGroup(instanceId, level, starCount, hudData, level3);
        if (group == null)
        {
            DestroyOwnedGroup(instanceId);
            return;
        }

        if (hudData.m_level2 != null)
        {
            hudData.m_level2.gameObject.SetActive(false);
        }

        level3.gameObject.SetActive(false);
        group.SetActive(true);
    }

    private static SummonQualityPresetTag? ResolveCachedTag(Character character, int instanceId)
    {
        if (TagCache.TryGetValue(instanceId, out CachedTag? cached) &&
            cached.Character == character &&
            (cached.Tag != null || Time.frameCount - cached.LastCheckedFrame < MissingTagRefreshFrames))
        {
            return cached.Tag;
        }

        SummonQualityPresetTag? tag = character.GetComponent<SummonQualityPresetTag>();
        TagCache[instanceId] = new CachedTag(character, tag, Time.frameCount);
        return tag;
    }

    private static void PruneTagCache()
    {
        TagRemoveBuffer.Clear();
        foreach (int instanceId in TagCache.Keys)
        {
            if (!VisibleCharacters.Contains(instanceId))
            {
                TagRemoveBuffer.Add(instanceId);
            }
        }

        foreach (int instanceId in TagRemoveBuffer)
        {
            TagCache.Remove(instanceId);
        }

        TagRemoveBuffer.Clear();
        VisibleCharacters.Clear();
    }

    private static GameObject? GetOrCreateLevelGroup(
        int instanceId,
        int level,
        int starCount,
        EnemyHud.HudData hudData,
        RectTransform level3)
    {
        Transform guiTransform = hudData.m_gui.transform;
        if (ActiveGroups.TryGetValue(instanceId, out HudLevelGroup? current) &&
            current.Level == level &&
            current.GuiTransform == guiTransform &&
            current.Group != null)
        {
            return current.Group;
        }

        DestroyOwnedGroup(instanceId);

        string levelName = $"level_{level}";
        Transform existing = guiTransform.Find(levelName);
        if (existing != null)
        {
            ActiveGroups[instanceId] = new HudLevelGroup(level, existing.gameObject, guiTransform, ownsGroup: false);
            return existing.gameObject;
        }

        GameObject group = Object.Instantiate(level3.gameObject, guiTransform);
        group.name = levelName;
        group.SetActive(false);

        if (!TryAddExtraStars(group.transform, starCount))
        {
            Object.Destroy(group);
            return null;
        }

        ActiveGroups[instanceId] = new HudLevelGroup(level, group, guiTransform, ownsGroup: true);
        return group;
    }

    private static bool TryAddExtraStars(Transform levelGroup, int starCount)
    {
        Transform? starTemplate = FindStarTemplate(levelGroup);
        if (starTemplate == null)
        {
            return false;
        }

        for (int starNumber = 3; starNumber <= starCount; starNumber++)
        {
            Transform star = Object.Instantiate(starTemplate.gameObject, levelGroup).transform;
            star.name = $"star_{starNumber}";
            star.localPosition = GetExtraStarPosition(starTemplate.localPosition, starNumber);
            star.localRotation = starTemplate.localRotation;
            star.localScale = starTemplate.localScale;
        }

        return true;
    }

    private static Transform? FindStarTemplate(Transform levelGroup)
    {
        Transform direct = levelGroup.Find("star");
        if (direct != null)
        {
            return direct;
        }

        for (int index = 0; index < levelGroup.childCount; index++)
        {
            Transform child = levelGroup.GetChild(index);
            if (child.name.StartsWith("star"))
            {
                return child;
            }
        }

        return null;
    }

    private static Vector3 GetExtraStarPosition(Vector3 templatePosition, int starNumber)
    {
        int zeroBasedOffsetFromSecondStar = starNumber - 1;
        return new Vector3(
            StarSpacing * (zeroBasedOffsetFromSecondStar % 5) - 8f,
            (zeroBasedOffsetFromSecondStar / 5) * -StarSpacing,
            templatePosition.z);
    }

    private static void DestroyOwnedGroup(int instanceId)
    {
        if (!ActiveGroups.TryGetValue(instanceId, out HudLevelGroup? group))
        {
            return;
        }

        if (group.OwnsGroup && group.Group != null)
        {
            Object.Destroy(group.Group);
        }

        ActiveGroups.Remove(instanceId);
    }

    private static void HideAllOwnedGroups()
    {
        foreach (HudLevelGroup group in ActiveGroups.Values)
        {
            if (group.OwnsGroup && group.Group != null)
            {
                group.Group.SetActive(false);
            }
        }
    }

    private sealed class HudLevelGroup
    {
        internal HudLevelGroup(int level, GameObject group, Transform guiTransform, bool ownsGroup)
        {
            Level = level;
            Group = group;
            GuiTransform = guiTransform;
            OwnsGroup = ownsGroup;
        }

        internal int Level { get; }

        internal GameObject Group { get; }

        internal Transform GuiTransform { get; }

        internal bool OwnsGroup { get; }
    }

    private sealed class CachedTag
    {
        internal CachedTag(Character character, SummonQualityPresetTag? tag, int lastCheckedFrame)
        {
            Character = character;
            Tag = tag;
            LastCheckedFrame = lastCheckedFrame;
        }

        internal Character Character { get; }

        internal SummonQualityPresetTag? Tag { get; }

        internal int LastCheckedFrame { get; }
    }
}
