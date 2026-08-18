using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryCooldownGroupSystem
{
    private static readonly ConditionalWeakTable<Character, CharacterCooldownState> Cooldowns = new();

    internal static bool IsReady(Character attacker, string cooldownGroup)
    {
        if (attacker == null || string.IsNullOrWhiteSpace(cooldownGroup))
        {
            return true;
        }

        if (ShouldBypassCooldowns(attacker))
        {
            return true;
        }

        string key = cooldownGroup.Trim();
        CharacterCooldownState state = Cooldowns.GetValue(attacker, _ => new CharacterCooldownState());
        EnsureCurrentApplyRevision(state);
        return !TryGetActive(state, key, SecondaryAttackManager.GetNetworkTimeSeconds(), out _);
    }

    internal static bool TryConsume(
        Character attacker,
        string cooldownGroup,
        float duration,
        Sprite? icon)
    {
        if (attacker == null)
        {
            return true;
        }

        if (ShouldBypassCooldowns(attacker))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(cooldownGroup))
        {
            return true;
        }

        string key = cooldownGroup.Trim();
        CharacterCooldownState state = Cooldowns.GetValue(attacker, _ => new CharacterCooldownState());
        EnsureCurrentApplyRevision(state);
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        if (TryGetActive(state, key, now, out _))
        {
            return false;
        }

        state.EntriesByGroup.Remove(key);
        float normalizedDuration = Mathf.Max(0f, duration);
        if (normalizedDuration <= 0f)
        {
            return true;
        }

        state.EntriesByGroup[key] = new CooldownEntry(
            now + normalizedDuration,
            normalizedDuration,
            icon);
        return true;
    }

    internal static bool IsCooldownActive(
        Character attacker,
        string cooldownGroup,
        out float remaining)
    {
        remaining = 0f;
        if (attacker == null || string.IsNullOrWhiteSpace(cooldownGroup))
        {
            return false;
        }

        if (ShouldBypassCooldowns(attacker))
        {
            return false;
        }

        string key = cooldownGroup.Trim();
        if (!Cooldowns.TryGetValue(attacker, out CharacterCooldownState state))
        {
            return false;
        }

        EnsureCurrentApplyRevision(state);
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        if (!TryGetActive(state, key, now, out CooldownEntry entry))
        {
            return false;
        }

        remaining = (float)Math.Max(0d, entry.ReadyAt - now);
        return true;
    }

    internal static void UpdateActiveCooldowns(Player player)
    {
        if (player == null)
        {
            return;
        }

        if (ShouldBypassCooldowns(player))
        {
            return;
        }

        if (!Cooldowns.TryGetValue(player, out CharacterCooldownState state))
        {
            return;
        }

        EnsureCurrentApplyRevision(state);
        PruneExpired(state, SecondaryAttackManager.GetNetworkTimeSeconds());
    }

    internal static void CollectHudEntries(
        Player player,
        List<SecondaryCooldownHudSystem.Entry> entries)
    {
        if (player == null || entries == null)
        {
            return;
        }

        if (ShouldBypassCooldowns(player))
        {
            return;
        }

        if (!Cooldowns.TryGetValue(player, out CharacterCooldownState state))
        {
            return;
        }

        EnsureCurrentApplyRevision(state);
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        PruneExpired(state, now);
        foreach (CooldownEntry entry in state.EntriesByGroup.Values)
        {
            float remaining = (float)Math.Max(0d, entry.ReadyAt - now);
            entries.Add(new SecondaryCooldownHudSystem.Entry(
                entry.Icon,
                remaining,
                entry.Duration));
        }
    }

    internal static void ClearAllCooldowns(Character attacker)
    {
        if (!Cooldowns.TryGetValue(attacker, out CharacterCooldownState state))
        {
            return;
        }

        state.EntriesByGroup.Clear();
        state.ApplyRevision = SecondaryAttackFacade.CurrentAppliedWorldSnapshot.ApplyRevision;
    }

    private static bool ShouldBypassCooldowns(Character attacker)
    {
        if (!SecondaryAttackAdminAccessSystem.ShouldBypassPresetCooldowns(attacker))
        {
            return false;
        }

        ClearAllCooldowns(attacker);
        return true;
    }

    private static bool TryGetActive(
        CharacterCooldownState state,
        string key,
        double now,
        out CooldownEntry entry)
    {
        if (state.EntriesByGroup.TryGetValue(key, out entry) && now < entry.ReadyAt)
        {
            return true;
        }

        state.EntriesByGroup.Remove(key);
        entry = default;
        return false;
    }

    private static void PruneExpired(CharacterCooldownState state, double now)
    {
        List<string> keys = state.UpdateKeys;
        keys.Clear();
        foreach ((string key, CooldownEntry entry) in state.EntriesByGroup)
        {
            if (entry.ReadyAt <= now)
            {
                keys.Add(key);
            }
        }

        foreach (string key in keys)
        {
            state.EntriesByGroup.Remove(key);
        }

        keys.Clear();
    }

    private static void EnsureCurrentApplyRevision(CharacterCooldownState state)
    {
        int applyRevision = SecondaryAttackFacade.CurrentAppliedWorldSnapshot.ApplyRevision;
        if (state.ApplyRevision == applyRevision)
        {
            return;
        }

        state.EntriesByGroup.Clear();
        state.ApplyRevision = applyRevision;
    }

    private readonly struct CooldownEntry
    {
        internal CooldownEntry(double readyAt, float duration, Sprite? icon)
        {
            ReadyAt = readyAt;
            Duration = duration;
            Icon = icon;
        }

        internal double ReadyAt { get; }

        internal float Duration { get; }

        internal Sprite? Icon { get; }
    }

    private sealed class CharacterCooldownState
    {
        public int ApplyRevision { get; set; }

        public Dictionary<string, CooldownEntry> EntriesByGroup { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public List<string> UpdateKeys { get; } = new();
    }
}
