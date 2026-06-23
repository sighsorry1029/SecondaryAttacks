using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal sealed class SecondaryAttacksCharacterRpc : MonoBehaviour
{
    private Character _character = null!;
    private ZNetView? _nview;

    private void Awake()
    {
        _character = GetComponent<Character>();
        _nview = GetComponent<ZNetView>();
        if (_nview == null || !_nview.IsValid())
        {
            return;
        }

        _nview.Register<float, float, float>("SecondaryAttacks_ApplySummonEmpower", RPC_ApplySummonEmpower);
        _nview.Register<float, int>("SecondaryAttacks_ConvertShieldToHeal", RPC_ConvertShieldToHeal);
        _nview.Register<float>(BackstabSkillGainSystem.GrantSneakSkillRpcName, RPC_GrantBackstabSneakSkill);
        _nview.Register<Vector3, float>(SneakAmbushSystem.RpcName, RPC_SpawnSneakAmbushVfx);
        _nview.Register(StaffRuntimeSystem.StaffTargetEffectRpcName, RPC_SpawnStaffTargetEffect);
    }

    private void RPC_ApplySummonEmpower(long sender, float expiry, float moveSpeedFactor, float attackSpeedFactor)
    {
        if (_character == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner())
        {
            return;
        }

        StaffRuntimeSystem.ApplySummonEmpowerState(_character, expiry, moveSpeedFactor, attackSpeedFactor);
    }

    private void RPC_ConvertShieldToHeal(long sender, float healFactor, int shieldStatusEffectHash)
    {
        if (_character == null || _nview == null || !_nview.IsValid() || !_nview.IsOwner())
        {
            return;
        }

        StaffRuntimeSystem.ApplyShieldConvertToCharacter(_character, healFactor, shieldStatusEffectHash);
    }

    private void RPC_GrantBackstabSneakSkill(long sender, float amount)
    {
        if (_character is not Player player || _nview == null || !_nview.IsValid() || !_nview.IsOwner())
        {
            return;
        }

        BackstabSkillGainSystem.GrantLocal(player, amount);
    }

    private void RPC_SpawnSneakAmbushVfx(long sender, Vector3 position, float yaw)
    {
        SneakAmbushSystem.SpawnFromRpc(_character, position, yaw);
    }

    private void RPC_SpawnStaffTargetEffect(long sender)
    {
        StaffRuntimeSystem.CreateStaffTargetEffect(_character);
    }
}

[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSenseTarget), typeof(Character))]
internal static class BaseAICanSenseTargetSneakAmbushPatch
{
    private static bool Prefix(BaseAI __instance, Character target, ref bool __result)
    {
        if (!SneakAmbushSystem.ShouldBlockSense(__instance, target))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSenseTarget), typeof(Character), typeof(bool))]
internal static class BaseAICanSenseTargetPassiveSneakAmbushPatch
{
    private static bool Prefix(BaseAI __instance, Character target, ref bool __result)
    {
        if (!SneakAmbushSystem.ShouldBlockSense(__instance, target))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget), typeof(Character))]
internal static class BaseAICanSeeTargetSneakAmbushPatch
{
    private static bool Prefix(BaseAI __instance, Character target, ref bool __result)
    {
        if (!SneakAmbushSystem.ShouldBlockSense(__instance, target))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanHearTarget), typeof(Character))]
internal static class BaseAICanHearTargetSneakAmbushPatch
{
    private static bool Prefix(BaseAI __instance, Character target, ref bool __result)
    {
        if (!SneakAmbushSystem.ShouldBlockSense(__instance, target))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Projectile), "UpdateVisual")]
internal static class ProjectileUpdateVisualPatch
{
    private static void Prefix(Projectile __instance)
    {
        CopiedThrowProjectileVisualSystem.PrepareProjectileIfNeeded(__instance);
    }

