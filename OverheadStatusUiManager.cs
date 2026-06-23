using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class OverheadStatusUiManager
{
    private const string CharacterHudTextName = "SecondaryAttacks_StatusText";
    private const float NameGap = 2f;
    private static readonly Dictionary<int, TextMeshProUGUI> ActiveTexts = new();
    private static readonly Dictionary<int, Character> TrackedCharacters = new();
    private static readonly Dictionary<int, StatusTextState> StatusStates = new();
    private static readonly List<int> RemoveBuffer = new();

    internal static void RefreshTrackedCharacter(Character? character)
    {
        if (character == null)
        {
            return;
        }

        int instanceId = character.GetInstanceID();
        if (HasDisplayStatus(character))
        {
            TrackedCharacters[instanceId] = character;
            return;
        }

        RemoveTrackedCharacter(instanceId);
    }

    internal static void Update(EnemyHud enemyHud)
    {
        if (enemyHud?.m_hudRoot == null)
        {
            return;
        }

        RemoveBuffer.Clear();
        foreach ((int instanceId, Character character) in TrackedCharacters)
        {
            if (character == null || character.IsDead() || !TryBuildStatusSnapshot(character, out StatusTextSnapshot snapshot))
            {
                RemoveBuffer.Add(instanceId);
                continue;
            }

            TextMeshProUGUI statusText = GetOrCreateStatusText(enemyHud, instanceId);
            string text = GetOrCreateStatusState(instanceId).ResolveText(snapshot);
            UpdateStatusText(enemyHud, character, statusText, text, snapshot.LineCount);
        }

        foreach (int instanceId in RemoveBuffer)
        {
            RemoveTrackedCharacter(instanceId);
        }
    }

    private static bool TryBuildStatusSnapshot(Character character, out StatusTextSnapshot snapshot)
    {
        bool hasEmpower = StaffRuntimeSystem.TryGetSummonEmpower(character, out _, out _, out float remainingTime);
        bool hasShield = StaffRuntimeSystem.TryGetDisplayedShieldRemaining(character, out float shieldRemaining);
        snapshot = new StatusTextSnapshot(
            hasEmpower,
            hasEmpower ? Mathf.CeilToInt(remainingTime) : 0,
            hasShield,
            hasShield ? Mathf.CeilToInt(shieldRemaining) : 0);
        return snapshot.HasText;
    }

    private static TextMeshProUGUI GetOrCreateStatusText(EnemyHud enemyHud, int characterInstanceId)
    {
        if (ActiveTexts.TryGetValue(characterInstanceId, out TextMeshProUGUI? existing) && existing != null)
        {
            return existing;
        }

        GameObject textObject = new($"{CharacterHudTextName}_{characterInstanceId}");
        textObject.transform.SetParent(enemyHud.m_hudRoot.transform, false);
        TextMeshProUGUI statusText = textObject.AddComponent<TextMeshProUGUI>();
        TextMeshProUGUI sourceText = enemyHud.m_baseHudPlayer.transform.Find("Name").GetComponent<TextMeshProUGUI>();
        statusText.font = sourceText.font;
        statusText.fontSharedMaterial = sourceText.fontSharedMaterial;
        statusText.fontSize = sourceText.fontSize * 0.72f;
        statusText.color = sourceText.color;
        statusText.alignment = TextAlignmentOptions.Bottom;
        statusText.textWrappingMode = TextWrappingModes.NoWrap;
        statusText.overflowMode = TextOverflowModes.Overflow;
        statusText.richText = false;
        statusText.raycastTarget = false;

        RectTransform statusRect = statusText.rectTransform;
        statusRect.anchorMin = new Vector2(0f, 0f);
        statusRect.anchorMax = new Vector2(0f, 0f);
        statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.sizeDelta = sourceText.rectTransform.sizeDelta + new Vector2(0f, 18f);

        ActiveTexts[characterInstanceId] = statusText;
        return statusText;
    }

    private static bool HasDisplayStatus(Character character)
    {
        return StaffRuntimeSystem.TryGetDisplayedShieldRemaining(character, out _) ||
               StaffRuntimeSystem.TryGetSummonEmpower(character, out _, out _, out _);
    }

    private static void UpdateStatusText(
        EnemyHud enemyHud,
        Character character,
        TextMeshProUGUI statusText,
        string text,
        int lineCount)
    {
        if (!enemyHud.m_huds.TryGetValue(character, out EnemyHud.HudData? hudData) ||
            hudData?.m_gui == null ||
            hudData.m_name == null)
        {
            UpdateFallbackStatusText(enemyHud, character, statusText, text, lineCount);
            return;
        }

        if (!hudData.m_gui.activeInHierarchy)
        {
            statusText.gameObject.SetActive(false);
            return;
        }

        RectTransform nameRect = hudData.m_name.rectTransform;
        RectTransform statusRect = statusText.rectTransform;
        float lineHeight = Mathf.Max(14f, statusText.fontSize + 2f);
        EnsureStatusParent(statusText, hudData.m_gui.transform);
        statusRect.anchorMin = nameRect.anchorMin;
        statusRect.anchorMax = nameRect.anchorMax;
        statusRect.pivot = new Vector2(0.5f, 0f);
        statusRect.sizeDelta = new Vector2(
            Mathf.Max(nameRect.rect.width, 120f),
            lineHeight * Mathf.Max(1, lineCount));

        SetStatusText(statusText, text);
        statusRect.anchoredPosition = nameRect.anchoredPosition + new Vector2(0f, GetNameTopOffset(nameRect) + NameGap);
        statusText.gameObject.SetActive(true);
    }

    private static void UpdateFallbackStatusText(
        EnemyHud enemyHud,
        Character character,
        TextMeshProUGUI statusText,
        string text,
        int lineCount)
    {
        if (character != Player.m_localPlayer)
        {
            statusText.gameObject.SetActive(false);
            return;
        }

        Camera mainCamera = Utils.GetMainCamera();
        if (!mainCamera)
        {
            statusText.gameObject.SetActive(false);
            return;
        }

        EnsureStatusParent(statusText, enemyHud.m_hudRoot.transform);
        RectTransform statusRect = statusText.rectTransform;
        float lineHeight = Mathf.Max(14f, statusText.fontSize + 2f);
        statusRect.anchorMin = new Vector2(0f, 0f);
        statusRect.anchorMax = new Vector2(0f, 0f);
        statusRect.pivot = new Vector2(0.5f, 0.5f);
        statusRect.sizeDelta = new Vector2(120f, lineHeight * Mathf.Max(1, lineCount));

        Vector3 screenPoint = mainCamera.WorldToScreenPointScaled(character.GetHeadPoint() + Vector3.up * 0.35f);
        bool visible = screenPoint.z > 0f
                       && screenPoint.x >= 0f
                       && screenPoint.x <= Screen.width
                       && screenPoint.y >= 0f
                       && screenPoint.y <= Screen.height;
        SetStatusText(statusText, text);
        statusRect.position = screenPoint;
        statusText.gameObject.SetActive(visible);
    }

    private static void SetStatusText(TextMeshProUGUI statusText, string text)
    {
        if (!string.Equals(statusText.text, text, System.StringComparison.Ordinal))
        {
            statusText.text = text;
        }
    }

    private static void EnsureStatusParent(TextMeshProUGUI statusText, Transform parent)
    {
        if (statusText.transform.parent == parent)
        {
            return;
        }

        statusText.rectTransform.SetParent(parent, false);
        statusText.rectTransform.localScale = Vector3.one;
    }

    private static float GetNameTopOffset(RectTransform nameRect)
    {
        float height = nameRect.rect.height > 0f ? nameRect.rect.height : Mathf.Max(14f, nameRect.sizeDelta.y);
        return height * (1f - nameRect.pivot.y);
    }

    private static void RemoveTrackedCharacter(int instanceId)
    {
        TrackedCharacters.Remove(instanceId);
        StatusStates.Remove(instanceId);
        if (!ActiveTexts.TryGetValue(instanceId, out TextMeshProUGUI? text))
        {
            return;
        }

        if (text != null)
        {
            Object.Destroy(text.gameObject);
        }

        ActiveTexts.Remove(instanceId);
    }

    private static void HideAll()
    {
        foreach (TextMeshProUGUI text in ActiveTexts.Values)
        {
            if (text != null)
            {
                text.gameObject.SetActive(false);
            }
        }
    }

    private static StatusTextState GetOrCreateStatusState(int instanceId)
    {
        if (!StatusStates.TryGetValue(instanceId, out StatusTextState? state))
        {
            state = new StatusTextState();
            StatusStates[instanceId] = state;
        }

        return state;
    }

    private static string BuildStatusText(StatusTextSnapshot snapshot)
    {
        string empowerLabel = SecondaryAttackLocalization.Localize(SecondaryAttackLocalization.HudEmpower, "Empower");
        if (snapshot.HasEmpower && snapshot.HasShield)
        {
            return $"{empowerLabel} {snapshot.EmpowerSeconds}s\n{snapshot.ShieldSeconds}";
        }

        if (snapshot.HasEmpower)
        {
            return $"{empowerLabel} {snapshot.EmpowerSeconds}s";
        }

        return snapshot.ShieldSeconds.ToString();
    }

    private readonly struct StatusTextSnapshot
    {
        internal StatusTextSnapshot(bool hasEmpower, int empowerSeconds, bool hasShield, int shieldSeconds)
        {
            HasEmpower = hasEmpower;
            EmpowerSeconds = empowerSeconds;
            HasShield = hasShield;
            ShieldSeconds = shieldSeconds;
        }

        internal bool HasEmpower { get; }

        internal int EmpowerSeconds { get; }

        internal bool HasShield { get; }

        internal int ShieldSeconds { get; }

        internal bool HasText => HasEmpower || HasShield;

        internal int LineCount => (HasEmpower ? 1 : 0) + (HasShield ? 1 : 0);

        internal bool Equals(StatusTextSnapshot other)
        {
            return HasEmpower == other.HasEmpower &&
                   EmpowerSeconds == other.EmpowerSeconds &&
                   HasShield == other.HasShield &&
                   ShieldSeconds == other.ShieldSeconds;
        }
    }

    private sealed class StatusTextState
    {
        private StatusTextSnapshot _snapshot;
        private bool _hasSnapshot;
        private string _text = string.Empty;

        internal string ResolveText(StatusTextSnapshot snapshot)
        {
            if (!_hasSnapshot || !_snapshot.Equals(snapshot))
            {
                _snapshot = snapshot;
                _text = BuildStatusText(snapshot);
                _hasSnapshot = true;
            }

            return _text;
        }
    }
}
