using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Object = UnityEngine.Object;
using ProjectileLaunchData = SecondaryAttacks.ProjectileRuntimeSystem.ProjectileLaunchData;

namespace SecondaryAttacks;

internal static partial class SecondaryAttackManager
{
    private const string SummonEmpowerExpiryZdoKey = "SecondaryAttacks_SummonEmpowerExpiry";
    private const string SummonEmpowerMoveSpeedBonusZdoKey = "SecondaryAttacks_SummonEmpowerMoveSpeedBonus";
    private const string SummonEmpowerAttackCooldownReductionZdoKey = "SecondaryAttacks_SummonEmpowerAttackCooldownReduction";
    private const string ShieldRemainingDisplayZdoKey = "SecondaryAttacks_ShieldRemainingDisplay";
    private const string ShieldDisplayExpiryZdoKey = "SecondaryAttacks_ShieldDisplayExpiry";
    private const string CharacterRpcComponentName = "SecondaryAttacks_CharacterRpc";
    private const string ApplySummonEmpowerRpcName = "SecondaryAttacks_ApplySummonEmpower";
    private const string ConvertShieldToHealRpcName = "SecondaryAttacks_ConvertShieldToHeal";
    private const string StaffRapidFireAnimation = "staff_rapidfire";

    private static readonly ConditionalWeakTable<Player, BowSecondaryState> BowSecondaryStates = new();
    private static readonly ConditionalWeakTable<Character, AsyncSecondaryActivityState> AsyncSecondaryActivityStates = new();
    private static readonly ConditionalWeakTable<ItemDrop.ItemData, RuntimeWeaponDefinitionState> RuntimeWeaponDefinitionStates = new();
    private static readonly HashSet<string> ReportedRuntimeDumps = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SortedSet<string> PlayerAnimatorTriggers = new(StringComparer.Ordinal);
    private static bool _animatorDumpWritten;
    private static bool _customAnimationDumpWritten;

    internal static string ApplySummonEmpowerRpcNameForStaffRuntime => ApplySummonEmpowerRpcName;

    internal static string ConvertShieldToHealRpcNameForStaffRuntime => ConvertShieldToHealRpcName;

    internal static string SummonEmpowerExpiryZdoKeyForStaffRuntime => SummonEmpowerExpiryZdoKey;

    internal static string SummonEmpowerMoveSpeedBonusZdoKeyForStaffRuntime => SummonEmpowerMoveSpeedBonusZdoKey;

    internal static string SummonEmpowerAttackCooldownReductionZdoKeyForStaffRuntime => SummonEmpowerAttackCooldownReductionZdoKey;

    internal static string ShieldRemainingDisplayZdoKeyForStaffRuntime => ShieldRemainingDisplayZdoKey;

    internal static string ShieldDisplayExpiryZdoKeyForStaffRuntime => ShieldDisplayExpiryZdoKey;

    [Conditional("SECONDARY_ATTACKS_DEBUG_LOGGING")]
    internal static void LogStaffDebug(string message)
    {
    }

    [Conditional("SECONDARY_ATTACKS_DEBUG_LOGGING")]
    internal static void LogRangedDebug(string message)
    {
    }

    internal static string DescribeItemForRangedDebug(ItemDrop.ItemData? item)
    {
        return SecondaryAttackRuntimeFacade.DescribeItemForRangedDebug(item);
    }

    internal static string DescribeAttackForRangedDebug(Attack? attack)
    {
        return SecondaryAttackRuntimeFacade.DescribeAttackForRangedDebug(attack);
    }


