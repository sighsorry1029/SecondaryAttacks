using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SecondaryAttacks;

internal static class SecondaryCooldownHudSystem
{
    private const int MaxSlots = 10;
    private const int Columns = 5;
    private const int Rows = 2;
    private const float SlotSize = 34f;
    private const float SlotSpacing = 4f;
    private static readonly Color FilledBackgroundColor = new(0f, 0f, 0f, 0.48f);
    private static readonly Color EmptyBackgroundColor = new(0f, 0f, 0f, 0.2f);
    private static readonly List<Entry> Entries = [];
    private static readonly List<CooldownSlot> Slots = [];
    private static GameObject? RootObject;
    private static RectTransform? RootRect;
    private static TMP_Text? FontSource;

    internal static bool IsEnabled =>
        SecondaryAttacksPlugin.SecondaryCooldownHudEnabled.Value == SecondaryAttacksPlugin.Toggle.On;

    internal static bool ShouldSuppressStatusEffects => IsEnabled;

    internal static bool ShouldSuppressCooldownStatusEffects(Character? character)
    {
        return IsEnabled && character != null && character == Player.m_localPlayer;
    }

    internal static void Update(Player? player)
    {
        if (!IsEnabled || player == null || player != Player.m_localPlayer || Hud.instance == null || Hud.instance.m_rootObject == null)
        {
            Hide();
            return;
        }

        EnsureHud();
        if (RootObject == null || RootRect == null)
        {
            return;
        }

        bool previewMode = InventoryGui.IsVisible();
        Entries.Clear();
        SneakAmbushChargeSystem.CollectHudEntries(player, Entries);
        MeleePresetCooldownSystem.CollectHudEntries(player, Entries);
        RangedSecondaryCooldownSystem.CollectHudEntries(player, Entries);
        Entries.Sort((left, right) =>
        {
            int modeCompare = left.SortOrder.CompareTo(right.SortOrder);
            return modeCompare != 0 ? modeCompare : left.Remaining.CompareTo(right.Remaining);
        });

        bool visible = previewMode || Entries.Count > 0;
        RootObject.SetActive(visible);
        if (!visible)
        {
            return;
        }

        ApplyConfiguredTransform();
        if (previewMode)
        {
            RootObject.transform.SetAsLastSibling();
        }

        UpdateSlots(previewMode);
    }

    internal static void Hide()
    {
        if (RootObject != null)
        {
            RootObject.SetActive(false);
        }
    }

    private static void EnsureHud()
    {
        if (RootObject != null && RootRect != null && AreSlotsValid())
        {
            return;
        }

        ResetHudReferences();

        FontSource = ResolveFontSource();
        if (FontSource == null)
        {
            return;
        }

        GameObject root = new("SecondaryAttacks_CooldownHud");
        root.SetActive(false);
        root.transform.SetParent(Hud.instance.m_rootObject.transform, false);
        RootObject = root;
        RootRect = root.AddComponent<RectTransform>();
        RootRect.anchorMin = Vector2.zero;
        RootRect.anchorMax = Vector2.zero;
        RootRect.pivot = new Vector2(0.5f, 0.5f);
        float width = Columns * SlotSize + (Columns - 1) * SlotSpacing;
        float height = Rows * SlotSize + (Rows - 1) * SlotSpacing;
        RootRect.sizeDelta = new Vector2(width, height);

        GridLayoutGroup grid = root.AddComponent<GridLayoutGroup>();
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;
        grid.cellSize = new Vector2(SlotSize, SlotSize);
        grid.spacing = new Vector2(SlotSpacing, SlotSpacing);
        grid.childAlignment = TextAnchor.UpperLeft;

        for (int index = 0; index < MaxSlots; index++)
        {
            Slots.Add(CreateSlot(root.transform, index));
        }

        root.SetActive(false);
    }

    private static void ResetHudReferences()
    {
        if (RootObject != null)
        {
            Object.Destroy(RootObject);
        }

        RootObject = null;
        RootRect = null;
        FontSource = null;
        Slots.Clear();
    }

    private static TMP_Text? ResolveFontSource()
    {
        if (HasFont(Hud.instance.m_hoverName))
        {
            return Hud.instance.m_hoverName;
        }

        if (HasFont(Hud.instance.m_pieceDescription))
        {
            return Hud.instance.m_pieceDescription;
        }

        TMP_Text[] sources = Hud.instance.m_rootObject.GetComponentsInChildren<TMP_Text>(true);
        foreach (TMP_Text source in sources)
        {
            if (HasFont(source))
            {
                return source;
            }
        }

        return null;
    }

    private static bool HasFont(TMP_Text? source)
    {
        return source != null && source.font != null;
    }

    private static CooldownSlot CreateSlot(Transform parent, int index)
    {
        GameObject slotObject = new($"CooldownSlot_{index}");
        slotObject.transform.SetParent(parent, false);
        RectTransform slotRect = slotObject.AddComponent<RectTransform>();
        slotRect.sizeDelta = new Vector2(SlotSize, SlotSize);

        Image background = slotObject.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.42f);
        background.raycastTarget = false;

