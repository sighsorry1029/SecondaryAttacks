using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class WeaponEffectManager
{
    private const string GeneratedStatusPrefix = "SecondaryAttacks_";

    private static readonly ConditionalWeakTable<Character, Dictionary<string, EffectStackState>> StackStates = new();
    private static bool _isApplyingEffectDamage;

    internal static bool IsApplyingGeneratedEffectDamage => _isApplyingEffectDamage;

    public static void ApplyToObjectDb(
        ObjectDB objectDb,
        IReadOnlyDictionary<string, SecondaryAttackDefinition> definitions,
        IReadOnlyDictionary<string, EffectBehaviorConfig> effectConfigs)
    {
        RemoveGeneratedStatuses(objectDb);
        MeleePresetCooldownSystem.RegisterStatusEffects(objectDb);
        SneakAmbushChargeSystem.RegisterStatusEffect(objectDb);
        RangedSecondaryCooldownSystem.RegisterStatusEffect(objectDb);
        RegisterRuntimeStatuses(objectDb, definitions.Values);
    }

    public static WeaponEffectDamageState? TryHandleCharacterDamagePrefix(Character target, ref HitData hit)
    {
        if (_isApplyingEffectDamage ||
            LaunchSlamSystem.IsApplyingLandingDamage ||
            KnockbackChainSystem.IsApplyingChainDamage ||
            MeleeProjectileHitCascadeSystem.IsApplyingImpactBurstDamage)
        {
            return null;
        }

        if (!DirectWeaponHitContextSystem.ShouldCountWeaponEffectHit)
        {
            return null;
        }

        if ((Object)(object)hit.GetAttacker() != (Object)(object)Player.m_localPlayer)
        {
            return null;
        }

        if (!TryGetEffectiveAttackContext(out Player localPlayer, out string weaponPrefabName, out bool secondaryAttack, out SecondaryAttackDefinition? definition))
        {
            return null;
        }

        if (definition?.SneakAmbush != null)
        {
            SneakAmbushSystem.TryTriggerForSecondaryHit(localPlayer, target, secondaryAttack, definition);
        }

        List<ConfiguredWeaponEffectDefinition>? postDamageEffects = null;
        if (definition != null)
        {
            WeaponEffectRuntimeCache effectCache = definition.EffectRuntimeCache;
            foreach (ConfiguredWeaponEffectDefinition effect in effectCache.GetImmediateEffects(secondaryAttack))
            {
                ApplyConfiguredEffect(localPlayer, target, weaponPrefabName, effect, ref hit);
            }

            ConfiguredWeaponEffectDefinition[] postDamageCandidates = effectCache.GetPostDamageEffects(secondaryAttack);
            foreach (ConfiguredWeaponEffectDefinition effect in postDamageCandidates)
            {
                if (!TryPreparePostDamageEffect(localPlayer, target, weaponPrefabName, effect))
                {
                    continue;
                }

                postDamageEffects ??= new List<ConfiguredWeaponEffectDefinition>(postDamageCandidates.Length);
                postDamageEffects.Add(effect);
            }
        }

        if (!KnockbackChainSystem.TryApplyForSecondaryHit(localPlayer, target, secondaryAttack, definition, ref hit))
        {
            LaunchSlamSystem.TryApplyForSecondaryHit(localPlayer, target, secondaryAttack, definition, ref hit);
        }

        return postDamageEffects is { Count: > 0 }
            ? new WeaponEffectDamageState(localPlayer, target, hit.Clone(), target.GetHealth(), postDamageEffects)
            : null;
    }

    public static void TryHandleCharacterDamagePostfix(WeaponEffectDamageState? state)
    {
        if (state == null ||
            state.Target == null ||
            state.Attacker == null)
        {
            return;
        }

        float actualDamage = Mathf.Max(0f, state.HealthBefore - state.Target.GetHealth());
        if (actualDamage <= 0f)
        {
            return;
        }

        foreach (ConfiguredWeaponEffectDefinition effect in state.Effects)
        {
            ApplyPostDamageEffect(state.Attacker, state.Target, state.SourceHit, effect, actualDamage);
        }
    }

    internal static float ResolveScalarValue(ScalarValueMode mode, float value, Character self, Character target)
    {
        return mode switch
        {
            ScalarValueMode.Fixed => value,
            ScalarValueMode.TargetMaxHealthPercent => target.GetMaxHealth() * value * 0.01f,
            ScalarValueMode.SelfMaxHealthPercent => self.GetMaxHealth() * value * 0.01f,
            ScalarValueMode.SelfMaxStaminaPercent => self is Player player ? player.GetMaxStamina() * value * 0.01f : 0f,
            _ => value
        };
    }

    internal static void SetDamageValue(ref HitData.DamageTypes damageTypes, HitData.DamageType damageType, float value)
    {
        switch (damageType)
        {
            case HitData.DamageType.Blunt:
                damageTypes.m_blunt = value;
                break;
            case HitData.DamageType.Slash:
                damageTypes.m_slash = value;
                break;
            case HitData.DamageType.Pierce:
                damageTypes.m_pierce = value;
                break;
            case HitData.DamageType.Chop:
                damageTypes.m_chop = value;
                break;
            case HitData.DamageType.Pickaxe:
                damageTypes.m_pickaxe = value;
                break;
            case HitData.DamageType.Fire:
                damageTypes.m_fire = value;
                break;
            case HitData.DamageType.Frost:
                damageTypes.m_frost = value;
                break;
            case HitData.DamageType.Lightning:
                damageTypes.m_lightning = value;
                break;
            case HitData.DamageType.Poison:
                damageTypes.m_poison = value;
                break;
            case HitData.DamageType.Spirit:
                damageTypes.m_spirit = value;
                break;
            default:
                damageTypes.m_damage = value;
                break;
        }
    }

    private static bool TryGetEffectiveAttackContext(
        out Player localPlayer,
        out string weaponPrefabName,
        out bool secondaryAttack,
        out SecondaryAttackDefinition? definition)
    {
        localPlayer = Player.m_localPlayer;
        weaponPrefabName = string.Empty;
        secondaryAttack = false;
        definition = null;
        if (localPlayer == null)
        {
            return false;
        }

        bool hasProjectileContext = SecondaryAttackRuntimeFacade.TryGetProjectileHitAttackContext(
            out weaponPrefabName,
            out secondaryAttack,
            out definition,
            out bool disableCurrentAttackFallback);
        if (hasProjectileContext)
        {
            return true;
        }

        if (disableCurrentAttackFallback)
        {
            return false;
        }

        return TryGetCurrentAttackContext(out localPlayer, out weaponPrefabName, out secondaryAttack, out definition);
    }

    private static bool TryGetCurrentAttackContext(
        out Player localPlayer,
        out string weaponPrefabName,
        out bool secondaryAttack,
        out SecondaryAttackDefinition? definition)
    {
        localPlayer = Player.m_localPlayer;
        weaponPrefabName = string.Empty;
        secondaryAttack = false;
        definition = null;

        if (localPlayer == null)
        {
            return false;
        }

        Attack? currentAttack = ((Humanoid)localPlayer).m_currentAttack;
        if (currentAttack?.m_weapon?.m_dropPrefab == null)
        {
            return false;
        }

        weaponPrefabName = currentAttack.m_weapon.m_dropPrefab.name;
        secondaryAttack = ((Humanoid)localPlayer).m_currentAttackIsSecondary;
        SecondaryAttackRuntimeFacade.TryGetDefinition(currentAttack.m_weapon, out definition!);
        return true;
    }

    private static void RemoveGeneratedStatuses(ObjectDB objectDb)
    {
        objectDb.m_StatusEffects.RemoveAll(statusEffect =>
            statusEffect != null &&
            ((Object)statusEffect).name.StartsWith(GeneratedStatusPrefix, StringComparison.Ordinal));
        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            ItemDrop? itemDrop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            StatusEffect? equipStatus = itemDrop?.m_itemData?.m_shared?.m_equipStatusEffect;
            if (equipStatus != null &&
                ((Object)equipStatus).name.StartsWith($"{GeneratedStatusPrefix}equip_", StringComparison.Ordinal))
            {
                itemDrop!.m_itemData.m_shared.m_equipStatusEffect = null;
            }
        }
    }

    private static void RegisterRuntimeStatuses(ObjectDB objectDb, IEnumerable<SecondaryAttackDefinition> definitions)
    {
        Dictionary<string, ConfiguredWeaponEffectDefinition> uniqueEffects = new(StringComparer.OrdinalIgnoreCase);
        foreach (ConfiguredWeaponEffectDefinition effect in definitions
                     .SelectMany(definition => definition.ConfiguredEffects)
                     .Where(effect => !string.IsNullOrWhiteSpace(effect.StatusEffectName)))
        {
            uniqueEffects.TryAdd(effect.StatusEffectName, effect);
        }

        foreach ((string _, ConfiguredWeaponEffectDefinition effect) in uniqueEffects)
        {
            StatusEffect? runtimeStatusEffect = CreateRuntimeStatusEffect(effect);
            if (runtimeStatusEffect != null)
            {
                objectDb.m_StatusEffects.Add(runtimeStatusEffect);
            }
        }
    }

    private static void ApplyConfiguredEquipStatuses(ObjectDB objectDb, IReadOnlyDictionary<string, SecondaryAttackDefinition> definitions)
    {
        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null)
            {
                continue;
            }

            ItemDrop? itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop == null || !definitions.TryGetValue(itemPrefab.name, out SecondaryAttackDefinition? definition))
            {
                continue;
            }

            if (definition.ConfiguredEffects.Count == 0)
            {
                continue;
            }

            if (itemDrop.m_itemData.m_shared.m_equipStatusEffect != null)
            {
                continue;
            }

            itemDrop.m_itemData.m_shared.m_equipStatusEffect = CreateEquipStatusEffect(itemPrefab.name, definition);
        }
    }

    private static StatusEffect? CreateRuntimeStatusEffect(ConfiguredWeaponEffectDefinition effect)
    {
        return effect.Type switch
        {
            WeaponEffectType.Dot => CreateConfiguredStatusEffect<ConfiguredDotStatusEffect>(effect),
            WeaponEffectType.ResistanceShred => CreateConfiguredStatusEffect<ConfiguredResistanceShredStatusEffect>(effect),
            WeaponEffectType.Haste => CreateConfiguredStatusEffect<ConfiguredHasteStatusEffect>(effect),
            _ => null
        };
    }

    private static T CreateConfiguredStatusEffect<T>(ConfiguredWeaponEffectDefinition effect)
        where T : ConfiguredRuntimeStatusEffectBase
    {
        T statusEffect = ScriptableObject.CreateInstance<T>();
        statusEffect.Initialize(effect);
        return statusEffect;
    }

    private static StatusEffect CreateEquipStatusEffect(string weaponPrefabName, SecondaryAttackDefinition definition)
    {
        ConfiguredEquipStatusEffect statusEffect = ScriptableObject.CreateInstance<ConfiguredEquipStatusEffect>();
        string label = string.Join(", ", definition.ConfiguredEffects.Select(effect => effect.Id));
        string tooltip = string.Join("\n", definition.ConfiguredEffects.Select(effect => effect.Id));
        statusEffect.Initialize(
            $"{GeneratedStatusPrefix}equip_{NormalizeKey(weaponPrefabName)}",
            label,
            tooltip);
        return statusEffect;
    }

    private static string NormalizeKey(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static void ApplyConfiguredEffect(Player attacker, Character target, string weaponPrefabName, ConfiguredWeaponEffectDefinition effect, ref HitData hit)
    {
        switch (effect.Type)
        {
            case WeaponEffectType.Execute:
                ApplyExecuteEffect(target, effect, ref hit);
                return;
            case WeaponEffectType.StaggerChance:
                ApplyStaggerChanceEffect(target, effect);
                return;
        }

        if (!TryConsumeEffectStack(target, effect, weaponPrefabName))
        {
            return;
        }

        if (!RollProc(effect.ProcChance))
        {
            return;
        }

        switch (effect.Type)
        {
            case WeaponEffectType.Adrenaline:
                attacker.AddStamina(ResolveScalarValue(effect.StaminaRestoreMode, effect.StaminaRestoreValue, attacker, target));
                break;
            case WeaponEffectType.Haste:
                attacker.m_seman.AddStatusEffect(effect.StatusEffectName.GetStableHashCode(), true, 0, 0f);
                break;
            case WeaponEffectType.Vampirism:
                attacker.Heal(ResolveScalarValue(effect.HealMode, effect.HealValue, attacker, target), showText: true);
                break;
            case WeaponEffectType.Dot:
                target.m_seman.AddStatusEffect(effect.StatusEffectName.GetStableHashCode(), true, 0, 0f);
                break;
            case WeaponEffectType.ResistanceShred:
                target.m_seman.AddStatusEffect(effect.StatusEffectName.GetStableHashCode(), true, 0, 0f);
                break;
            case WeaponEffectType.BurstDamage:
                ApplyBurstDamage(target, effect);
                break;
        }
    }

    private static void ApplyExecuteEffect(Character target, ConfiguredWeaponEffectDefinition effect, ref HitData hit)
    {
        float maxHealth = target.GetMaxHealth();
        if (maxHealth <= 0f || target.GetHealth() / maxHealth > effect.HealthThresholdPercent * 0.01f || !RollProc(effect.ProcChance))
        {
            return;
        }

        hit.ApplyModifier(effect.DamageMultiplier);
    }

    private static void ApplyStaggerChanceEffect(Character target, ConfiguredWeaponEffectDefinition effect)
    {
        if (!RollProc(effect.ProcChance))
        {
            return;
        }

        target.Stagger(Vector3.zero);
    }

    private static void ApplyBurstDamage(Character target, ConfiguredWeaponEffectDefinition effect)
    {
        float damage = ResolveScalarValue(effect.DamageMode, effect.DamageValue, target, target);
        if (damage <= 0f)
        {
            return;
        }

        HitData burstHit = new();
        SetDamageValue(ref burstHit.m_damage, effect.DamageType, damage);
        burstHit.m_point = ((Component)target).transform.position;
        target.Damage(burstHit);
    }

    private static bool TryPreparePostDamageEffect(Player attacker, Character target, string weaponPrefabName, ConfiguredWeaponEffectDefinition effect)
    {
        if (effect.Type == WeaponEffectType.Executioner &&
            !IsTargetBelowExecutionThreshold(target, effect))
        {
            return false;
        }

        return TryConsumeEffectStack(target, effect, weaponPrefabName) && RollProc(effect.ProcChance);
    }

    private static bool IsTargetBelowExecutionThreshold(Character target, ConfiguredWeaponEffectDefinition effect)
    {
        float maxHealth = target.GetMaxHealth();
        return maxHealth > 0f && target.GetHealth() / maxHealth <= effect.HealthThresholdPercent * 0.01f;
    }

    private static void ApplyPostDamageEffect(
        Player attacker,
        Character target,
        HitData sourceHit,
        ConfiguredWeaponEffectDefinition effect,
        float actualDamage)
    {
        switch (effect.Type)
        {
            case WeaponEffectType.Adrenaline:
                attacker.AddStamina(actualDamage * effect.ValuePercent * 0.01f);
                break;
            case WeaponEffectType.Haste:
                attacker.m_seman.AddStatusEffect(effect.StatusEffectName.GetStableHashCode(), true, 0, 0f);
                break;
            case WeaponEffectType.Vampirism:
                attacker.Heal(actualDamage * effect.ValuePercent * 0.01f, showText: true);
                break;
            case WeaponEffectType.Bleeding:
                WeaponEffectBleedingController.Apply(target, attacker, sourceHit, effect, actualDamage * effect.ValuePercent * 0.01f);
                break;
            case WeaponEffectType.Bash:
                ApplyBashStagger(attacker, target, sourceHit, actualDamage * effect.ValuePercent * 0.01f);
                break;
            case WeaponEffectType.Piercing:
                ApplyEffectDamage(attacker, target, sourceHit, effect.DamageType, actualDamage * effect.ValuePercent * 0.01f);
                break;
            case WeaponEffectType.Executioner:
                ApplyEffectDamage(attacker, target, sourceHit, effect.DamageType, actualDamage * effect.ValuePercent * 0.01f);
                break;
            case WeaponEffectType.Decapitator:
            case WeaponEffectType.Smasher:
            case WeaponEffectType.Juggernaut:
                ApplyWeaknessBonusDamage(attacker, target, sourceHit, effect, actualDamage);
                break;
        }
    }

    internal static void ApplyEffectDamage(
        Player attacker,
        Character target,
        HitData sourceHit,
        HitData.DamageType damageType,
        float damage)
    {
        if (damage <= 0f || target.IsDead())
        {
            return;
        }

        HitData effectHit = new()
        {
            m_point = sourceHit.m_point,
            m_dir = sourceHit.m_dir,
            m_pushForce = 0f,
            m_backstabBonus = 1f,
            m_staggerMultiplier = 0f,
            m_dodgeable = false,
            m_blockable = false,
            m_skill = Skills.SkillType.None,
            m_skillRaiseAmount = 0f,
            m_hitType = sourceHit.m_hitType
        };
        effectHit.SetAttacker(attacker);
        SetDamageValue(ref effectHit.m_damage, damageType, damage);

        _isApplyingEffectDamage = true;
        try
        {
            target.Damage(effectHit);
        }
        finally
        {
            _isApplyingEffectDamage = false;
        }
    }

    private static void ApplyBashStagger(Player attacker, Character target, HitData sourceHit, float staggerDamage)
    {
        if (staggerDamage <= 0f || target.IsDead())
        {
            return;
        }

        HitData staggerHit = sourceHit.Clone();
        staggerHit.m_damage = new HitData.DamageTypes();
        staggerHit.m_skill = Skills.SkillType.None;
        staggerHit.m_skillRaiseAmount = 0f;
        staggerHit.SetAttacker(attacker);
        target.AddStaggerDamage(staggerDamage, sourceHit.m_dir, staggerHit);
    }

    private static void ApplyWeaknessBonusDamage(
        Player attacker,
        Character target,
        HitData sourceHit,
        ConfiguredWeaponEffectDefinition effect,
        float actualDamage)
    {
        float currentMultiplier = GetDamageModifierMultiplier(target.GetDamageModifier(effect.DamageType));
        float weakMultiplier = GetDamageModifierMultiplier(HitData.DamageModifier.Weak);
        if (currentMultiplier <= 0f || currentMultiplier >= weakMultiplier)
        {
            return;
        }

        float desiredActualBonus = actualDamage * (weakMultiplier / currentMultiplier - 1f);
        float rawBonusDamage = desiredActualBonus / currentMultiplier;
        ApplyEffectDamage(attacker, target, sourceHit, effect.DamageType, rawBonusDamage);
    }

    private static float GetDamageModifierMultiplier(HitData.DamageModifier modifier)
    {
        return modifier switch
        {
            HitData.DamageModifier.Immune => 0f,
            HitData.DamageModifier.VeryResistant => 0.25f,
            HitData.DamageModifier.Resistant => 0.5f,
            HitData.DamageModifier.SlightlyResistant => 0.75f,
            HitData.DamageModifier.Normal => 1f,
            HitData.DamageModifier.Ignore => 1f,
            HitData.DamageModifier.SlightlyWeak => 1.5f,
            HitData.DamageModifier.Weak => 2f,
            HitData.DamageModifier.VeryWeak => 4f,
            _ => 1f
        };
    }

    private static bool TryConsumeEffectStack(Character recipient, ConfiguredWeaponEffectDefinition effect, string weaponPrefabName)
    {
        if (effect.StacksRequired <= 1)
        {
            return true;
        }

        string stackKey = $"{weaponPrefabName}:{effect.Id}";
        Dictionary<string, EffectStackState> stateTable = StackStates.GetOrCreateValue(recipient);
        if (!stateTable.TryGetValue(stackKey, out EffectStackState state) ||
            (state.ExpirationTime > 0f && Time.time > state.ExpirationTime))
        {
            state = new EffectStackState();
        }

        state.Count++;
        state.ExpirationTime = effect.StackWindow > 0f ? Time.time + effect.StackWindow : 0f;
        if (state.Count < effect.StacksRequired)
        {
            stateTable[stackKey] = state;
            return false;
        }

        stateTable.Remove(stackKey);
        return true;
    }

    private static bool RollProc(float procChance)
    {
        return procChance >= 100f || UnityEngine.Random.Range(0f, 100f) < procChance;
    }

    internal static void AppendConfiguredEffectTooltip(ItemDrop.ItemData item, ref string tooltip)
    {
        if (item?.m_dropPrefab == null ||
            string.IsNullOrWhiteSpace(item.m_dropPrefab.name) ||
            !SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.TryGetValue(item.m_dropPrefab.name, out SecondaryAttackDefinition? definition) ||
            definition.ConfiguredEffects.Count == 0)
        {
            return;
        }

        List<string> lines = new();
        HashSet<string> appended = new(StringComparer.OrdinalIgnoreCase);
        foreach (ConfiguredWeaponEffectDefinition effect in definition.ConfiguredEffects)
        {
            if (!appended.Add(effect.Id))
            {
                continue;
            }

            string line = BuildTooltipLine(effect);
            if (!string.IsNullOrWhiteSpace(line))
            {
                lines.Add(line);
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        string tooltipBlock = string.Join("\n", lines);
        tooltip = string.IsNullOrWhiteSpace(tooltip)
            ? tooltipBlock
            : $"{tooltip}\n\n{tooltipBlock}";
    }

    private static string BuildTooltipLine(ConfiguredWeaponEffectDefinition effect)
    {
        string hits = effect.StacksRequired <= 1 ? "Hits" : $"Every {effect.StacksRequired} hits on the same target";
        string value = FormatPercent(effect.ValuePercent);
        string duration = FormatSeconds(effect.Duration);
        return effect.Type switch
        {
            WeaponEffectType.Adrenaline => $"{hits} restore {value} damage as stamina.",
            WeaponEffectType.Haste => $"{hits} grant +{value} movement speed for {duration}.",
            WeaponEffectType.Vampirism => $"{hits} restore {value} damage as health.",
            WeaponEffectType.Bleeding => $"{hits} deal {value} damage as bleeding over {duration}.",
            WeaponEffectType.Bash => $"{hits} deal {value} damage as stagger.",
            WeaponEffectType.Piercing => $"{hits} deal {value} bonus {FormatDamageType(effect.DamageType)} damage.",
            WeaponEffectType.Executioner => $"Hits against targets below {FormatPercent(effect.HealthThresholdPercent)} health deal {value} bonus damage.",
            WeaponEffectType.Decapitator => $"{hits} deal bonus slash damage.",
            WeaponEffectType.Smasher => $"{hits} deal bonus blunt damage.",
            WeaponEffectType.Juggernaut => $"{hits} deal bonus pierce damage.",
            _ => ""
        };
    }

    private static string FormatPercent(float value)
    {
        return Mathf.Approximately(value, Mathf.Round(value))
            ? $"{Mathf.RoundToInt(value)}%"
            : $"{value:0.##}%";
    }

    private static string FormatSeconds(float value)
    {
        return Mathf.Approximately(value, Mathf.Round(value))
            ? $"{Mathf.RoundToInt(value)}s"
            : $"{value:0.##}s";
    }

    private static string FormatDamageType(HitData.DamageType damageType)
    {
        return damageType switch
        {
            HitData.DamageType.Blunt => "blunt",
            HitData.DamageType.Slash => "slash",
            HitData.DamageType.Pierce => "pierce",
            HitData.DamageType.Chop => "chop",
            HitData.DamageType.Pickaxe => "pickaxe",
            HitData.DamageType.Fire => "fire",
            HitData.DamageType.Frost => "frost",
            HitData.DamageType.Lightning => "lightning",
            HitData.DamageType.Poison => "poison",
            HitData.DamageType.Spirit => "spirit",
            _ => "damage"
        };
    }

    private sealed class EffectStackState
    {
        public int Count { get; set; }

        public float ExpirationTime { get; set; }
    }
}

internal sealed class WeaponEffectDamageState
{
    public WeaponEffectDamageState(
        Player attacker,
        Character target,
        HitData sourceHit,
        float healthBefore,
        List<ConfiguredWeaponEffectDefinition> effects)
    {
        Attacker = attacker;
        Target = target;
        SourceHit = sourceHit;
        HealthBefore = healthBefore;
        Effects = effects;
    }

    public Player Attacker { get; }

    public Character Target { get; }

    public HitData SourceHit { get; }

    public float HealthBefore { get; }

    public List<ConfiguredWeaponEffectDefinition> Effects { get; }
}

internal sealed class WeaponEffectBleedingController : MonoBehaviour
{
    private readonly List<BleedInstance> _instances = new();

    internal static void Apply(
        Character target,
        Player attacker,
        HitData sourceHit,
        ConfiguredWeaponEffectDefinition effect,
        float totalDamage)
    {
        if (totalDamage <= 0f || target.IsDead())
        {
            return;
        }

        WeaponEffectBleedingController controller =
            ((Component)target).GetComponent<WeaponEffectBleedingController>() ??
            ((Component)target).gameObject.AddComponent<WeaponEffectBleedingController>();
        controller.Add(attacker, sourceHit, effect, totalDamage);
    }

    private void Add(Player attacker, HitData sourceHit, ConfiguredWeaponEffectDefinition effect, float totalDamage)
    {
        float interval = Mathf.Max(0.01f, effect.TickInterval);
        int tickCount = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(interval, effect.Duration) / interval));
        _instances.Add(new BleedInstance(
            attacker,
            sourceHit.Clone(),
            effect.DamageType,
            totalDamage / tickCount,
            interval,
            tickCount));
    }

    private void Update()
    {
        Character? target = GetComponent<Character>();
        if (target == null || target.IsDead())
        {
            Destroy(this);
            return;
        }

        float dt = Time.deltaTime;
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            BleedInstance instance = _instances[i];
            instance.Timer -= dt;
            if (instance.Timer <= 0f)
            {
                instance.Timer += instance.Interval;
                instance.RemainingTicks--;
                WeaponEffectManager.ApplyEffectDamage(
                    instance.Attacker,
                    target,
                    instance.SourceHit,
                    instance.DamageType,
                    instance.TickDamage);
            }

            if (instance.RemainingTicks <= 0)
            {
                _instances.RemoveAt(i);
            }
            else
            {
                _instances[i] = instance;
            }
        }

        if (_instances.Count == 0)
        {
            Destroy(this);
        }
    }

    private struct BleedInstance
    {
        public BleedInstance(
            Player attacker,
            HitData sourceHit,
            HitData.DamageType damageType,
            float tickDamage,
            float interval,
            int remainingTicks)
        {
            Attacker = attacker;
            SourceHit = sourceHit;
            DamageType = damageType;
            TickDamage = tickDamage;
            Interval = interval;
            Timer = interval;
            RemainingTicks = remainingTicks;
        }

        public Player Attacker { get; }

        public HitData SourceHit { get; }

        public HitData.DamageType DamageType { get; }

        public float TickDamage { get; }

        public float Interval { get; }

        public float Timer { get; set; }

        public int RemainingTicks { get; set; }
    }
}