    // Public compatibility bridge for external integrations. Internal runtime code should call SecondaryAttackRuntimeFacade directly.
    public static bool TryGetDefinition(ItemDrop.ItemData weapon, out SecondaryAttackDefinition definition)
    {
        return SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out definition);
    }

    public static bool TryGetDefinition(string weaponPrefabName, out SecondaryAttackDefinition definition)
    {
        return SecondaryAttackRuntimeFacade.TryGetDefinition(weaponPrefabName, out definition);
    }

    public static bool TryGetCurrentWeaponDefinition(out SecondaryAttackDefinition definition, out bool secondaryAttack)
    {
        return SecondaryAttackRuntimeFacade.TryGetCurrentWeaponDefinition(out definition, out secondaryAttack);
    }

    public static bool ShouldHandleBowDraw(ItemDrop.ItemData weapon)
    {
        return SecondaryAttackRuntimeFacade.ShouldHandleBowDraw(weapon);
    }

    public static bool CanStartConfiguredSecondary(Humanoid humanoid, ItemDrop.ItemData weapon)
    {
        return SecondaryAttackRuntimeFacade.CanStartConfiguredSecondary(humanoid, weapon);
    }

    public static void RegisterActiveAttack(Attack attack, ItemDrop.ItemData weapon)
    {
        SecondaryAttackRuntimeFacade.RegisterActiveAttack(attack, weapon);
    }

    public static bool TryHandleCustomAttackTrigger(Attack attack)
    {
        return SecondaryAttackRuntimeFacade.TryHandleCustomAttackTrigger(attack);
    }

    public static bool TryHandleCustomProjectileBurst(Attack attack)
    {
        return SecondaryAttackRuntimeFacade.TryHandleCustomProjectileBurst(attack);
    }

    internal static Attack BuildSecondaryAttack(Attack sourceAttack, SecondaryAttackDefinition definition)
    {
        Attack secondaryAttack = CloneAttack(sourceAttack);
        if (definition.BehaviorType == SecondaryAttackBehaviorType.SummonEmpower ||
            definition.BehaviorType == SecondaryAttackBehaviorType.ShieldConvert)
        {
            secondaryAttack.m_attackType = Attack.AttackType.None;
            secondaryAttack.m_bowDraw = false;
            secondaryAttack.m_requiresReload = false;
            secondaryAttack.m_projectiles = 1;
            secondaryAttack.m_projectileBursts = 1;
            secondaryAttack.m_attackChainLevels = 1;
            secondaryAttack.m_attackRandomAnimations = 0;
        }
        else if (definition.BehaviorType == SecondaryAttackBehaviorType.Projectile)
        {
            secondaryAttack.m_attackType = Attack.AttackType.Projectile;
            secondaryAttack.m_bowDraw = sourceAttack.m_bowDraw;
            secondaryAttack.m_requiresReload = sourceAttack.m_requiresReload;
            if (definition.Behavior is not ProjectileSecondaryBehavior { Preset: SecondaryAttackPreset.Piercing or SecondaryAttackPreset.Burst })
            {
                secondaryAttack.m_projectiles = 1;
                secondaryAttack.m_projectileBursts = 1;
            }
        }
        else if (definition.BehaviorType == SecondaryAttackBehaviorType.Aftershock)
        {
            secondaryAttack.m_attackType = Attack.AttackType.Area;
            secondaryAttack.m_bowDraw = false;
            secondaryAttack.m_requiresReload = false;
            secondaryAttack.m_projectiles = 1;
            secondaryAttack.m_projectileBursts = 1;
        }

        secondaryAttack.m_attackAnimation = definition.AttackAnimation;
        if (definition.BehaviorType == SecondaryAttackBehaviorType.Projectile &&
            string.Equals(secondaryAttack.m_attackAnimation, StaffRapidFireAnimation, StringComparison.Ordinal))
        {
            secondaryAttack.m_loopingAttack = true;
        }

        secondaryAttack.m_attackHealth = definition.RawAttackHealth;
        secondaryAttack.m_attackHealthPercentage = definition.RawAttackHealthPercentage;
        secondaryAttack.m_attackStamina = definition.RawAttackStamina;
        secondaryAttack.m_attackEitr = definition.RawAttackEitr;
        secondaryAttack.m_drawStaminaDrain = definition.RawDrawStamina;
        secondaryAttack.m_drawEitrDrain = definition.RawDrawEitr;
        secondaryAttack.m_reloadStaminaDrain = definition.RawReloadStamina;
        secondaryAttack.m_reloadEitrDrain = definition.RawReloadEitr;
        secondaryAttack.m_damageMultiplier *= definition.OutputMultiplier;
        secondaryAttack.m_forceMultiplier *= definition.OutputMultiplier;
        secondaryAttack.m_staggerMultiplier *= definition.OutputMultiplier;
        if (definition.HasCustomAttackAnimation)
        {
            secondaryAttack.m_attackChainLevels = 1;
            secondaryAttack.m_attackRandomAnimations = 0;
        }

        return secondaryAttack;
    }

    public static void UpdateCustomBowDraw(
        Player player,
        ItemDrop.ItemData weapon,
        float dt,
        ref float attackDrawTime,
        bool blocking,
        bool attackHold,
        bool secondaryAttackHold,
        bool secondaryAttackPressed,
        ZSyncAnimation zanim,
        SEMan seman)
    {
        BowSecondaryState state = BowSecondaryStates.GetValue(player, _ => new BowSecondaryState());
        string currentPrefabName = weapon.m_dropPrefab != null ? weapon.m_dropPrefab.name : "";
        if (!string.Equals(state.PrefabName, currentPrefabName, StringComparison.Ordinal))
        {
            state.PrefabName = currentPrefabName;
            state.PendingSecondary = false;
        }

        if (!TryGetDefinition(weapon, out SecondaryAttackDefinition definition))
        {
            state.PendingSecondary = false;
            return;
        }

        bool drawHeld = attackHold;
        float drawStaminaDrain = GetSkillAdjustedDrawCost(player, weapon, weapon.m_shared.m_attack.m_drawStaminaDrain);
        float drawEitrDrain = weapon.m_shared.m_attack.m_drawEitrDrain;
        bool hasStamina = drawStaminaDrain <= 0f || player.HaveStamina();
        bool hasEitr = drawEitrDrain <= 0f || player.HaveEitr();

        if (blocking || player.InMinorAction() || player.IsAttached())
        {
            attackDrawTime = -1f;
            state.PendingSecondary = false;
            if (!string.IsNullOrEmpty(weapon.m_shared.m_attack.m_drawAnimationState))
            {
                zanim.SetBool(weapon.m_shared.m_attack.m_drawAnimationState, value: false);
            }

            return;
        }

        if (drawHeld && attackDrawTime == 0f)
        {
            state.PendingSecondary = false;
        }
        else if (secondaryAttackPressed && drawHeld && attackDrawTime > 0f)
        {
            state.PendingSecondary = true;
            RangedSecondaryCooldownSystem.CanStart(player, weapon);
        }

        if (attackDrawTime < 0f)
        {
            if (!drawHeld)
            {
                attackDrawTime = 0f;
            }

            return;
        }

        if (drawHeld && hasStamina && hasEitr && attackDrawTime >= 0f)
        {
            if (attackDrawTime == 0f)
            {
                if (!weapon.m_shared.m_attack.StartDraw(player, weapon))
                {
                    attackDrawTime = -1f;
                    state.PendingSecondary = false;
                    return;
                }

                weapon.m_shared.m_holdStartEffect.Create(player.transform.position, Quaternion.identity, player.transform);
            }

            attackDrawTime += Time.fixedDeltaTime;
            if (!string.IsNullOrEmpty(weapon.m_shared.m_attack.m_drawAnimationState))
            {
                zanim.SetBool(weapon.m_shared.m_attack.m_drawAnimationState, value: true);
                zanim.SetFloat("drawpercent", player.GetAttackDrawPercentage());
            }

            player.UseStamina(drawStaminaDrain * dt);
            player.UseEitr(drawEitrDrain * dt);
            return;
        }

        if (attackDrawTime > 0f)
        {
            if (hasStamina && hasEitr)
            {
                bool pendingSecondary = state.PendingSecondary;
                float extraStaminaCost = 0f;
                if (!pendingSecondary || CanPayBowSecondaryReleaseExtraStamina(player, weapon, definition, drawStaminaDrain, attackDrawTime, out extraStaminaCost))
                {
                    bool started = player.StartAttack(null, pendingSecondary);
                    if (started && pendingSecondary && extraStaminaCost > 0f)
                    {
                        player.UseStamina(extraStaminaCost);
                    }
                }
                else
                {
                    Hud.instance?.StaminaBarEmptyFlash();
                }
            }

            if (!string.IsNullOrEmpty(weapon.m_shared.m_attack.m_drawAnimationState))
            {
                zanim.SetBool(weapon.m_shared.m_attack.m_drawAnimationState, value: false);
            }

            attackDrawTime = 0f;
            state.PendingSecondary = false;
        }
    }

    private static bool CanPayBowSecondaryReleaseExtraStamina(
        Player player,
        ItemDrop.ItemData weapon,
        SecondaryAttackDefinition definition,
        float drawStaminaDrain,
        float attackDrawTime,
        out float extraStaminaCost)
    {
        extraStaminaCost = 0f;
        if (weapon.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Bow || !weapon.m_shared.m_attack.m_bowDraw)
        {
            return true;
        }

        float resourceMultiplier = Mathf.Max(0f, definition.ResourceMultiplier);
        if (resourceMultiplier <= 1f || drawStaminaDrain <= 0f)
        {
            return true;
        }

        float fullChargeTime = GetSkillAdjustedFullDrawTime(player, weapon);
        float chargedTime = Mathf.Min(Mathf.Max(0f, attackDrawTime), fullChargeTime);
        extraStaminaCost = drawStaminaDrain * chargedTime * (resourceMultiplier - 1f);
        return extraStaminaCost <= 0f || player.HaveStamina(extraStaminaCost);
    }

    private static float GetSkillAdjustedFullDrawTime(Player player, ItemDrop.ItemData weapon)
    {
        float baseFullChargeTime = Mathf.Max(0f, weapon.m_shared.m_attack.m_drawDurationMin);
        if (baseFullChargeTime <= 0f)
        {
            return 0f;
        }

        float skillFactor = player.GetSkillFactor(weapon.m_shared.m_skillType);
        return Mathf.Lerp(baseFullChargeTime, baseFullChargeTime * 0.2f, skillFactor);
    }

    internal static bool TryPrepareReloadSecondaryResourceCost(
        Humanoid humanoid,
        ItemDrop.ItemData weapon,
        out ReloadSecondaryResourceCostContext context)
    {
        context = ReloadSecondaryResourceCostContext.Empty;
        if (weapon == null || !TryGetDefinition(weapon, out SecondaryAttackDefinition definition))
        {
            return true;
        }

        if (definition.BehaviorType != SecondaryAttackBehaviorType.Projectile)
        {
            return true;
        }

        Attack primaryAttack = weapon.m_shared.m_attack;
        if (!primaryAttack.m_requiresReload || !IsCurrentReloadWeaponLoaded(humanoid, weapon))
        {
            LogRangedDebug($"reload secondary cost skipped weapon=[{DescribeItemForRangedDebug(weapon)}] requiresReload={primaryAttack.m_requiresReload} loaded={IsCurrentReloadWeaponLoaded(humanoid, weapon)}");
            return true;
        }

        float multiplierDelta = Mathf.Max(0f, definition.ResourceMultiplier) - 1f;
        if (Mathf.Approximately(multiplierDelta, 0f))
        {
            LogRangedDebug($"reload secondary cost skipped zero multiplier delta weapon=[{DescribeItemForRangedDebug(weapon)}] resourceMultiplier={definition.ResourceMultiplier:0.###}");
            return true;
        }

        float reloadTime = Mathf.Max(0f, weapon.GetWeaponLoadingTime());
        if (reloadTime <= 0f)
        {
            LogRangedDebug($"reload secondary cost skipped zero reload time weapon=[{DescribeItemForRangedDebug(weapon)}]");
            return true;
        }

        float staminaDelta = Mathf.Max(0f, primaryAttack.m_reloadStaminaDrain) * reloadTime * multiplierDelta;
        float eitrDelta = Mathf.Max(0f, primaryAttack.m_reloadEitrDrain) * reloadTime * multiplierDelta;
        if (staminaDelta > 0f && !humanoid.HaveStamina(staminaDelta))
        {
            LogRangedDebug($"reload secondary cost failed stamina weapon=[{DescribeItemForRangedDebug(weapon)}] staminaDelta={staminaDelta:0.###} eitrDelta={eitrDelta:0.###}");
            if (humanoid.IsPlayer())
            {
                Hud.instance?.StaminaBarEmptyFlash();
            }

            return false;
        }

        if (eitrDelta > 0f && !humanoid.TryUseEitr(eitrDelta))
        {
            LogRangedDebug($"reload secondary cost failed eitr weapon=[{DescribeItemForRangedDebug(weapon)}] staminaDelta={staminaDelta:0.###} eitrDelta={eitrDelta:0.###}");
            return false;
        }

        context = new ReloadSecondaryResourceCostContext(staminaDelta, eitrDelta);
        LogRangedDebug($"reload secondary cost prepared weapon=[{DescribeItemForRangedDebug(weapon)}] reloadTime={reloadTime:0.###} resourceMultiplier={definition.ResourceMultiplier:0.###} staminaDelta={staminaDelta:0.###} eitrDelta={eitrDelta:0.###}");
        return true;
    }

    private static bool IsCurrentReloadWeaponLoaded(Humanoid humanoid, ItemDrop.ItemData weapon)
    {
        if (humanoid is Player player)
        {
            return player.m_weaponLoaded == weapon;
        }

        return humanoid.IsWeaponLoaded();
    }

    internal static void ApplyReloadSecondaryResourceCost(Humanoid humanoid, ReloadSecondaryResourceCostContext? context)
    {
        if (context == null || !context.HasDelta)
        {
            return;
        }

        if (context.StaminaDelta > 0f)
        {
            humanoid.UseStamina(context.StaminaDelta);
        }
        else if (context.StaminaDelta < 0f)
        {
            humanoid.AddStamina(-context.StaminaDelta);
        }

        if (context.EitrDelta > 0f)
        {
            humanoid.UseEitr(context.EitrDelta);
        }
        else if (context.EitrDelta < 0f)
        {
            humanoid.AddEitr(-context.EitrDelta);
        }

        LogRangedDebug($"reload secondary cost applied staminaDelta={context.StaminaDelta:0.###} eitrDelta={context.EitrDelta:0.###} currentWeapon=[{DescribeItemForRangedDebug(humanoid.GetCurrentWeapon())}]");
    }

    private static bool TryParsePreset(string presetText, out SecondaryAttackPreset preset)
    {
        string configuredPreset = presetText.Trim();
        foreach (SecondaryAttackPreset candidate in Enum.GetValues(typeof(SecondaryAttackPreset)))
        {
            if (ProjectileRuntimeSystem.GetPresetName(candidate) == configuredPreset)
            {
                preset = candidate;
                return true;
            }
        }

        preset = default;
        return false;
    }

    private static void ApplyAttackResourceScaling(SecondaryAttackDefinition definition, Attack sourceAttack, float resourceMultiplier)
    {
        float multiplier = Mathf.Max(0f, resourceMultiplier);
        definition.ResourceMultiplier = multiplier;
        definition.RawAttackHealth = Mathf.Max(0f, sourceAttack.m_attackHealth * multiplier);
        definition.RawAttackHealthPercentage = Mathf.Max(0f, sourceAttack.m_attackHealthPercentage * multiplier);
        definition.RawAttackStamina = Mathf.Max(0f, sourceAttack.m_attackStamina * multiplier);
        definition.RawAttackEitr = Mathf.Max(0f, sourceAttack.m_attackEitr * multiplier);
        definition.RawDrawStamina = Mathf.Max(0f, sourceAttack.m_drawStaminaDrain * multiplier);
        definition.RawDrawEitr = Mathf.Max(0f, sourceAttack.m_drawEitrDrain * multiplier);
        definition.RawReloadStamina = Mathf.Max(0f, sourceAttack.m_reloadStaminaDrain * multiplier);
        definition.RawReloadEitr = Mathf.Max(0f, sourceAttack.m_reloadEitrDrain * multiplier);
    }

    internal static Attack ResolveSourceAttack(ObjectDB objectDb, ItemDrop itemDrop, SecondaryAttackDefinition definition)
    {
        if (definition.BehaviorType == SecondaryAttackBehaviorType.Projectile)
        {
            return itemDrop.m_itemData.m_shared.m_attack;
        }

        if (definition.BehaviorType == SecondaryAttackBehaviorType.SummonEmpower ||
            definition.BehaviorType == SecondaryAttackBehaviorType.ShieldConvert)
        {
            return itemDrop.m_itemData.m_shared.m_attack;
        }

        if (definition.BehaviorType == SecondaryAttackBehaviorType.Aftershock)
        {
            AftershockSecondaryBehavior? aftershockBehavior = definition.Behavior as AftershockSecondaryBehavior;
            string aftershockSourcePrefab = string.IsNullOrWhiteSpace(aftershockBehavior?.SourcePrefabName)
                ? itemDrop.m_itemData.m_dropPrefab?.name ?? itemDrop.name
                : aftershockBehavior!.SourcePrefabName;
            if (TryResolveAftershockSourceAttack(objectDb, aftershockSourcePrefab, out Attack? aftershockSourceAttack, out _))
            {
                return aftershockSourceAttack!;
            }

            return itemDrop.m_itemData.m_shared.m_secondaryAttack ?? itemDrop.m_itemData.m_shared.m_attack;
        }

        if (definition.BehaviorType == SecondaryAttackBehaviorType.FractureLine)
        {
            FractureLineSecondaryBehavior? fractureLineBehavior = definition.Behavior as FractureLineSecondaryBehavior;
            string fractureLineSourcePrefab = string.IsNullOrWhiteSpace(fractureLineBehavior?.SourcePrefabName)
                ? itemDrop.m_itemData.m_dropPrefab?.name ?? itemDrop.name
                : fractureLineBehavior!.SourcePrefabName;
            if (TryResolveFractureLineSourceAttack(objectDb, fractureLineSourcePrefab, out Attack? fractureLineSourceAttack, out _))
            {
                return fractureLineSourceAttack!;
            }

            return itemDrop.m_itemData.m_shared.m_secondaryAttack ?? itemDrop.m_itemData.m_shared.m_attack;
        }

        CopiedSecondaryBehavior? copiedBehavior = definition.Behavior as CopiedSecondaryBehavior;
        string sourcePrefabName = string.IsNullOrWhiteSpace(copiedBehavior?.SourcePrefabName)
            ? itemDrop.m_itemData.m_dropPrefab?.name ?? itemDrop.name
            : copiedBehavior!.SourcePrefabName;
        if (TryResolveSecondarySourceAttack(objectDb, sourcePrefabName, out Attack? sourceAttack, out _))
        {
            return sourceAttack!;
        }

        return itemDrop.m_itemData.m_shared.m_secondaryAttack;
    }

    internal static bool TryResolveSecondarySourceAttack(ObjectDB objectDb, string sourcePrefabName, out Attack? sourceAttack, out string reason)
    {
        sourceAttack = null;
        reason = "";
        ItemDrop? sourceItemDrop = FindItemDropByPrefabName(objectDb, sourcePrefabName);
        if (sourceItemDrop == null)
        {
            reason = $"source weapon '{sourcePrefabName}' was not found in ObjectDB.";
            return false;
        }

        sourceAttack = sourceItemDrop.m_itemData.m_shared.m_secondaryAttack;
        if (sourceAttack == null || string.IsNullOrWhiteSpace(sourceAttack.m_attackAnimation))
        {
            reason = $"source weapon '{sourcePrefabName}' does not have a valid secondary attack.";
            return false;
        }

        return true;
    }

    private static bool TryResolveAftershockSourceAttack(ObjectDB objectDb, string sourcePrefabName, out Attack? sourceAttack, out string reason)
    {
        sourceAttack = null;
        reason = "";
        ItemDrop? sourceItemDrop = FindItemDropByPrefabName(objectDb, sourcePrefabName);
        if (sourceItemDrop == null)
        {
            reason = $"aftershock source weapon '{sourcePrefabName}' was not found in ObjectDB.";
            return false;
        }

        Attack? primaryAttack = sourceItemDrop.m_itemData.m_shared.m_attack;
        if (IsValidAftershockSourceAttack(primaryAttack))
        {
            sourceAttack = primaryAttack;
            return true;
        }

        Attack? secondaryAttack = sourceItemDrop.m_itemData.m_shared.m_secondaryAttack;
        if (IsValidAftershockSourceAttack(secondaryAttack))
        {
            sourceAttack = secondaryAttack;
            return true;
        }

        reason = $"aftershock source weapon '{sourcePrefabName}' does not have a valid Area primary or secondary attack.";
        return false;
    }

    private static bool IsValidAftershockSourceAttack(Attack? attack)
    {
        return attack != null &&
               attack.m_attackType == Attack.AttackType.Area &&
               !string.IsNullOrWhiteSpace(attack.m_attackAnimation) &&
               attack.m_attackRayWidth > 0f;
    }

    private static bool TryResolveFractureLineSourceAttack(ObjectDB objectDb, string sourcePrefabName, out Attack? sourceAttack, out string reason)
    {
        sourceAttack = null;
        reason = "";
        ItemDrop? sourceItemDrop = FindItemDropByPrefabName(objectDb, sourcePrefabName);
        if (sourceItemDrop == null)
        {
            reason = $"fractureLine source weapon '{sourcePrefabName}' was not found in ObjectDB.";
            return false;
        }

        Attack? secondaryAttack = sourceItemDrop.m_itemData.m_shared.m_secondaryAttack;
        if (IsValidFractureLineSourceAttack(secondaryAttack))
        {
            sourceAttack = secondaryAttack;
            return true;
        }

        Attack? primaryAttack = sourceItemDrop.m_itemData.m_shared.m_attack;
        if (IsValidFractureLineSourceAttack(primaryAttack))
        {
            sourceAttack = primaryAttack;
            return true;
        }

        reason = $"fractureLine source weapon '{sourcePrefabName}' does not have a valid melee primary or secondary attack.";
        return false;
    }

    private static bool IsValidFractureLineSourceAttack(Attack? attack)
    {
        return attack != null &&
               (attack.m_attackType == Attack.AttackType.Horizontal || attack.m_attackType == Attack.AttackType.Vertical) &&
               !string.IsNullOrWhiteSpace(attack.m_attackAnimation);
    }

    private static ItemDrop? FindItemDropByPrefabName(ObjectDB objectDb, string prefabName)
    {
        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null || !string.Equals(itemPrefab.name, prefabName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return itemPrefab.GetComponent<ItemDrop>();
        }

        return null;
    }

    internal static bool TryResolveWeaponEffectDefinition(
        string effectId,
        EffectBehaviorConfig effectConfig,
        out ConfiguredWeaponEffectDefinition? definition,
        out string reason)
    {
        return WeaponEffectDefinitionCompiler.TryResolve(effectId, effectConfig, out definition, out reason);
    }

    private static float GetSkillAdjustedDrawCost(Player player, ItemDrop.ItemData weapon, float rawDrawCost)
    {
        if (rawDrawCost <= 0f)
        {
            return 0f;
        }

        float skillFactor = player.GetSkillFactor(weapon.m_shared.m_skillType);
        return rawDrawCost - rawDrawCost * 0.33f * skillFactor;
    }

}
