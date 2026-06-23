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
        if (player == null || !player.IsCrouching())
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

    private static void Postfix(SneakVisibilitySystem.CrouchSpeedState __state)
    {
        SneakVisibilitySystem.RestoreCrouchSpeed(__state);
    }
}
