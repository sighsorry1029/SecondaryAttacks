using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ProjectileLaunchData = SecondaryAttacks.ProjectileRuntimeSystem.ProjectileLaunchData;

namespace SecondaryAttacks;

internal static partial class SecondaryAttackManager
{
    private static readonly Dictionary<Humanoid, ItemDrop.ItemData> ReloadConsumptionWeapons = new();

    private static int GetCurrentSnapshotId()
    {
        return SecondaryAttackFacade.CurrentCompiledSnapshot.SnapshotId;
    }

    internal static void ResetWorldApplyTransientState()
    {
    }

    internal static bool TryMarkCompatibilityWarningReported(string warningKey)
    {
        return SecondaryAttackWarningLog.TryMarkWarning(warningKey);
    }

    internal static bool TryMarkCompatibilityIssueReported(string issueKey)
    {
        return SecondaryAttackWarningLog.TryMarkIssue(issueKey);
    }

    internal static int GetRuntimeWeaponAppliedWorldRevision(ItemDrop.ItemData weapon)
    {
        RuntimeWeaponDefinitionState state = RuntimeWeaponDefinitionStates.GetValue(weapon, _ => new RuntimeWeaponDefinitionState());
        return state.AppliedWorldRevision;
    }

    internal static void SetRuntimeWeaponAppliedWorldRevision(ItemDrop.ItemData weapon, int applyRevision)
    {
        RuntimeWeaponDefinitionState state = RuntimeWeaponDefinitionStates.GetValue(weapon, _ => new RuntimeWeaponDefinitionState());
        state.AppliedWorldRevision = applyRevision;
    }

    internal static void TryRestorePersistedReloadedWeaponState(Player player, ItemDrop.ItemData weapon)
    {
        if (player == null || weapon == null || !IsReloadableWeapon(weapon))
        {
            return;
        }

        if (player.m_weaponLoaded == weapon || !weapon.m_customData.TryGetValue(SecondaryAttackRuntimeFacade.ReloadLoadedCustomDataKey, out string value) || value != "1")
        {
            return;
        }

        player.SetWeaponLoaded(weapon);
    }

    internal static void OnWeaponLoadedStateChanged(Player player, ItemDrop.ItemData? previousWeapon, ItemDrop.ItemData? newWeapon)
    {
        if (player == null)
        {
            return;
        }

        if (newWeapon != null)
        {
            SetPersistedReloadState(player, newWeapon, loaded: true);
            return;
        }

        if (previousWeapon == null)
        {
            return;
        }

        bool consumed = IsReloadStateConsumptionActive(player, previousWeapon);
        if (consumed)
        {
            SetPersistedReloadState(player, previousWeapon, loaded: false);
        }
        else
        {
            SetPersistedReloadState(player, previousWeapon, loaded: true);
        }
    }

    internal static bool BeginReloadStateConsumption(Attack attack)
    {
        if (attack?.m_character == null || attack.m_weapon == null || !attack.m_requiresReload)
        {
            return false;
        }

        ReloadConsumptionWeapons[attack.m_character] = attack.m_weapon;
        return true;
    }

    internal static void EndReloadStateConsumption(Attack attack, bool active)
    {
        if (!active || attack?.m_character == null)
        {
            return;
        }

        ReloadConsumptionWeapons.Remove(attack.m_character);
    }

    private static bool IsReloadableWeapon(ItemDrop.ItemData weapon)
    {
        return weapon?.m_shared?.m_attack?.m_requiresReload == true;
    }

    private static bool IsReloadStateConsumptionActive(Humanoid humanoid, ItemDrop.ItemData weapon)
    {
        return ReloadConsumptionWeapons.TryGetValue(humanoid, out ItemDrop.ItemData consumedWeapon) && consumedWeapon == weapon;
    }

    private static void SetPersistedReloadState(Player player, ItemDrop.ItemData weapon, bool loaded)
    {
        if (!IsReloadableWeapon(weapon))
        {
            return;
        }

        bool changed;
        if (loaded)
        {
            changed = !weapon.m_customData.TryGetValue(SecondaryAttackRuntimeFacade.ReloadLoadedCustomDataKey, out string value) || value != "1";
            weapon.m_customData[SecondaryAttackRuntimeFacade.ReloadLoadedCustomDataKey] = "1";
        }
        else
        {
            changed = weapon.m_customData.Remove(SecondaryAttackRuntimeFacade.ReloadLoadedCustomDataKey);
        }

        if (changed)
        {
            player.GetInventory()?.Changed();
        }
    }

