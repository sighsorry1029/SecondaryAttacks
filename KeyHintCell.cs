using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal sealed class KeyHintCell
{
    private readonly bool _hideOnRestore;
    private readonly List<TMP_Text> _keys = [];
    private readonly List<GameObject> _keyParents = [];
    private readonly List<TMP_Text> _extraTexts = [];
    private readonly List<string> _originalKeyTexts = [];
    private readonly List<bool> _originalKeyStates = [];
    private readonly List<bool> _originalKeyParentStates = [];
    private readonly List<bool> _originalExtraTextStates = [];
    private TMP_Text? _label;
    private bool _capturedOriginals;
    private bool _originalLabelState;
    private bool _originalRootActive;
    private string _originalLabel = string.Empty;
    private float? _originalLabelPreferredWidth;

    private KeyHintCell(GameObject root, bool hideOnRestore)
    {
        Root = root;
        _hideOnRestore = hideOnRestore;
        RefreshChildren();
    }

    internal GameObject Root { get; }

    internal bool IsValid => Root != null && (_label != null || _keys.Count > 0 || _extraTexts.Count > 0);

    internal bool HasKeyTexts => _keys.Count > 0;

    internal static KeyHintCell? CloneFrom(GameObject? template, string name, bool hideOnRestore)
    {
        if (!IsUsableTemplate(template) || template!.transform.parent == null)
        {
            return null;
        }

        GameObject clone = Object.Instantiate(template, template.transform.parent, false);
        clone.name = name;
        clone.SetActive(false);
        return new KeyHintCell(clone, hideOnRestore);
    }

    internal static KeyHintCell? FromGameObject(GameObject? root, bool hideOnRestore = false)
    {
        return IsUsableTemplate(root) ? new KeyHintCell(root!, hideOnRestore) : null;
    }

    internal static bool IsUsableTemplate(GameObject? template)
    {
        return template != null &&
               template.transform.parent != null &&
               !template.name.StartsWith("SecondaryAttacks_") &&
               template.GetComponentsInChildren<TMP_Text>(includeInactive: true).Length > 0;
    }

    internal void Set(string label, string key, float preferredTextWidth = 0f)
    {
        SetSingleKey(key, label, preferredTextWidth, updateLabel: true);
    }

    internal void SetKey(string key)
    {
        SetSingleKey(key, label: null, preferredTextWidth: 0f, updateLabel: false);
    }

    private void SetSingleKey(string key, string? label, float preferredTextWidth, bool updateLabel)
    {
        if (Root == null)
        {
            return;
        }

        CaptureOriginals();
        Root.SetActive(true);

        if (updateLabel && _label != null)
        {
            SetText(_label, label ?? string.Empty);
            if (preferredTextWidth > 0f && _label.TryGetComponent(out LayoutElement layoutElement))
            {
                layoutElement.preferredWidth = preferredTextWidth;
            }
        }

        TMP_Text? selectedKey = _keys.FirstOrDefault();
        SetKeyVisibility(selectedKey, _label);
        if (selectedKey != null)
        {
            SetText(selectedKey, key);
        }

        foreach (TMP_Text extraText in _extraTexts)
        {
            if (extraText != null)
            {
                extraText.gameObject.SetActive(false);
            }
        }
    }

    internal void SetText(string value)
    {
        if (Root == null)
        {
            return;
        }

        CaptureOriginals();
        Root.SetActive(true);
        TMP_Text? target = _label ?? _keys.FirstOrDefault() ?? _extraTexts.FirstOrDefault();
        SetText(target, value);
        SetKeyVisibility(target != null && _keys.Contains(target) ? target : null, target);

        foreach (TMP_Text extraText in _extraTexts)
        {
            if (extraText != null && extraText != target)
            {
                extraText.gameObject.SetActive(false);
            }
        }
    }

    internal void Restore()
    {
        if (Root == null)
        {
            return;
        }

        if (!_capturedOriginals)
        {
            if (_hideOnRestore)
            {
                Root.SetActive(false);
            }

            return;
        }

        Root.SetActive(_hideOnRestore ? false : _originalRootActive);
        if (_label != null)
        {
            SetText(_label, _originalLabel);
            _label.gameObject.SetActive(_originalLabelState);
            if (_originalLabelPreferredWidth.HasValue &&
                _label.TryGetComponent(out LayoutElement layoutElement))
            {
                layoutElement.preferredWidth = _originalLabelPreferredWidth.Value;
            }
        }

        for (int i = 0; i < _keys.Count && i < _originalKeyTexts.Count; i++)
        {
            SetText(_keys[i], _originalKeyTexts[i]);
        }

        for (int i = 0; i < _keys.Count && i < _originalKeyStates.Count; i++)
        {
            if (_keys[i] != null)
            {
                _keys[i].gameObject.SetActive(_originalKeyStates[i]);
            }
        }

        for (int i = 0; i < _keyParents.Count && i < _originalKeyParentStates.Count; i++)
        {
            if (_keyParents[i] != null)
            {
                _keyParents[i].SetActive(_originalKeyParentStates[i]);
            }
        }

        for (int i = 0; i < _extraTexts.Count && i < _originalExtraTextStates.Count; i++)
        {
            if (_extraTexts[i] != null)
            {
                _extraTexts[i].gameObject.SetActive(_originalExtraTextStates[i]);
            }
        }

    }

    internal void MoveToStart()
    {
        if (Root != null)
        {
            Root.transform.SetAsFirstSibling();
        }
    }

    internal void RebuildParentLayout()
    {
        if (Root != null && Root.transform.parent is RectTransform parent)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
        }
    }

    private void CaptureOriginals()
    {
        if (_capturedOriginals || Root == null)
        {
            return;
        }

        _capturedOriginals = true;
        _originalRootActive = Root.activeSelf;
        _originalLabel = _label != null ? _label.text : string.Empty;
        _originalLabelState = _label != null && _label.gameObject.activeSelf;
        _originalLabelPreferredWidth = _label != null &&
                                       _label.TryGetComponent(out LayoutElement layoutElement)
            ? layoutElement.preferredWidth
            : null;

        _originalKeyTexts.Clear();
        foreach (TMP_Text key in _keys)
        {
            _originalKeyTexts.Add(key != null ? key.text : string.Empty);
        }

        _originalKeyStates.Clear();
        foreach (TMP_Text key in _keys)
        {
            _originalKeyStates.Add(key != null && key.gameObject.activeSelf);
        }

        _originalKeyParentStates.Clear();
        foreach (GameObject keyParent in _keyParents)
        {
            _originalKeyParentStates.Add(keyParent != null && keyParent.activeSelf);
        }

        _originalExtraTextStates.Clear();
        foreach (TMP_Text extraText in _extraTexts)
        {
            _originalExtraTextStates.Add(extraText != null && extraText.gameObject.activeSelf);
        }
    }

    private void RefreshChildren()
    {
        _keys.Clear();
        _keyParents.Clear();
        _extraTexts.Clear();
        _label = null;

        TMP_Text[] texts = Root
            .GetComponentsInChildren<TMP_Text>(includeInactive: true)
            .Where(static text => text != null)
            .ToArray();
        foreach (TMP_Text text in texts)
        {
            Localization.instance?.RemoveTextFromCache(text);
            if (text is TextMeshProUGUI textMesh)
            {
                textMesh.raycastTarget = false;
            }
        }

        _keys.AddRange(texts.Where(static text => string.Equals(text.name, "Key", StringComparison.OrdinalIgnoreCase)));
        if (_keys.Count == 0)
        {
            TMP_Text? inferredKey = texts.FirstOrDefault(static text => LooksLikeKeyBindingText(text.text))
                                   ?? texts.OrderBy(static text => text.transform.position.x).LastOrDefault();
            if (inferredKey != null && texts.Length > 1)
            {
                _keys.Add(inferredKey);
            }
        }

        _label = texts.FirstOrDefault(text => string.Equals(text.name, "Text", StringComparison.OrdinalIgnoreCase) &&
                                              !_keys.Contains(text))
                 ?? texts.FirstOrDefault(text => !_keys.Contains(text) && !LooksLikeKeyBindingText(text.text))
                 ?? texts.FirstOrDefault(text => !_keys.Contains(text));

        foreach (TMP_Text key in _keys)
        {
            _keyParents.Add(key.transform.parent != null ? key.transform.parent.gameObject : key.gameObject);
        }

        _extraTexts.AddRange(texts.Where(text => text != _label && !_keys.Contains(text)));
        SortKeysBySiblingIndex();
    }

    private void SortKeysBySiblingIndex()
    {
        if (_keys.Count <= 1)
        {
            return;
        }

        List<int> order = Enumerable.Range(0, _keys.Count)
            .OrderBy(i => _keyParents[i] != null ? _keyParents[i].transform.GetSiblingIndex() : i)
            .ToList();
        if (order.Count <= 1)
        {
            return;
        }

        List<TMP_Text> orderedKeys = [];
        List<GameObject> orderedParents = [];
        foreach (int index in order)
        {
            orderedKeys.Add(_keys[index]);
            orderedParents.Add(_keyParents[index]);
        }

        _keys.Clear();
        _keys.AddRange(orderedKeys);
        _keyParents.Clear();
        _keyParents.AddRange(orderedParents);
    }

    private static void SetText(TMP_Text? text, string value)
    {
        if (text == null)
        {
            return;
        }

        if (!text.gameObject.activeSelf)
        {
            text.gameObject.SetActive(true);
        }

        if (string.Equals(text.text, value, StringComparison.Ordinal))
        {
            return;
        }

        Localization.instance?.RemoveTextFromCache(text);
        text.text = value;
    }

    private void SetKeyVisibility(TMP_Text? visibleKey, TMP_Text? protectedText)
    {
        foreach (GameObject keyParent in _keyParents.Distinct())
        {
            if (keyParent == null)
            {
                continue;
            }

            bool containsVisibleKey = ContainsText(keyParent, visibleKey);
            bool containsProtectedText = keyParent == Root || ContainsText(keyParent, protectedText);
            keyParent.SetActive(containsVisibleKey || containsProtectedText);
        }

        foreach (TMP_Text key in _keys)
        {
            if (key != null)
            {
                key.gameObject.SetActive(key == visibleKey);
            }
        }
    }

    private static bool ContainsText(GameObject container, TMP_Text? text)
    {
        return text != null &&
               (container == text.gameObject || text.transform.IsChildOf(container.transform));
    }

    private static bool LooksLikeKeyBindingText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string normalized = new(text
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
        return normalized.Contains("mouse") ||
               normalized.Contains("ctrl") ||
               normalized.Contains("shift") ||
               normalized.Contains("alt") ||
               normalized.Contains("button") ||
               normalized.Contains("key") ||
               normalized.Contains("sprite") ||
               normalized.Length <= 2;
    }
}
