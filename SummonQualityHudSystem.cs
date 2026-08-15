using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class SummonQualityHudSystem
{
    private const int MaxExtendedStars = 9;
    private const float StarSpacing = 16f;
    private const string LifetimeTextName = "SecondaryAttacks_SummonLifetimeText";
    private const float LifetimeTextWidth = 64f;
    private const float LifetimeTextMinimumHeight = 18f;
    private const int MissingTagRefreshFrames = 60;
    private static readonly string[] CreatureManagerContentNames =
    {
        "CreatureManager_LevelContent",
        "CreatureManager_BossLevelContent"
    };
    private static readonly Dictionary<int, HudLevelGroup> ActiveGroups = new();
    private static readonly Dictionary<int, HudLifetimeText> ActiveLifetimeTexts = new();
    private static readonly Dictionary<int, CachedTag> TagCache = new();
    private static readonly HashSet<int> RemoveBuffer = new();
    private static readonly HashSet<int> VisibleCharacters = new();
    private static readonly List<int> TagRemoveBuffer = new();
    private static readonly Vector3[] HealthCorners = new Vector3[4];
    private static readonly Vector3[] RowCorners = new Vector3[4];

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

        foreach (int instanceId in ActiveLifetimeTexts.Keys)
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
            UpdateHud(enemyHud, character, hudData, instanceId);
        }

        foreach (int instanceId in RemoveBuffer)
        {
            DestroyOwnedGroup(instanceId);
            DestroyLifetimeText(instanceId);
        }

        PruneTagCache();
    }

    private static void UpdateHud(
        EnemyHud enemyHud,
        Character character,
        EnemyHud.HudData hudData,
        int instanceId)
    {
        UpdateQualityHud(character, hudData, instanceId);
        UpdateLifetimeHud(enemyHud, character, hudData, instanceId);
    }

    private static void UpdateQualityHud(Character character, EnemyHud.HudData hudData, int instanceId)
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

        if (SummonQualityHudCompatibility.HasActiveExternalStarHud(hudData))
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

    private static void UpdateLifetimeHud(
        EnemyHud enemyHud,
        Character character,
        EnemyHud.HudData hudData,
        int instanceId)
    {
        if (!MagicSummonQualityPresetSystem.TryGetSummonLifetime(character, out float remainingSeconds))
        {
            DestroyLifetimeText(instanceId);
            return;
        }

        RectTransform? healthRoot = GetHealthRoot(hudData);
        RectTransform? contentParent = GetHudContentParent(hudData, healthRoot);
        RectTransform? row = FindLifetimeRow(character, hudData, instanceId, contentParent);
        TextMeshProUGUI? sourceText = hudData.m_name != null
            ? hudData.m_name
            : enemyHud.m_baseHudPlayer?.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
        if (healthRoot == null || contentParent == null || row == null || sourceText?.font == null)
        {
            DestroyLifetimeText(instanceId);
            return;
        }

        HudLifetimeText? lifetimeText = GetOrCreateLifetimeText(
            instanceId,
            hudData.m_gui.transform,
            contentParent,
            sourceText);
        if (lifetimeText == null)
        {
            DestroyLifetimeText(instanceId);
            return;
        }

        long displayedSeconds = GetDisplayedSeconds(remainingSeconds);
        if (lifetimeText.DisplayedSeconds != displayedSeconds)
        {
            lifetimeText.Text.text = displayedSeconds.ToString(CultureInfo.InvariantCulture) + "s";
            lifetimeText.DisplayedSeconds = displayedSeconds;
        }

        PositionLifetimeText(lifetimeText.Text.rectTransform, contentParent, healthRoot, row);
        lifetimeText.Text.gameObject.SetActive(true);
    }

    private static long GetDisplayedSeconds(float remainingSeconds)
    {
        if (float.IsNaN(remainingSeconds) || remainingSeconds <= 0f)
        {
            return 0L;
        }

        if (float.IsPositiveInfinity(remainingSeconds) || remainingSeconds >= long.MaxValue)
        {
            return long.MaxValue;
        }

        return (long)Math.Ceiling(remainingSeconds);
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

        // CLLC owns level_N groups; the fallback must never take ownership of those external objects.
        string levelName = $"SecondaryAttacks_level_{level}";
        Transform existing = guiTransform.Find(levelName);
        if (existing != null)
        {
            if (existing.GetComponent<SummonQualityHudMarker>() == null)
            {
                return null;
            }

            ActiveGroups[instanceId] = new HudLevelGroup(level, existing.gameObject, guiTransform);
            return existing.gameObject;
        }

        GameObject group = Object.Instantiate(level3.gameObject, guiTransform);
        group.name = levelName;
        group.AddComponent<SummonQualityHudMarker>();
        group.SetActive(false);

        if (!TryAddExtraStars(group.transform, starCount))
        {
            Object.Destroy(group);
            return null;
        }

        ActiveGroups[instanceId] = new HudLevelGroup(level, group, guiTransform);
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

    private static RectTransform? GetHealthRoot(EnemyHud.HudData hudData)
    {
        RectTransform? healthBar = hudData.m_healthFast != null
            ? hudData.m_healthFast.transform as RectTransform
            : null;
        return healthBar?.parent as RectTransform;
    }

    private static RectTransform? GetHudContentParent(
        EnemyHud.HudData hudData,
        RectTransform? healthRoot)
    {
        if (healthRoot?.parent is RectTransform healthParent)
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

        return hudData.m_gui.transform as RectTransform;
    }

    private static RectTransform? FindLifetimeRow(
        Character character,
        EnemyHud.HudData hudData,
        int instanceId,
        RectTransform? contentParent)
    {
        Transform hudRoot = hudData.m_gui.transform;
        if (SummonQualityHudCompatibility.HasActiveExternalStarHud(hudData))
        {
            RectTransform? externalRow = FindActiveCreatureManagerRow(contentParent) ??
                                         FindActiveRect(hudRoot, $"SLS_level_{character.GetLevel()}") ??
                                         FindActiveRect(hudRoot, "SLS_level_n") ??
                                         FindActiveExternalLevelRow(hudRoot, character.GetLevel());
            if (externalRow != null)
            {
                return externalRow;
            }
        }

        if (ActiveGroups.TryGetValue(instanceId, out HudLevelGroup? ownedGroup) &&
            ownedGroup.Group != null &&
            ownedGroup.Group.activeSelf &&
            ownedGroup.Group.transform is RectTransform ownedRow)
        {
            return ownedRow;
        }

        int level = character.GetLevel();
        if (level == 2 && hudData.m_level2 != null && hudData.m_level2.gameObject.activeSelf)
        {
            return hudData.m_level2;
        }

        if (level >= 3 && hudData.m_level3 != null && hudData.m_level3.gameObject.activeSelf)
        {
            return hudData.m_level3;
        }

        // Level-one summons have no visible star block. The inactive vanilla block still
        // provides the correct baseline for the row where stars would otherwise appear.
        return hudData.m_level2 ?? hudData.m_level3;
    }

    private static RectTransform? FindActiveCreatureManagerRow(Transform? contentParent)
    {
        foreach (string contentName in CreatureManagerContentNames)
        {
            Transform? content = contentParent?.Find(contentName);
            Transform? starGroup = content?.Find("CreatureManager_StarGroup");
            if (content != null &&
                content.gameObject.activeSelf &&
                starGroup != null &&
                starGroup.gameObject.activeSelf)
            {
                return content as RectTransform;
            }
        }

        return null;
    }

    private static RectTransform? FindActiveExternalLevelRow(Transform hudRoot, int level)
    {
        RectTransform? exactLevel = FindActiveRect(hudRoot, $"level_{level}");
        if (exactLevel != null && exactLevel.GetComponent<SummonQualityHudMarker>() == null)
        {
            return exactLevel;
        }

        for (int index = 0; index < hudRoot.childCount; index++)
        {
            Transform child = hudRoot.GetChild(index);
            if (!child.gameObject.activeSelf ||
                child.GetComponent<SummonQualityHudMarker>() != null ||
                child is not RectTransform rect ||
                !IsNumericLevelName(child.name))
            {
                continue;
            }

            return rect;
        }

        return null;
    }

    private static bool IsNumericLevelName(string name)
    {
        const string prefix = "level_";
        if (!name.StartsWith(prefix) || name.Length == prefix.Length)
        {
            return false;
        }

        for (int index = prefix.Length; index < name.Length; index++)
        {
            if (!char.IsDigit(name[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static RectTransform? FindActiveRect(Transform? parent, string name)
    {
        Transform? child = parent?.Find(name);
        return child != null && child.gameObject.activeSelf
            ? child as RectTransform
            : null;
    }

    private static HudLifetimeText? GetOrCreateLifetimeText(
        int instanceId,
        Transform guiTransform,
        RectTransform contentParent,
        TextMeshProUGUI sourceText)
    {
        if (ActiveLifetimeTexts.TryGetValue(instanceId, out HudLifetimeText? current) &&
            current.GuiTransform == guiTransform &&
            current.Text != null)
        {
            if (current.Text.transform.parent != contentParent)
            {
                current.Text.rectTransform.SetParent(contentParent, false);
            }

            return current;
        }

        DestroyLifetimeText(instanceId);

        Transform existing = contentParent.Find(LifetimeTextName);
        if (existing != null)
        {
            TextMeshProUGUI? existingText = existing.GetComponent<TextMeshProUGUI>();
            if (existing.GetComponent<SummonLifetimeHudMarker>() == null || existingText == null)
            {
                return null;
            }

            HudLifetimeText recovered = new(existingText, guiTransform);
            ActiveLifetimeTexts[instanceId] = recovered;
            return recovered;
        }

        GameObject textObject = new(LifetimeTextName, typeof(RectTransform));
        textObject.SetActive(false);
        RectTransform textRect = (RectTransform)textObject.transform;
        textRect.SetParent(contentParent, false);
        textObject.AddComponent<SummonLifetimeHudMarker>();
        LayoutElement layoutElement = textObject.AddComponent<LayoutElement>();
        layoutElement.ignoreLayout = true;

        TextMeshProUGUI lifetimeText = textObject.AddComponent<TextMeshProUGUI>();
        lifetimeText.font = sourceText.font;
        lifetimeText.fontSharedMaterial = sourceText.fontSharedMaterial;
        lifetimeText.fontSize = Mathf.Max(10f, sourceText.fontSize * 0.72f);
        lifetimeText.fontStyle = sourceText.fontStyle;
        lifetimeText.color = sourceText.color;
        lifetimeText.alignment = TextAlignmentOptions.MidlineRight;
        lifetimeText.textWrappingMode = TextWrappingModes.NoWrap;
        lifetimeText.overflowMode = TextOverflowModes.Overflow;
        lifetimeText.richText = false;
        lifetimeText.raycastTarget = false;
        lifetimeText.text = string.Empty;

        textRect.pivot = new Vector2(1f, 0.5f);
        textRect.sizeDelta = new Vector2(
            LifetimeTextWidth,
            Mathf.Max(LifetimeTextMinimumHeight, lifetimeText.fontSize + 4f));
        textRect.localScale = Vector3.one;
        EnsureLastSibling(textRect);

        HudLifetimeText created = new(lifetimeText, guiTransform);
        ActiveLifetimeTexts[instanceId] = created;
        return created;
    }

    private static void PositionLifetimeText(
        RectTransform textRect,
        RectTransform contentParent,
        RectTransform healthRoot,
        RectTransform row)
    {
        healthRoot.GetWorldCorners(HealthCorners);
        row.GetWorldCorners(RowCorners);

        Vector3 healthRightWorld = (HealthCorners[2] + HealthCorners[3]) * 0.5f;
        Vector3 rowCenterWorld = (RowCorners[0] + RowCorners[2]) * 0.5f;
        Vector3 healthRightLocal = contentParent.InverseTransformPoint(healthRightWorld);
        Vector3 rowCenterLocal = contentParent.InverseTransformPoint(rowCenterWorld);

        Vector2 parentPivot = contentParent.pivot;
        textRect.anchorMin = parentPivot;
        textRect.anchorMax = parentPivot;
        textRect.pivot = new Vector2(1f, 0.5f);
        textRect.anchoredPosition = new Vector2(healthRightLocal.x, rowCenterLocal.y);
        textRect.localScale = Vector3.one;
        EnsureLastSibling(textRect);
    }

    private static void EnsureLastSibling(RectTransform rect)
    {
        if (rect.GetSiblingIndex() != rect.parent.childCount - 1)
        {
            rect.SetAsLastSibling();
        }
    }

    private static void DestroyLifetimeText(int instanceId)
    {
        if (!ActiveLifetimeTexts.TryGetValue(instanceId, out HudLifetimeText? lifetimeText))
        {
            return;
        }

        if (lifetimeText.Text != null)
        {
            lifetimeText.Text.gameObject.SetActive(false);
            Object.Destroy(lifetimeText.Text.gameObject);
        }

        ActiveLifetimeTexts.Remove(instanceId);
    }

    private static void DestroyOwnedGroup(int instanceId)
    {
        if (!ActiveGroups.TryGetValue(instanceId, out HudLevelGroup? group))
        {
            return;
        }

        if (group.Group != null)
        {
            group.Group.SetActive(false);
            Object.Destroy(group.Group);
        }

        ActiveGroups.Remove(instanceId);
    }

    private static void HideAllOwnedGroups()
    {
        foreach (HudLevelGroup group in ActiveGroups.Values)
        {
            if (group.Group != null)
            {
                group.Group.SetActive(false);
            }
        }

        foreach (HudLifetimeText lifetimeText in ActiveLifetimeTexts.Values)
        {
            if (lifetimeText.Text != null)
            {
                lifetimeText.Text.gameObject.SetActive(false);
            }
        }
    }

    private sealed class HudLevelGroup
    {
        internal HudLevelGroup(int level, GameObject group, Transform guiTransform)
        {
            Level = level;
            Group = group;
            GuiTransform = guiTransform;
        }

        internal int Level { get; }

        internal GameObject Group { get; }

        internal Transform GuiTransform { get; }

    }

    private sealed class HudLifetimeText
    {
        internal HudLifetimeText(TextMeshProUGUI text, Transform guiTransform)
        {
            Text = text;
            GuiTransform = guiTransform;
        }

        internal TextMeshProUGUI Text { get; }

        internal Transform GuiTransform { get; }

        internal long DisplayedSeconds { get; set; } = -1L;
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

internal sealed class SummonLifetimeHudMarker : MonoBehaviour
{
}