    private static void Postfix(Projectile __instance)
    {
        CopiedThrowProjectileVisualSystem.EnsureProjectileVisualSpinIfNeeded(__instance);
    }
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
internal static class ProjectileSetupCopiedThrowVisualPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Projectile __instance, ItemDrop.ItemData item)
    {
        SecondaryAttackRuntimeFacade.TryApplyProjectileSetupAttribution(__instance, item);
        SecondaryAttackAdrenalineSystem.TryApplyAttackUseAdrenalineProjectileHitConversion(__instance, item);
        CopiedThrowProjectileVisualSystem.TryApplyToProjectileSetup(__instance, item);
        OverchargedBombSystem.ScaleCreatedVisuals(__instance.gameObject);
    }
}

[HarmonyPatch(typeof(Aoe), nameof(Aoe.Setup))]
internal static class AoeSetupOverchargedBombVisualPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Aoe __instance)
    {
        OverchargedBombSystem.ScaleCreatedVisuals(__instance.gameObject);
    }
}

[HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
internal static class EffectListCreateOverchargedBombVisualPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(GameObject[] __result)
    {
        OverchargedBombSystem.ScaleCreatedVisuals(__result);
    }
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
internal static class ProjectileOnHitPatch
{
    [HarmonyPriority(Priority.Last)]
    private static bool Prefix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water, Vector3 normal, out SecondaryAttackHarmonyDispatch.ProjectileOnHitState __state)
    {
        return SecondaryAttackHarmonyDispatch.ProjectileOnHitPrefix(__instance, collider, hitPoint, water, normal, out __state);
    }

    [HarmonyPriority(Priority.First)]
    private static void Postfix(Projectile __instance, Collider collider, Vector3 hitPoint, bool water, Vector3 normal, SecondaryAttackHarmonyDispatch.ProjectileOnHitState __state) =>
        SecondaryAttackHarmonyDispatch.ProjectileOnHitPostfix(__instance, collider, hitPoint, water, normal, __state);
}

[HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
internal static class ProjectileOnHitOverchargedBombPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Projectile __instance, out OverchargedBombSystem.ProjectileHitScaleState __state)
    {
        __state = OverchargedBombSystem.BeginProjectileHit(__instance);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(OverchargedBombSystem.ProjectileHitScaleState __state)
    {
        OverchargedBombSystem.EndProjectileHit(__state);
    }
}

[HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
internal static class DestructibleDamageProjectileToolTierPatch
{
    private static void Prefix(HitData hit)
    {
        SecondaryAttackProjectileToolTierSystem.ApplyCurrentProjectileHitToolTierIfNeeded(hit, "Destructible.Damage");
    }
}

[HarmonyPatch(typeof(MineRock), nameof(MineRock.Damage))]
internal static class MineRockDamageProjectileToolTierPatch
{
    private static void Prefix(HitData hit)
    {
        SecondaryAttackProjectileToolTierSystem.ApplyCurrentProjectileHitToolTierIfNeeded(hit, "MineRock.Damage");
    }
}

[HarmonyPatch(typeof(MineRock5), nameof(MineRock5.Damage))]
internal static class MineRock5DamageProjectileToolTierPatch
{
    private static void Prefix(HitData hit)
    {
        SecondaryAttackProjectileToolTierSystem.ApplyCurrentProjectileHitToolTierIfNeeded(hit, "MineRock5.Damage");
    }
}

[HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]
internal static class TreeBaseDamageProjectileToolTierPatch
{
    private static void Prefix(HitData hit)
    {
        SecondaryAttackProjectileToolTierSystem.ApplyCurrentProjectileHitToolTierIfNeeded(hit, "TreeBase.Damage");
    }
}

[HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Damage))]
internal static class TreeLogDamageProjectileToolTierPatch
{
    private static void Prefix(HitData hit)
    {
        SecondaryAttackProjectileToolTierSystem.ApplyCurrentProjectileHitToolTierIfNeeded(hit, "TreeLog.Damage");
    }
}

