using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SecondaryAttacks;

internal static class StaffRuntimeSystem
{
    internal const string StaffTargetEffectRpcName = "SecondaryAttacks_SpawnStaffTargetEffect";

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
        SecondaryAttackManager.LogStaffDebug(
            $"Triggering staff special for '{activeAttack.Definition.PrefabName}': summonEmpower={activeAttack.Definition.BehaviorType == SecondaryAttackBehaviorType.SummonEmpower}, shieldConvert={activeAttack.Definition.BehaviorType == SecondaryAttackBehaviorType.ShieldConvert}, attackAnimation='{attack.m_attackAnimation}', attackType={attack.m_attackType}.");
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
            cooldown,
            out _);
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

        return MeleePresetCooldownSystem.IsReady(humanoid, weapon, presetName, cooldown);
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
            SecondaryAttackManager.LogStaffDebug($"Summon empower aborted for '{definition.PrefabName}': attacker is not a local player.");
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
        int affectedTargets = 0;

        foreach (Character candidate in Character.GetAllCharacters())
        {
            if (!IsMatchingSummonEmpowerTarget(player, candidate, definition, origin))
            {
                continue;
            }

            if (TryApplySummonEmpower(candidate, expiry, moveSpeedFactor, attackSpeedFactor))
            {
                affectedTargets++;
            }
        }

        SecondaryAttackManager.LogStaffDebug(
            $"Summon empower '{definition.PrefabName}' applied: moveSpeedFactor={moveSpeedFactor:0.###}, attackSpeedFactor={attackSpeedFactor:0.###}, duration={behavior.Duration:0.##}, targets={affectedTargets}, summonPrefabs=[{string.Join(", ", behavior.SummonSourcePrefabs)}].");
    }

    private static void StartShieldConvert(Attack attack, SecondaryAttackDefinition definition)
    {
        if (attack.m_character is not Player player)
        {
            SecondaryAttackManager.LogStaffDebug($"Shield convert aborted for '{definition.PrefabName}': attacker is not a local player.");
            return;
        }

        ShieldConvertSecondaryBehavior? behavior = definition.Behavior as ShieldConvertSecondaryBehavior;
        if (behavior == null)
        {
            return;
        }

        Vector3 origin = player.GetCenterPoint();
        int convertedTargets = 0;
        foreach (Character candidate in Character.GetAllCharacters())
        {
            if (!IsValidShieldConvertTarget(player, candidate, behavior.Radius, origin))
            {
                continue;
            }

            if (TryConvertShieldToHeal(
                candidate,
                behavior.HealFactor,
                behavior.ShieldStatusEffectHash))
            {
                convertedTargets++;
            }
        }

        SecondaryAttackManager.LogStaffDebug(
            $"Shield convert '{definition.PrefabName}' applied: radius={behavior.Radius:0.##}, healFactor={behavior.HealFactor:0.###}, targets={convertedTargets}.");
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

    private static bool TryApplySummonEmpower(Character target, float expiry, float moveSpeedFactor, float attackSpeedFactor)
    {
        if (target == null)
        {
            return false;
        }

        if (SecondaryAttackManager.HasCharacterAuthority(target))
        {
            ApplySummonEmpowerState(target, expiry, moveSpeedFactor, attackSpeedFactor);
            return true;
        }

        if (!SecondaryAttackManager.TryGetCharacterZdo(target, out ZNetView? nview, out _))
        {
            return false;
        }

        nview!.InvokeRPC(SecondaryAttackManager.ApplySummonEmpowerRpcNameForStaffRuntime, expiry, moveSpeedFactor, attackSpeedFactor);
        return true;
    }

    private static bool TryConvertShieldToHeal(Character target, float healFactor, int shieldStatusEffectHash)
    {
        if (target == null)
        {
            return false;
        }

        if (SecondaryAttackManager.HasCharacterAuthority(target))
        {
            return ApplyShieldConvertToCharacter(target, healFactor, shieldStatusEffectHash);
        }

        if (!SecondaryAttackManager.TryGetCharacterZdo(target, out ZNetView? nview, out _))
        {
            return false;
        }

        nview!.InvokeRPC(SecondaryAttackManager.ConvertShieldToHealRpcNameForStaffRuntime, healFactor, shieldStatusEffectHash);
        return true;
    }

    internal static void ApplySummonEmpowerState(Character character, float expiry, float moveSpeedFactor, float attackSpeedFactor)
    {
        if (!SecondaryAttackManager.TryGetCharacterZdo(character, out _, out ZDO? zdo))
        {
            return;
        }

        zdo!.Set(SecondaryAttackManager.SummonEmpowerExpiryZdoKeyForStaffRuntime, Mathf.Max(0f, expiry));
        zdo.Set(SecondaryAttackManager.SummonEmpowerMoveSpeedBonusZdoKeyForStaffRuntime, Mathf.Max(0.05f, moveSpeedFactor));
        zdo.Set(SecondaryAttackManager.SummonEmpowerAttackCooldownReductionZdoKeyForStaffRuntime, Mathf.Max(0.05f, attackSpeedFactor));
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
            zdo!.Set(SecondaryAttackManager.ShieldRemainingDisplayZdoKeyForStaffRuntime, Mathf.Max(0f, remaining));
            zdo.Set(SecondaryAttackManager.ShieldDisplayExpiryZdoKeyForStaffRuntime, now + Mathf.Max(0f, remainingTime));
            OverheadStatusUiManager.RefreshTrackedCharacter(character);
            return;
        }

        zdo!.Set(SecondaryAttackManager.ShieldRemainingDisplayZdoKeyForStaffRuntime, 0f);
        zdo.Set(SecondaryAttackManager.ShieldDisplayExpiryZdoKeyForStaffRuntime, 0f);
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
        float expiry = zdo!.GetFloat(SecondaryAttackManager.SummonEmpowerExpiryZdoKeyForStaffRuntime, 0f);
        if (expiry <= now)
        {
            zdo.Set(SecondaryAttackManager.SummonEmpowerExpiryZdoKeyForStaffRuntime, 0f);
            zdo.Set(SecondaryAttackManager.SummonEmpowerMoveSpeedBonusZdoKeyForStaffRuntime, 0f);
            zdo.Set(SecondaryAttackManager.SummonEmpowerAttackCooldownReductionZdoKeyForStaffRuntime, 0f);
            return false;
        }

        moveSpeedFactor = Mathf.Max(0.05f, zdo.GetFloat(SecondaryAttackManager.SummonEmpowerMoveSpeedBonusZdoKeyForStaffRuntime, 1f));
        attackSpeedFactor = Mathf.Max(0.05f, zdo.GetFloat(SecondaryAttackManager.SummonEmpowerAttackCooldownReductionZdoKeyForStaffRuntime, 1f));
        remainingTime = expiry - now;
        return remainingTime > 0f;
    }

    internal static bool TryGetDisplayedShieldRemaining(Character character, out float remaining)
    {
        remaining = 0f;
        if (!SecondaryAttackManager.TryGetCharacterZdo(character, out _, out ZDO? zdo))
        {
            return SecondaryAttackManager.TryGetShieldRemaining(character, preferredStatusEffectHash: 0, out _, out remaining, out _);
        }

        float now = (float)SecondaryAttackManager.GetNetworkTimeSeconds();
        float expiry = zdo!.GetFloat(SecondaryAttackManager.ShieldDisplayExpiryZdoKeyForStaffRuntime, 0f);
        if (expiry <= now)
        {
            return false;
        }

        remaining = Mathf.Max(0f, zdo.GetFloat(SecondaryAttackManager.ShieldRemainingDisplayZdoKeyForStaffRuntime, 0f));
        return remaining > 0f;
    }
}
