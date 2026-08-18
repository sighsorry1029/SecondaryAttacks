using System.Collections.Generic;
using UnityEngine;

namespace SecondaryAttacks;

internal static class BurstObserverPresentationSystem
{
    internal const string RpcName = "SecondaryAttacks_BurstPresentationV1";
    internal const float MinPresentationInterval = 0.05f;

    private const float MaxSpawnDistanceFromCharacter = 32f;
    private const float MaxObserverDistance = 96f;
    private static readonly Dictionary<int, PresentationEffects> EffectsByWeaponHash = new();
    private static ObjectDB? _cachedObjectDb;
    private static int _cachedApplyRevision = -1;

    internal static int PlayLocalRepeatAnimation(Attack attack)
    {
        if (attack?.m_character == null || string.IsNullOrWhiteSpace(attack.m_attackAnimation))
        {
            return 0;
        }

        string trigger = ResolveRepeatAnimationTriggerWithoutChangingRandomState(attack);
        int triggerHash = ZSyncAnimation.GetHash(trigger);
        Animator? animator = attack.m_character.GetZAnim()?.m_animator ??
                             attack.m_character.GetComponentInChildren<Animator>();
        animator?.SetTrigger(triggerHash);
        return triggerHash;
    }

    internal static void Broadcast(
        Attack attack,
        SecondaryAttackDefinition definition,
        int animationTriggerHash,
        Vector3 spawnPoint,
        Vector3 aimDirection)
    {
        Character? character = attack?.m_character;
        GameObject? weaponPrefab = attack?.m_weapon?.m_dropPrefab;
        if (character == null ||
            weaponPrefab == null ||
            definition.Behavior is not ProjectileSecondaryBehavior { Preset: SecondaryAttackPreset.Burst } ||
            !IsFinite(spawnPoint) ||
            !TryNormalizeDirection(aimDirection, out Vector3 normalizedAim) ||
            ZRoutedRpc.instance == null)
        {
            return;
        }

        ZNetView? nview = character.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid() || !nview.IsOwner())
        {
            return;
        }