[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
internal static class WearNTearDamageProjectileToolTierPatch
{
    private static void Prefix(HitData hit)
    {
        SecondaryAttackProjectileToolTierSystem.ApplyCurrentProjectileHitToolTierIfNeeded(hit, "WearNTear.Damage");
    }
}

[HarmonyPatch(typeof(Character), "Awake")]
internal static class CharacterAwakeSecondaryAttacksPatch
{
    private static void Postfix(Character __instance)
    {
        if (__instance.GetComponent<ZNetView>() == null)
        {
            return;
        }

        if (__instance.GetComponent<SecondaryAttacksCharacterRpc>() == null)
        {
            __instance.gameObject.AddComponent<SecondaryAttacksCharacterRpc>();
        }
    }
}

[HarmonyPatch(typeof(EnemyHud), "LateUpdate")]
internal static class EnemyHudLateUpdatePatch
{
    private static void Postfix(EnemyHud __instance)
    {
        OverheadStatusUiManager.Update(__instance);
        SummonQualityHudSystem.Update(__instance);
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class PlayerUpdatePendingConfigPatch
{
    private static void Postfix(Player __instance, bool ___m_attackHold, bool ___m_secondaryAttackHold, bool ___m_secondaryAttack, ref bool ___m_blocking)
    {
        SecondaryAttackHarmonyDispatch.PlayerUpdatePostfix(__instance, ___m_attackHold, ___m_secondaryAttackHold, ___m_secondaryAttack, ref ___m_blocking);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.IsBlocking))]
internal static class HumanoidIsBlockingStickyDetonatorPatch
{
    private static bool Prefix(Humanoid __instance, ref bool __result)
    {
        if (!StickyDetonatorSystem.ShouldSuppressBlock(__instance))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
internal static class PlayerUpdatePlacementGhostPatch
{
    private static void Postfix(Player __instance)
    {
        SecondaryAttackHarmonyDispatch.PlayerUpdatePlacementGhostPostfix(__instance);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.TryPlacePiece))]
internal static class PlayerTryPlacePiecePatch
{
    private static bool Prefix(Player __instance, Piece piece, ref bool __result)
    {
        return SecondaryAttackHarmonyDispatch.PlayerTryPlacePiecePrefix(__instance, piece, ref __result);
    }

    private static void Postfix(Player __instance, Piece piece, bool __result)
    {
        SecondaryAttackHarmonyDispatch.PlayerTryPlacePiecePostfix(__instance, piece, __result);
    }
}

[HarmonyPatch(typeof(SEMan), "Internal_AddStatusEffect")]
internal static class SEManInternalAddStatusEffectShieldDisplayPatch
{
    private static void Postfix(SEMan __instance, int nameHash)
    {
        if (__instance.GetStatusEffect(nameHash) is not SE_Shield)
        {
            return;
        }

        Character? character = SecondaryAttackManager.GetSeManCharacter(__instance);
        if (character != null)
        {
            StaffRuntimeSystem.SyncShieldDisplayState(character);
        }
    }
}

[HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveStatusEffect), new[] { typeof(int), typeof(bool) })]
internal static class SEManRemoveStatusEffectShieldDisplayPatch
{
    private static void Prefix(SEMan __instance, int nameHash, out bool __state)
    {
        __state = __instance.GetStatusEffect(nameHash) is SE_Shield;
    }

    private static void Postfix(SEMan __instance, bool __result, bool __state)
    {
        if (!__result || !__state)
        {
            return;
        }

        Character? character = SecondaryAttackManager.GetSeManCharacter(__instance);
        if (character != null)
        {
            StaffRuntimeSystem.SyncShieldDisplayState(character);
        }
    }
}

[HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.OnDamaged))]
internal static class SEShieldOnDamagedShieldDisplayPatch
{
    private static void Postfix(SE_Shield __instance)
    {
        if (__instance.m_character == null)
        {
            return;
        }

        StaffRuntimeSystem.SyncShieldDisplayState(__instance.m_character);
    }
}

