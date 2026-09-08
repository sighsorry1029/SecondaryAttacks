using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SecondaryAttacks;

internal static class SecondaryAttackSkillTooltipSystem
{
    private static readonly ConditionalWeakTable<UITooltip, SkillTooltipBinding> TooltipBindings = new();
    private static readonly ConditionalWeakTable<SkillsDialog, List<UITooltip>> DialogTooltips = new();
    private static readonly Vector3[] Corners = new Vector3[4];
    private static bool _layoutFailureLogged;

    private const string BloodMagicHeadingToken = "$sa_skill_tooltip_blood_magic_heading";
    private const string BloodMagicCooldownToken = "$sa_skill_tooltip_blood_magic_cooldown";
    private const string BloodMagicLifetimeToken = "$sa_skill_tooltip_blood_magic_lifetime";
    private const string BloodMagicMaxHealthCostToken = "$sa_skill_tooltip_blood_magic_max_health_cost";
    private const string BloodMagicHealthGainToken = "$sa_skill_tooltip_blood_magic_health_gain";

    private const string SneakHeadingToken = "$sa_skill_tooltip_sneak_heading";
    private const string SneakBackstabGainToken = "$sa_skill_tooltip_sneak_backstab_gain";
    private const string SneakVisibilityToken = "$sa_skill_tooltip_sneak_visibility";
    private const string SneakMovementToken = "$sa_skill_tooltip_sneak_movement";
    private const string SneakAmbushToken = "$sa_skill_tooltip_sneak_ambush";

    internal static void AppendSkillTooltips(SkillsDialog dialog, Player player)
    {
        Skills? playerSkills = player.GetSkills();
        if (playerSkills == null)
        {
            return;
        }

        List<Skills.Skill> skills = playerSkills.GetSkillList();
        for (int index = 0; index < skills.Count; index++)
        {
            Skills.Skill? skill = skills[index];
            Skills.SkillDef? skillInfo = skill?.m_info;
            if (skillInfo == null ||
                skillInfo.m_skill is not (Skills.SkillType.BloodMagic or Skills.SkillType.Sneak))
            {
                continue;
            }

            UITooltip? tooltip = FindSkillTooltip(
                dialog,
                index,
                skillInfo.m_description);
            if (tooltip == null)
            {
                continue;
            }

            string text = skillInfo.m_skill == Skills.SkillType.BloodMagic
                ? AppendBloodMagicSection(tooltip.m_text)
                : AppendSneakSection(tooltip.m_text);
            if (!string.Equals(text, tooltip.m_text, StringComparison.Ordinal))
            {
                tooltip.Set(
                    tooltip.m_topic,
                    text,
                    tooltip.m_anchor,
                    tooltip.m_fixedPosition);
            }

            BindTooltip(dialog, tooltip);
        }
    }

    internal static void ClearTooltipBindings(SkillsDialog dialog)
    {
        if (!DialogTooltips.TryGetValue(dialog, out List<UITooltip>? tooltips))
        {
            return;
        }

        // Setup reuses rows for different skills; discard the old association before vanilla resets them.
        foreach (UITooltip tooltip in tooltips)
        {
            if (tooltip != null && TooltipBindings.TryGetValue(tooltip, out SkillTooltipBinding? binding))
            {
                tooltip.m_anchor = binding.OriginalAnchor;
                tooltip.m_fixedPosition = binding.OriginalFixedPosition;
                if (UITooltip.m_current == tooltip)
                {
                    UITooltip.HideTooltip();
                }
            }

            TooltipBindings.Remove(tooltip!); // A destroyed Unity object is still a valid managed table key.
        }

        tooltips.Clear();
    }