        GameObject iconObject = new("Icon");
        iconObject.transform.SetParent(slotObject.transform, false);
        RectTransform iconRect = iconObject.AddComponent<RectTransform>();
        iconRect.anchorMin = new Vector2(0.5f, 0.5f);
        iconRect.anchorMax = new Vector2(0.5f, 0.5f);
        iconRect.pivot = new Vector2(0.5f, 0.5f);
        iconRect.sizeDelta = new Vector2(SlotSize - 5f, SlotSize - 5f);
        Image icon = iconObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        GameObject overlayObject = new("Overlay");
        overlayObject.transform.SetParent(slotObject.transform, false);
        RectTransform overlayRect = overlayObject.AddComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        Image overlay = overlayObject.AddComponent<Image>();
        overlay.color = new Color(0f, 0f, 0f, 0.58f);
        overlay.type = Image.Type.Filled;
        overlay.fillMethod = Image.FillMethod.Radial360;
        overlay.fillOrigin = 2;
        overlay.fillClockwise = false;
        overlay.raycastTarget = false;

        GameObject textObject = new("Text");
        textObject.transform.SetParent(slotObject.transform, false);
        RectTransform textRect = textObject.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        if (FontSource != null)
        {
            text.font = FontSource.font;
            text.fontSharedMaterial = FontSource.fontSharedMaterial;
        }

        text.alignment = TextAlignmentOptions.Center;
        text.color = Color.white;
        text.fontSize = 14f;
        text.fontStyle = FontStyles.Bold;
        text.richText = false;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;