[HarmonyPatch(typeof(SE_Shield), nameof(SE_Shield.IsDone))]
internal static class SEShieldIsDoneShieldDisplayPatch
{
    private static void Postfix(SE_Shield __instance, bool __result)
    {
        if (!__result || __instance.m_character == null)
        {
            return;
        }

        StaffRuntimeSystem.SyncShieldDisplayState(__instance.m_character);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.UseHealth))]
internal static class CharacterUseHealthBloodMagicSkillGainPatch
{
    private static void Prefix(Character __instance, out float __state)
    {
        __state = __instance != null ? __instance.GetHealth() : 0f;
    }

    private static void Postfix(Character __instance, float __state)
    {
        if (__instance != null)
        {
            BloodMagicSkillGainSystem.TryGrantForHealthUse(__instance, __state);
        }
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.GetAttackHealth))]
internal static class AttackGetAttackHealthBloodMagicCostPatch
{
    private static void Postfix(Attack __instance, ref float __result)
    {
        BloodMagicSkillGainSystem.ApplyMaxHealthPercentageCost(__instance, ref __result);
    }
}

[HarmonyPatch(typeof(Character), "RPC_Damage")]
internal static class CharacterRpcDamageBackstabSkillGainPatch
{
    private static void Prefix(Character __instance, HitData hit, out BackstabSkillGainSystem.BackstabDamageState __state)
    {
        __state = BackstabSkillGainSystem.CaptureBackstabState(__instance, hit);
    }

    private static void Postfix(Character __instance, HitData hit, BackstabSkillGainSystem.BackstabDamageState __state)
    {
        BackstabSkillGainSystem.TryGrantForBackstab(__instance, hit, __state);
    }
}

[HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
internal static class SkillsRaiseSkillBloodMagicPatch
{
    private static bool Prefix(Skills.SkillType skillType, ref float factor)
    {
        if (BloodMagicSkillGainSystem.ShouldBlockBloodMagicRaise(skillType))
        {
            return false;
        }

        HarvestSweepSystem.ApplySkillRaiseFactor(skillType, ref factor);
        return true;
    }
}

[HarmonyPatch(typeof(SEMan), nameof(SEMan.ApplyStatusEffectSpeedMods))]
internal static class SEManApplyStatusEffectSpeedModsPatch
{
    private static void Postfix(SEMan __instance, ref float speed)
    {
        Character? character = SecondaryAttackManager.GetSeManCharacter(__instance);
        if (character == null)
        {
            return;
        }

        if (!StaffRuntimeSystem.TryGetSummonEmpower(character, out float moveSpeedFactor, out _, out _) ||
            Mathf.Approximately(moveSpeedFactor, 1f))
        {
            return;
        }

        speed *= moveSpeedFactor;
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetTimeSinceLastAttack))]
internal static class HumanoidGetTimeSinceLastAttackPatch
{
    private static void Postfix(Humanoid __instance, ref float __result)
    {
        if (!StaffRuntimeSystem.TryGetSummonEmpower(__instance, out _, out float attackSpeedFactor, out _) ||
            Mathf.Approximately(attackSpeedFactor, 1f))
        {
            return;
        }

        __result *= Mathf.Max(0.05f, attackSpeedFactor);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
internal static class AttackStartCooldownAdjustmentPatch
{
    private static void Prefix(Attack __instance, out float __state)
    {
        __state = __instance?.m_attackUseAdrenaline ?? 0f;
        if (__instance != null &&
            (SecondaryAttackAdrenalineSystem.ShouldSuppressAttackUseAdrenaline(__instance) ||
             SecondaryAttackAdrenalineSystem.TryBeginAttackUseAdrenalineProjectileHitConversion(__instance)))
        {
            __instance.m_attackUseAdrenaline = 0f;
        }
    }

    private static void Postfix(Attack __instance, bool __result, float __state)
    {
        if (__instance == null)
        {
            return;
        }

        __instance.m_attackUseAdrenaline = __state;

        if (!__result || __instance.m_character == null || __instance.m_weapon == null)
        {
            return;
        }

        if (!StaffRuntimeSystem.TryGetSummonEmpower(__instance.m_character, out _, out float attackSpeedFactor, out _) ||
            Mathf.Approximately(attackSpeedFactor, 1f))
        {
            return;
        }

        float intervalReduction = 1f - 1f / Mathf.Max(0.05f, attackSpeedFactor);
        __instance.m_weapon.m_lastAttackTime -= __instance.m_weapon.m_shared.m_aiAttackInterval * intervalReduction;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.AddAdrenaline))]
internal static class PlayerAddAdrenalineSecondaryAttackPatch
{
    private static bool Prefix(Player __instance, ref float v)
    {
        return SecondaryAttackAdrenalineSystem.TryModify(__instance, ref v);
    }
}

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
internal static class ObjectDbAwakePatch
{
    private static void Postfix(ObjectDB __instance)
    {
        SecondaryAttackFacade.ApplyPendingConfigToObjectDb(__instance, emitMissingWarnings: false);
    }
}

[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
internal static class ObjectDbCopyOtherDbPatch
{
    private static void Postfix(ObjectDB __instance)
    {
        SecondaryAttackFacade.ApplyPendingConfigToObjectDb(__instance, emitMissingWarnings: false);
    }
}

[HarmonyPatch(typeof(ZNetScene), nameof(ZNetScene.Awake))]
[HarmonyAfter(SecondaryAttackCompat.MagicPluginGuid)]
internal static class ZNetSceneAwakeSummonPrefabOverridePatch
{
    private static void Postfix(ZNetScene __instance)
    {
        SecondaryAttackFacade.ApplyPendingConfigToZNetScene(__instance, emitMissingWarnings: false);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdateAttackBowDraw))]
internal static class PlayerUpdateAttackBowDrawPatch
{
    private static bool Prefix(
        Player __instance,
        ItemDrop.ItemData weapon,
        float dt,
        ref float ___m_attackDrawTime,
        ref bool ___m_blocking,
        ref bool ___m_attackHold,
        ref bool ___m_secondaryAttackHold,
        ref bool ___m_secondaryAttack,
        ZSyncAnimation ___m_zanim,
        SEMan ___m_seman)
    {
        if (weapon == null || !SecondaryAttackRuntimeFacade.ShouldHandleBowDraw(weapon))
        {
            return true;
        }

        SecondaryAttackManager.UpdateCustomBowDraw(
            __instance,
            weapon,
            dt,
            ref ___m_attackDrawTime,
            ___m_blocking,
            ___m_attackHold,
            ___m_secondaryAttackHold,
            ___m_secondaryAttack,
            ___m_zanim,
            ___m_seman);
        return false;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdateWeaponLoading))]
internal static class PlayerUpdateWeaponLoadingReloadStatePatch
{
    private static void Prefix(Player __instance, ItemDrop.ItemData weapon)
    {
        SecondaryAttackManager.TryRestorePersistedReloadedWeaponState(__instance, weapon);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.SetWeaponLoaded))]
internal static class PlayerSetWeaponLoadedReloadStatePatch
{
    private static void Prefix(Player __instance, out ItemDrop.ItemData? __state)
    {
        __state = __instance.m_weaponLoaded;
    }