    private static void BindTooltip(SkillsDialog dialog, UITooltip tooltip)
    {
        Canvas? canvas = tooltip.GetComponentInParent<Canvas>();
        RectTransform? panel = FindSkillPanel(dialog);
        if (canvas == null || canvas.transform is not RectTransform canvasRect || panel == null)
        {
            return;
        }

        RectTransform? row = tooltip.transform as RectTransform;
        for (Transform current = tooltip.transform; current != null && current != dialog.m_listRoot; current = current.parent)
        {
            if (current.parent == dialog.m_listRoot)
            {
                row = current as RectTransform;
                break;
            }
        }

        if (row == null)
        {
            return;
        }

        TooltipBindings.Remove(tooltip);
        TooltipBindings.Add(tooltip, new SkillTooltipBinding(dialog, panel, row, canvas, canvasRect, tooltip));
        DialogTooltips.GetValue(dialog, _ => new List<UITooltip>(2)).Add(tooltip);
        // Keep gamepad tooltips on the same canvas as mouse tooltips, outside the scroll viewport mask.
        tooltip.m_anchor = canvasRect;
        tooltip.m_fixedPosition = Vector2.zero;
    }

    private static RectTransform? FindSkillPanel(SkillsDialog dialog)
    {
        if (dialog.m_listRoot == null)
        {
            return null;
        }

        // SkillsDialog is a full-screen container. The list's branch below it is the visible SkillsFrame.
        for (Transform current = dialog.m_listRoot.parent; current != null && current != dialog.transform; current = current.parent)
        {
            if (current.parent == dialog.transform && current is RectTransform frame)
            {
                // Vanilla's backdrop extends beyond the frame; keep the gap outside its visible edge.
                return frame.Find("bkg") as RectTransform ?? frame;
            }
        }

        return null;
    }

