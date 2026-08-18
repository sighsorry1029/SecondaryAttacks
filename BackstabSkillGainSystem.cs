using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class BackstabSkillGainSystem
{
    internal const string GrantSneakSkillRpcName = "SecondaryAttacks_GrantBackstabSneakSkill";
    private const float BackstabTimeDetectionWindow = 1f;
    private static readonly AccessTools.FieldRef<Character, float>? BackstabTimeField = TryCreateBackstabTimeFieldRef();

    internal static BackstabDamageState CaptureBackstabState(Character target, HitData hit)
    {
        if (target == null || hit == null || hit.m_backstabBonus <= 1f)
        {
            return BackstabDamageState.Inactive;
        }

        return new BackstabDamageState(true, GetBackstabTime(target));
    }

    internal static void TryGrantForBackstab(Character target, HitData hit, BackstabDamageState state)
    {
        if (!state.Active || target == null || hit == null)
        {
            return;
        }

        float amount = GetRaiseAmount();
        if (amount <= 0f || !DidBackstabSucceed(target, state.PreviousBackstabTime))
        {
            return;
        }

        Character attacker = hit.GetAttacker();
        if (attacker is not Player player)
        {
            return;
        }

        GrantToPlayer(player, amount);
    }

    internal static void GrantLocal(Player player, float amount)
    {
        if (player == null || amount <= 0f)
        {
            return;
        }

        float raiseAmount = Mathf.Min(Mathf.Max(0f, amount), GetRaiseAmount());
        if (raiseAmount <= 0f)
        {
            return;
        }

        player.RaiseSkill(Skills.SkillType.Sneak, raiseAmount);
    }

    private static float GetRaiseAmount()
    {
        return Mathf.Max(0f, SecondaryAttacksPlugin.BackstabSneakSkillRaiseAmount?.Value ?? 0f);
    }

    private static bool DidBackstabSucceed(Character target, float previousBackstabTime)
    {
        float currentBackstabTime = GetBackstabTime(target);
        return currentBackstabTime > previousBackstabTime + 0.001f &&
               currentBackstabTime >= Time.time - BackstabTimeDetectionWindow &&
               currentBackstabTime <= Time.time + 0.001f;
    }

    private static void GrantToPlayer(Player player, float amount)
    {
        if ((Object)(object)player == (Object)(object)Player.m_localPlayer)
        {
            GrantLocal(player, amount);
            return;
        }

        if (SecondaryAttackManager.TryGetCharacterZdo(player, out ZNetView? nview, out _))
        {
            nview!.InvokeRPC(GrantSneakSkillRpcName, amount);
        }
    }

    internal static bool TryReduceBackstabTime(Character target, float seconds)
    {
        if (target == null || seconds <= 0f || !TryGetBackstabTime(target, out float currentBackstabTime))
        {
            return false;
        }

        try
        {
            BackstabTimeField!(target) = currentBackstabTime - seconds;
            return true;
        }
        catch (System.FieldAccessException)
        {
            return false;
        }
    }

    private static float GetBackstabTime(Character target)
    {
        return TryGetBackstabTime(target, out float backstabTime) ? backstabTime : 0f;
    }

    private static bool TryGetBackstabTime(Character target, out float backstabTime)
    {
        if (target == null || BackstabTimeField == null)
        {
            backstabTime = 0f;
            return false;
        }

        try
        {
            backstabTime = BackstabTimeField(target);
            return true;
        }
        catch (System.FieldAccessException)
        {
            backstabTime = 0f;
            return false;
        }
    }

    private static AccessTools.FieldRef<Character, float>? TryCreateBackstabTimeFieldRef()
    {
        try
        {
            return AccessTools.FieldRefAccess<Character, float>("m_backstabTime");
        }
        catch (System.Exception)
        {
            return null;
        }
    }

    internal readonly struct BackstabDamageState
    {
        internal static BackstabDamageState Inactive => new(false, 0f);

        internal BackstabDamageState(bool active, float previousBackstabTime)
        {
            Active = active;
            PreviousBackstabTime = previousBackstabTime;
        }

        internal bool Active { get; }

        internal float PreviousBackstabTime { get; }
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
