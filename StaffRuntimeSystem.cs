using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SecondaryAttacks;

internal static class StaffRuntimeSystem
{
    internal const string StaffTargetEffectRpcName = "SecondaryAttacks_SpawnStaffTargetEffect";

    private const string SummonEmpowerExpiryZdoKey = "SecondaryAttacks_SummonEmpowerExpiry";
    private const string SummonEmpowerMoveSpeedBonusZdoKey = "SecondaryAttacks_SummonEmpowerMoveSpeedBonus";
    private const string SummonEmpowerAttackCooldownReductionZdoKey = "SecondaryAttacks_SummonEmpowerAttackCooldownReduction";
    private const string ShieldRemainingDisplayZdoKey = "SecondaryAttacks_ShieldRemainingDisplay";
    private const string ShieldDisplayExpiryZdoKey = "SecondaryAttacks_ShieldDisplayExpiry";
    private const string ApplySummonEmpowerRpcName = "SecondaryAttacks_ApplySummonEmpower";
    private const string ConvertShieldToHealRpcName = "SecondaryAttacks_ConvertShieldToHeal";
    private const string SummonEmpowerPresetName = "summonEmpower";
    private const string ShieldConvertPresetName = "shieldConvert";
    private const string StaffTargetEffectPrefabName = "fx_bloodweapon_hit";

    internal static bool TryTriggerStaffSpecialFromRuntimeFacade(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        if (activeAttack.Triggered)
        {
            return true;
        }

        if (!TryConsumeStaffSpecialCooldown(attack, activeAttack.Definition))
        {
            return false;
        }

        activeAttack.Triggered = true;
        SecondaryAttackManager.PlayTriggeredAttackEffects(attack, activeAttack.Definition.DurabilityFactor);

        if (activeAttack.Definition.BehaviorType == SecondaryAttackBehaviorType.SummonEmpower)
        {
            StartSummonEmpower(attack, activeAttack.Definition);
            return true;
        }

        if (activeAttack.Definition.BehaviorType == SecondaryAttackBehaviorType.ShieldConvert)
        {
            StartShieldConvert(attack, activeAttack.Definition);
        }

        return true;
    }

    private static bool TryConsumeStaffSpecialCooldown(Attack attack, SecondaryAttackDefinition definition)
    {
        if (attack?.m_character == null)
        {
            return true;
        }

        if (!TryResolveStaffSpecialCooldown(definition, out string presetName, out MeleePresetCooldownDefinition cooldown))
        {
            return true;
        }

        return MeleePresetCooldownSystem.TryConsume(
            attack.m_character,
            attack.m_weapon,
            presetName,
            cooldown);
    }

