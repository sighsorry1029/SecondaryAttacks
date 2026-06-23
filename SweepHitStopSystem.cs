using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SweepHitStopSystem
{
    internal static bool TryGetSuppression(Character? character, out string presetName)
    {
        presetName = "";
        if (character == null)
        {
            return false;
        }

        SpinningSweepController? spinningSweep = character.GetComponent<SpinningSweepController>();
        if (spinningSweep?.SuppressesHitStop == true)
        {
            presetName = "spinningSweep";
            return true;
        }

        HarvestSweepController? harvestSweep = character.GetComponent<HarvestSweepController>();
        if (harvestSweep?.SuppressesHitStop == true)
        {
            presetName = "harvestSweep";
            return true;
        }

        return false;
    }

    internal static bool IsDebugLoggingEnabled(string presetName)
    {
        return presetName == "spinningSweep"
            ? SpinningSweepSystem.IsDebugLoggingEnabled()
            : presetName == "harvestSweep" && HarvestSweepSystem.IsDebugLoggingEnabled();
    }

    internal static bool ShouldSuppress(Character? character, float duration)
    {
        if (duration <= 0f || !TryGetSuppression(character, out string presetName))
        {
            return false;
        }

        if (IsDebugLoggingEnabled(presetName))
        {
            string characterName = character != null ? character.name : "<null>";
            SecondaryAttacksPlugin.ModLogger.LogInfo($"[SweepHitStop] suppressed source=Character.FreezeFrame preset={presetName} character={characterName} duration={duration:0.###} frame={Time.frameCount}.");
        }

        return true;
    }

    internal static bool TryGetAnimationSpeed(Character? character, out float speed)
    {
        speed = 1f;
        if (character == null)
        {
            return false;
        }

        SpinningSweepController? spinningSweep = character.GetComponent<SpinningSweepController>();
        if (spinningSweep?.TryGetAnimationSpeed(out speed) == true)
        {
            return true;
        }

        HarvestSweepController? harvestSweep = character.GetComponent<HarvestSweepController>();
        return harvestSweep?.TryGetAnimationSpeed(out speed) == true;
    }

    internal static void ApplyAnimationSpeed(CharacterAnimEvent? animEvent)
    {
        if (animEvent?.m_animator == null || !TryGetAnimationSpeed(animEvent.m_character, out float speed))
        {
            return;
        }

        animEvent.m_animator.speed = speed;
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.FreezeFrame))]
internal static class CharacterFreezeFrameSweepHitStopPatch
{
    private static bool Prefix(Character __instance, float duration)
    {
        return !SweepHitStopSystem.ShouldSuppress(__instance, duration);
    }
}

[HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
internal static class CharacterAnimEventCustomFixedUpdateSweepAnimationSpeedPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Prefix(CharacterAnimEvent __instance)
    {
        SweepHitStopSystem.ApplyAnimationSpeed(__instance);
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CharacterAnimEvent __instance)
    {
        SweepHitStopSystem.ApplyAnimationSpeed(__instance);
    }
}