    private static readonly MethodInfo MemberwiseCloneMethod = AccessTools.Method(typeof(object), "MemberwiseClone")!;

    internal static Attack CloneAttack(Attack? sourceAttack)
    {
        return sourceAttack == null
            ? new Attack()
            : (Attack)MemberwiseCloneMethod.Invoke(sourceAttack, Array.Empty<object>())!;
    }

    internal static void NormalizeCopiedProjectileAim(Attack? attack, SecondaryAttackDefinition? definition)
    {
        if (attack == null ||
            definition == null ||
            definition.Behavior is not CopiedSecondaryBehavior ||
            definition.OnProjectileHit == null && definition.Boomerang == null ||
            attack.m_attackType != Attack.AttackType.Projectile && attack.m_attackProjectile == null)
        {
            return;
        }

        attack.m_useCharacterFacing = false;
        attack.m_useCharacterFacingYAim = false;
    }

    internal static bool TryCreateDefinition(
        SecondaryAttackDefinitionBuildContext buildContext,
        string prefabName,
        ItemDrop itemDrop,
        NormalizedWeaponConfig weaponConfig,
        out SecondaryAttackDefinition? definition)
    {
        return SecondaryAttackDefinitionCompiler.TryCreateDefinition(buildContext, prefabName, itemDrop, weaponConfig, out definition);
    }

    internal static bool HasCharacterAuthority(Character? character)
    {
        return TryGetCharacterZdo(character, out ZNetView? nview, out _) && nview!.IsOwner();
    }

    internal static bool TryGetCharacterZdo(Character? character, out ZNetView? nview, out ZDO? zdo)
    {
        nview = character != null ? character.GetComponent<ZNetView>() : null;
        zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
        return nview != null && nview.IsValid() && zdo != null;
    }

    internal static float GetNetworkTimeSeconds()
    {
        return ZNet.instance != null ? (float)ZNet.instance.GetTimeSeconds() : Time.time;
    }

    internal static bool TryGetShieldRemaining(
        Character character,
        int preferredStatusEffectHash,
        out SE_Shield? shield,
        out float remaining,
        out float remainingTime)
    {
        shield = null;
        remaining = 0f;
        remainingTime = 0f;
        SEMan? seMan = character?.GetSEMan();
        if (seMan == null)
        {
            return false;
        }

        if (preferredStatusEffectHash != 0 && seMan.GetStatusEffect(preferredStatusEffectHash) is SE_Shield preferredShield &&
            ShieldAccess.TryReadRemaining(preferredShield, out remaining, out remainingTime))
        {
            shield = preferredShield;
            return true;
        }

        foreach (StatusEffect statusEffect in seMan.GetStatusEffects())
        {
            if (statusEffect is not SE_Shield candidateShield)
            {
                continue;
            }

            if (!ShieldAccess.TryReadRemaining(candidateShield, out remaining, out remainingTime))
            {
                continue;
            }

            shield = candidateShield;
            return true;
        }

        return false;
    }

    internal static void PlayTriggeredAttackEffects(Attack attack)
    {
        PlayTriggeredAttackEffects(attack, 1f);
    }

    internal static void PlayTriggeredAttackEffects(Attack attack, float durabilityFactor)
    {
        DrainAttackDurability(attack, durabilityFactor);

        Transform origin = attack.m_character.transform;
        attack.m_weapon.m_shared.m_triggerEffect.Create(origin.position, attack.m_character.transform.rotation, origin);
        attack.m_triggerEffect.Create(origin.position, attack.m_character.transform.rotation, origin);
        attack.m_character.AddNoise(attack.m_attackHitNoise);
    }

    internal static void DrainAttackDurability(Attack attack, float durabilityFactor)
    {
        if (attack?.m_weapon == null || attack.m_character == null)
        {
            return;
        }

        DrainItemDurability(attack.m_character, attack.m_weapon, durabilityFactor);
    }

    internal static void DrainItemDurability(Character character, ItemDrop.ItemData weapon, float durabilityFactor)
    {
        if (character == null ||
            weapon?.m_shared == null ||
            !weapon.m_shared.m_useDurability ||
            !character.IsPlayer())
        {
            return;
        }

        float drain = GetItemDurabilityDrain(weapon) * Mathf.Max(0f, durabilityFactor);
        if (drain <= 0f)
        {
            return;
        }

        weapon.m_durability = Mathf.Max(0f, weapon.m_durability - drain);
    }

