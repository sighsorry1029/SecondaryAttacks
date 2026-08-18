using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class SneakAmbushSystem
{
    internal const string RpcName = "SecondaryAttacks_SpawnSneakAmbushVfx";
    internal const string OwnerSmokeRpcName = "SecondaryAttacks_ApplySneakAmbushSmoke";
    private const string SmokeBombExplosionPrefabName = "smokebomb_explosion";
    private const string SmokeBombVfxChildName = "smoke particles";
    private const float DestroyDelay = 10f;
    private const float MinPreparedSecondsToTrigger = 0.25f;

    private static readonly Dictionary<Character, SmokeSenseBlockState> SenseBlocks = new();
    private static readonly List<PendingSmokeEffect> PendingSmokeEffects = new();
    private static readonly List<Character> SenseBlockRemoveBuffer = new();
    private static GameObject? _updaterObject;

    internal static void TryTriggerForSecondaryHit(
        Player player,
        Character target,
        bool secondaryAttack,
        SecondaryAttackDefinition? definition)
    {
        if (player == null ||
            target == null ||
            !secondaryAttack ||
            definition?.SneakAmbush == null ||
            !SecondaryAttackManager.HasCharacterAuthority(player) ||
            !IsValidSneakAmbushTarget(player, target))
        {
            return;
        }

        SneakAmbushDefinition sneakAmbush = definition.SneakAmbush;
        float preparedSeconds = SneakAmbushChargeSystem.GetPreparedSeconds(player, sneakAmbush);
        ResolveSmokeEffect(sneakAmbush, preparedSeconds, out float range, out float senseBlockDuration, out float backstabResetSeconds);
        if (!HasEffectiveSmokeEffect(range))
        {
            return;
        }

        if (!MeleePresetCooldownSystem.TryConsume(
                player,
                null,
                "sneakAmbush",
                sneakAmbush.PresetCooldown))
        {
            return;
        }

        SneakAmbushChargeSystem.Consume(player);

        QueueSmokeEffect(player, range, senseBlockDuration, backstabResetSeconds);
        Vector3 position = player.transform.position;
        float yaw = player.transform.eulerAngles.y;
        if (SecondaryAttackManager.TryGetCharacterZdo(player, out ZNetView? nview, out _) && ZRoutedRpc.instance != null)
        {
            nview!.InvokeRPC(ZNetView.Everybody, RpcName, position, yaw);
            return;
        }

        SpawnVisual(position, Quaternion.Euler(0f, yaw, 0f));
    }

    internal static void SpawnFromRpc(Character owner, Vector3 position, float yaw)
    {
        if (owner == null)
        {
            return;
        }

        SpawnVisual(position, Quaternion.Euler(0f, yaw, 0f));
    }

    internal static void ApplyOwnerSmokeFromRpc(
        Character monster,
        ZNetView? monsterView,
        ZDOID hiddenPlayerId,
        float range,
        float senseBlockDuration,
        float backstabResetSeconds)
    {
        if (monster == null ||
            monsterView == null ||
            !monsterView.IsValid() ||
            !monsterView.IsOwner() ||
            hiddenPlayerId.IsNone() ||
            monster.IsDead() ||
            monster.IsTamed() ||
            monster.GetBaseAI() is not MonsterAI monsterAI)
        {
            return;
        }

        Player? hiddenPlayer = ResolvePlayer(hiddenPlayerId);
        if (hiddenPlayer == null ||
            hiddenPlayer.IsDead() ||
            !IsValidSneakAmbushTarget(hiddenPlayer, monster))
        {
            return;
        }

        float allowedRange = Mathf.Max(0f, range) + 1f;
        if (allowedRange > 0f &&
            (monster.transform.position - hiddenPlayer.transform.position).sqrMagnitude > allowedRange * allowedRange)
        {
            return;
        }

        ApplyOwnedSmokeEffect(hiddenPlayer, monster, monsterAI, senseBlockDuration, backstabResetSeconds);
    }

    private static bool IsValidSneakAmbushTarget(Player player, Character target)
    {
        if (target == player || target.IsDead() || target.IsTamed())
        {
            return false;
        }

        if (BaseAI.IsEnemy(player, target))
        {
            return true;
        }

        BaseAI? baseAI = target.GetBaseAI();
        return baseAI != null && baseAI.IsAggravatable();
    }

    private static void SpawnVisual(Vector3 position, Quaternion rotation)
    {
        GameObject? prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(SmokeBombExplosionPrefabName) : null;
        Transform? source = prefab != null ? prefab.transform.Find(SmokeBombVfxChildName) : null;
        if (source == null)
        {
            if (SecondaryAttackWarningLog.TryMarkWarning("smoke_strike_vfx_missing"))
            {
                SecondaryAttacksPlugin.ModLogger.LogWarning($"Sneak Ambush VFX requires child '{SmokeBombVfxChildName}' on prefab '{SmokeBombExplosionPrefabName}', but it was not found.");
            }

            return;
        }

        Vector3 spawnPosition = position + rotation * source.localPosition;
        Quaternion spawnRotation = rotation * source.localRotation;
        GameObject instance = Object.Instantiate(source.gameObject, spawnPosition, spawnRotation);
        Object.Destroy(instance, DestroyDelay);
    }

    private static void ResolveSmokeEffect(
        SneakAmbushDefinition sneakAmbush,
        float preparedSeconds,
        out float range,
        out float senseBlockDuration,
        out float backstabResetSeconds)
    {
        float prepared = Mathf.Max(0f, preparedSeconds);
        if (prepared < MinPreparedSecondsToTrigger)
        {
            range = 0f;
            senseBlockDuration = 0f;
            backstabResetSeconds = 0f;
            return;
        }

        range = Mathf.Max(0f, prepared * sneakAmbush.AggroResetRangePerChargeSecond);
        senseBlockDuration = Mathf.Max(0f, prepared * sneakAmbush.SenseBlockDurationPerChargeSecond);
        backstabResetSeconds = Mathf.Max(0f, prepared * sneakAmbush.BackstabResetSecondsPerChargeSecond);
    }

    private static bool HasEffectiveSmokeEffect(float range)
    {
        return range > 0f;
    }

    private static void QueueSmokeEffect(Player player, float range, float senseBlockDuration, float backstabResetSeconds)
    {
        PendingSmokeEffects.Add(new PendingSmokeEffect(player, Time.frameCount, range, senseBlockDuration, backstabResetSeconds));
        EnsureUpdater();
    }

    private static void ApplySmokeEffect(Player player, float range, float senseBlockDuration, float backstabResetSeconds)
    {
        ZDOID hiddenPlayerId = player.GetZDOID();
        if (hiddenPlayerId.IsNone())
        {
            return;
        }

        Vector3 playerPosition = player.transform.position;
        float rangeSqr = Mathf.Max(0f, range);
        rangeSqr *= rangeSqr;
        foreach (Character character in Character.GetAllCharacters())
        {
            if (!TryGetAffectedMonster(player, character, playerPosition, rangeSqr, out MonsterAI? monsterAI))
            {
                continue;
            }

            if (!SecondaryAttackManager.TryGetCharacterZdo(character, out ZNetView? nview, out _))
            {
                continue;
            }

            if (nview!.IsOwner())
            {
                ApplyOwnedSmokeEffect(player, character, monsterAI!, senseBlockDuration, backstabResetSeconds);
            }
            else
            {
                nview.InvokeRPC(OwnerSmokeRpcName, hiddenPlayerId, range, senseBlockDuration, backstabResetSeconds);
            }
        }
    }

    private static void ApplyOwnedSmokeEffect(
        Player player,
        Character monster,
        MonsterAI monsterAI,
        float senseBlockDuration,
        float backstabResetSeconds)
    {
        ClearMonsterAwareness(monster, monsterAI, backstabResetSeconds);
        if (senseBlockDuration <= 0f)
        {
            return;
        }

        EnsureUpdater();
        float until = Time.time + senseBlockDuration;
        if (SenseBlocks.TryGetValue(monster, out SmokeSenseBlockState? previousBlock))
        {
            previousBlock.HiddenPlayer = player;
            previousBlock.Until = Mathf.Max(previousBlock.Until, until);
        }
        else
        {
            SenseBlocks[monster] = new SmokeSenseBlockState(player, until);
        }
    }

    internal static bool ShouldBlockSense(BaseAI baseAI, Character target)
    {
        if (baseAI == null || target == null)
        {
            return false;
        }

        Character monster = baseAI.m_character != null ? baseAI.m_character : baseAI.GetComponent<Character>();
        if (monster == null || !SenseBlocks.TryGetValue(monster, out SmokeSenseBlockState? block))
        {
            return false;
        }

        if (monster.IsDead() || Time.time >= block.Until || block.HiddenPlayer == null || block.HiddenPlayer.IsDead())
        {
            SenseBlocks.Remove(monster);
            return false;
        }

        return target == block.HiddenPlayer;
    }

    private static bool TryGetAffectedMonster(Player player, Character? character, Vector3 center, float rangeSqr, out MonsterAI? monsterAI)
    {
        monsterAI = null;
        if (character == null || character == player || character.IsDead())
        {
            return false;
        }

        if ((character.transform.position - center).sqrMagnitude > rangeSqr || !BaseAI.IsEnemy(player, character))
        {
            return false;
        }

        monsterAI = character.GetBaseAI() as MonsterAI;
        return monsterAI != null;
    }

    private static Player? ResolvePlayer(ZDOID playerId)
    {
        if (playerId.IsNone())
        {
            return null;
        }

        GameObject? playerObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(playerId) : null;
        if (playerObject != null && playerObject.TryGetComponent(out Player player))
        {
            return player;
        }

        foreach (Character character in Character.GetAllCharacters())
        {
            if (character is Player candidate && candidate.GetZDOID().Equals(playerId))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void ClearMonsterAwareness(Character character, MonsterAI monsterAI, float backstabResetSeconds)
    {
        monsterAI.SetTarget(null);
        monsterAI.SetTargetInfo(ZDOID.None);
        monsterAI.SetAlerted(false);
        monsterAI.m_targetCreature = null;
        monsterAI.m_targetStatic = null;
        monsterAI.m_lastKnownTargetPos = character.transform.position;
        monsterAI.m_beenAtLastPos = true;
        monsterAI.m_timeSinceSensedTargetCreature = 999f;
        monsterAI.m_timeSinceHurt = 999f;
        monsterAI.m_updateTargetTimer = 0f;
        monsterAI.m_unableToAttackTargetTimer = 0f;

        ZNetView? nview = character.m_nview;
        if (nview != null && nview.IsValid())
        {
            ZDO zdo = nview.GetZDO();
            zdo.Set("target", ZDOID.None);
            if (nview.IsOwner())
            {
                monsterAI.SetTargetInfo(ZDOID.None);
                monsterAI.SetAlerted(false);
            }
        }

        BackstabSkillGainSystem.TryReduceBackstabTime(character, backstabResetSeconds);
    }

    private static void EnsureUpdater()
    {
        if (_updaterObject != null)
        {
            return;
        }

        _updaterObject = new GameObject("SecondaryAttacks_SneakAmbushUpdater");
        _updaterObject.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(_updaterObject);
        _updaterObject.AddComponent<SneakAmbushUpdater>();
    }

    private sealed class SneakAmbushUpdater : MonoBehaviour
    {
        private void Update()
        {
            FlushPendingSmokeEffects();
            if (SenseBlocks.Count == 0)
            {
                return;
            }

            float now = Time.time;
            SenseBlockRemoveBuffer.Clear();
            foreach (KeyValuePair<Character, SmokeSenseBlockState> entry in SenseBlocks)
            {
                Character character = entry.Key;
                if (character == null)
                {
                    SenseBlockRemoveBuffer.Add(character!);
                    continue;
                }

                if (character.IsDead() || now >= entry.Value.Until || entry.Value.HiddenPlayer == null || entry.Value.HiddenPlayer.IsDead())
                {
                    SenseBlockRemoveBuffer.Add(character);
                    continue;
                }

                if (character.GetBaseAI() is MonsterAI monsterAI)
                {
                    ClearMonsterAwareness(character, monsterAI, 0f);
                }
            }

            foreach (Character character in SenseBlockRemoveBuffer)
            {
                SenseBlocks.Remove(character);
            }

            SenseBlockRemoveBuffer.Clear();
        }

        private static void FlushPendingSmokeEffects()
        {
            if (PendingSmokeEffects.Count == 0)
            {
                return;
            }

            int currentFrame = Time.frameCount;
            for (int i = PendingSmokeEffects.Count - 1; i >= 0; i--)
            {
                PendingSmokeEffect effect = PendingSmokeEffects[i];
                if (currentFrame <= effect.CreatedFrame)
                {
                    continue;
                }

                PendingSmokeEffects.RemoveAt(i);
                if (effect.Player == null || effect.Player.IsDead())
                {
                    continue;
                }

                ApplySmokeEffect(effect.Player, effect.Range, effect.SenseBlockDuration, effect.BackstabResetSeconds);
            }
        }
    }

    private sealed class PendingSmokeEffect
    {
        public PendingSmokeEffect(Player player, int createdFrame, float range, float senseBlockDuration, float backstabResetSeconds)
        {
            Player = player;
            CreatedFrame = createdFrame;
            Range = range;
            SenseBlockDuration = senseBlockDuration;
            BackstabResetSeconds = backstabResetSeconds;
        }

        public Player Player { get; }

        public int CreatedFrame { get; }

        public float Range { get; }

        public float SenseBlockDuration { get; }

        public float BackstabResetSeconds { get; }
    }

    private sealed class SmokeSenseBlockState
    {
        public SmokeSenseBlockState(Player hiddenPlayer, float until)
        {
            HiddenPlayer = hiddenPlayer;
            Until = until;
        }

        public Player HiddenPlayer { get; set; }

        public float Until { get; set; }
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
