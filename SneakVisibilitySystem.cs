using System;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SneakVisibilitySystem
{
    private const float MinimumVisibility = 0.1f;

    internal readonly struct UpdateStealthState(float previousStealthFactor)
    {
        internal readonly float PreviousStealthFactor = previousStealthFactor;
    }

    internal static UpdateStealthState Capture(Player player)
    {
        return new UpdateStealthState(player != null ? player.m_stealthFactor : 1f);
    }

    internal static void Apply(Player player, float dt, UpdateStealthState state)
    {
        if (player == null ||
            player.m_nview == null ||
            !player.m_nview.IsValid() ||
            !player.m_nview.IsOwner() ||
            QuickstepSystem.IsActive(player) ||
            !player.IsCrouching())
        {
            return;
        }

        float factor = Mathf.Clamp(SecondaryAttacksPlugin.SneakVisibilitySkillEffectFactor?.Value ?? 1f, 1f, 2f);
        if (Mathf.Approximately(factor, 1f))
        {
            return;
        }

        float target = CalculateTargetVisibility(player, factor);
        float adjusted = Mathf.MoveTowards(state.PreviousStealthFactor, target, dt / 4f);
        if (Mathf.Approximately(adjusted, player.m_stealthFactor))
        {
            return;
        }

        player.m_stealthFactorTarget = target;
        player.m_stealthFactor = adjusted;
        player.m_nview.GetZDO().Set(ZDOVars.s_stealth, adjusted);
    }

    internal static void ApplyMovementSpeed(Player player, ref float speedFactor)
    {
        if (player == null ||
            QuickstepSystem.IsActive(player) ||
            !player.IsCrouching())
        {
            return;
        }

        float maxMultiplier = Mathf.Clamp(SecondaryAttacksPlugin.SneakMovementSpeedSkillFactor?.Value ?? 1f, 1f, 2f);
        if (Mathf.Approximately(maxMultiplier, 1f))
        {
            return;
        }

        float sneak = Mathf.Clamp01(player.m_skills.GetSkillFactor(Skills.SkillType.Sneak));
        speedFactor *= Mathf.Lerp(1f, maxMultiplier, sneak);
    }

    internal static CrouchSpeedState ApplyCrouchSpeed(Player player)
    {
        if (player == null ||
            player.m_nview == null ||
            !player.m_nview.IsValid() ||
            !player.m_nview.IsOwner() ||
            QuickstepSystem.IsActive(player) ||
            !player.IsCrouching())
        {
            return default;
        }

        float maxMultiplier = Mathf.Clamp(SecondaryAttacksPlugin.SneakMovementSpeedSkillFactor?.Value ?? 1f, 1f, 2f);
        if (Mathf.Approximately(maxMultiplier, 1f))
        {
            return default;
        }

        float sneak = Mathf.Clamp01(player.m_skills.GetSkillFactor(Skills.SkillType.Sneak));
        float multiplier = Mathf.Lerp(1f, maxMultiplier, sneak);
        if (Mathf.Approximately(multiplier, 1f))
        {
            return default;
        }

        float original = player.m_crouchSpeed;
        player.m_crouchSpeed = original * multiplier;
        return new CrouchSpeedState(player, original);
    }

    internal static void RestoreCrouchSpeed(CrouchSpeedState state)
    {
        if (state.Player != null)
        {
            state.Player.m_crouchSpeed = state.OriginalCrouchSpeed;
        }
    }

    internal static void RestoreCrouchSpeed(ref CrouchSpeedState state)
    {
        RestoreCrouchSpeed(state);
        state = default;
    }

    private static float CalculateTargetVisibility(Player player, float factor)
    {
        float sneak = Mathf.Clamp01(player.m_skills.GetSkillFactor(Skills.SkillType.Sneak));
        float light = StealthSystem.instance != null
            ? Mathf.Clamp01(StealthSystem.instance.GetLightFactor(player.GetCenterPoint()))
            : 1f;

        float baseVisibility = 0.5f + light * 0.5f;
        float vanillaMaxReduction = 0.3f + light * 0.1f;
        float target = baseVisibility - vanillaMaxReduction * sneak * factor;
        target = Mathf.Clamp(target, MinimumVisibility, 1f);

        player.m_seman.ModifyStealth(target, ref target);
        return Mathf.Clamp(target, MinimumVisibility, 1f);
    }

    internal readonly struct CrouchSpeedState(Player? player, float originalCrouchSpeed)
    {
        internal readonly Player? Player = player;
        internal readonly float OriginalCrouchSpeed = originalCrouchSpeed;
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdateStealth))]
internal static class PlayerUpdateStealthSneakVisibilityPatch
{
    private static void Prefix(Player __instance, out SneakVisibilitySystem.UpdateStealthState __state)
    {
        __state = SneakVisibilitySystem.Capture(__instance);
    }

