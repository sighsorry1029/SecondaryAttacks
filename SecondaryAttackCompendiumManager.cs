using System;
using System.Text;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SecondaryAttacks;

internal static class SecondaryAttackCompendiumManager
{
    private const string PageTopic = "$sa_compendium_title";
    private const string BodyIconPrefix = "SecondaryAttacks_CompendiumPresetIcon_";
    private const char IconMarker = '*';
    private const float IconSize = 18f;
    private const float IconTextGap = 5f;

    internal static void AddPresetPage(TextsDialog dialog)
    {
        if (dialog?.m_texts == null)
        {
            return;
        }

        dialog.m_texts.RemoveAll(text => IsPresetPage(text?.m_topic));
        if (SecondaryAttackPresetCatalog.Entries.Count == 0)
        {
            return;
        }

        dialog.m_texts.Add(new TextsDialog.TextInfo(PageTopic, BuildPageText()));
    }

    internal static void RefreshPageContentIcons(TextsDialog dialog, TextsDialog.TextInfo info)
    {
        if (dialog?.m_textArea == null || info == null)
        {
            return;
        }

        ClearPageContentIcons(dialog);
        if (!IsPresetPage(info.m_topic))
        {
            return;
        }

        TMP_Text textArea = dialog.m_textArea;
        RectTransform? content = textArea.transform.parent as RectTransform;
        if (content == null)
        {
            return;
        }

        textArea.ForceMeshUpdate();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        textArea.ForceMeshUpdate();

        TMP_TextInfo textInfo = textArea.textInfo;
        int entryIndex = 0;
        for (int characterIndex = 0;
             characterIndex < textInfo.characterCount && entryIndex < SecondaryAttackPresetCatalog.Entries.Count;
             characterIndex++)
        {
            TMP_CharacterInfo character = textInfo.characterInfo[characterIndex];
            if (character.character != IconMarker)
            {
                continue;
            }

            SecondaryAttackPresetInfo entry = SecondaryAttackPresetCatalog.Entries[entryIndex];
            AttachBodyIcon(content, textArea.rectTransform, character, SecondaryAttackPresetCatalog.ResolveIcon(entry.Key), entryIndex);
            entryIndex++;
        }
    }

    private static string BuildPageText()
    {
        StringBuilder builder = new();
        builder
            .Append("<color=#D6D6D6>")
            .Append(SecondaryAttackLocalization.Localize(
                "$sa_compendium_intro",
                "Attack preset cooldowns are reduced according to the equipped weapon's skill level."))
            .Append("</color>\n\n");

        SecondaryAttackPresetGroup? previousGroup = null;
        foreach (SecondaryAttackPresetInfo entry in SecondaryAttackPresetCatalog.Entries)
        {
            if (previousGroup != entry.Group)
            {
                if (previousGroup != null)
                {
                    builder.Append('\n');
                }

                builder
                    .Append("<color=#FFD27A><b>")
                    .Append(LocalizeGroupHeading(entry.Group))
                    .Append("</b></color>\n\n");
                previousGroup = entry.Group;
            }

            builder
                .Append("<color=#00000000>")
                .Append(IconMarker)
                .Append("</color>    ")
                .Append("<color=orange><b>")
                .Append(SecondaryAttackLocalization.Localize(entry.NameToken, entry.FallbackName))
                .Append("</b></color>\n     ")
                .Append(SecondaryAttackLocalization.Localize(entry.DescriptionToken, entry.FallbackDescription))
                .Append("\n\n");
        }

        return builder.ToString().TrimEnd();
    }

    private static string LocalizeGroupHeading(SecondaryAttackPresetGroup group)
    {
        return group switch
        {
            SecondaryAttackPresetGroup.Ranged => SecondaryAttackLocalization.Localize("$sa_compendium_group_ranged", "Ranged Presets"),
            SecondaryAttackPresetGroup.Melee => SecondaryAttackLocalization.Localize("$sa_compendium_group_melee", "Melee Presets"),
            SecondaryAttackPresetGroup.BloodMagic => SecondaryAttackLocalization.Localize("$sa_compendium_group_blood_magic", "Blood Magic Presets"),
            SecondaryAttackPresetGroup.SummonQuality => SecondaryAttackLocalization.Localize("$sa_compendium_group_summon_quality", "Magic Summon Quality"),
            _ => ""
        };
    }

    private static void AttachBodyIcon(
        RectTransform content,
        RectTransform textArea,
        TMP_CharacterInfo marker,
        Sprite? sprite,
        int index)
    {
        if (content == null || textArea == null || sprite == null)
        {
            return;
        }

        GameObject icon = new($"{BodyIconPrefix}{index}", typeof(RectTransform), typeof(Image), typeof(LayoutElement))
        {
            layer = textArea.gameObject.layer
        };
        RectTransform rect = (RectTransform)icon.transform;
        rect.SetParent(content, false);
        rect.anchorMin = content.pivot;
        rect.anchorMax = content.pivot;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(IconSize, IconSize);

        Vector3 center = (marker.bottomLeft + marker.topLeft) * 0.5f;
        center.x += IconSize * 0.5f + IconTextGap;
        Vector3 worldCenter = textArea.TransformPoint(center);
        Vector3 contentCenter = content.InverseTransformPoint(worldCenter);
        rect.anchoredPosition = new Vector2(contentCenter.x, contentCenter.y);

        Image image = icon.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;

        LayoutElement layout = icon.GetComponent<LayoutElement>();
        layout.ignoreLayout = true;
    }

    private static void ClearPageContentIcons(TextsDialog dialog)
    {
        if (dialog?.m_textArea == null)
        {
            return;
        }

        ClearIconChildren(dialog.m_textArea.transform);
        if (dialog.m_textArea.transform.parent != null)
        {
            ClearIconChildren(dialog.m_textArea.transform.parent);
        }
    }

    private static void ClearIconChildren(Transform parent)
    {
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Transform child = parent.GetChild(index);
            if (child.name.StartsWith(BodyIconPrefix, StringComparison.Ordinal))
            {
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }
    }

    private static bool IsPresetPage(string? topic)
    {
        return string.Equals(topic, PageTopic, StringComparison.Ordinal);
    }
}

[HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
internal static class SecondaryAttackTextsDialogUpdateTextsListPatch
{
    private static void Postfix(TextsDialog __instance)
    {
        SecondaryAttackCompendiumManager.AddPresetPage(__instance);
    }
}

[HarmonyPatch(typeof(TextsDialog), nameof(TextsDialog.ShowText), new[] { typeof(TextsDialog.TextInfo) })]
internal static class SecondaryAttackTextsDialogShowTextPatch
{
    private static void Postfix(TextsDialog __instance, TextsDialog.TextInfo text)
    {
        SecondaryAttackCompendiumManager.RefreshPageContentIcons(__instance, text);
    }
}
