using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class SneakAmbushChargeSystem
{
    private const string StatusEffectName = "SecondaryAttacks_SneakAmbushCharge";
    private const string FallbackIconPrefabName = "KnifeWood";
    private const float ChargeDecayPerSecond = 1f;

    private static readonly ConditionalWeakTable<Player, SneakAmbushChargeState> States = new();

    internal static void RegisterStatusEffect(ObjectDB objectDb)
    {
        if (objectDb == null ||
            objectDb.m_StatusEffects.Exists(statusEffect => statusEffect != null && ((Object)statusEffect).name == StatusEffectName))
        {
            return;
        }

        SneakAmbushChargeStatusEffect statusEffect = ScriptableObject.CreateInstance<SneakAmbushChargeStatusEffect>();
        statusEffect.Initialize(ResolveIcon(objectDb, FallbackIconPrefabName));
        objectDb.m_StatusEffects.Add(statusEffect);
    }

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
            RemoveStatus(player);
            return;
        }

        if (secondaryAttackActive ||
            MeleePresetCooldownSystem.IsCooldownActive(player, weapon, "sneakAmbush", out _))
        {
            state.ChargeSeconds = 0f;
            state.ClearDisplay();
            RemoveStatus(player);
            return;
        }

        float maxSeconds = Mathf.Max(0f, sneakAmbush!.ChargeMaxSeconds);
        if (maxSeconds <= 0f)
        {
            state.Clear();
            RemoveStatus(player);
            return;
        }

        float dt = Mathf.Max(0f, Time.deltaTime);
        bool isSneaking = player.IsCrouching();
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

        if (state.Display)
        {
            ApplyStatus(player, weapon, state);
        }
        else
        {
            RemoveStatus(player);
        }
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
        RemoveStatus(player);
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
        RemoveStatus(player);
    }

    internal static bool TryGetSnapshot(out Snapshot snapshot)
    {
        snapshot = Snapshot.Empty;
        Player? player = Player.m_localPlayer;
        if (player == null ||
            !States.TryGetValue(player, out SneakAmbushChargeState state) ||
            !state.Display ||
            state.MaxSeconds <= 0f)
        {
            return false;
        }

        snapshot = new Snapshot(
            Mathf.Clamp(state.ChargeSeconds, 0f, state.MaxSeconds),
            state.MaxSeconds);
        return true;
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

    private static void ApplyStatus(Player player, ItemDrop.ItemData? weapon, SneakAmbushChargeState state)
    {
        if (SecondaryCooldownHudSystem.ShouldSuppressCooldownStatusEffects(player))
        {
            if (!state.HudClearedStatus)
            {
                RemoveStatus(player);
                state.HudClearedStatus = true;
            }

            return;
        }

        state.HudClearedStatus = false;
        SEMan? seMan = player.GetSEMan();
        if (seMan == null)
        {
            return;
        }

        if (seMan.GetStatusEffect(StatusHash) == null)
        {
            seMan.AddStatusEffect(StatusHash, resetTime: true, itemLevel: 0, skillLevel: 0f);
        }

        if (seMan.GetStatusEffect(StatusHash) is StatusEffect statusEffect)
        {
            statusEffect.m_icon = ResolveWeaponIcon(weapon) ?? statusEffect.m_icon;
        }
    }

    private static void RemoveStatus(Player player)
    {
        player.GetSEMan()?.RemoveStatusEffect(StatusHash, quiet: true);
    }

    private static int StatusHash => StatusEffectName.GetStableHashCode();

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

    internal readonly struct Snapshot
    {
        internal static readonly Snapshot Empty = new(0f, 1f);

        internal Snapshot(float chargeSeconds, float maxSeconds)
        {
            ChargeSeconds = chargeSeconds;
            MaxSeconds = Mathf.Max(0.001f, maxSeconds);
        }

        internal float ChargeSeconds { get; }

        internal float MaxSeconds { get; }

        internal float ChargeFraction => Mathf.Clamp01(ChargeSeconds / MaxSeconds);
    }

    private sealed class SneakAmbushChargeState
    {
        public float ChargeSeconds { get; set; }

        public float PendingAttackChargeSeconds { get; set; }

        public bool HasPendingAttackCharge { get; set; }

        public float MaxSeconds { get; set; }

        public bool Display { get; set; }

        public bool HudClearedStatus { get; set; }

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
            HudClearedStatus = false;
            ClearPendingAttack();
            ClearDisplay();
        }
    }
}

internal sealed class SneakAmbushChargeStatusEffect : StatusEffect
{
    public void Initialize(Sprite? icon)
    {
        ((Object)this).name = "SecondaryAttacks_SneakAmbushCharge";
        m_name = SecondaryAttackLocalization.StatusSneakAmbushCharge;
        m_category = "SecondaryAttacks";
        m_tooltip = SecondaryAttackLocalization.TooltipSneakAmbushCharge;
        m_icon = icon;
        m_startMessage = "";
        m_stopMessage = "";
    }

    public override bool CanAdd(Character character)
    {
        return character == Player.m_localPlayer;
    }

    public override bool IsDone()
    {
        return !SneakAmbushChargeSystem.TryGetSnapshot(out _);
    }

    public override string GetIconText()
    {
        return SneakAmbushChargeSystem.TryGetSnapshot(out SneakAmbushChargeSystem.Snapshot snapshot)
            ? $"{Mathf.RoundToInt(snapshot.ChargeFraction * 100f)}%"
            : "";
    }

    public override string GetTooltipString()
    {
        if (!SneakAmbushChargeSystem.TryGetSnapshot(out SneakAmbushChargeSystem.Snapshot snapshot))
        {
            return "";
        }

        return SecondaryAttackLocalization.Format(
            SecondaryAttackLocalization.TooltipSneakAmbushChargeProgress,
            "Prepared: {0} / {1}s\nHigher Sneak skill charges faster.",
            snapshot.ChargeSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture),
            snapshot.MaxSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
    }
}
