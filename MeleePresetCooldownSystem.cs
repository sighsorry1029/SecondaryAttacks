using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class MeleePresetCooldownSystem
{
    private static readonly ConditionalWeakTable<Character, CharacterCooldownState> Cooldowns = new();
    private static readonly Dictionary<string, CooldownStatusRegistration> StatusesByPreset = BuildStatusMap();

    internal static void RegisterStatusEffects(ObjectDB objectDb)
    {
        if (objectDb == null)
        {
            return;
        }

        HashSet<string> registered = new(StringComparer.OrdinalIgnoreCase);
        foreach (CooldownStatusRegistration registration in StatusesByPreset.Values)
        {
            if (!registered.Add(registration.StatusEffectName) ||
                objectDb.m_StatusEffects.Exists(statusEffect => statusEffect != null && ((Object)statusEffect).name == registration.StatusEffectName))
            {
                continue;
            }

            MeleePresetCooldownStatusEffect statusEffect = ScriptableObject.CreateInstance<MeleePresetCooldownStatusEffect>();
            statusEffect.Initialize(
                registration.StatusEffectName,
                registration.DisplayNameToken,
                SecondaryAttackLocalization.TooltipSecondaryRecharging,
                ResolveIcon(objectDb, registration.IconPrefabName));
            objectDb.m_StatusEffects.Add(statusEffect);
        }
    }

    internal static bool TryConsume(
        Character attacker,
        ItemDrop.ItemData? weapon,
        string presetName,
        MeleePresetCooldownDefinition cooldown,
        out float finalCooldown)
    {
        finalCooldown = 0f;
        if (attacker == null || cooldown == null)
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
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        if (state.ReadyAtByPreset.TryGetValue(key, out double readyAt) && now < readyAt)
        {
            SyncCooldownStatusTtl(attacker, weapon, key, (float)Math.Max(0d, readyAt - now));
            return false;
        }

        SyncCooldownStatusTtl(attacker, weapon, key, 0f);
        ClearCooldownState(state, key);

        state.ReadyAtByPreset[key] = now + finalCooldown;
        state.DurationByPreset[key] = finalCooldown;
        state.IconByPreset[key] = ResolveWeaponIcon(weapon) ?? ResolveRegisteredIcon(key);
        state.HudClearedStatusByPreset.Remove(key);
        ApplyCooldownStatus(attacker, weapon, key, finalCooldown);
        return true;
    }

    internal static void UpdateActiveCooldowns(Player player)
    {
        if (player == null || !Cooldowns.TryGetValue(player, out CharacterCooldownState state))
        {
            return;
        }

        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        ItemDrop.ItemData? weapon = ResolveCurrentWeapon(player);
        bool suppressStatusEffects = SecondaryCooldownHudSystem.ShouldSuppressCooldownStatusEffects(player);
        if (!suppressStatusEffects)
        {
            state.HudClearedStatusByPreset.Clear();
        }

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
                if (!suppressStatusEffects)
                {
                    SyncCooldownStatusTtl(player, weapon, key, 0f);
                }

                ClearCooldownState(state, key);
                continue;
            }

            float remaining = (float)Math.Max(0d, readyAt - now);
            if (suppressStatusEffects)
            {
                if (state.HudClearedStatusByPreset.Add(key))
                {
                    ClearCooldownStatus(player, key);
                }

                continue;
            }

            SyncCooldownStatusTtl(player, weapon, key, remaining);
            if (remaining <= 0f)
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
        if (attacker == null || cooldown == null || Mathf.Max(0f, cooldown.Cooldown) <= 0f)
        {
            return true;
        }

        string key = string.IsNullOrWhiteSpace(presetName) ? "unknown" : presetName.Trim();
        CharacterCooldownState state = Cooldowns.GetValue(attacker, _ => new CharacterCooldownState());
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        if (state.ReadyAtByPreset.TryGetValue(key, out double readyAt) && now < readyAt)
        {
            SyncCooldownStatusTtl(attacker, weapon, key, (float)Math.Max(0d, readyAt - now));
            return false;
        }

        SyncCooldownStatusTtl(attacker, weapon, key, 0f);
        ClearCooldownState(state, key);
        return true;
    }

    internal static bool IsCooldownActive(Character attacker, ItemDrop.ItemData? weapon, string presetName, out float remaining)
    {
        remaining = 0f;
        if (attacker == null || !Cooldowns.TryGetValue(attacker, out CharacterCooldownState state))
        {
            return false;
        }

        string key = string.IsNullOrWhiteSpace(presetName) ? "unknown" : presetName.Trim();
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        if (state.ReadyAtByPreset.TryGetValue(key, out double readyAt) && now < readyAt)
        {
            remaining = (float)Math.Max(0d, readyAt - now);
            weapon ??= ResolveCurrentWeapon(attacker);
            SyncCooldownStatusTtl(attacker, weapon, key, remaining);
            return true;
        }

        if (state.ReadyAtByPreset.ContainsKey(key))
        {
            weapon ??= ResolveCurrentWeapon(attacker);
            SyncCooldownStatusTtl(attacker, weapon, key, 0f);
            ClearCooldownState(state, key);
        }

        return false;
    }

    internal static void CollectHudEntries(Player player, List<SecondaryCooldownHudSystem.Entry> entries)
    {
        if (player == null || entries == null || !Cooldowns.TryGetValue(player, out CharacterCooldownState state))
        {
            return;
        }

        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        ItemDrop.ItemData? weapon = ResolveCurrentWeapon(player);
        bool suppressStatusEffects = SecondaryCooldownHudSystem.ShouldSuppressCooldownStatusEffects(player);
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
                if (!suppressStatusEffects)
                {
                    SyncCooldownStatusTtl(player, weapon, key, 0f);
                }

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

    internal static string DescribeState(
        Character attacker,
        ItemDrop.ItemData? weapon,
        string presetName,
        MeleePresetCooldownDefinition cooldown)
    {
        if (attacker == null)
        {
            return "attacker=<null>";
        }

        if (cooldown == null)
        {
            return "cooldown=<null>";
        }

        float baseCooldown = Mathf.Max(0f, cooldown.Cooldown);
        string key = string.IsNullOrWhiteSpace(presetName) ? "unknown" : presetName.Trim();
        if (baseCooldown <= 0f)
        {
            return $"key={key} baseCooldown={baseCooldown:0.###} ready=true reason=no-cooldown";
        }

        weapon ??= ResolveCurrentWeapon(attacker);
        float skillLevel = ResolveCooldownSkillLevel(attacker, weapon, cooldown.CooldownSkill);
        float reduction = Mathf.Clamp01(skillLevel / 100f) * Mathf.Clamp01(cooldown.CooldownReductionFactor);
        float finalCooldown = Mathf.Max(0f, baseCooldown * (1f - reduction));
        CharacterCooldownState state = Cooldowns.GetValue(attacker, _ => new CharacterCooldownState());
        double now = SecondaryAttackManager.GetNetworkTimeSeconds();
        double tableRemaining = state.ReadyAtByPreset.TryGetValue(key, out double readyAt)
            ? Math.Max(0d, readyAt - now)
            : 0d;
        float statusTtl = TryGetActiveCooldownStatusTtl(attacker, key, out float ttl) ? ttl : 0f;
        bool ready = tableRemaining <= 0d;
        return
            $"key={key} baseCooldown={baseCooldown:0.###} finalCooldown={finalCooldown:0.###} skill={skillLevel:0.###} reduction={reduction:0.###} tableRemaining={tableRemaining:0.###} statusTtl={statusTtl:0.###} ready={ready}";
    }

    private static Dictionary<string, CooldownStatusRegistration> BuildStatusMap()
    {
        CooldownStatusRegistration[] registrations =
        [
            new("sneakAmbush", "KnifeWood", SecondaryAttackLocalization.StatusSneakAmbushCooldown),
            new("cleavingThrust", "THSwordWood", SecondaryAttackLocalization.StatusCleavingThrustCooldown),
            new("impactBurst", "Battleaxe", SecondaryAttackLocalization.StatusImpactBurstCooldown),
            new("boomerang", "AxeBronze", SecondaryAttackLocalization.StatusBoomerangCooldown),
            new("spinningSweep", "AtgeirWood", SecondaryAttackLocalization.StatusSpinningSweepCooldown),
            new("launchSlam", "MaceWood", SecondaryAttackLocalization.StatusLaunchSlamCooldown),
            new("knockbackChain", "FistBjornClaw", SecondaryAttackLocalization.StatusKnockbackChainCooldown),
            new("aftershock", "SledgeWood", SecondaryAttackLocalization.StatusAftershockCooldown),
            new("riftTrail", "SwordWood", SecondaryAttackLocalization.StatusRiftTrailCooldown),
            new("fractureLine", "PickaxeAntler", SecondaryAttackLocalization.StatusFractureLineCooldown),
            new("harvestSweep", "Scythe", SecondaryAttackLocalization.StatusHarvestSweepCooldown),
            new("spearRain", "SpearWood", SecondaryAttackLocalization.StatusSpearRainCooldown),
            new("summonEmpower", "StaffSkeleton", SecondaryAttackLocalization.StatusSummonEmpowerCooldown),
            new("shieldConvert", "StaffShield", SecondaryAttackLocalization.StatusShieldConvertCooldown)
        ];

        Dictionary<string, CooldownStatusRegistration> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (CooldownStatusRegistration registration in registrations)
        {
            map[registration.PresetName] = registration;
        }

        return map;
    }

    private static void ApplyCooldownStatus(Character attacker, ItemDrop.ItemData? weapon, string presetName, float cooldown)
    {
        if (attacker == null || cooldown <= 0f || !StatusesByPreset.TryGetValue(presetName, out CooldownStatusRegistration? registration))
        {
            return;
        }

        if (SecondaryCooldownHudSystem.ShouldSuppressCooldownStatusEffects(attacker))
        {
            ClearCooldownStatus(attacker, presetName);
            return;
        }

        SEMan? seMan = attacker.GetSEMan();
        if (seMan == null)
        {
            return;
        }

        int statusHash = registration.StatusEffectName.GetStableHashCode();
        seMan.AddStatusEffect(statusHash, resetTime: true, itemLevel: 0, skillLevel: 0f);
        if (seMan.GetStatusEffect(statusHash) is StatusEffect statusEffect)
        {
            statusEffect.m_ttl = cooldown;
            statusEffect.m_icon = ResolveWeaponIcon(weapon) ?? statusEffect.m_icon;
        }
    }

    private static bool TryGetActiveCooldownStatusTtl(Character attacker, string presetName, out float ttl)
    {
        if (!TryGetActiveCooldownStatus(attacker, presetName, out StatusEffect? statusEffect))
        {
            ttl = 0f;
            return false;
        }

        ttl = Mathf.Max(0f, statusEffect!.GetRemaningTime());
        return ttl > 0f;
    }

    private static void SyncCooldownStatusTtl(Character attacker, ItemDrop.ItemData? weapon, string presetName, float remaining)
    {
        remaining = Mathf.Max(0f, remaining);
        if (SecondaryCooldownHudSystem.ShouldSuppressCooldownStatusEffects(attacker))
        {
            ClearCooldownStatus(attacker, presetName);
            return;
        }

        if (TryGetActiveCooldownStatus(attacker, presetName, out StatusEffect? statusEffect))
        {
            statusEffect!.m_ttl = statusEffect.m_time + remaining;
            return;
        }

        if (remaining > 0f)
        {
            ApplyCooldownStatus(attacker, weapon, presetName, remaining);
        }
    }

    private static void ClearCooldownState(CharacterCooldownState state, string presetName)
    {
        state.ReadyAtByPreset.Remove(presetName);
        state.DurationByPreset.Remove(presetName);
        state.IconByPreset.Remove(presetName);
        state.HudClearedStatusByPreset.Remove(presetName);
    }

    private static void ClearCooldownStatus(Character attacker, string presetName)
    {
        if (attacker == null || !StatusesByPreset.TryGetValue(presetName, out CooldownStatusRegistration? registration))
        {
            return;
        }

        SEMan? seMan = attacker.GetSEMan();
        if (seMan?.GetStatusEffect(registration.StatusEffectName.GetStableHashCode()) is StatusEffect statusEffect)
        {
            statusEffect.m_ttl = statusEffect.m_time;
        }
    }

    private static bool TryGetActiveCooldownStatus(Character attacker, string presetName, out StatusEffect? statusEffect)
    {
        statusEffect = null;
        if (attacker == null || !StatusesByPreset.TryGetValue(presetName, out CooldownStatusRegistration? registration))
        {
            return false;
        }

        SEMan? seMan = attacker.GetSEMan();
        if (seMan == null)
        {
            return false;
        }

        int statusHash = registration.StatusEffectName.GetStableHashCode();
        statusEffect = seMan.GetStatusEffect(statusHash);
        return statusEffect != null && statusEffect.GetRemaningTime() > 0f;
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

    private static Sprite? ResolveRegisteredIcon(string presetName)
    {
        if (ObjectDB.instance == null || !StatusesByPreset.TryGetValue(presetName, out CooldownStatusRegistration? registration))
        {
            return null;
        }

        return ResolveIcon(ObjectDB.instance, registration.IconPrefabName);
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
        public Dictionary<string, double> ReadyAtByPreset { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, float> DurationByPreset { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, Sprite?> IconByPreset { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> UpdateKeys { get; } = new();

        public HashSet<string> HudClearedStatusByPreset { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CooldownStatusRegistration
    {
        public CooldownStatusRegistration(string presetName, string iconPrefabName, string displayNameToken)
        {
            PresetName = presetName;
            IconPrefabName = iconPrefabName;
            DisplayNameToken = displayNameToken;
            StatusEffectName = $"SecondaryAttacks_Cooldown_{presetName}";
        }

        public string PresetName { get; }

        public string StatusEffectName { get; }

        public string IconPrefabName { get; }

        public string DisplayNameToken { get; }
    }
}

internal sealed class MeleePresetCooldownStatusEffect : StatusEffect
{
    public void Initialize(string statusName, string displayName, string tooltip, Sprite? icon)
    {
        ((Object)this).name = statusName;
        m_name = displayName;
        m_tooltip = tooltip;
        m_icon = icon;
        m_ttl = 0f;
    }
}