    internal static bool CanStartStaffSpecial(Humanoid humanoid, ItemDrop.ItemData weapon)
    {
        if (humanoid == null || weapon == null)
        {
            return true;
        }

        SecondaryAttackManager.EnsureRuntimeWeaponDefinitionApplied(weapon);
        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition) ||
            !TryResolveStaffSpecialCooldown(definition, out string presetName, out MeleePresetCooldownDefinition cooldown))
        {
            return true;
        }

        return MeleePresetCooldownSystem.IsReady(humanoid, presetName, cooldown);
    }

    private static bool TryResolveStaffSpecialCooldown(
        SecondaryAttackDefinition definition,
        out string presetName,
        out MeleePresetCooldownDefinition cooldown)
    {
        if (definition.Behavior is SummonEmpowerSecondaryBehavior summonEmpower)
        {
            presetName = SummonEmpowerPresetName;
            cooldown = summonEmpower.PresetCooldown;
            return true;
        }

        if (definition.Behavior is ShieldConvertSecondaryBehavior shieldConvert)
        {
            presetName = ShieldConvertPresetName;
            cooldown = shieldConvert.PresetCooldown;
            return true;
        }

        presetName = "";
        cooldown = null!;
        return false;
    }

    private static void StartSummonEmpower(Attack attack, SecondaryAttackDefinition definition)
    {
        if (attack.m_character is not Player player)
        {
            return;
        }

        SummonEmpowerSecondaryBehavior? behavior = definition.Behavior as SummonEmpowerSecondaryBehavior;
        if (behavior == null)
        {
            return;
        }

        float moveSpeedFactor = Mathf.Max(0.05f, behavior.MoveSpeedFactor);
        float attackSpeedFactor = Mathf.Max(0.05f, behavior.AttackSpeedFactor);
        float expiry = (float)SecondaryAttackManager.GetNetworkTimeSeconds() + Mathf.Max(0.1f, behavior.Duration);
        Vector3 origin = player.GetCenterPoint();

        foreach (Character candidate in Character.GetAllCharacters())
        {
            if (!IsMatchingSummonEmpowerTarget(player, candidate, definition, origin))
            {
                continue;
            }

            ApplySummonEmpower(candidate, expiry, moveSpeedFactor, attackSpeedFactor);
        }
    }

    private static void StartShieldConvert(Attack attack, SecondaryAttackDefinition definition)
    {
        if (attack.m_character is not Player player)
        {
            return;
        }

        ShieldConvertSecondaryBehavior? behavior = definition.Behavior as ShieldConvertSecondaryBehavior;
        if (behavior == null)
        {
            return;
        }

        Vector3 origin = player.GetCenterPoint();
        foreach (Character candidate in Character.GetAllCharacters())
        {
            if (!IsValidShieldConvertTarget(player, candidate, behavior.Radius, origin))
            {
                continue;
            }

            ConvertShieldToHeal(
                candidate,
                behavior.HealFactor,
                behavior.ShieldStatusEffectHash);
        }
    }

    private static bool IsMatchingSummonEmpowerTarget(Player player, Character candidate, SecondaryAttackDefinition definition, Vector3 origin)
    {
        if (candidate == null || candidate.IsDead() || candidate.IsPlayer())
        {
            return false;
        }

        SummonEmpowerSecondaryBehavior? behavior = definition.Behavior as SummonEmpowerSecondaryBehavior;
        if (behavior == null)
        {
            return false;
        }

        if ((candidate.GetCenterPoint() - origin).sqrMagnitude > behavior.Radius * behavior.Radius)
        {
            return false;
        }

        string prefabName = Utils.GetPrefabName(candidate.gameObject);
        return behavior.SummonSourcePrefabs.Any(sourcePrefab =>
            string.Equals(sourcePrefab, prefabName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsValidShieldConvertTarget(Player player, Character candidate, float radius, Vector3 origin)
    {
        if (candidate == null || candidate.IsDead())
        {
            return false;
        }

        if ((candidate.GetCenterPoint() - origin).sqrMagnitude > radius * radius)
        {
            return false;
        }

        return candidate == player || !BaseAI.IsEnemy(player, candidate);
    }

    private static void ApplySummonEmpower(Character target, float expiry, float moveSpeedFactor, float attackSpeedFactor)
    {
        if (target == null)
        {
            return;
        }

        if (SecondaryAttackManager.HasCharacterAuthority(target))
        {
            ApplySummonEmpowerState(target, expiry, moveSpeedFactor, attackSpeedFactor);
            return;
        }

        if (!SecondaryAttackManager.TryGetCharacterZdo(target, out ZNetView? nview, out _))
        {
            return;
        }

        nview!.InvokeRPC(ApplySummonEmpowerRpcName, expiry, moveSpeedFactor, attackSpeedFactor);
    }

    private static void ConvertShieldToHeal(Character target, float healFactor, int shieldStatusEffectHash)
    {
        if (target == null)
        {
            return;
        }

        if (SecondaryAttackManager.HasCharacterAuthority(target))
        {
            ApplyShieldConvertToCharacter(target, healFactor, shieldStatusEffectHash);
            return;
        }

        if (!SecondaryAttackManager.TryGetCharacterZdo(target, out ZNetView? nview, out _))
        {
            return;
        }

        nview!.InvokeRPC(ConvertShieldToHealRpcName, healFactor, shieldStatusEffectHash);
    }

    internal static void ApplySummonEmpowerState(Character character, float expiry, float moveSpeedFactor, float attackSpeedFactor)
    {
        if (!SecondaryAttackManager.TryGetCharacterZdo(character, out _, out ZDO? zdo))
        {
            return;
        }

        zdo!.Set(SummonEmpowerExpiryZdoKey, Mathf.Max(0f, expiry));
        zdo.Set(SummonEmpowerMoveSpeedBonusZdoKey, Mathf.Max(0.05f, moveSpeedFactor));
        zdo.Set(SummonEmpowerAttackCooldownReductionZdoKey, Mathf.Max(0.05f, attackSpeedFactor));
        OverheadStatusUiManager.RefreshTrackedCharacter(character);
        BroadcastStaffTargetEffect(character);
    }

    internal static bool ApplyShieldConvertToCharacter(Character character, float healFactor, int shieldStatusEffectHash)
    {
        if (character == null || character.IsDead() || healFactor <= 0f)
        {
            return false;
        }

        if (!SecondaryAttackManager.TryGetShieldRemaining(character, shieldStatusEffectHash, out SE_Shield? shield, out float remaining, out _))
        {
            SyncShieldDisplayState(character);
            return false;
        }

        float healAmount = Mathf.Max(0f, remaining * healFactor);
        if (healAmount > 0f)
        {
            character.Heal(healAmount);
        }

        character.GetSEMan().RemoveStatusEffect(shield!.NameHash());

        SyncShieldDisplayState(character);
        BroadcastStaffTargetEffect(character);
        return true;
    }

    internal static void CreateStaffTargetEffect(Character character)
    {
        if (character == null)
        {
            return;
        }

        SecondaryAttackNamedEffectSystem.Create(
            StaffTargetEffectPrefabName,
            character.GetCenterPoint(),
            character.transform.rotation,
            "staff_target_effect");
    }

    private static void BroadcastStaffTargetEffect(Character character)
    {
        if (!SecondaryAttackManager.TryGetCharacterZdo(character, out ZNetView? nview, out _))
        {
            CreateStaffTargetEffect(character);
            return;
        }

        nview!.InvokeRPC(ZNetView.Everybody, StaffTargetEffectRpcName);
    }

    internal static void SyncShieldDisplayState(Character character)
    {
        if (!SecondaryAttackManager.TryGetCharacterZdo(character, out _, out ZDO? zdo))
        {
            return;
        }

        float now = (float)SecondaryAttackManager.GetNetworkTimeSeconds();
        if (SecondaryAttackManager.TryGetShieldRemaining(character, preferredStatusEffectHash: 0, out _, out float remaining, out float remainingTime))
        {
            zdo!.Set(ShieldRemainingDisplayZdoKey, Mathf.Max(0f, remaining));
            zdo.Set(ShieldDisplayExpiryZdoKey, now + Mathf.Max(0f, remainingTime));
            OverheadStatusUiManager.RefreshTrackedCharacter(character);
            return;
        }

        zdo!.Set(ShieldRemainingDisplayZdoKey, 0f);
        zdo.Set(ShieldDisplayExpiryZdoKey, 0f);
        OverheadStatusUiManager.RefreshTrackedCharacter(character);
    }

    internal static bool TryGetSummonEmpower(Character character, out float moveSpeedFactor, out float attackSpeedFactor, out float remainingTime)
    {
        moveSpeedFactor = 1f;
        attackSpeedFactor = 1f;
        remainingTime = 0f;
        if (!SecondaryAttackManager.TryGetCharacterZdo(character, out _, out ZDO? zdo))
        {
            return false;
        }

        float now = (float)SecondaryAttackManager.GetNetworkTimeSeconds();
        float expiry = zdo!.GetFloat(SummonEmpowerExpiryZdoKey, 0f);
        if (expiry <= now)
        {
            if (expiry > 0f && SecondaryAttackManager.HasCharacterAuthority(character))
            {
                zdo.Set(SummonEmpowerExpiryZdoKey, 0f);
                zdo.Set(SummonEmpowerMoveSpeedBonusZdoKey, 0f);
                zdo.Set(SummonEmpowerAttackCooldownReductionZdoKey, 0f);
            }

            return false;
        }

        moveSpeedFactor = Mathf.Max(0.05f, zdo.GetFloat(SummonEmpowerMoveSpeedBonusZdoKey, 1f));
        attackSpeedFactor = Mathf.Max(0.05f, zdo.GetFloat(SummonEmpowerAttackCooldownReductionZdoKey, 1f));
        remainingTime = expiry - now;
        return remainingTime > 0f;
    }

    internal static bool TryGetSummonEmpowerAttackSpeedFactor(Character? character, out float attackSpeedFactor)
    {
        attackSpeedFactor = 1f;
        if (character == null ||
            character.IsPlayer() ||
            !TryGetSummonEmpower(character, out _, out float configuredFactor, out _))
        {
            return false;
        }

        attackSpeedFactor = Mathf.Max(1f, configuredFactor);
        return !Mathf.Approximately(attackSpeedFactor, 1f);
    }

    internal static bool TryGetDisplayedShieldRemaining(Character character, out float remaining)
    {
        remaining = 0f;
        if (!SecondaryAttackManager.TryGetCharacterZdo(character, out _, out ZDO? zdo))
        {
            return SecondaryAttackManager.TryGetShieldRemaining(character, preferredStatusEffectHash: 0, out _, out remaining, out _);
        }

        float now = (float)SecondaryAttackManager.GetNetworkTimeSeconds();
        float expiry = zdo!.GetFloat(ShieldDisplayExpiryZdoKey, 0f);
        if (expiry <= now)
        {
            return false;
        }

        remaining = Mathf.Max(0f, zdo.GetFloat(ShieldRemainingDisplayZdoKey, 0f));
        return remaining > 0f;
    }
}