    internal static float GetItemDurabilityDrain(ItemDrop.ItemData weapon)
    {
        float drain = weapon?.m_shared?.m_useDurabilityDrain ?? 0f;
        return drain > 0f ? drain : 1f;
    }

    internal static SecondaryAttackDurabilityAdjustmentState BeginSecondaryAttackDurabilityAdjustment(Attack attack)
    {
        if (attack?.m_weapon?.m_shared == null ||
            attack.m_character == null ||
            !attack.m_weapon.m_shared.m_useDurability ||
            !attack.m_character.IsPlayer() ||
            !SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) ||
            activeAttack == null)
        {
            return SecondaryAttackDurabilityAdjustmentState.Empty;
        }

        float factor = Mathf.Max(0f, ResolveActiveAttackDurabilityFactor(activeAttack));
        if (Mathf.Approximately(factor, 1f))
        {
            return SecondaryAttackDurabilityAdjustmentState.Empty;
        }

        return new SecondaryAttackDurabilityAdjustmentState(attack.m_weapon, attack.m_weapon.m_durability, factor);
    }

    internal static void EndSecondaryAttackDurabilityAdjustment(SecondaryAttackDurabilityAdjustmentState state)
    {
        if (!state.Applies || state.Weapon?.m_shared == null)
        {
            return;
        }

        float actualDrain = state.BeforeDurability - state.Weapon.m_durability;
        if (actualDrain <= 0.001f)
        {
            return;
        }

        float targetDrain = actualDrain * state.Factor;
        state.Weapon.m_durability = Mathf.Clamp(
            state.BeforeDurability - targetDrain,
            0f,
            Mathf.Max(state.Weapon.m_shared.m_maxDurability, state.BeforeDurability));
    }

    internal static float ResolveActiveAttackDurabilityFactor(ActiveSecondaryAttack activeAttack)
    {
        return activeAttack.Definition.DurabilityFactor;
    }

    internal readonly struct SecondaryAttackDurabilityAdjustmentState
    {
        internal static readonly SecondaryAttackDurabilityAdjustmentState Empty = new(null, 0f, 1f);

        internal SecondaryAttackDurabilityAdjustmentState(ItemDrop.ItemData? weapon, float beforeDurability, float factor)
        {
            Weapon = weapon;
            BeforeDurability = beforeDurability;
            Factor = factor;
        }

        internal ItemDrop.ItemData? Weapon { get; }

        internal float BeforeDurability { get; }

        internal float Factor { get; }

        internal bool Applies => Weapon != null;
    }

    internal static Character? GetSeManCharacter(SEMan seMan)
    {
        return ShieldAccess.GetSeManCharacter(seMan);
    }

    internal static float ClosestSegmentProgress(Vector3 start, Vector3 end, Vector3 point)
    {
        Vector3 segment = end - start;
        float lengthSq = segment.sqrMagnitude;
        if (lengthSq <= Mathf.Epsilon)
        {
            return 0f;
        }

        return Mathf.Clamp01(Vector3.Dot(point - start, segment) / lengthSq);
    }

    internal static Vector3 ResolveSafeClosestPoint(Collider collider, Vector3 origin)
    {
        if (collider is MeshCollider meshCollider && !meshCollider.convex)
        {
#pragma warning disable CS0618
            return meshCollider.ClosestPointOnBounds(origin);
#pragma warning restore CS0618
        }

        return collider.ClosestPoint(origin);
    }

    internal static void RegisterAsyncSecondaryWork(Character? owner)
    {
        if (owner == null)
        {
            return;
        }

        AsyncSecondaryActivityState state = AsyncSecondaryActivityStates.GetValue(owner, _ => new AsyncSecondaryActivityState());
        state.ActiveCount++;
    }

    internal static void UnregisterAsyncSecondaryWork(Character? owner)
    {
        if (owner == null)
        {
            return;
        }

        if (!AsyncSecondaryActivityStates.TryGetValue(owner, out AsyncSecondaryActivityState? state))
        {
            return;
        }

        state.ActiveCount = Mathf.Max(0, state.ActiveCount - 1);
    }

    private static bool HasActiveAsyncSecondaryWork(Character? owner)
    {
        return owner != null &&
               AsyncSecondaryActivityStates.TryGetValue(owner, out AsyncSecondaryActivityState? state) &&
               state.ActiveCount > 0;
    }

    internal static bool HasActiveAsyncSecondaryWorkForFacade(Character? owner)
    {
        return HasActiveAsyncSecondaryWork(owner);
    }

    private sealed class AsyncSecondaryActivityState
    {
        public int ActiveCount { get; set; }
    }
}
