using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SecondaryAttacks;

internal static class MeleePresetCooldownSystem
{
    private static readonly ConditionalWeakTable<Character, CharacterCooldownState> Cooldowns = new();

    internal static bool TryConsume(
        Character attacker,
        ItemDrop.ItemData? weapon,
        string presetName,
        MeleePresetCooldownDefinition cooldown,
        out float finalCooldown)
    {
        finalCooldown = 0f;
        if (attacker == null)
        {
            return true;
        }

        if (SecondaryAttackAdminAccessSystem.ShouldBypassPresetCooldowns(attacker))
        {
            ClearCooldown(attacker, presetName);
            return true;
        }

        if (cooldown == null)
        {
            return true;
        }

        float baseCooldown = Mathf.Max(0f, cooldown.Cooldown);
        if (baseCooldown <= 0f)
        {
            return true;
        }

        weapon ??= ResolveCurrentWeapon(attacker);
        float skillLevel = ResolveCooldownSkillLevel(attacker, weapon, cooldown.CooldownSkill);
        float reduction = Mathf.Clamp01(skillLevel / 100f) * Mathf.Clamp01(cooldown.CooldownReductionFactor);
        finalCooldown = Mathf.Max(0f, baseCooldown * (1f - reduction));
        if (finalCooldown <= 0f)
        {
            return true;
        }

        string key = string.IsNullOrWhiteSpace(presetName) ? "unknown" : presetName.Trim();
        CharacterCooldownState state = Cooldowns.GetValue(attacker, _ => new CharacterCooldownState());
        EnsureCurrentApplyRevision(state);
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        if (state.ReadyAtByPreset.TryGetValue(key, out double readyAt) && now < readyAt)
        {
            return false;
        }

        ClearCooldownState(state, key);

        state.ReadyAtByPreset[key] = now + finalCooldown;
        state.DurationByPreset[key] = finalCooldown;
        state.IconByPreset[key] = ResolveWeaponIcon(weapon) ?? ResolveRegisteredIcon(key);
        return true;
    }

    internal static void UpdateActiveCooldowns(Player player)
    {
        if (player == null)
        {
            return;
        }

        if (SecondaryAttackAdminAccessSystem.ShouldBypassPresetCooldowns(player))
        {
            ClearAllCooldowns(player);
            return;
        }

        if (!Cooldowns.TryGetValue(player, out CharacterCooldownState state))
        {
            return;
        }

        EnsureCurrentApplyRevision(state);

        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        List<string> keys = state.UpdateKeys;
        keys.Clear();
        foreach (string key in state.ReadyAtByPreset.Keys)
        {
            keys.Add(key);
        }

        foreach (string key in keys)
        {
            if (!state.ReadyAtByPreset.TryGetValue(key, out double readyAt) || readyAt <= now)
            {
                ClearCooldownState(state, key);
            }
        }

        keys.Clear();
    }

    internal static bool IsReady(
        Character attacker,
        ItemDrop.ItemData? weapon,
        string presetName,
        MeleePresetCooldownDefinition cooldown)
    {
        if (attacker == null)
        {
            return true;
        }

        if (SecondaryAttackAdminAccessSystem.ShouldBypassPresetCooldowns(attacker))
        {
            ClearCooldown(attacker, presetName);
            return true;
        }

        if (cooldown == null || Mathf.Max(0f, cooldown.Cooldown) <= 0f)
        {
            return true;
        }

        string key = string.IsNullOrWhiteSpace(presetName) ? "unknown" : presetName.Trim();
        CharacterCooldownState state = Cooldowns.GetValue(attacker, _ => new CharacterCooldownState());
        EnsureCurrentApplyRevision(state);
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        if (state.ReadyAtByPreset.TryGetValue(key, out double readyAt) && now < readyAt)
        {
            return false;
        }

        ClearCooldownState(state, key);
        return true;
    }

    internal static bool IsCooldownActive(Character attacker, ItemDrop.ItemData? weapon, string presetName, out float remaining)
    {
        remaining = 0f;
        if (attacker == null)
        {
            return false;
        }

        if (SecondaryAttackAdminAccessSystem.ShouldBypassPresetCooldowns(attacker))
        {
            ClearCooldown(attacker, presetName);
            return false;
        }

        if (!Cooldowns.TryGetValue(attacker, out CharacterCooldownState state))
        {
            return false;
        }

        EnsureCurrentApplyRevision(state);

        string key = string.IsNullOrWhiteSpace(presetName) ? "unknown" : presetName.Trim();
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        if (state.ReadyAtByPreset.TryGetValue(key, out double readyAt) && now < readyAt)
        {
            remaining = (float)Math.Max(0d, readyAt - now);
            return true;
        }

        if (state.ReadyAtByPreset.ContainsKey(key))
        {
            ClearCooldownState(state, key);
        }

        return false;
    }

