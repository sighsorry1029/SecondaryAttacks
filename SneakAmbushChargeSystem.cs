using System.Runtime.CompilerServices;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SneakAmbushChargeSystem
{
    private const string FallbackIconPrefabName = "KnifeWood";
    private const float ChargeDecayPerSecond = 1f;

    private static readonly ConditionalWeakTable<Player, SneakAmbushChargeState> States = new();

    internal static void Update(Player player)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        SneakAmbushChargeState state = States.GetValue(player, _ => new SneakAmbushChargeState());
        bool secondaryAttackActive = IsSecondaryAttackActive(player);
        if (!secondaryAttackActive)
        {
            state.ClearPendingAttack();
        }

        if (!TryResolveCurrentSneakAmbush(player, out ItemDrop.ItemData? weapon, out SneakAmbushDefinition? sneakAmbush))
        {
            state.Clear();
            return;
        }

        if (secondaryAttackActive ||
            MeleePresetCooldownSystem.IsCooldownActive(player, weapon, "sneakAmbush", out _))
        {
            state.ChargeSeconds = 0f;
            state.ClearDisplay();
            return;
        }

        float maxSeconds = Mathf.Max(0f, sneakAmbush!.ChargeMaxSeconds);
        if (maxSeconds <= 0f)
        {
            state.Clear();
            return;
        }

        float dt = Mathf.Max(0f, Time.deltaTime);
        bool isSneaking = player.IsCrouching() && !QuickstepSystem.IsActive(player);
        if (dt > 0f)
        {
            if (isSneaking)
            {
                float sneakLevel = Mathf.Clamp(player.GetSkillLevel(Skills.SkillType.Sneak), 0f, 100f);
                float chargeRate = Mathf.Lerp(1f, Mathf.Max(0f, sneakAmbush.ChargeSkillFactor), sneakLevel / 100f);
                state.ChargeSeconds += dt * chargeRate;
            }
            else
            {
                state.ChargeSeconds -= dt * ChargeDecayPerSecond;
            }
        }

        state.ChargeSeconds = Mathf.Clamp(state.ChargeSeconds, 0f, maxSeconds);
        state.MaxSeconds = maxSeconds;
        state.Display = isSneaking || state.ChargeSeconds > 0f;
    }

    internal static void BeginSecondaryAttack(Player player, ItemDrop.ItemData? weapon)
    {
        if (player == null || weapon == null)
        {
            return;
        }

        SecondaryAttackManager.EnsureRuntimeWeaponDefinitionApplied(weapon);
        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition) ||
            definition.SneakAmbush == null)
        {
            return;
        }

        SneakAmbushDefinition sneakAmbush = definition.SneakAmbush;
        SneakAmbushChargeState state = States.GetValue(player, _ => new SneakAmbushChargeState());
        float maxSeconds = Mathf.Max(0f, sneakAmbush.ChargeMaxSeconds);
        state.PendingAttackChargeSeconds = Mathf.Clamp(state.ChargeSeconds, 0f, maxSeconds);
        state.HasPendingAttackCharge = true;
        state.ChargeSeconds = 0f;
        state.ClearDisplay();
    }

    internal static float GetPreparedSeconds(Player player, SneakAmbushDefinition sneakAmbush)
    {
        if (player == null || sneakAmbush == null || !States.TryGetValue(player, out SneakAmbushChargeState state))
        {
            return 0f;
        }

        if (state.HasPendingAttackCharge)
        {
            return Mathf.Clamp(state.PendingAttackChargeSeconds, 0f, Mathf.Max(0f, sneakAmbush.ChargeMaxSeconds));
        }

        return Mathf.Clamp(state.ChargeSeconds, 0f, Mathf.Max(0f, sneakAmbush.ChargeMaxSeconds));
    }

    internal static void Consume(Player player)
    {
        if (player == null || !States.TryGetValue(player, out SneakAmbushChargeState state))
        {
            return;
        }

        state.ChargeSeconds = 0f;
        state.ClearPendingAttack();
        state.ClearDisplay();
    }

    internal static void CollectHudEntries(Player player, System.Collections.Generic.List<SecondaryCooldownHudSystem.Entry> entries)
    {
        if (player == null ||
            entries == null ||
            !States.TryGetValue(player, out SneakAmbushChargeState state) ||
            !state.Display ||
            state.MaxSeconds <= 0f)
        {
            return;
        }

        TryResolveCurrentSneakAmbush(player, out ItemDrop.ItemData? weapon, out _);
        float chargeSeconds = Mathf.Clamp(state.ChargeSeconds, 0f, state.MaxSeconds);
        entries.Add(new SecondaryCooldownHudSystem.Entry(
            ResolveWeaponIcon(weapon) ?? ResolveFallbackIcon(),
            chargeSeconds,
            state.MaxSeconds,
            "",
            SecondaryCooldownHudSystem.FillMode.Charge));
    }

    private static bool TryResolveCurrentSneakAmbush(Player player, out ItemDrop.ItemData? weapon, out SneakAmbushDefinition? sneakAmbush)
    {
        weapon = null;
        sneakAmbush = null;
        weapon = ((Humanoid)player).GetCurrentWeapon();
        if (weapon == null)
        {
            return false;
        }

        SecondaryAttackManager.EnsureRuntimeWeaponDefinitionApplied(weapon);
        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition) ||
            definition.SneakAmbush == null)
        {
            return false;
        }

        sneakAmbush = definition.SneakAmbush;
        return true;
    }

    private static bool IsSecondaryAttackActive(Player player)
    {
        Humanoid humanoid = player;
        return player.InAttack() && humanoid.m_currentAttack != null && humanoid.m_currentAttackIsSecondary;
    }

    private static Sprite? ResolveIcon(ObjectDB objectDb, string itemPrefabName)
    {
        ItemDrop? itemDrop = objectDb.GetItemPrefab(itemPrefabName)?.GetComponent<ItemDrop>();
        return itemDrop?.m_itemData?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }

    private static Sprite? ResolveWeaponIcon(ItemDrop.ItemData? weapon)
    {
        return weapon?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }

    private static Sprite? ResolveFallbackIcon()
    {
        return ObjectDB.instance != null ? ResolveIcon(ObjectDB.instance, FallbackIconPrefabName) : null;
    }

    private sealed class SneakAmbushChargeState
    {
        public float ChargeSeconds { get; set; }

        public float PendingAttackChargeSeconds { get; set; }

        public bool HasPendingAttackCharge { get; set; }

        public float MaxSeconds { get; set; }

        public bool Display { get; set; }

        public void ClearDisplay()
        {
            MaxSeconds = 0f;
            Display = false;
        }

        public void ClearPendingAttack()
        {
            PendingAttackChargeSeconds = 0f;
            HasPendingAttackCharge = false;
        }

        public void Clear()
        {
            ChargeSeconds = 0f;
            ClearPendingAttack();
            ClearDisplay();
        }
    }
}