internal abstract class ConfiguredRuntimeStatusEffectBase : StatusEffect
{
    protected ConfiguredWeaponEffectDefinition Effect = null!;

    public void Initialize(ConfiguredWeaponEffectDefinition effect)
    {
        Effect = effect;
        ((Object)this).name = effect.StatusEffectName;
        m_name = effect.Id;
        m_tooltip = effect.Id;
        m_ttl = effect.Duration;
    }
}

internal sealed class ConfiguredDotStatusEffect : ConfiguredRuntimeStatusEffectBase
{
    private float _tickTimer;

    public override void UpdateStatusEffect(float dt)
    {
        base.UpdateStatusEffect(dt);
        _tickTimer += dt;
        if (_tickTimer < Effect.TickInterval)
        {
            return;
        }

        _tickTimer = 0f;
        float damage = WeaponEffectManager.ResolveScalarValue(Effect.DamageMode, Effect.DamageValue, m_character, m_character);
        if (damage <= 0f)
        {
            return;
        }

        HitData dotHit = new();
        WeaponEffectManager.SetDamageValue(ref dotHit.m_damage, Effect.DamageType, damage);
        dotHit.m_point = ((Component)m_character).transform.position;
        m_character.Damage(dotHit);
    }
}

internal sealed class ConfiguredResistanceShredStatusEffect : ConfiguredRuntimeStatusEffectBase
{
    public override void ModifyDamageMods(ref HitData.DamageModifiers modifiers)
    {
        base.ModifyDamageMods(ref modifiers);
        switch (Effect.DamageType)
        {
            case HitData.DamageType.Blunt:
                modifiers.m_blunt = Effect.Modifier;
                break;
            case HitData.DamageType.Slash:
                modifiers.m_slash = Effect.Modifier;
                break;
            case HitData.DamageType.Pierce:
                modifiers.m_pierce = Effect.Modifier;
                break;
            case HitData.DamageType.Chop:
                modifiers.m_chop = Effect.Modifier;
                break;
            case HitData.DamageType.Pickaxe:
                modifiers.m_pickaxe = Effect.Modifier;
                break;
            case HitData.DamageType.Fire:
                modifiers.m_fire = Effect.Modifier;
                break;
            case HitData.DamageType.Frost:
                modifiers.m_frost = Effect.Modifier;
                break;
            case HitData.DamageType.Lightning:
                modifiers.m_lightning = Effect.Modifier;
                break;
            case HitData.DamageType.Poison:
                modifiers.m_poison = Effect.Modifier;
                break;
            case HitData.DamageType.Spirit:
                modifiers.m_spirit = Effect.Modifier;
                break;
        }

        if (Effect.ConsumeOnModify)
        {
            m_ttl = 0.1f;
        }
    }
}

internal sealed class ConfiguredHasteStatusEffect : ConfiguredRuntimeStatusEffectBase
{
    public override void ModifySpeed(float baseSpeed, ref float speed, Character character, Vector3 dir)
    {
        base.ModifySpeed(baseSpeed, ref speed, character, dir);
        speed *= Effect.MoveSpeedMultiplier;
    }
}

internal sealed class ConfiguredEquipStatusEffect : StatusEffect
{
    public void Initialize(string statusName, string label, string tooltip)
    {
        ((Object)this).name = statusName;
        m_name = label;
        m_tooltip = tooltip;
        m_ttl = 0f;
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
internal static class CharacterDamageWeaponEffectPatch
{
    private static void Prefix(Character __instance, ref HitData hit, out WeaponEffectDamageState? __state)
    {
        __state = WeaponEffectManager.TryHandleCharacterDamagePrefix(__instance, ref hit);
    }

    private static void Postfix(WeaponEffectDamageState? __state)
    {
        WeaponEffectManager.TryHandleCharacterDamagePostfix(__state);
    }
}