    private static void Postfix(Player __instance, ItemDrop.ItemData weapon, ItemDrop.ItemData? __state)
    {
        SecondaryAttackManager.OnWeaponLoadedStateChanged(__instance, __state, weapon);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
internal static class PlayerAwakeAnimationDumpPatch
{
    private static void Postfix(Player __instance)
    {
        SecondaryAttackManager.DumpPlayerAnimatorReferences(__instance);
    }
}

[HarmonyPatch(typeof(Player), "Start")]
internal static class PlayerStartCustomAnimationDumpPatch
{
    private static void Postfix(Player __instance)
    {
        SecondaryAttackManager.DumpCustomAnimationReferences(__instance);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
internal static class HumanoidStartAttackPatch
{
    private static bool Prefix(
        Humanoid __instance,
        bool secondaryAttack,
        ref bool __result,
        ItemDrop.ItemData ___m_leftItem,
        ItemDrop.ItemData ___m_rightItem,
        out SecondaryAttackStartAttackDispatch.StartAttackState __state)
    {
        return SecondaryAttackStartAttackDispatch.Prefix(
            __instance,
            secondaryAttack,
            ref __result,
            ___m_leftItem,
            ___m_rightItem,
            out __state);
    }

    private static void Postfix(
        Humanoid __instance,
        bool secondaryAttack,
        bool __result,
        SecondaryAttackStartAttackDispatch.StartAttackState __state) =>
        SecondaryAttackStartAttackDispatch.Postfix(__instance, secondaryAttack, __result, __state);
}

[HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
internal static class AttackOnAttackTriggerPatch
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Attack __instance, out AttackOnAttackTriggerState __state)
    {
        bool reloadState = SecondaryAttackManager.BeginReloadStateConsumption(__instance);
        __state = new AttackOnAttackTriggerState(reloadState);
        return !SecondaryAttackRuntimeFacade.TryHandleCustomAttackTrigger(__instance);
    }

    private static void Postfix(Attack __instance, AttackOnAttackTriggerState __state)
    {
        SecondaryAttackManager.EndReloadStateConsumption(__instance, __state.ReloadState);
        SecondaryAttackRuntimeFacade.TryTriggerRiftTrailAfterAttack(__instance);
        SecondaryAttackRuntimeFacade.TryTriggerFractureLineAfterAttack(__instance);
    }

    private readonly struct AttackOnAttackTriggerState
    {
        internal AttackOnAttackTriggerState(bool reloadState)
        {
            ReloadState = reloadState;
        }

        internal bool ReloadState { get; }
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
internal static class AttackDoMeleeAttackSecondaryDurabilityFactorPatch
{
    private static void Prefix(Attack __instance, out SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        __state = SecondaryAttackManager.BeginSecondaryAttackDurabilityAdjustment(__instance);
    }

    private static void Postfix(SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(__state);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
internal static class AttackDoAreaAttackSecondaryDurabilityFactorPatch
{
    private static void Prefix(Attack __instance, out SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        __state = SecondaryAttackManager.BeginSecondaryAttackDurabilityAdjustment(__instance);
    }

    private static void Postfix(SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(__state);
    }
}

[HarmonyPatch(typeof(Attack), "ProjectileAttackTriggered")]
internal static class AttackProjectileAttackTriggeredSecondaryDurabilityFactorPatch
{
    private static void Prefix(Attack __instance, out SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        __state = SecondaryAttackManager.BeginSecondaryAttackDurabilityAdjustment(__instance);
    }

    private static void Postfix(SecondaryAttackManager.SecondaryAttackDurabilityAdjustmentState __state)
    {
        SecondaryAttackManager.EndSecondaryAttackDurabilityAdjustment(__state);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst))]
internal static class AttackFireProjectileBurstPatch
{
    private static bool Prefix(Attack __instance, out CopiedThrowProjectileVisualSystem.BurstScope __state)
    {
        return SecondaryAttackHarmonyDispatch.AttackFireProjectileBurstPrefix(__instance, out __state);
    }

    private static void Postfix(CopiedThrowProjectileVisualSystem.BurstScope __state)
    {
        SecondaryAttackHarmonyDispatch.AttackFireProjectileBurstPostfix(__state);
    }
}