    private static void Postfix(Player __instance, float dt, SneakVisibilitySystem.UpdateStealthState __state)
    {
        SneakVisibilitySystem.Apply(__instance, dt, __state);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.GetJogSpeedFactor))]
internal static class PlayerGetJogSpeedFactorSneakMovementPatch
{
    private static void Postfix(Player __instance, ref float __result)
    {
        SneakVisibilitySystem.ApplyMovementSpeed(__instance, ref __result);
    }
}

[HarmonyPatch(typeof(Character), "UpdateWalking")]
internal static class CharacterUpdateWalkingSneakMovementPatch
{
    private static void Prefix(Character __instance, out SneakVisibilitySystem.CrouchSpeedState __state)
    {
        __state = __instance is Player player
            ? SneakVisibilitySystem.ApplyCrouchSpeed(player)
            : default;
    }

    private static void Postfix(ref SneakVisibilitySystem.CrouchSpeedState __state)
    {
        SneakVisibilitySystem.RestoreCrouchSpeed(ref __state);
    }

    private static void Finalizer(ref SneakVisibilitySystem.CrouchSpeedState __state)
    {
        SneakVisibilitySystem.RestoreCrouchSpeed(ref __state);
    }
}

internal static class SneakCrouchPreservationSystem
{
    [ThreadStatic] private static DotDamageKind _activeDamageKind;
    [ThreadStatic] private static Player? _activePlayer;

    internal static DotDamageScope Begin(StatusEffect statusEffect, DotDamageKind damageKind)
    {
        Player? player = statusEffect?.m_character as Player;
        bool shouldTrack =
            SecondaryAttacksPlugin.KeepCrouchingDuringElementalDamageOverTime?.Value == SecondaryAttacksPlugin.Toggle.On &&
            player != null &&
            player.m_nview != null &&
            player.m_nview.IsValid() &&
            player.m_nview.IsOwner();

        if (!shouldTrack && _activeDamageKind == DotDamageKind.None)
        {
            return default;
        }

        DotDamageScope scope = new(_activeDamageKind, _activePlayer, active: true);
        _activeDamageKind = shouldTrack ? damageKind : DotDamageKind.None;
        _activePlayer = shouldTrack ? player : null;
        return scope;
    }

    internal static void End(ref DotDamageScope scope)
    {
        if (!scope.Active)
        {
            return;
        }

        _activeDamageKind = scope.PreviousDamageKind;
        _activePlayer = scope.PreviousPlayer;
        scope = default;
    }

    internal static CrouchRestoreState Capture(Humanoid humanoid, HitData hit)
    {
        if (humanoid is not Player player ||
            !ReferenceEquals(_activePlayer, player) ||
            !player.m_crouchToggled ||
            !IsExpectedDamageOverTimeHit(hit, _activeDamageKind))
        {
            return default;
        }

        return new CrouchRestoreState(player);
    }

    internal static void Restore(ref CrouchRestoreState state)
    {
        Player? player = state.Player;
        state = default;
        if (player == null ||
            player.GetHealth() <= 0f ||
            player.IsDead() ||
            player.IsStaggering() ||
            player.IsKnockedBack())
        {
            return;
        }

        player.SetCrouch(crouch: true);
    }