    internal static void CollectHudEntries(Player player, List<SecondaryCooldownHudSystem.Entry> entries)
    {
        if (player == null || entries == null)
        {
            return;
        }

        if (SecondaryAttackAdminAccessSystem.ShouldBypassPresetCooldowns(player))
        {
            ClearAllCooldowns(player);
            return;
        }

        if (!Cooldowns.TryGetValue(player, out CharacterCooldownState state))
        {
            return;
        }

        EnsureCurrentApplyRevision(state);

        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        ItemDrop.ItemData? weapon = ResolveCurrentWeapon(player);
        List<string> keys = state.UpdateKeys;
        keys.Clear();
        foreach (string key in state.ReadyAtByPreset.Keys)
        {
            keys.Add(key);
        }

        foreach (string key in keys)
        {
            if (!state.ReadyAtByPreset.TryGetValue(key, out double readyAt) || readyAt <= now)
            {
                ClearCooldownState(state, key);
                continue;
            }

            float remaining = (float)Math.Max(0d, readyAt - now);
            float duration = state.DurationByPreset.TryGetValue(key, out float storedDuration)
                ? storedDuration
                : remaining;
            Sprite? icon = state.IconByPreset.TryGetValue(key, out Sprite? storedIcon)
                ? storedIcon
                : ResolveWeaponIcon(weapon) ?? ResolveRegisteredIcon(key);
            entries.Add(new SecondaryCooldownHudSystem.Entry(icon, remaining, duration));
        }

        keys.Clear();
    }

    private static void ClearCooldownState(CharacterCooldownState state, string presetName)
    {
        state.ReadyAtByPreset.Remove(presetName);
        state.DurationByPreset.Remove(presetName);
        state.IconByPreset.Remove(presetName);
    }

    private static void ClearCooldown(Character attacker, string presetName)
    {
        string key = string.IsNullOrWhiteSpace(presetName) ? "unknown" : presetName.Trim();
        if (Cooldowns.TryGetValue(attacker, out CharacterCooldownState state))
        {
            EnsureCurrentApplyRevision(state);
            ClearCooldownState(state, key);
        }
    }

    private static void ClearAllCooldowns(Character attacker)
    {
        if (Cooldowns.TryGetValue(attacker, out CharacterCooldownState state))
        {
            state.ReadyAtByPreset.Clear();
            state.DurationByPreset.Clear();
            state.IconByPreset.Clear();
            state.ApplyRevision = SecondaryAttackFacade.CurrentAppliedWorldSnapshot.ApplyRevision;
        }
    }

    private static void EnsureCurrentApplyRevision(CharacterCooldownState state)
    {
        int applyRevision = SecondaryAttackFacade.CurrentAppliedWorldSnapshot.ApplyRevision;
        if (state.ApplyRevision == applyRevision)
        {
            return;
        }

        state.ReadyAtByPreset.Clear();
        state.DurationByPreset.Clear();
        state.IconByPreset.Clear();
        state.ApplyRevision = applyRevision;
    }

    private static Sprite? ResolveWeaponIcon(ItemDrop.ItemData? weapon)
    {
        return weapon?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }

    private static Sprite? ResolveRegisteredIcon(string presetName)
    {
        return SecondaryAttackPresetCatalog.ResolveIcon(presetName);
    }

    private static ItemDrop.ItemData? ResolveCurrentWeapon(Character attacker)
    {
        return attacker is Humanoid humanoid ? humanoid.GetCurrentWeapon() : null;
    }

    private static float ResolveCooldownSkillLevel(Character attacker, ItemDrop.ItemData? weapon, string configuredSkill)
    {
        if (!TryResolveSkillType(configuredSkill, weapon, out Skills.SkillType skillType))
        {
            return 0f;
        }

        return Mathf.Clamp(attacker.GetSkillLevel(skillType), 0f, 100f);
    }

    private static bool TryResolveSkillType(string configuredSkill, ItemDrop.ItemData? weapon, out Skills.SkillType skillType)
    {
        string normalized = NormalizeSkillToken(configuredSkill);
        if (string.IsNullOrEmpty(normalized) ||
            normalized == "weapon" ||
            normalized == "current" ||
            normalized == "equipped")
        {
            if (weapon?.m_shared == null || weapon.m_shared.m_skillType == Skills.SkillType.None)
            {
                skillType = Skills.SkillType.None;
                return false;
            }

            skillType = weapon.m_shared.m_skillType;
            return true;
        }

        if (normalized is "none" or "off" or "disabled")
        {
            skillType = Skills.SkillType.None;
            return false;
        }

        string candidate = normalized switch
        {
            "sword" or "swords" => "Swords",
            "knife" or "knives" => "Knives",
            "spear" or "spears" => "Spears",
            "club" or "clubs" or "mace" or "maces" => "Clubs",
            "fist" or "fists" or "unarmed" => "Unarmed",
            "axe" or "axes" => "Axes",
            "polearm" or "polearms" or "atgeir" or "atgeirs" => "Polearms",
            "pickaxe" or "pickaxes" => "Pickaxes",
            "bow" or "bows" => "Bows",
            "crossbow" or "crossbows" => "Crossbows",
            "sneak" or "sneaking" => "Sneak",
            "block" or "blocking" => "Blocking",
            "bloodmagic" or "blood" => "BloodMagic",
            "elementalmagic" or "elemental" => "ElementalMagic",
            "woodcutting" or "woodcut" => "WoodCutting",
            "farming" => "Farming",
            "fishing" => "Fishing",
            _ => configuredSkill?.Trim() ?? ""
        };

        return Enum.TryParse(candidate, true, out skillType) && skillType != Skills.SkillType.None;
    }

    private static string NormalizeSkillToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        return value!.Trim()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "")
            .ToLowerInvariant();
    }

    private sealed class CharacterCooldownState
    {
        public int ApplyRevision { get; set; }

        public Dictionary<string, double> ReadyAtByPreset { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, float> DurationByPreset { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Sprite?> IconByPreset { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> UpdateKeys { get; } = new();
    }
}
