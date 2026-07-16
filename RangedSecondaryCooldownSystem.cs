using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SecondaryAttacks;

internal static class RangedSecondaryCooldownSystem
{
    private const string FallbackIconPrefabName = "Bow";

    private static readonly ConditionalWeakTable<Character, CharacterCooldownState> Cooldowns = new();

    internal static bool CanStart(Humanoid humanoid, ItemDrop.ItemData? weapon)
    {
        if (humanoid == null ||
            weapon == null ||
            !SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition) ||
            definition.Behavior is not ProjectileSecondaryBehavior projectileBehavior)
        {
            return true;
        }

        return CanUse(humanoid, weapon, projectileBehavior);
    }

    internal static bool CanUse(Attack attack, ProjectileSecondaryBehavior behavior)
    {
        if (attack?.m_character == null)
        {
            return true;
        }

        return CanUse(attack.m_character, attack.m_weapon, behavior);
    }

    internal static bool StartCooldown(Attack attack, ProjectileSecondaryBehavior behavior)
    {
        if (attack?.m_character == null)
        {
            return true;
        }

        return StartCooldown(attack.m_character, attack.m_weapon, behavior);
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
        List<string> expiredKeys = state.UpdateKeys;
        expiredKeys.Clear();
        foreach ((string key, double readyAt) in state.ReadyAtByWeaponKey)
        {
            if (readyAt <= now)
            {
                expiredKeys.Add(key);
            }
        }

        foreach (string key in expiredKeys)
        {
            state.ReadyAtByWeaponKey.Remove(key);
            state.DurationByWeaponKey.Remove(key);
            state.IconByWeaponKey.Remove(key);
        }

        expiredKeys.Clear();
    }

    private static bool CanUse(Character attacker, ItemDrop.ItemData? weapon, ProjectileSecondaryBehavior behavior)
    {
        if (attacker == null)
        {
            return true;
        }

        if (SecondaryAttackAdminAccessSystem.ShouldBypassPresetCooldowns(attacker))
        {
            ClearAllCooldowns(attacker);
            return true;
        }

        if (behavior == null || Mathf.Max(0f, behavior.Cooldown) <= 0f)
        {
            return true;
        }

        string key = ResolveWeaponKey(weapon);
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        CharacterCooldownState state = Cooldowns.GetValue(attacker, _ => new CharacterCooldownState());
        EnsureCurrentApplyRevision(state);
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        if (!state.ReadyAtByWeaponKey.TryGetValue(key, out double readyAt) || readyAt <= now)
        {
            state.ReadyAtByWeaponKey.Remove(key);
            state.DurationByWeaponKey.Remove(key);
            state.IconByWeaponKey.Remove(key);
            return true;
        }

        return false;
    }

    private static bool StartCooldown(Character attacker, ItemDrop.ItemData? weapon, ProjectileSecondaryBehavior behavior)
    {
        if (attacker == null)
        {
            return true;
        }

        if (SecondaryAttackAdminAccessSystem.ShouldBypassPresetCooldowns(attacker))
        {
            ClearAllCooldowns(attacker);
            return true;
        }

        if (behavior == null)
        {
            return true;
        }

        float baseCooldown = Mathf.Max(0f, behavior.Cooldown);
        if (baseCooldown <= 0f)
        {
            return true;
        }

        string key = ResolveWeaponKey(weapon);
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        float skillLevel = ResolveCooldownSkillLevel(attacker, weapon);
        float reduction = Mathf.Clamp01(skillLevel / 100f) * Mathf.Clamp01(behavior.CooldownReductionFactor);
        float finalCooldown = Mathf.Max(0f, baseCooldown * (1f - reduction));
        if (finalCooldown <= 0f)
        {
            return true;
        }

        CharacterCooldownState state = Cooldowns.GetValue(attacker, _ => new CharacterCooldownState());
        EnsureCurrentApplyRevision(state);
        state.ReadyAtByWeaponKey[key] = SecondaryAttackManager.GetNetworkTimeSeconds() + finalCooldown;
        state.DurationByWeaponKey[key] = finalCooldown;
        state.IconByWeaponKey[key] = ResolveWeaponIcon(weapon) ?? ResolveRegisteredIcon();

        return true;
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
        List<string> keys = state.UpdateKeys;
        keys.Clear();
        foreach (string key in state.ReadyAtByWeaponKey.Keys)
        {
            keys.Add(key);
        }

        foreach (string key in keys)
        {
            if (!state.ReadyAtByWeaponKey.TryGetValue(key, out double readyAt) || readyAt <= now)
            {
                state.ReadyAtByWeaponKey.Remove(key);
                state.DurationByWeaponKey.Remove(key);
                state.IconByWeaponKey.Remove(key);
                continue;
            }

            float remaining = (float)Math.Max(0d, readyAt - now);
            float duration = state.DurationByWeaponKey.TryGetValue(key, out float storedDuration)
                ? storedDuration
                : remaining;
            Sprite? icon = state.IconByWeaponKey.TryGetValue(key, out Sprite? storedIcon)
                ? storedIcon
                : ResolveRegisteredIcon();
            entries.Add(new SecondaryCooldownHudSystem.Entry(icon, remaining, duration));
        }

        keys.Clear();
    }

    private static void ClearAllCooldowns(Character attacker)
    {
        if (Cooldowns.TryGetValue(attacker, out CharacterCooldownState state))
        {
            state.ReadyAtByWeaponKey.Clear();
            state.DurationByWeaponKey.Clear();
            state.IconByWeaponKey.Clear();
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

        state.ReadyAtByWeaponKey.Clear();
        state.DurationByWeaponKey.Clear();
        state.IconByWeaponKey.Clear();
        state.ApplyRevision = applyRevision;
    }

    private static string ResolveWeaponKey(ItemDrop.ItemData? weapon)
    {
        return weapon?.m_dropPrefab != null
            ? weapon.m_dropPrefab.name
            : "";
    }

    private static float ResolveCooldownSkillLevel(Character attacker, ItemDrop.ItemData? weapon)
    {
        if (weapon?.m_shared == null || weapon.m_shared.m_skillType == Skills.SkillType.None)
        {
            return 0f;
        }

        return Mathf.Clamp(attacker.GetSkillLevel(weapon.m_shared.m_skillType), 0f, 100f);
    }

    private static Sprite? ResolveWeaponIcon(ItemDrop.ItemData? weapon)
    {
        return weapon?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }

    private static Sprite? ResolveIcon(ObjectDB objectDb, string itemPrefabName)
    {
        ItemDrop? itemDrop = objectDb.GetItemPrefab(itemPrefabName)?.GetComponent<ItemDrop>();
        return itemDrop?.m_itemData?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }

    private static Sprite? ResolveRegisteredIcon()
    {
        return ObjectDB.instance != null ? ResolveIcon(ObjectDB.instance, FallbackIconPrefabName) : null;
    }

    private sealed class CharacterCooldownState
    {
        public int ApplyRevision { get; set; }

        public Dictionary<string, double> ReadyAtByWeaponKey { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, float> DurationByWeaponKey { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Sprite?> IconByWeaponKey { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> UpdateKeys { get; } = new();
    }
}
