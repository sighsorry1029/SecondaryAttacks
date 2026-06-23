using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class SummonPrefabOverrideSystem
{
    private const string CloneContainerName = "SecondaryAttacks_SummonClonedPrefabs";
    private const string FriendlyTameablePrefabName = "Skeleton_Friendly";
    private const string DefaultSummonNameFormat = "{0} (Summon)";
    private const float DefaultSummonOwnerLogoutSeconds = 120f;
    private static readonly ConditionalWeakTable<ZNetScene, SceneState> SceneStates = new();
    private static GameObject? _cloneContainer;

    internal static void RestoreScene(ZNetScene scene)
    {
        if (scene == null)
        {
            return;
        }

        SceneState state = SceneStates.GetValue(scene, _ => new SceneState());
        RestoreOriginalSpawnPrefabs(state);
    }

    internal static void Apply(
        ZNetScene scene,
        IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> overrides,
        int snapshotId,
        bool emitMissingWarnings)
    {
        if (scene == null)
        {
            return;
        }

        SceneState state = SceneStates.GetValue(scene, _ => new SceneState());
        RestoreOriginalSpawnPrefabs(state);
        PruneUnusedClonePrefabs(scene, state, BuildDesiredClonePrefabNames(overrides));

        if (overrides.Count == 0)
        {
            state.LastAppliedSnapshotId = snapshotId;
            return;
        }

        int appliedCount = 0;
        foreach (NormalizedMagicSummonOverrideConfig summonOverride in overrides.Values)
        {
            if (summonOverride.Summons.Count == 0)
            {
                continue;
            }

            if (!TryResolveTargetSpawnAbility(scene, summonOverride, emitMissingWarnings, out SpawnAbility? spawnAbility, out string targetName) ||
                spawnAbility == null)
            {
                continue;
            }

            CaptureOriginalSpawnPrefabs(state, spawnAbility);
            List<GameObject> spawnPrefabs = new();
            foreach (NormalizedMagicSummonCloneConfig summon in summonOverride.Summons)
            {
                GameObject? clonePrefab = CreateOrUpdateClonePrefab(scene, state, summonOverride.EntryId, summon, emitMissingWarnings);
                if (clonePrefab != null)
                {
                    for (int weightIndex = 0; weightIndex < summon.Weight; weightIndex++)
                    {
                        spawnPrefabs.Add(clonePrefab);
                    }
                }
            }

            if (spawnPrefabs.Count == 0)
            {
                continue;
            }

            spawnAbility.m_spawnPrefab = spawnPrefabs.ToArray();
            appliedCount++;
            SecondaryAttackManager.LogStaffDebug(
                $"Applied summon override '{summonOverride.EntryId}' to '{targetName}' with spawn prefabs: {string.Join(", ", spawnPrefabs.ConvertAll(prefab => prefab.name))}.");
        }

        state.LastAppliedSnapshotId = snapshotId;
        if (appliedCount > 0)
        {
            SecondaryAttacksPlugin.ModLogger.LogInfo($"Applied {appliedCount} magic summon prefab override(s).");
        }
    }

    private static HashSet<string> BuildDesiredClonePrefabNames(
        IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> overrides)
    {
        HashSet<string> desiredClonePrefabNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (NormalizedMagicSummonOverrideConfig summonOverride in overrides.Values)
        {
            foreach (NormalizedMagicSummonCloneConfig summon in summonOverride.Summons)
            {
                if (!string.IsNullOrWhiteSpace(summon.ClonePrefab))
                {
                    desiredClonePrefabNames.Add(summon.ClonePrefab);
                }
            }
        }

        return desiredClonePrefabNames;
    }

    private static void PruneUnusedClonePrefabs(
        ZNetScene scene,
        SceneState state,
        HashSet<string> desiredClonePrefabNames)
    {
        List<string> removedClonePrefabNames = new();
        foreach ((string clonePrefabName, GameObject clonePrefab) in state.CreatedClonePrefabs)
        {
            if (desiredClonePrefabNames.Contains(clonePrefabName))
            {
                continue;
            }

            if (clonePrefab != null)
            {
                UnregisterClonePrefab(scene, clonePrefab);
                Object.Destroy(clonePrefab);
            }

            removedClonePrefabNames.Add(clonePrefabName);
        }

        foreach (string clonePrefabName in removedClonePrefabNames)
        {
            state.CreatedClonePrefabs.Remove(clonePrefabName);
            state.CloneSourcePrefabs.Remove(clonePrefabName);
        }
    }

    private static void RestoreOriginalSpawnPrefabs(SceneState state)
    {
        foreach ((SpawnAbility spawnAbility, GameObject[] originalSpawnPrefabs) in state.OriginalSpawnPrefabs)
        {
            if (spawnAbility == null)
            {
                continue;
            }

            spawnAbility.m_spawnPrefab = (GameObject[])originalSpawnPrefabs.Clone();
        }
    }

    private static void CaptureOriginalSpawnPrefabs(SceneState state, SpawnAbility spawnAbility)
    {
        if (state.OriginalSpawnPrefabs.ContainsKey(spawnAbility))
        {
            return;
        }

        GameObject[] originalSpawnPrefabs = spawnAbility.m_spawnPrefab ?? Array.Empty<GameObject>();
        state.OriginalSpawnPrefabs[spawnAbility] = (GameObject[])originalSpawnPrefabs.Clone();
    }

    private static bool TryResolveTargetSpawnAbility(
        ZNetScene scene,
        NormalizedMagicSummonOverrideConfig summonOverride,
        bool emitMissingWarnings,
        out SpawnAbility? spawnAbility,
        out string targetName)
    {
        spawnAbility = null;
        targetName = "";

        string itemPrefabName = summonOverride.EntryId.Trim();
        GameObject? itemPrefab = ResolveItemPrefab(scene, itemPrefabName);
        ItemDrop? itemDrop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
        if (itemDrop?.m_itemData?.m_shared == null)
        {
            WarnOnce(emitMissingWarnings, $"missing_item:{summonOverride.EntryId}:{itemPrefabName}",
                $"Summon override '{summonOverride.EntryId}' skipped: item prefab '{itemPrefabName}' was not found.");
            return false;
        }

        if (TryResolveSpawnAbilityFromAttack(itemDrop.m_itemData.m_shared.m_attack, out spawnAbility, out targetName) ||
            TryResolveSpawnAbilityFromAttack(itemDrop.m_itemData.m_shared.m_secondaryAttack, out spawnAbility, out targetName))
        {
            return true;
        }

        WarnOnce(emitMissingWarnings, $"missing_item_spawnability:{summonOverride.EntryId}:{itemPrefabName}",
            $"Summon override '{summonOverride.EntryId}' skipped: item prefab '{itemPrefabName}' has no attack projectile with a SpawnAbility payload.");
        return false;
    }

    private static GameObject? ResolveItemPrefab(ZNetScene scene, string itemPrefabName)
    {
        if (string.IsNullOrWhiteSpace(itemPrefabName))
        {
            return null;
        }

        GameObject? itemPrefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemPrefabName) : null;
        return itemPrefab != null ? itemPrefab : scene.GetPrefab(itemPrefabName);
    }

    private static bool TryResolveSpawnAbilityFromAttack(Attack? attack, out SpawnAbility? spawnAbility, out string targetName)
    {
        spawnAbility = null;
        targetName = "";
        return attack?.m_attackProjectile != null &&
               TryResolveSpawnAbilityFromPrefab(attack.m_attackProjectile, out spawnAbility, out targetName);
    }

    private static bool TryResolveSpawnAbilityFromPrefab(GameObject prefab, out SpawnAbility? spawnAbility, out string targetName)
    {
        spawnAbility = prefab.GetComponent<SpawnAbility>();
        if (spawnAbility != null)
        {
            targetName = prefab.name;
            return true;
        }

        Projectile? projectile = prefab.GetComponent<Projectile>();
        GameObject? spawnOnHit = projectile?.m_spawnOnHit;
        spawnAbility = spawnOnHit != null ? spawnOnHit.GetComponent<SpawnAbility>() : null;
        if (spawnAbility != null)
        {
            targetName = spawnOnHit!.name;
            return true;
        }

        targetName = prefab.name;
        return false;
    }

    private static GameObject? CreateOrUpdateClonePrefab(
        ZNetScene scene,
        SceneState state,
        string entryId,
        NormalizedMagicSummonCloneConfig summon,
        bool emitMissingWarnings)
    {
        GameObject? sourcePrefab = scene.GetPrefab(summon.SourcePrefab);
        if (sourcePrefab == null)
        {
            WarnOnce(emitMissingWarnings, $"missing_source:{entryId}:{summon.SourcePrefab}",
                $"Summon override '{entryId}' skipped clone '{summon.ClonePrefab}': sourcePrefab '{summon.SourcePrefab}' was not found in ZNetScene.");
            return null;
        }

        if (state.CreatedClonePrefabs.TryGetValue(summon.ClonePrefab, out GameObject? existingClone) &&
            existingClone != null)
        {
            if (!state.CloneSourcePrefabs.TryGetValue(summon.ClonePrefab, out string existingSource) ||
                string.Equals(existingSource, summon.SourcePrefab, StringComparison.OrdinalIgnoreCase))
            {
                ConfigureFriendlySummonClone(scene, existingClone, sourcePrefab, summon);
                return existingClone;
            }

            UnregisterClonePrefab(scene, existingClone);
            Object.Destroy(existingClone);
            state.CreatedClonePrefabs.Remove(summon.ClonePrefab);
            state.CloneSourcePrefabs.Remove(summon.ClonePrefab);
        }

        GameObject? scenePrefabWithCloneName = scene.GetPrefab(summon.ClonePrefab);
        if (scenePrefabWithCloneName != null)
        {
            WarnOnce(emitMissingWarnings, $"clone_name_exists:{entryId}:{summon.ClonePrefab}",
                $"Summon override '{entryId}' skipped clone '{summon.ClonePrefab}': that prefab name already exists in ZNetScene.");
            return null;
        }

        GameObject clone = Object.Instantiate(sourcePrefab, GetCloneContainerTransform());
        clone.name = summon.ClonePrefab;
        ConfigureFriendlySummonClone(scene, clone, sourcePrefab, summon);
        if (clone.GetComponent<Character>() == null)
        {
            WarnOnce(emitMissingWarnings, $"clone_not_character:{entryId}:{summon.ClonePrefab}",
                $"Summon override '{entryId}' skipped clone '{summon.ClonePrefab}': sourcePrefab '{summon.SourcePrefab}' has no Character component.");
            Object.Destroy(clone);
            return null;
        }

        RegisterClonePrefab(scene, clone);
        state.CreatedClonePrefabs[summon.ClonePrefab] = clone;
        state.CloneSourcePrefabs[summon.ClonePrefab] = summon.SourcePrefab;
        return clone;
    }

    private static void ConfigureFriendlySummonClone(
        ZNetScene scene,
        GameObject clone,
        GameObject sourcePrefab,
        NormalizedMagicSummonCloneConfig summon)
    {
        clone.SetActive(sourcePrefab.activeSelf);

        Character? character = clone.GetComponent<Character>();
        if (character == null)
        {
            return;
        }

        string displayName = ResolveSummonDisplayName(sourcePrefab, summon);
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            character.m_name = displayName;
        }

        if (summon.Health.HasValue && summon.Health.Value > 0f)
        {
            character.m_health = summon.Health.Value;
        }

        character.m_group = "";
        character.m_defeatSetGlobalKey = "";
        character.m_faction = Character.Faction.Players;
        TryCopyFriendlyDeathEffects(scene, character);
        ConfigureFriendlyMonsterAi(clone);
        RemoveIfPresent<CharacterDrop>(clone);
        RemoveIfPresent<Procreation>(clone);
        ConfigureTameable(scene, clone, character);
    }

    private static string ResolveSummonDisplayName(GameObject sourcePrefab, NormalizedMagicSummonCloneConfig summon)
    {
        if (!string.IsNullOrWhiteSpace(summon.DisplayName))
        {
            return summon.DisplayName.Trim();
        }

        string sourceName = sourcePrefab.GetComponent<Character>()?.m_name ?? "";
        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = Utils.GetPrefabName(sourcePrefab);
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            sourceName = sourcePrefab.name;
        }

        string localizedName = Localization.instance != null
            ? Localization.instance.Localize(sourceName.Trim()).Trim()
            : sourceName.Trim();
        if (string.IsNullOrWhiteSpace(localizedName))
        {
            localizedName = sourceName.Trim();
        }

        return HasSummonNameSuffix(localizedName)
            ? localizedName
            : SecondaryAttackLocalization.Format(
                SecondaryAttackLocalization.SummonNameFormat,
                DefaultSummonNameFormat,
                localizedName);
    }

    private static bool HasSummonNameSuffix(string localizedName)
    {
        string localizedSuffix = SecondaryAttackLocalization.Format(SecondaryAttackLocalization.SummonNameFormat, DefaultSummonNameFormat, "");
        if (!string.IsNullOrEmpty(localizedSuffix) && localizedName.EndsWith(localizedSuffix, StringComparison.Ordinal))
        {
            return true;
        }

        string englishSuffix = string.Format(DefaultSummonNameFormat, "");
        return !string.IsNullOrEmpty(englishSuffix) && localizedName.EndsWith(englishSuffix, StringComparison.Ordinal);
    }

    private static void TryCopyFriendlyDeathEffects(ZNetScene scene, Character character)
    {
        Character? friendlyCharacter = scene.GetPrefab(FriendlyTameablePrefabName)?.GetComponent<Character>();
        if (friendlyCharacter != null && friendlyCharacter.m_deathEffects != null)
        {
            character.m_deathEffects = friendlyCharacter.m_deathEffects;
        }
    }

    private static void ConfigureFriendlyMonsterAi(GameObject clone)
    {
        MonsterAI? monsterAi = clone.GetComponent<MonsterAI>();
        if (monsterAi == null)
        {
            return;
        }

        ((BaseAI)monsterAi).m_hearRange = Mathf.Max(((BaseAI)monsterAi).m_hearRange, 9999f);
        monsterAi.m_alertRange = Mathf.Max(monsterAi.m_alertRange, 20f);
    }

    private static void ConfigureTameable(
        ZNetScene scene,
        GameObject clone,
        Character character)
    {
        Tameable tameable = clone.GetComponent<Tameable>();
        if (tameable == null)
        {
            tameable = clone.AddComponent<Tameable>();
        }

        Tameable? friendlyTameable = scene.GetPrefab(FriendlyTameablePrefabName)?.GetComponent<Tameable>();
        if (friendlyTameable != null)
        {
            tameable.m_tamedEffect = friendlyTameable.m_tamedEffect;
            tameable.m_sootheEffect = friendlyTameable.m_sootheEffect;
            tameable.m_petEffect = friendlyTameable.m_petEffect;
        }

        tameable.m_startsTamed = true;
        tameable.m_commandable = false;
        tameable.m_unsummonDistance = 0f;
        tameable.m_unsummonOnOwnerLogoutSeconds = DefaultSummonOwnerLogoutSeconds;
        tameable.m_levelUpOwnerSkill = Skills.SkillType.BloodMagic;
        tameable.m_dropSaddleOnDeath = false;
        tameable.m_unSummonEffect = character.m_deathEffects;
    }

    private static void RemoveIfPresent<T>(GameObject clone) where T : Component
    {
        T component = clone.GetComponent<T>();
        if (component != null)
        {
            Object.Destroy(component);
        }
    }

    private static Transform GetCloneContainerTransform()
    {
        if (_cloneContainer != null)
        {
            return _cloneContainer.transform;
        }

        _cloneContainer = new GameObject(CloneContainerName);
        _cloneContainer.SetActive(false);
        Object.DontDestroyOnLoad(_cloneContainer);
        return _cloneContainer.transform;
    }

    private static void RegisterClonePrefab(ZNetScene scene, GameObject clone)
    {
        if (!scene.m_prefabs.Contains(clone))
        {
            scene.m_prefabs.Add(clone);
        }

        scene.m_namedPrefabs[clone.name.GetStableHashCode()] = clone;
    }

    private static void UnregisterClonePrefab(ZNetScene scene, GameObject clone)
    {
        scene.m_prefabs.Remove(clone);
        int hash = clone.name.GetStableHashCode();
        if (scene.m_namedPrefabs.TryGetValue(hash, out GameObject? existing) && existing == clone)
        {
            scene.m_namedPrefabs.Remove(hash);
        }
    }

    private static void WarnOnce(bool emitMissingWarnings, string warningKey, string message)
    {
        SecondaryAttackWarningLog.WarnOnce($"summon_override:{warningKey}", message, emitMissingWarnings);
    }

    private sealed class SceneState
    {
        internal int LastAppliedSnapshotId { get; set; }

        internal Dictionary<SpawnAbility, GameObject[]> OriginalSpawnPrefabs { get; } = new();

        internal Dictionary<string, GameObject> CreatedClonePrefabs { get; } = new(StringComparer.OrdinalIgnoreCase);

        internal Dictionary<string, string> CloneSourcePrefabs { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
