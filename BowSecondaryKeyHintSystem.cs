using HarmonyLib;
using TMPro;
using UnityEngine;

namespace SecondaryAttacks;

internal static class BowSecondaryKeyHintSystem
{
    private static readonly string[] BowSecondaryHintKeys = new string[1];
    private static readonly string[] DetonateBlockHintKeys = new string[1];
    private static KeyHints? _activeKeyHints;
    private static KeyHintCell? _bowSecondaryHint;
    private static KeyHintCell? _detonateBlockHint;
    private static GameObject? _bowSecondaryHintTemplate;
    private static KeyHints? _cachedTemplateHints;
    private static GameObject? _cachedCombatHintTemplate;
    private static bool _showingBowSecondaryHint;
    private static bool _showingDetonateBlockHint;
    private static bool _cachedTemplateGamepadActive;
    private static bool _lastGamepadActive;
    private static bool _lastDetonateBlockGamepadActive;
    private static bool _keyHintApplied;
    private static bool _detonateBlockHintApplied;
    private static string _lastButtonLabel = string.Empty;
    private static string _lastDetonateBlockButtonLabel = string.Empty;

    internal static void InitializeKeyHints(KeyHints hints)
    {
        _activeKeyHints = hints;
        DestroyBowSecondaryHint();
        RestoreDetonateBlockHint();
        _detonateBlockHint = null;
        _showingBowSecondaryHint = false;
        UpdateKeyHint(hints);
    }

    internal static void RefreshKeyHintUi()
    {
        if (_activeKeyHints != null)
        {
            UpdateKeyHint(_activeKeyHints);
        }
    }

    internal static void UpdateKeyHint(KeyHints hints)
    {
        if (hints == null)
        {
            return;
        }

        _activeKeyHints = hints;
        if (!ShouldAllowCustomCombatHints(hints))
        {
            HideBowSecondaryHint();
            RestoreDetonateBlockHint();
            if (hints.m_combatHints != null)
            {
                hints.m_combatHints.SetActive(false);
            }

            return;
        }

        UpdateDetonateBlockHint(hints);
        if (!ShouldShowBowSecondaryHint())
        {
            HideBowSecondaryHint();
            return;
        }

        bool gamepadActive = ZInput.IsGamepadActive();
        EnsureBowSecondaryHint(hints, gamepadActive);
        if (_bowSecondaryHint?.IsValid != true)
        {
            return;
        }

        if (hints.m_combatHints != null)
        {
            hints.m_combatHints.SetActive(true);
        }

        SetVanillaCombatHintActive(hints.m_secondaryAttackGP, false);
        SetVanillaCombatHintActive(hints.m_secondaryAttackKB, false);
        bool layoutDirty = !_showingBowSecondaryHint;
        string buttonLabel = ResolveSecondaryAttackButtonLabel(gamepadActive);
        if (!_keyHintApplied ||
            _lastGamepadActive != gamepadActive ||
            !string.Equals(_lastButtonLabel, buttonLabel, System.StringComparison.Ordinal))
        {
            BowSecondaryHintKeys[0] = buttonLabel;
            _bowSecondaryHint.SetKeys(BowSecondaryHintKeys, hideExtraTexts: true);
            _lastButtonLabel = buttonLabel;
            _lastGamepadActive = gamepadActive;
            _keyHintApplied = true;
            layoutDirty = true;
        }

        if (_bowSecondaryHint.Root != null && _bowSecondaryHint.Root.transform.GetSiblingIndex() != 0)
        {
            _bowSecondaryHint.MoveToStart();
            layoutDirty = true;
        }

        if (layoutDirty)
        {
            _bowSecondaryHint.RebuildParentLayout();
        }

        _showingBowSecondaryHint = true;
    }