    private static bool IsExpectedDamageOverTimeHit(HitData? hit, DotDamageKind damageKind)
    {
        if (hit == null || hit.HaveAttacker())
        {
            return false;
        }

        HitData.DamageTypes damage = hit.m_damage;
        return damageKind switch
        {
            DotDamageKind.Burning =>
                hit.m_hitType == HitData.HitType.Burning &&
                (damage.m_fire > 0f || damage.m_spirit > 0f) &&
                HasOnlyBurningDamage(damage),
            DotDamageKind.Poisoned =>
                hit.m_hitType == HitData.HitType.Poisoned &&
                damage.m_poison > 0f &&
                HasOnlyPoisonDamage(damage),
            _ => false
        };
    }

    private static bool HasOnlyBurningDamage(HitData.DamageTypes damage)
    {
        return damage.m_damage <= 0f &&
               damage.m_blunt <= 0f &&
               damage.m_slash <= 0f &&
               damage.m_pierce <= 0f &&
               damage.m_chop <= 0f &&
               damage.m_pickaxe <= 0f &&
               damage.m_frost <= 0f &&
               damage.m_lightning <= 0f &&
               damage.m_poison <= 0f;
    }

    private static bool HasOnlyPoisonDamage(HitData.DamageTypes damage)
    {
        return damage.m_damage <= 0f &&
               damage.m_blunt <= 0f &&
               damage.m_slash <= 0f &&
               damage.m_pierce <= 0f &&
               damage.m_chop <= 0f &&
               damage.m_pickaxe <= 0f &&
               damage.m_fire <= 0f &&
               damage.m_frost <= 0f &&
               damage.m_lightning <= 0f &&
               damage.m_spirit <= 0f;
    }

    internal enum DotDamageKind
    {
        None,
        Burning,
        Poisoned
    }

    internal struct DotDamageScope(DotDamageKind previousDamageKind, Player? previousPlayer, bool active)
    {
        internal readonly DotDamageKind PreviousDamageKind = previousDamageKind;
        internal readonly Player? PreviousPlayer = previousPlayer;
        internal bool Active = active;
    }

    internal struct CrouchRestoreState(Player? player)
    {
        internal readonly Player? Player = player;
    }
}

[HarmonyPatch(typeof(SE_Burning), nameof(SE_Burning.UpdateStatusEffect), typeof(float))]
internal static class SEBurningUpdateStatusEffectCrouchPreservationPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(SE_Burning __instance, out SneakCrouchPreservationSystem.DotDamageScope __state)
    {
        __state = SneakCrouchPreservationSystem.Begin(
            __instance,
            SneakCrouchPreservationSystem.DotDamageKind.Burning);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref SneakCrouchPreservationSystem.DotDamageScope __state)
    {
        SneakCrouchPreservationSystem.End(ref __state);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Finalizer(ref SneakCrouchPreservationSystem.DotDamageScope __state)
    {
        SneakCrouchPreservationSystem.End(ref __state);
    }
}

[HarmonyPatch(typeof(SE_Poison), nameof(SE_Poison.UpdateStatusEffect), typeof(float))]
internal static class SEPoisonUpdateStatusEffectCrouchPreservationPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(SE_Poison __instance, out SneakCrouchPreservationSystem.DotDamageScope __state)
    {
        __state = SneakCrouchPreservationSystem.Begin(
            __instance,
            SneakCrouchPreservationSystem.DotDamageKind.Poisoned);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref SneakCrouchPreservationSystem.DotDamageScope __state)
    {
        SneakCrouchPreservationSystem.End(ref __state);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Finalizer(ref SneakCrouchPreservationSystem.DotDamageScope __state)
    {
        SneakCrouchPreservationSystem.End(ref __state);
    }
}

[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnDamaged), typeof(HitData))]
internal static class HumanoidOnDamagedCrouchPreservationPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(
        Humanoid __instance,
        HitData hit,
        out SneakCrouchPreservationSystem.CrouchRestoreState __state)
    {
        __state = SneakCrouchPreservationSystem.Capture(__instance, hit);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref SneakCrouchPreservationSystem.CrouchRestoreState __state)
    {
        SneakCrouchPreservationSystem.Restore(ref __state);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Finalizer(ref SneakCrouchPreservationSystem.CrouchRestoreState __state)
    {
        SneakCrouchPreservationSystem.Restore(ref __state);
    }
}
