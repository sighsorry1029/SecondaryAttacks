using System;
using UnityEngine;

namespace SecondaryAttacks;

internal static class MeleePresetCooldownSystem
{
    internal static bool TryConsume(
        Character attacker,
        ItemDrop.ItemData? weapon,
        string presetName,
        MeleePresetCooldownDefinition cooldown)
    {
        if (attacker == null)
        {
            return true;
        }

        if (cooldown == null)
        {
            return true;
        }

        string cooldownGroup =
            SecondaryAttackCooldownGroupResolver.ResolveMeleeGroup(presetName, cooldown);
        weapon ??= ResolveCurrentWeapon(attacker);
        float baseCooldown = Mathf.Max(0f, cooldown.Cooldown);
        float skillLevel = ResolveCooldownSkillLevel(attacker, weapon, cooldown.CooldownSkill);
        float reduction = Mathf.Clamp01(skillLevel / 100f) * Mathf.Clamp01(cooldown.CooldownReductionFactor);
        float finalCooldown = Mathf.Max(0f, baseCooldown * (1f - reduction));
        Sprite? icon = ResolveWeaponIcon(weapon) ?? ResolveRegisteredIcon(presetName);
        return SecondaryCooldownGroupSystem.TryConsume(
            attacker,
            cooldownGroup,
            finalCooldown,
            icon);
    }

    internal static bool IsReady(
        Character attacker,
        string presetName,
        MeleePresetCooldownDefinition cooldown)
    {
        if (attacker == null)
        {
            return true;
        }

        if (cooldown == null)
        {
            return true;
        }

        string cooldownGroup =
            SecondaryAttackCooldownGroupResolver.ResolveMeleeGroup(presetName, cooldown);
        return SecondaryCooldownGroupSystem.IsReady(attacker, cooldownGroup);
    }

    internal static bool IsCooldownActive(
        Character attacker,
        string presetName,
        MeleePresetCooldownDefinition cooldown,
        out float remaining)
    {
        remaining = 0f;
        if (attacker == null)
        {
            return false;
        }

        if (cooldown == null)
        {
            return false;
        }

        string cooldownGroup =
            SecondaryAttackCooldownGroupResolver.ResolveMeleeGroup(presetName, cooldown);
        return SecondaryCooldownGroupSystem.IsCooldownActive(
            attacker,
            cooldownGroup,
            out remaining);
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

    private static float ResolveCooldownSkillLevel(
        Character attacker,
        ItemDrop.ItemData? weapon,
        string configuredSkill)
    {
        if (!TryResolveSkillType(configuredSkill, weapon, out Skills.SkillType skillType))
        {
            return 0f;
        }

        return Mathf.Clamp(attacker.GetSkillLevel(skillType), 0f, 100f);
    }

    private static bool TryResolveSkillType(
        string configuredSkill,
        ItemDrop.ItemData? weapon,
        out Skills.SkillType skillType)
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

        return Enum.TryParse(candidate, true, out skillType) &&
               skillType != Skills.SkillType.None;
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
}
