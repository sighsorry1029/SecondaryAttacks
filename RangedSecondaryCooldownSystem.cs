using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class RangedSecondaryCooldownSystem
{
    private const string StatusEffectName = "SecondaryAttacks_Cooldown_rangedSecondary";
    private const string FallbackIconPrefabName = "Bow";

    private static readonly ConditionalWeakTable<Character, CharacterCooldownState> Cooldowns = new();

    internal static void RegisterStatusEffect(ObjectDB objectDb)
    {
        if (objectDb == null ||
            objectDb.m_StatusEffects.Exists(statusEffect => statusEffect != null && ((Object)statusEffect).name == StatusEffectName))
        {
            return;
        }

        MeleePresetCooldownStatusEffect statusEffect = ScriptableObject.CreateInstance<MeleePresetCooldownStatusEffect>();
        statusEffect.Initialize(
            StatusEffectName,
            SecondaryAttackLocalization.StatusSecondaryCooldown,
            SecondaryAttackLocalization.TooltipSecondaryRecharging,
            ResolveIcon(objectDb, FallbackIconPrefabName));
        objectDb.m_StatusEffects.Add(statusEffect);
    }

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

        if (!Cooldowns.TryGetValue(player, out CharacterCooldownState state))
        {
            if (!SecondaryCooldownHudSystem.ShouldSuppressCooldownStatusEffects(player))
            {
                ClearCooldownStatus(player);
            }

            return;
        }

        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        bool suppressStatusEffects = SecondaryCooldownHudSystem.ShouldSuppressCooldownStatusEffects(player);
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
        if (suppressStatusEffects)
        {
            if (state.ReadyAtByWeaponKey.Count > 0 && !state.HudClearedStatus)
            {
                ClearCooldownStatus(player);
                state.HudClearedStatus = true;
            }
            else if (state.ReadyAtByWeaponKey.Count == 0)
            {
                state.HudClearedStatus = false;
            }

            return;
        }

        state.HudClearedStatus = false;
        ItemDrop.ItemData? weapon = player.GetCurrentWeapon();
        string currentKey = ResolveWeaponKey(weapon);
        if (!string.IsNullOrWhiteSpace(currentKey) &&
            state.ReadyAtByWeaponKey.TryGetValue(currentKey, out double currentReadyAt) &&
            currentReadyAt > now)
        {
            SyncCooldownStatusTtl(player, weapon, (float)Math.Max(0d, currentReadyAt - now));
            return;
        }

        ClearCooldownStatus(player);
    }

    private static bool CanUse(Character attacker, ItemDrop.ItemData? weapon, ProjectileSecondaryBehavior behavior)
    {
        if (attacker == null || behavior == null || Mathf.Max(0f, behavior.Cooldown) <= 0f)
        {
            return true;
        }

        string key = ResolveWeaponKey(weapon);
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        CharacterCooldownState state = Cooldowns.GetValue(attacker, _ => new CharacterCooldownState());
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        if (!state.ReadyAtByWeaponKey.TryGetValue(key, out double readyAt) || readyAt <= now)
        {
            state.ReadyAtByWeaponKey.Remove(key);
            state.DurationByWeaponKey.Remove(key);
            state.IconByWeaponKey.Remove(key);
            SyncCooldownStatusTtl(attacker, weapon, 0f);
            return true;
        }

        SyncCooldownStatusTtl(attacker, weapon, (float)Math.Max(0d, readyAt - now));
        return false;
    }

    private static bool StartCooldown(Character attacker, ItemDrop.ItemData? weapon, ProjectileSecondaryBehavior behavior)
    {
        if (attacker == null || behavior == null)
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
        state.ReadyAtByWeaponKey[key] = SecondaryAttackManager.GetNetworkTimeSeconds() + finalCooldown;
        state.DurationByWeaponKey[key] = finalCooldown;
        state.IconByWeaponKey[key] = ResolveWeaponIcon(weapon) ?? ResolveRegisteredIcon();
        state.HudClearedStatus = false;
        if (SecondaryCooldownHudSystem.ShouldSuppressCooldownStatusEffects(attacker))
        {
            ClearCooldownStatus(attacker);
            state.HudClearedStatus = true;
        }
        else
        {
            SyncCooldownStatusTtl(attacker, weapon, finalCooldown);
        }

        return true;
    }

    internal static void CollectHudEntries(Player player, List<SecondaryCooldownHudSystem.Entry> entries)
    {
        if (player == null || entries == null || !Cooldowns.TryGetValue(player, out CharacterCooldownState state))
        {
            return;
        }

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

    private static void SyncCooldownStatusTtl(Character attacker, ItemDrop.ItemData? weapon, float remaining)
    {
        if (attacker == null)
        {
            return;
        }

        if (SecondaryCooldownHudSystem.ShouldSuppressCooldownStatusEffects(attacker))
        {
            ClearCooldownStatus(attacker);
            return;
        }

        remaining = Mathf.Max(0f, remaining);
        SEMan? seMan = attacker.GetSEMan();
        if (seMan == null)
        {
            return;
        }

        int statusHash = StatusEffectName.GetStableHashCode();
        if (seMan.GetStatusEffect(statusHash) is StatusEffect statusEffect)
        {
            statusEffect.m_ttl = statusEffect.m_time + remaining;
            if (remaining > 0f)
            {
                statusEffect.m_icon = ResolveWeaponIcon(weapon) ?? statusEffect.m_icon;
            }

            return;
        }

        if (remaining <= 0f)
        {
            return;
        }

        seMan.AddStatusEffect(statusHash, resetTime: true, itemLevel: 0, skillLevel: 0f);
        if (seMan.GetStatusEffect(statusHash) is StatusEffect addedStatus)
        {
            addedStatus.m_ttl = remaining;
            addedStatus.m_icon = ResolveWeaponIcon(weapon) ?? addedStatus.m_icon;
        }
    }

    private static void ClearCooldownStatus(Character attacker)
    {
        if (attacker == null)
        {
            return;
        }

        SEMan? seMan = attacker.GetSEMan();
        if (seMan?.GetStatusEffect(StatusEffectName.GetStableHashCode()) is StatusEffect statusEffect)
        {
            statusEffect.m_ttl = statusEffect.m_time;
        }
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
        public Dictionary<string, double> ReadyAtByWeaponKey { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, float> DurationByWeaponKey { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Sprite?> IconByWeaponKey { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> UpdateKeys { get; } = new();

        public bool HudClearedStatus { get; set; }
    }
}
