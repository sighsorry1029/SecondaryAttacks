using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SweepHitStopSystem
{
    private static bool HasSuppression(Character? character)
    {
        if (character == null)
        {
            return false;
        }

        SpinningSweepController? spinningSweep = character.GetComponent<SpinningSweepController>();
        if (spinningSweep?.SuppressesHitStop == true)
        {
            return true;
        }

        HarvestSweepController? harvestSweep = character.GetComponent<HarvestSweepController>();
        if (harvestSweep?.SuppressesHitStop == true)
        {
            return true;
        }

        return false;
    }

    internal static bool ShouldSuppress(Character? character, float duration)
    {
        return duration > 0f && HasSuppression(character);
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