    internal static void UpdateVisibleTooltip(UITooltip tooltip)
    {
        if (UITooltip.m_current != tooltip || UITooltip.m_tooltip == null ||
            !TooltipBindings.TryGetValue(tooltip, out SkillTooltipBinding? binding))
        {
            return;
        }

        try
        {
            if (binding.Dialog == null || !binding.Dialog.isActiveAndEnabled ||
                binding.Row == null || !binding.Row.gameObject.activeInHierarchy)
            {
                UITooltip.HideTooltip();
                return;
            }

            GameObject root = UITooltip.m_tooltip;
            if (!root.activeSelf)
            {
                return; // Preserve vanilla's hover delay.
            }

            if (binding.View == null || binding.View.Root != root)
            {
                binding.View = SkillTooltipView.Create(root, binding.CanvasRect);
                if (binding.View == null)
                {
                    tooltip.m_anchor = binding.OriginalAnchor;
                    tooltip.m_fixedPosition = binding.OriginalFixedPosition;
                    TooltipBindings.Remove(tooltip);
                    return;
                }
            }

            Camera? camera = binding.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : binding.Canvas.worldCamera;
            Rect safeArea = Screen.safeArea;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(binding.CanvasRect, safeArea.min, camera, out Vector2 screenMin) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(binding.CanvasRect, safeArea.max, camera, out Vector2 screenMax))
            {
                return;
            }

            Rect viewport = Rect.MinMaxRect(screenMin.x, screenMin.y, screenMax.x, screenMax.y);
            Rect panelBounds = GetCanvasBounds(binding.Panel, binding.CanvasRect);
            Rect rowBounds = GetCanvasBounds(binding.Row, binding.CanvasRect);
            binding.View.UpdateLayout();
            float scale = SecondaryAttackSkillTooltipLayout.GetScale(viewport, binding.View.Size);
            Vector2 topLeft = SecondaryAttackSkillTooltipLayout.GetTopLeft(viewport, panelBounds, rowBounds.yMax, binding.View.Size * scale);
            binding.View.Panel.localScale = new Vector3(scale, scale, 1f);
            // Vanilla clamps the first child, not just the root. Place that child absolutely to avoid offset accumulation.
            binding.View.Panel.position = binding.CanvasRect.TransformPoint(new Vector3(topLeft.x, topLeft.y, 0f));
        }
        catch (Exception exception)
        {
            tooltip.m_anchor = binding.OriginalAnchor;
            tooltip.m_fixedPosition = binding.OriginalFixedPosition;
            TooltipBindings.Remove(tooltip);
            UITooltip.HideTooltip();
            if (!_layoutFailureLogged)
            {
                _layoutFailureLogged = true;
                SecondaryAttacksPlugin.ModLogger.LogWarning("Could not position the skill tooltip: " + exception.GetBaseException().Message);
            }
        }
    }

    private static Rect GetCanvasBounds(RectTransform rect, RectTransform canvas)
    {
        rect.GetWorldCorners(Corners);
        Vector2 min = canvas.InverseTransformPoint(Corners[0]);
        Vector2 max = min;
        for (int index = 1; index < Corners.Length; index++)
        {
            Vector2 point = canvas.InverseTransformPoint(Corners[index]);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }

        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private sealed class SkillTooltipBinding
    {
        internal readonly SkillsDialog Dialog;
        internal readonly RectTransform Panel;
        internal readonly RectTransform Row;
        internal readonly Canvas Canvas;
        internal readonly RectTransform CanvasRect;
        internal readonly RectTransform? OriginalAnchor;
        internal readonly Vector2 OriginalFixedPosition;
        internal SkillTooltipView? View;

        internal SkillTooltipBinding(SkillsDialog dialog, RectTransform panel, RectTransform row, Canvas canvas, RectTransform canvasRect, UITooltip tooltip)
        {
            Dialog = dialog;
            Panel = panel;
            Row = row;
            Canvas = canvas;
            CanvasRect = canvasRect;
            OriginalAnchor = tooltip.m_anchor;
            OriginalFixedPosition = tooltip.m_fixedPosition;
        }
    }

    private sealed class SkillTooltipView
    {
        private const float Padding = SecondaryAttackSkillTooltipLayout.Padding;
        private const float TopicGap = 8f;
        internal readonly GameObject Root;
        internal readonly RectTransform Panel;
        private readonly TMP_Text _body;
        private readonly TMP_Text? _topic;
        private string? _bodyText;
        private string? _topicText;
        internal Vector2 Size => Panel.sizeDelta;

        private SkillTooltipView(GameObject root, RectTransform panel, TMP_Text body, TMP_Text? topic)
        {
            Root = root;
            Panel = panel;
            _body = body;
            _topic = topic;
        }

        internal static SkillTooltipView? Create(GameObject root, RectTransform canvas)
        {
            TMP_Text? body = Utils.FindChild(root.transform, "Text")?.GetComponent<TMP_Text>();
            TMP_Text? topic = Utils.FindChild(root.transform, "Topic")?.GetComponent<TMP_Text>();
            if (body == null || body.font == null || body.transform == root.transform)
            {
                return null;
            }

            root.transform.SetParent(canvas, false);
            root.transform.localScale = Vector3.one;
            root.transform.localRotation = Quaternion.identity;
            DisableAutomaticLayout(root);
            CanvasGroup group = root.GetComponent<CanvasGroup>() ?? root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            GameObject panelObject = new("SecondaryAttacks_SkillTooltip", typeof(RectTransform), typeof(Image));
            RectTransform panel = (RectTransform)panelObject.transform;
            panel.SetParent(root.transform, false);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
            Image background = panelObject.GetComponent<Image>();
            background.color = new Color(0.055f, 0.045f, 0.04f, 0.94f);
            background.raycastTarget = false;

            PrepareText(body, panel, TextAlignmentOptions.TopLeft);
            if (topic != null && topic != body)
            {
                PrepareText(topic, panel, TextAlignmentOptions.Top);
            }

            // Reuse the cloned game's text/font/material, but give this instance a predictable layout and backdrop.
            for (int index = 0; index < root.transform.childCount; index++)
            {
                Transform child = root.transform.GetChild(index);
                if (child != panel)
                {
                    child.gameObject.SetActive(false);
                }
            }

            panel.SetAsFirstSibling();
            return new SkillTooltipView(root, panel, body, topic == body ? null : topic);
        }

        private static void PrepareText(TMP_Text text, RectTransform panel, TextAlignmentOptions alignment)
        {
            DisableAutomaticLayout(text.gameObject);
            RectTransform rect = text.rectTransform;
            rect.SetParent(panel, false);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.Normal;
            text.overflowMode = TextOverflowModes.Overflow;
            text.enableAutoSizing = false;
            text.margin = Vector4.zero;
            text.raycastTarget = false;
            text.gameObject.SetActive(true);
        }

        private static void DisableAutomaticLayout(GameObject target)
        {
            LayoutGroup? layout = target.GetComponent<LayoutGroup>();
            ContentSizeFitter? fitter = target.GetComponent<ContentSizeFitter>();
            AspectRatioFitter? aspect = target.GetComponent<AspectRatioFitter>();
            if (layout != null) layout.enabled = false;
            if (fitter != null) fitter.enabled = false;
            if (aspect != null) aspect.enabled = false;
        }

        internal void UpdateLayout()
        {
            if (_bodyText == _body.text && _topicText == _topic?.text)
            {
                return;
            }

            _bodyText = _body.text;
            _topicText = _topic?.text;
            const float innerWidth = SecondaryAttackSkillTooltipLayout.BodyWidth;
            float y = Padding;
            if (_topic != null)
            {
                bool hasTopic = !string.IsNullOrWhiteSpace(_topicText);
                _topic.gameObject.SetActive(hasTopic);
                if (hasTopic)
                {
                    y += SetTextRect(_topic, innerWidth, y) + TopicGap;
                }
            }

            y += SetTextRect(_body, innerWidth, y);
            Panel.sizeDelta = new Vector2(SecondaryAttackSkillTooltipLayout.Width, y + Padding);
        }

        private static float SetTextRect(TMP_Text text, float width, float y)
        {
            float height = Mathf.Max(1f, Mathf.Ceil(text.GetPreferredValues(text.text, width, float.PositiveInfinity).y));
            text.rectTransform.anchoredPosition = new Vector2(Padding, -y);
            text.rectTransform.sizeDelta = new Vector2(width, height);
            return height;
        }
    }

    private static string AppendBloodMagicSection(string? original)
    {
        original ??= string.Empty;
        if (original.IndexOf(BloodMagicHeadingToken, StringComparison.Ordinal) >= 0)
        {
            return original;
        }

        StringBuilder section = new(BloodMagicHeadingToken);
        if (HasBloodMagicCooldownSkillScaling())
        {
            section.Append('\n').Append(BloodMagicCooldownToken);
        }

        if (SecondaryAttacksPlugin.BloodMagicSummonLifetimeSeconds.Value > 0 &&
            !Mathf.Approximately(
                Mathf.Max(1f, SecondaryAttacksPlugin.BloodMagicSummonLifetimeSkill100Multiplier.Value),
                1f))
        {
            section.Append('\n').Append(BloodMagicLifetimeToken);
        }

        if (SecondaryAttacksPlugin.BloodMagicHealthCostUsesMaxHealth.Value ==
            SecondaryAttacksPlugin.Toggle.On)
        {
            section.Append('\n').Append(BloodMagicMaxHealthCostToken);
        }

        if (SecondaryAttacksPlugin.BloodMagicHealthCostSkillRaiseFactor.Value > 0f)
        {
            section.Append('\n').Append(BloodMagicHealthGainToken);
        }

        if (section.Length == BloodMagicHeadingToken.Length)
        {
            return original;
        }

        return AppendSection(original, section);
    }

    private static string AppendSneakSection(string? original)
    {
        original ??= string.Empty;
        if (original.IndexOf(SneakHeadingToken, StringComparison.Ordinal) >= 0)
        {
            return original;
        }

        StringBuilder section = new(SneakHeadingToken);
        if (SecondaryAttacksPlugin.BackstabSneakSkillRaiseAmount.Value > 0f)
        {
            section.Append('\n').Append(SneakBackstabGainToken);
        }

        float visibilityFactor = Mathf.Clamp(
            SecondaryAttacksPlugin.SneakVisibilitySkillEffectFactor.Value,
            1f,
            2f);
        if (!Mathf.Approximately(visibilityFactor, 1f))
        {
            section.Append('\n').Append(SneakVisibilityToken);
        }

        float movementFactor = Mathf.Clamp(
            SecondaryAttacksPlugin.SneakMovementSpeedSkillFactor.Value,
            1f,
            2f);
        if (!Mathf.Approximately(movementFactor, 1f))
        {
            section.Append('\n').Append(SneakMovementToken);
        }

        if (HasSneakAmbushSkillScaling())
        {
            section.Append('\n').Append(SneakAmbushToken);
        }

        if (section.Length == SneakHeadingToken.Length)
        {
            return original;
        }

        return AppendSection(original, section);
    }

    private static bool HasBloodMagicCooldownSkillScaling()
    {
        foreach (SecondaryAttackDefinition definition in
                 SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.Values)
        {
            MeleePresetCooldownDefinition? cooldown = definition.Behavior switch
            {
                SummonEmpowerSecondaryBehavior summonEmpower => summonEmpower.PresetCooldown,
                ShieldConvertSecondaryBehavior shieldConvert => shieldConvert.PresetCooldown,
                _ => null
            };
            if (cooldown != null &&
                cooldown.Cooldown > 0f &&
                cooldown.CooldownReductionFactor > 0f &&
                UsesBloodMagicSkill(cooldown.CooldownSkill))
            {
                return true;
            }
        }

        return false;
    }

    private static bool UsesBloodMagicSkill(string? skillName)
    {
        if (string.IsNullOrWhiteSpace(skillName))
        {
            return false;
        }

        string normalized = skillName!.Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);
        return normalized.Equals("bloodmagic", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("blood", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSneakAmbushSkillScaling()
    {
        foreach (SecondaryAttackDefinition definition in
                 SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.Values)
        {
            SneakAmbushDefinition? sneakAmbush = definition.SneakAmbush;
            if (sneakAmbush != null &&
                sneakAmbush.ChargeMaxSeconds > 0f &&
                sneakAmbush.ChargeSkillFactor > 1f)
            {
                return true;
            }
        }

        return false;
    }

    private static string AppendSection(string original, StringBuilder section)
    {
        string text = section.ToString();
        int headingEnd = text.IndexOf('\n');
        // Include the paragraph break in the centered span so TMP restores left alignment for the body only.
        text = headingEnd >= 0
            ? "<align=center>" + text.Insert(headingEnd + 1, "</align>")
            : "<align=center>" + text + "</align>";
        return original.Length > 0
            ? original + "\n\n" + text
            : text;
    }

    private static UITooltip? FindSkillTooltip(
        SkillsDialog dialog,
        int skillIndex,
        string skillDescription)
    {
        if (dialog.m_elements != null &&
            skillIndex >= 0 &&
            skillIndex < dialog.m_elements.Count)
        {
            GameObject? indexedElement = dialog.m_elements[skillIndex];
            if (indexedElement != null)
            {
                UITooltip? indexedTooltip = indexedElement
                    .GetComponentInChildren<UITooltip>(true);
                if (indexedTooltip != null &&
                    MatchesSkillDescription(indexedTooltip.m_text, skillDescription))
                {
                    return indexedTooltip;
                }
            }
        }

        InventoryGui? inventory = dialog.GetComponentInParent<InventoryGui>();
        if (inventory == null)
        {
            return null;
        }

        UITooltip[] candidates = inventory.GetComponentsInChildren<UITooltip>(true);
        foreach (UITooltip candidate in candidates)
        {
            if (candidate != null &&
                candidate.gameObject.activeInHierarchy &&
                MatchesSkillDescription(candidate.m_text, skillDescription))
            {
                return candidate;
            }
        }

        foreach (UITooltip candidate in candidates)
        {
            if (candidate != null &&
                MatchesSkillDescription(candidate.m_text, skillDescription))
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool MatchesSkillDescription(
        string? tooltipText,
        string? skillDescription)
    {
        if (string.IsNullOrWhiteSpace(tooltipText) ||
            string.IsNullOrWhiteSpace(skillDescription))
        {
            return false;
        }

        if (tooltipText!.IndexOf(skillDescription!, StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        if (Localization.instance == null)
        {
            return false;
        }

        string localizedDescription = Localization.instance.Localize(skillDescription!);
        return !string.IsNullOrWhiteSpace(localizedDescription) &&
               !string.Equals(localizedDescription, skillDescription, StringComparison.Ordinal) &&
               tooltipText.IndexOf(localizedDescription, StringComparison.Ordinal) >= 0;
    }
}

[HarmonyPatch(typeof(SkillsDialog), nameof(SkillsDialog.Setup))]
internal static class SkillsDialogSetupSecondaryAttackTooltipPatch
{
    private static bool _failureLogged;

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static void Prefix(SkillsDialog __instance)
    {
        if (__instance != null)
        {
            SecondaryAttackSkillTooltipSystem.ClearTooltipBindings(__instance);
        }
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyAfter("randyknapp.mods.epicloot")]
    private static void Postfix(SkillsDialog __instance, Player player)
    {
        if (__instance == null || player == null)
        {
            return;
        }

        try
        {
            SecondaryAttackSkillTooltipSystem.AppendSkillTooltips(__instance, player);
        }
        catch (Exception exception)
        {
            if (_failureLogged)
            {
                return;
            }

            _failureLogged = true;
            SecondaryAttacksPlugin.ModLogger.LogWarning(
                "Could not extend the Blood Magic and Sneak skill tooltips: " +
                exception.GetBaseException().Message);
        }
    }
}

[HarmonyPatch(typeof(UITooltip), "LateUpdate")]
internal static class SkillTooltipPositionPatch
{
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(UITooltip __instance)
    {
        SecondaryAttackSkillTooltipSystem.UpdateVisibleTooltip(__instance);
    }
}

internal static class SecondaryAttackSkillTooltipLayout
{
    // SkillListTooltip/Text uses 250 UI units with no horizontal text margin in vanilla.
    internal const float BodyWidth = 250f;
    internal const float Padding = 16f;
    internal const float Width = BodyWidth + Padding * 2f;
    private const float Gap = 8f;
    private const float Margin = 12f;

    internal static float GetScale(Rect viewport, Vector2 size)
    {
        float widthScale = Mathf.Max(1f, viewport.width - Margin * 2f) / Mathf.Max(1f, size.x);
        float heightScale = Mathf.Max(1f, viewport.height - Margin * 2f) / Mathf.Max(1f, size.y);
        return Mathf.Min(1f, Mathf.Min(widthScale, heightScale));
    }

    internal static Vector2 GetTopLeft(Rect viewport, Rect skillPanel, float rowTop, Vector2 size)
    {
        float minX = viewport.xMin + Margin;
        float maxY = viewport.yMax - Margin;
        float x = Mathf.Clamp(skillPanel.xMin - Gap - size.x, minX, Mathf.Max(minX, viewport.xMax - Margin - size.x));
        float y = Mathf.Clamp(rowTop, Mathf.Min(maxY, viewport.yMin + Margin + size.y), maxY);
        return new Vector2(x, y);
    }
}