        Shadow shadow = textObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.9f);
        shadow.effectDistance = new Vector2(1f, -1f);

        GameObject badgeObject = new("Badge");
        badgeObject.transform.SetParent(slotObject.transform, false);
        RectTransform badgeRect = badgeObject.AddComponent<RectTransform>();
        badgeRect.anchorMin = new Vector2(1f, 1f);
        badgeRect.anchorMax = new Vector2(1f, 1f);
        badgeRect.pivot = new Vector2(1f, 1f);
        badgeRect.anchoredPosition = new Vector2(-2f, -1f);
        badgeRect.sizeDelta = new Vector2(14f, 14f);
        TextMeshProUGUI badge = badgeObject.AddComponent<TextMeshProUGUI>();
        if (FontSource != null)
        {
            badge.font = FontSource.font;
            badge.fontSharedMaterial = FontSource.fontSharedMaterial;
        }

        badge.alignment = TextAlignmentOptions.TopRight;
        badge.color = new Color(1f, 0.92f, 0.58f, 1f);
        badge.fontSize = 11f;
        badge.fontStyle = FontStyles.Bold;
        badge.richText = false;
        badge.textWrappingMode = TextWrappingModes.NoWrap;
        badge.raycastTarget = false;

        Shadow badgeShadow = badgeObject.AddComponent<Shadow>();
        badgeShadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
        badgeShadow.effectDistance = new Vector2(1f, -1f);

        return new CooldownSlot(slotObject, background, icon, overlay, text, badge);
    }

    private static void ApplyConfiguredTransform()
    {
        if (RootRect == null)
        {
            return;
        }

        RectTransform? parentRect = RootRect.parent as RectTransform;
        Vector2 parentSize = parentRect != null ? parentRect.rect.size : new Vector2(Screen.width, Screen.height);
        float x = Mathf.Clamp01(SecondaryAttacksPlugin.SecondaryCooldownHudPositionX.Value);
        float y = Mathf.Clamp01(SecondaryAttacksPlugin.SecondaryCooldownHudPositionY.Value);
        RootRect.anchoredPosition = new Vector2(parentSize.x * x, parentSize.y * y);
        RootRect.localScale = Vector3.one * Mathf.Clamp(SecondaryAttacksPlugin.SecondaryCooldownHudScale.Value, 1f, 2f);
    }

    private static void UpdateSlots(bool previewMode)
    {
        if (!AreSlotsValid())
        {
            ResetHudReferences();
            EnsureHud();
            if (RootObject == null || RootRect == null || Slots.Count != MaxSlots)
            {
                return;
            }
        }

        int count = Mathf.Min(Entries.Count, MaxSlots);
        for (int index = 0; index < Slots.Count; index++)
        {
            CooldownSlot slot = Slots[index];
            if (!slot.IsValid)
            {
                continue;
            }

            bool hasEntry = index < count;
            if (!hasEntry && !previewMode)
            {
                slot.SetRootActive(false);
                continue;
            }

            slot.SetRootActive(true);
            if (hasEntry)
            {
                slot.Set(Entries[index]);
            }
            else
            {
                slot.SetEmpty();
            }
        }
    }

    private static bool AreSlotsValid()
    {
        if (Slots.Count != MaxSlots)
        {
            return false;
        }

        foreach (CooldownSlot slot in Slots)
        {
            if (!slot.IsValid)
            {
                return false;
            }
        }

        return true;
    }

    internal enum FillMode
    {
        Cooldown,
        Charge
    }

    internal readonly struct Entry(Sprite? icon, float remaining, float duration, string badge = "", FillMode fillMode = FillMode.Cooldown)
    {
        internal readonly Sprite? Icon = icon;
        internal readonly float Remaining = Mathf.Max(0f, remaining);
        internal readonly float Duration = Mathf.Max(remaining, duration);
        internal readonly string Badge = badge ?? "";
        internal readonly FillMode Mode = fillMode;
        internal readonly float Fraction = duration > 0.001f ? Mathf.Clamp01(remaining / duration) : 0f;
        internal readonly int SortOrder = fillMode == FillMode.Charge ? 0 : 1;
    }

    private sealed class CooldownSlot(GameObject root, Image background, Image icon, Image overlay, TextMeshProUGUI text, TextMeshProUGUI badge)
    {
        private FillMode _textMode;
        private Sprite? _lastIcon;
        private string _lastBadge = "";
        private float _lastFillAmount = -1f;
        private int _lastTextValue = int.MinValue;
        private bool _hasEntry;
        private bool _emptyApplied;
        private bool _lastIconEnabled = true;
        private bool _lastOverlayEnabled = true;
        private bool _lastTextUsesMinutes;
        private bool _lastBadgeActive = true;

        internal GameObject Root { get; } = root;

        internal bool IsValid =>
            Root != null &&
            background != null &&
            icon != null &&
            overlay != null &&
            text != null &&
            badge != null;

        internal void SetRootActive(bool active)
        {
            if (Root.activeSelf != active)
            {
                Root.SetActive(active);
            }
        }

        internal void Set(Entry entry)
        {
            if (!_hasEntry)
            {
                background.color = FilledBackgroundColor;
            }

            _hasEntry = true;
            _emptyApplied = false;
            SetIcon(entry.Icon);
            SetOverlay(entry.Mode == FillMode.Charge ? 1f - entry.Fraction : entry.Fraction);
            SetMainText(entry);
            SetBadge(entry.Badge);
        }

        internal void SetEmpty()
        {
            if (_emptyApplied && !_hasEntry)
            {
                return;
            }

            _hasEntry = false;
            _emptyApplied = true;
            background.color = EmptyBackgroundColor;
            SetIcon(null);
            SetOverlayEnabled(false);
            SetText("");
            SetBadge("");
            _lastTextValue = int.MinValue;
            _lastFillAmount = -1f;
        }

        private void SetIcon(Sprite? sprite)
        {
            if (_lastIcon != sprite)
            {
                icon.sprite = sprite;
                _lastIcon = sprite;
            }

            bool iconEnabled = sprite != null;
            if (_lastIconEnabled != iconEnabled)
            {
                icon.enabled = iconEnabled;
                _lastIconEnabled = iconEnabled;
            }

            if (iconEnabled && icon.color != Color.white)
            {
                icon.color = Color.white;
            }
        }

        private void SetOverlay(float fillAmount)
        {
            SetOverlayEnabled(true);
            if (!Mathf.Approximately(_lastFillAmount, fillAmount))
            {
                overlay.fillAmount = fillAmount;
                _lastFillAmount = fillAmount;
            }
        }

        private void SetOverlayEnabled(bool enabled)
        {
            if (_lastOverlayEnabled == enabled)
            {
                return;
            }

            overlay.enabled = enabled;
            _lastOverlayEnabled = enabled;
        }

        private void SetMainText(Entry entry)
        {
            ResolveDisplayValue(entry, out int value, out bool usesMinutes);
            if (_textMode == entry.Mode &&
                _lastTextValue == value &&
                _lastTextUsesMinutes == usesMinutes)
            {
                return;
            }

            _textMode = entry.Mode;
            _lastTextValue = value;
            _lastTextUsesMinutes = usesMinutes;
            SetText(FormatDisplayText(entry.Mode, value, usesMinutes));
        }

        private void SetText(string value)
        {
            if (!string.Equals(text.text, value, System.StringComparison.Ordinal))
            {
                text.text = value;
            }
        }

        private void SetBadge(string value)
        {
            string badgeText = value ?? "";
            if (!string.Equals(_lastBadge, badgeText, System.StringComparison.Ordinal))
            {
                badge.text = badgeText;
                _lastBadge = badgeText;
            }

            bool badgeActive = !string.IsNullOrWhiteSpace(badgeText);
            if (_lastBadgeActive != badgeActive)
            {
                badge.gameObject.SetActive(badgeActive);
                _lastBadgeActive = badgeActive;
            }
        }

        private static void ResolveDisplayValue(Entry entry, out int value, out bool usesMinutes)
        {
            if (entry.Mode == FillMode.Charge)
            {
                value = Mathf.RoundToInt(Mathf.Clamp01(entry.Fraction) * 100f);
                usesMinutes = false;
                return;
            }

            int seconds = Mathf.CeilToInt(Mathf.Max(0f, entry.Remaining));
            usesMinutes = seconds >= 60;
            value = usesMinutes ? Mathf.CeilToInt(seconds / 60f) : seconds;
        }

        private static string FormatDisplayText(FillMode mode, int value, bool usesMinutes)
        {
            if (mode == FillMode.Charge)
            {
                return $"{value}%";
            }

            return usesMinutes ? $"{value}m" : value.ToString();
        }
    }
}