        int weaponPrefabHash = weaponPrefab.name.GetStableHashCode();
        nview.InvokeRPC(
            ZNetView.Everybody,
            RpcName,
            weaponPrefabHash,
            animationTriggerHash,
            spawnPoint,
            normalizedAim);
    }

    internal static void HandleRpc(
        Character character,
        ZNetView? nview,
        long sender,
        int weaponPrefabHash,
        int animationTriggerHash,
        Vector3 spawnPoint,
        Vector3 aimDirection,
        ref float nextAllowedPresentationAt)
    {
        if (ZNet.instance != null && ZNet.instance.IsDedicated())
        {
            return;
        }

        ZDO? zdo = nview != null && nview.IsValid() ? nview.GetZDO() : null;
        if (character == null ||
            nview == null ||
            !nview.IsValid() ||
            nview.IsOwner() ||
            zdo == null ||
            zdo.GetOwner() != sender ||
            !IsFinite(spawnPoint) ||
            !TryNormalizeDirection(aimDirection, out Vector3 normalizedAim) ||
            (spawnPoint - character.transform.position).sqrMagnitude >
            MaxSpawnDistanceFromCharacter * MaxSpawnDistanceFromCharacter)
        {
            return;
        }

        float now = Time.unscaledTime;
        if (now < nextAllowedPresentationAt)
        {
            return;
        }

        nextAllowedPresentationAt = now + MinPresentationInterval;
        if (animationTriggerHash != 0)
        {
            Animator? animator = character.GetZAnim()?.m_animator ?? character.GetComponentInChildren<Animator>();
            animator?.SetTrigger(animationTriggerHash);
        }

        GameCamera? gameCamera = GameCamera.instance;
        if (Player.m_localPlayer == null ||
            gameCamera == null ||
            (spawnPoint - gameCamera.transform.position).sqrMagnitude > MaxObserverDistance * MaxObserverDistance ||
            !TryResolvePresentationEffects(weaponPrefabHash, out PresentationEffects effects))
        {
            return;
        }

        Quaternion triggerEffectRotation = Quaternion.LookRotation(normalizedAim);
        Vector3 burstAimDirection = Mathf.Approximately(effects.Attack.m_launchAngle, 0f)
            ? normalizedAim
            : ProjectileRuntimeSystem.ApplyLaunchAngle(
                normalizedAim,
                effects.Attack.m_launchAngle,
                character.transform.right);
        Quaternion burstEffectRotation = Quaternion.LookRotation(burstAimDirection);
        effects.WeaponTriggerEffects.Create(spawnPoint, triggerEffectRotation);
        effects.AttackTriggerEffects.Create(spawnPoint, triggerEffectRotation);
        effects.BurstEffects.Create(spawnPoint, burstEffectRotation);
    }

    private static string ResolveRepeatAnimationTriggerWithoutChangingRandomState(Attack attack)
    {
        if (attack.m_attackChainLevels > 1)
        {
            int chainLevel = Mathf.Clamp(attack.m_currentAttackCainLevel, 0, attack.m_attackChainLevels - 1);
            return attack.m_attackAnimation + chainLevel;
        }

        if (attack.m_attackRandomAnimations >= 2)
        {
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            try
            {
                return attack.m_attackAnimation + UnityEngine.Random.Range(0, attack.m_attackRandomAnimations);
            }
            finally
            {
                UnityEngine.Random.state = randomState;
            }
        }

        return attack.m_attackAnimation;
    }

    private static bool TryResolvePresentationEffects(int weaponPrefabHash, out PresentationEffects effects)
    {
        ObjectDB? objectDb = ObjectDB.instance;
        int applyRevision = SecondaryAttackFacade.CurrentAppliedWorldSnapshot.ApplyRevision;
        if (!ReferenceEquals(_cachedObjectDb, objectDb) || _cachedApplyRevision != applyRevision)
        {
            EffectsByWeaponHash.Clear();
            _cachedObjectDb = objectDb;
            _cachedApplyRevision = applyRevision;
        }

        if (EffectsByWeaponHash.TryGetValue(weaponPrefabHash, out effects!) && effects.IsCurrent())
        {
            return true;
        }

        EffectsByWeaponHash.Remove(weaponPrefabHash);

        GameObject? weaponPrefab = objectDb != null ? objectDb.GetItemPrefab(weaponPrefabHash) : null;
        ItemDrop? itemDrop = weaponPrefab != null ? weaponPrefab.GetComponent<ItemDrop>() : null;
        if (weaponPrefab == null ||
            itemDrop?.m_itemData?.m_shared == null ||
            !SecondaryAttackRuntimeFacade.TryGetDefinition(weaponPrefab.name, out SecondaryAttackDefinition definition) ||
            definition.Behavior is not ProjectileSecondaryBehavior { Preset: SecondaryAttackPreset.Burst } ||
            definition.ConfiguredSecondaryAttack == null)
        {
            effects = null!;
            return false;
        }

        effects = new PresentationEffects(itemDrop.m_itemData.m_shared, definition.ConfiguredSecondaryAttack);
        EffectsByWeaponHash[weaponPrefabHash] = effects;
        return true;
    }

    private sealed class CachedEffectList
    {
        private readonly EffectList? _source;
        private readonly EffectList.EffectData[]? _sourceArray;
        private readonly EffectList.EffectData?[] _sourceEntries;
        private readonly GameObject?[] _sourcePrefabs;
        private readonly EffectList? _localEffects;

        internal CachedEffectList(EffectList? source)
        {
            _source = source;
            _sourceArray = source?.m_effectPrefabs;
            int length = _sourceArray?.Length ?? 0;
            _sourceEntries = new EffectList.EffectData?[length];
            _sourcePrefabs = new GameObject?[length];
            List<EffectList.EffectData> localEffects = new(length);
            for (int index = 0; index < length; index++)
            {
                EffectList.EffectData? effectData = _sourceArray![index];
                GameObject? effectPrefab = effectData?.m_prefab;
                _sourceEntries[index] = effectData;
                _sourcePrefabs[index] = effectPrefab;
                if (effectData != null &&
                    effectPrefab != null &&
                    effectPrefab.GetComponentInChildren<ZNetView>(true) == null)
                {
                    localEffects.Add(effectData);
                }
            }

            if (localEffects.Count > 0)
            {
                _localEffects = new EffectList { m_effectPrefabs = localEffects.ToArray() };
            }
        }

        internal bool Matches(EffectList? current)
        {
            if (!ReferenceEquals(_source, current) ||
                !ReferenceEquals(_sourceArray, current?.m_effectPrefabs) ||
                (_sourceArray?.Length ?? 0) != _sourceEntries.Length)
            {
                return false;
            }

            for (int index = 0; index < _sourceEntries.Length; index++)
            {
                EffectList.EffectData? currentEntry = _sourceArray![index];
                if (!ReferenceEquals(_sourceEntries[index], currentEntry) ||
                    !ReferenceEquals(_sourcePrefabs[index], currentEntry?.m_prefab))
                {
                    return false;
                }
            }

            return true;
        }

        internal void Create(Vector3 position, Quaternion rotation)
        {
            if (_localEffects != null && _localEffects.HasEffects())
            {
                _localEffects.Create(position, rotation);
            }
        }
    }

    private static bool TryNormalizeDirection(Vector3 direction, out Vector3 normalized)
    {
        normalized = Vector3.zero;
        if (!IsFinite(direction) || direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        normalized = direction.normalized;
        return IsFinite(normalized);
    }

    private static bool IsFinite(Vector3 value)
    {
        return !float.IsNaN(value.x) &&
               !float.IsInfinity(value.x) &&
               !float.IsNaN(value.y) &&
               !float.IsInfinity(value.y) &&
               !float.IsNaN(value.z) &&
               !float.IsInfinity(value.z);
    }

    private sealed class PresentationEffects
    {
        private readonly ItemDrop.ItemData.SharedData _sharedData;

        internal PresentationEffects(ItemDrop.ItemData.SharedData sharedData, Attack attack)
        {
            _sharedData = sharedData;
            Attack = attack;
            WeaponTriggerEffects = new CachedEffectList(sharedData.m_triggerEffect);
            AttackTriggerEffects = new CachedEffectList(attack.m_triggerEffect);
            BurstEffects = new CachedEffectList(attack.m_burstEffect);
        }

        internal Attack Attack { get; }

        internal CachedEffectList WeaponTriggerEffects { get; }

        internal CachedEffectList AttackTriggerEffects { get; }

        internal CachedEffectList BurstEffects { get; }

        internal bool IsCurrent()
        {
            return WeaponTriggerEffects.Matches(_sharedData.m_triggerEffect) &&
                   AttackTriggerEffects.Matches(Attack.m_triggerEffect) &&
                   BurstEffects.Matches(Attack.m_burstEffect);
        }
    }
}
