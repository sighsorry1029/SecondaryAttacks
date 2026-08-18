using System;
using System.Collections;
using System.Collections.Generic;
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
    private const string StaffRapidFireAnimation = "staff_rapidfire";

    private static readonly ConditionalWeakTable<Player, BowSecondaryState> BowSecondaryStates = new();
    private static readonly ConditionalWeakTable<ItemDrop.ItemData, RuntimeWeaponDefinitionState> RuntimeWeaponDefinitionStates = new();
    private static readonly SortedSet<string> PlayerAnimatorTriggers = new(StringComparer.Ordinal);
    private static bool _animatorDumpWritten;
    private static bool _customAnimationDumpWritten;

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

        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition))
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

    private static bool TryParsePreset(string presetText, out SecondaryAttackPreset preset)
    {
        string configuredPreset = presetText.Trim();
        preset = default;
        return SecondaryAttackPresetCatalog.TryGet(
                   configuredPreset,
                   out SecondaryAttackPresetInfo presetInfo) &&
               presetInfo.Group == SecondaryAttackPresetGroup.Ranged &&
               Enum.TryParse(presetInfo.Key, ignoreCase: true, out preset);
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

            return ResolveOriginalSecondaryAttack(
                       objectDb,
                       itemDrop.m_itemData.m_dropPrefab?.name ?? itemDrop.name,
                       itemDrop) ??
                   itemDrop.m_itemData.m_shared.m_attack;
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

            return ResolveOriginalSecondaryAttack(
                       objectDb,
                       itemDrop.m_itemData.m_dropPrefab?.name ?? itemDrop.name,
                       itemDrop) ??
                   itemDrop.m_itemData.m_shared.m_attack;
        }

        CopiedSecondaryBehavior? copiedBehavior = definition.Behavior as CopiedSecondaryBehavior;
        string sourcePrefabName = string.IsNullOrWhiteSpace(copiedBehavior?.SourcePrefabName)
            ? itemDrop.m_itemData.m_dropPrefab?.name ?? itemDrop.name
            : copiedBehavior!.SourcePrefabName;
        if (TryResolveSecondarySourceAttack(objectDb, sourcePrefabName, out Attack? sourceAttack, out _))
        {
            return sourceAttack!;
        }

        return ResolveOriginalSecondaryAttack(
                   objectDb,
                   itemDrop.m_itemData.m_dropPrefab?.name ?? itemDrop.name,
                   itemDrop) ??
               itemDrop.m_itemData.m_shared.m_attack;
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

        sourceAttack = ResolveOriginalSecondaryAttack(objectDb, sourcePrefabName, sourceItemDrop);
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

        Attack? secondaryAttack = ResolveOriginalSecondaryAttack(objectDb, sourcePrefabName, sourceItemDrop);
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

        Attack? secondaryAttack = ResolveOriginalSecondaryAttack(objectDb, sourcePrefabName, sourceItemDrop);
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

    private static Attack? ResolveOriginalSecondaryAttack(
        ObjectDB objectDb,
        string sourcePrefabName,
        ItemDrop sourceItemDrop)
    {
        return SecondaryAttackObjectDbStateStore.TryGetOriginalSecondaryAttack(
            objectDb,
            sourcePrefabName,
            out Attack? originalSecondaryAttack)
            ? originalSecondaryAttack
            : sourceItemDrop.m_itemData.m_shared.m_secondaryAttack;
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