    private static void UpdateDetonateBlockHint(KeyHints hints)
    {
        Player? player = Player.m_localPlayer;
        if (!ShouldShowCombatHints(player) || !StickyDetonatorSystem.ShouldShowDetonateBlockHint(player))
        {
            RestoreDetonateBlockHint();
            return;
        }

        bool gamepadActive = ZInput.IsGamepadActive();
        GameObject? targetHint = ResolveBlockHint(hints, gamepadActive);
        if (!KeyHintCell.IsUsableTemplate(targetHint))
        {
            RestoreDetonateBlockHint();
            return;
        }

        if (_detonateBlockHint == null || _detonateBlockHint.Root != targetHint)
        {
            RestoreDetonateBlockHint();
            _detonateBlockHint = KeyHintCell.FromGameObject(targetHint);
            _detonateBlockHintApplied = false;
        }

        if (_detonateBlockHint?.IsValid != true)
        {
            return;
        }

        if (hints.m_combatHints != null)
        {
            hints.m_combatHints.SetActive(true);
        }

        string buttonLabel = ResolveBlockButtonLabel(gamepadActive);
        if (!_detonateBlockHintApplied ||
            _lastDetonateBlockGamepadActive != gamepadActive ||
            !string.Equals(_lastDetonateBlockButtonLabel, buttonLabel, System.StringComparison.Ordinal))
        {
            ApplyDetonateBlockHint(_detonateBlockHint, buttonLabel);
            _lastDetonateBlockButtonLabel = buttonLabel;
            _lastDetonateBlockGamepadActive = gamepadActive;
            _detonateBlockHintApplied = true;
            _detonateBlockHint.RebuildParentLayout();
        }

        _showingDetonateBlockHint = true;
    }

    private static bool ShouldShowBowSecondaryHint()
    {
        Player? player = Player.m_localPlayer;
        if (!ShouldShowCombatHints(player))
        {
            return false;
        }

        ItemDrop.ItemData? weapon = ResolveEquippedBow(player);
        if (weapon?.m_shared == null)
        {
            return false;
        }

        if (weapon.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Bow)
        {
            return false;
        }

        if (!weapon.m_shared.m_attack.m_bowDraw)
        {
            return false;
        }

        return HasVisibleSecondaryAttack(weapon);
    }

    private static bool HasVisibleSecondaryAttack(ItemDrop.ItemData weapon)
    {
        if (weapon == null)
        {
            return false;
        }

        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition))
        {
            return false;
        }

        if (definition.BehaviorType != SecondaryAttackBehaviorType.Projectile)
        {
            return false;
        }

        return true;
    }

    private static bool ShouldAllowCustomCombatHints(KeyHints hints)
    {
        return hints.m_keyHintsEnabled &&
               !InventoryGui.IsVisible() &&
               !Menu.IsVisible() &&
               !Console.IsVisible() &&
               !Game.IsPaused() &&
               (Chat.instance == null || !Chat.instance.HasFocus()) &&
               (InventoryGui.instance == null ||
                (!InventoryGui.instance.IsSkillsPanelOpen &&
                 !InventoryGui.instance.IsTrophisPanelOpen &&
                 !InventoryGui.instance.IsTextPanelOpen));
    }

    private static bool ShouldShowCombatHints(Player? player)
    {
        return player != null &&
               !player.IsDead() &&
               !Hud.IsPieceSelectionVisible() &&
               !Hud.InRadial() &&
               !InventoryGui.IsVisible() &&
               !Menu.IsVisible() &&
               !Console.IsVisible() &&
               !Game.IsPaused() &&
               (Chat.instance == null || !Chat.instance.HasFocus()) &&
               (InventoryGui.instance == null ||
                (!InventoryGui.instance.IsSkillsPanelOpen &&
                 !InventoryGui.instance.IsTrophisPanelOpen &&
                 !InventoryGui.instance.IsTextPanelOpen)) &&
               !PlayerCustomizaton.IsBarberGuiVisible() &&
               player.GetDoodadController() == null;
    }

    private static void SetVanillaCombatHintActive(GameObject? hint, bool active)
    {
        if (hint != null)
        {
            if (hint.activeSelf != active)
            {
                hint.SetActive(active);
            }
        }
    }

    private static void EnsureBowSecondaryHint(KeyHints hints, bool gamepadActive)
    {
        if (_bowSecondaryHint != null &&
            _bowSecondaryHint.Root != null &&
            _bowSecondaryHintTemplate != null &&
            _bowSecondaryHintTemplate.transform.parent != null &&
            _cachedTemplateHints == hints &&
            _cachedTemplateGamepadActive == gamepadActive &&
            _bowSecondaryHint.Root.transform.parent == _bowSecondaryHintTemplate.transform.parent)
        {
            return;
        }

        GameObject? template = ResolveCachedCombatHintTemplate(hints, gamepadActive);
        if (template == null || template.transform.parent == null)
        {
            return;
        }

        if (_bowSecondaryHint != null &&
            _bowSecondaryHint.Root != null &&
            _bowSecondaryHint.Root.transform.parent == template.transform.parent)
        {
            _bowSecondaryHintTemplate = template;
            return;
        }

        DestroyBowSecondaryHint();
        _bowSecondaryHint = KeyHintCell.CloneFrom(template, "SecondaryAttacks_BowSecondaryHint", hideOnRestore: true);
        _bowSecondaryHintTemplate = template;
        _cachedTemplateHints = hints;
        _cachedTemplateGamepadActive = gamepadActive;
        _cachedCombatHintTemplate = template;
        _keyHintApplied = false;
    }

    private static GameObject? ResolveCachedCombatHintTemplate(KeyHints hints, bool gamepadActive)
    {
        if (_cachedTemplateHints == hints &&
            _cachedTemplateGamepadActive == gamepadActive &&
            _cachedCombatHintTemplate != null &&
            _cachedCombatHintTemplate.transform.parent != null)
        {
            return _cachedCombatHintTemplate;
        }

        _cachedTemplateHints = hints;
        _cachedTemplateGamepadActive = gamepadActive;
        _cachedCombatHintTemplate = ResolveCombatHintTemplate(hints, gamepadActive);
        return _cachedCombatHintTemplate;
    }

    private static string ResolveSecondaryAttackButtonLabel(bool gamepadActive)
    {
        string buttonName = gamepadActive ? "JoySecondaryAttack" : "SecondaryAttack";
        string boundKey = ZInput.instance?.GetBoundKeyString(buttonName, emptyStringOnMissing: true) ?? "";
        if (!string.IsNullOrWhiteSpace(boundKey))
        {
            return Localization.instance != null ? Localization.instance.Localize(boundKey) : boundKey;
        }

        return gamepadActive ? "RB" : "MMB";
    }

    private static string ResolveBlockButtonLabel(bool gamepadActive)
    {
        string buttonName = gamepadActive ? "JoyBlock" : "Block";
        string boundKey = ZInput.instance?.GetBoundKeyString(buttonName, emptyStringOnMissing: true) ?? "";
        if (!string.IsNullOrWhiteSpace(boundKey))
        {
            return Localization.instance != null ? Localization.instance.Localize(boundKey) : boundKey;
        }

        return gamepadActive ? "LT" : "Mouse-2";
    }

    private static void ApplyDetonateBlockHint(KeyHintCell hint, string buttonLabel)
    {
        string detonateLabel = SecondaryAttackLocalization.Localize(SecondaryAttackLocalization.HintDetonate, "Detonate");
        if (hint.HasKeyTexts)
        {
            DetonateBlockHintKeys[0] = buttonLabel;
            hint.Set(detonateLabel, DetonateBlockHintKeys, hideExtraTexts: true);
            return;
        }

        hint.SetText($"{detonateLabel} <mspace=0.6em>{buttonLabel}</mspace>");
    }

    private static GameObject? ResolveBlockHint(KeyHints hints, bool gamepadActive)
    {
        Transform? preferredGroup = ResolveCombatInputGroup(hints, gamepadActive);
        GameObject? blockHint = FindBlockHint(preferredGroup);
        if (blockHint != null)
        {
            return blockHint;
        }

        Transform? alternateGroup = ResolveCombatInputGroup(hints, !gamepadActive);
        return FindBlockHint(alternateGroup);
    }

    private static Transform? ResolveCombatInputGroup(KeyHints hints, bool gamepadActive)
    {
        GameObject? anchor = gamepadActive ? hints.m_primaryAttackGP : hints.m_primaryAttackKB;
        if (anchor?.transform.parent != null)
        {
            return anchor.transform.parent;
        }

        anchor = gamepadActive ? hints.m_secondaryAttackGP : hints.m_secondaryAttackKB;
        return anchor?.transform.parent;
    }

    private static GameObject? FindBlockHint(Transform? group)
    {
        if (group == null)
        {
            return null;
        }

        for (int i = 0; i < group.childCount; i++)
        {
            GameObject child = group.GetChild(i).gameObject;
            if ((string.Equals(child.name, "Block", System.StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(child.name, "Text - Block", System.StringComparison.OrdinalIgnoreCase)) &&
                KeyHintCell.IsUsableTemplate(child))
            {
                return child;
            }
        }

        TMP_Text[] texts = group.GetComponentsInChildren<TMP_Text>(includeInactive: true);
        for (int i = 0; i < texts.Length; i++)
        {
            TMP_Text text = texts[i];
            if (text == null || !ContainsBlockHintText(text.text))
            {
                continue;
            }

            Transform textTransform = text.transform;
            for (Transform? current = textTransform; current != null && current != group.parent; current = current.parent)
            {
                GameObject candidate = current.gameObject;
                if (KeyHintCell.IsUsableTemplate(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool ContainsBlockHintText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string text = value!;
        return text.IndexOf("$settings_block", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               text.IndexOf("Block", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static ItemDrop.ItemData? ResolveEquippedBow(Player? player)
    {
        if (player == null)
        {
            return null;
        }

        ItemDrop.ItemData currentWeapon = player.GetCurrentWeapon();
        if (IsBowDrawWeapon(currentWeapon))
        {
            return currentWeapon;
        }

        ItemDrop.ItemData rightItem = player.GetRightItem();
        if (IsBowDrawWeapon(rightItem))
        {
            return rightItem;
        }

        ItemDrop.ItemData leftItem = player.GetLeftItem();
        return IsBowDrawWeapon(leftItem) ? leftItem : null;
    }

    private static bool IsBowDrawWeapon(ItemDrop.ItemData? weapon)
    {
        return weapon?.m_shared != null &&
               weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow &&
               weapon.m_shared.m_attack.m_bowDraw;
    }

    private static GameObject? ResolveCombatHintTemplate(KeyHints hints, bool gamepadActive)
    {
        GameObject? preferredSecondary = gamepadActive ? hints.m_secondaryAttackGP : hints.m_secondaryAttackKB;
        if (KeyHintCell.IsUsableTemplate(preferredSecondary))
        {
            return preferredSecondary;
        }

        GameObject? alternateSecondary = gamepadActive ? hints.m_secondaryAttackKB : hints.m_secondaryAttackGP;
        if (KeyHintCell.IsUsableTemplate(alternateSecondary))
        {
            return alternateSecondary;
        }

        GameObject? preferredBowDraw = gamepadActive ? hints.m_bowDrawGP : hints.m_bowDrawKB;
        if (KeyHintCell.IsUsableTemplate(preferredBowDraw))
        {
            return preferredBowDraw;
        }

        GameObject? alternateBowDraw = gamepadActive ? hints.m_bowDrawKB : hints.m_bowDrawGP;
        if (KeyHintCell.IsUsableTemplate(alternateBowDraw))
        {
            return alternateBowDraw;
        }

        GameObject? preferredPrimary = gamepadActive ? hints.m_primaryAttackGP : hints.m_primaryAttackKB;
        if (KeyHintCell.IsUsableTemplate(preferredPrimary))
        {
            return preferredPrimary;
        }

        GameObject? alternatePrimary = gamepadActive ? hints.m_primaryAttackKB : hints.m_primaryAttackGP;
        if (KeyHintCell.IsUsableTemplate(alternatePrimary))
        {
            return alternatePrimary;
        }

        return null;
    }

    private static void HideBowSecondaryHint()
    {
        if (!_showingBowSecondaryHint)
        {
            return;
        }

        _bowSecondaryHint?.Restore();
        _bowSecondaryHint?.RebuildParentLayout();
        _showingBowSecondaryHint = false;
        _keyHintApplied = false;
        _lastButtonLabel = string.Empty;
    }

    private static void RestoreDetonateBlockHint()
    {
        if (!_showingDetonateBlockHint && !_detonateBlockHintApplied)
        {
            return;
        }

        _detonateBlockHint?.Restore();
        _detonateBlockHint?.RebuildParentLayout();
        _showingDetonateBlockHint = false;
        _detonateBlockHintApplied = false;
        _lastDetonateBlockButtonLabel = string.Empty;
    }

    private static void DestroyBowSecondaryHint()
    {
        if (_bowSecondaryHint?.Root != null)
        {
            Object.Destroy(_bowSecondaryHint.Root);
        }

        _bowSecondaryHint = null;
        _bowSecondaryHintTemplate = null;
        _cachedTemplateHints = null;
        _cachedCombatHintTemplate = null;
        _keyHintApplied = false;
        _lastButtonLabel = string.Empty;
    }

}

[HarmonyPatch(typeof(KeyHints), "Awake")]
internal static class KeyHintsAwakeBowSecondaryPatch
{
    private static void Postfix(KeyHints __instance)
    {
        BowSecondaryKeyHintSystem.InitializeKeyHints(__instance);
    }
}

[HarmonyPatch(typeof(KeyHints), "UpdateHints")]
internal static class KeyHintsUpdateBowSecondaryPatch
{
    private static void Postfix(KeyHints __instance)
    {
        BowSecondaryKeyHintSystem.UpdateKeyHint(__instance);
    }
}
