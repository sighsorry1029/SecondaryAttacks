using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class MagicSummonQualityPresetSystem
{
    private const int GlobalMaxQuality = 4;
    private const int GlobalFixedSummonLevel = 1;
    private const string SummonOwnerZdoKey = "SecondaryAttacks_SummonOwner";
    private const string SummonLifetimeExpiryZdoKey = "SecondaryAttacks_SummonExpiresAt";
    private static readonly ConditionalWeakTable<ObjectDB, ObjectDbState> ObjectDbStates = new();
    private static readonly ConditionalWeakTable<SpawnAbility, SpawnAbilityRuntimeState> PendingSpawnAbilityStates = new();
    private static readonly ConditionalWeakTable<SpawnAbility, SpawnAbilityRuntimeState> ActiveSpawnAbilityStates = new();
    private static readonly MethodInfo? TameableUnSummonMethod = AccessTools.DeclaredMethod(typeof(Tameable), "UnSummon");
    private static readonly Dictionary<string, QualityRule> RulesByItemPrefab = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, QualityRule> RulesBySharedName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, QualityRule> RulesBySpawnAbilityPrefab = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> LifetimeOverridesByItemPrefab = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> LifetimeOverridesBySharedName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> ActiveGroupIds = new(StringComparer.OrdinalIgnoreCase);
    private static List<QualityRule> _activeRules = new();

    internal static void RestoreObjectDb(ObjectDB objectDb)
    {
        if (objectDb == null)
        {
            return;
        }

        ObjectDbState state = ObjectDbStates.GetValue(objectDb, _ => new ObjectDbState());
        RestoreOriginalMaxQualities(objectDb, state);
        RulesBySharedName.Clear();
        LifetimeOverridesByItemPrefab.Clear();
        LifetimeOverridesBySharedName.Clear();
    }

    internal static void ApplyToObjectDb(
        ObjectDB objectDb,
        IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> summonConfigs)
    {
        if (objectDb == null)
        {
            return;
        }

        ObjectDbState state = ObjectDbStates.GetValue(objectDb, _ => new ObjectDbState());
        RebuildCoreRules(summonConfigs, objectDb);
        RebuildLifetimeOverrides(summonConfigs, objectDb);
        RulesBySharedName.Clear();

        if (_activeRules.Count == 0)
        {
            return;
        }

        foreach (QualityRule rule in _activeRules)
        {
            GameObject? itemPrefab = objectDb.GetItemPrefab(rule.ItemPrefabName);
            ItemDrop? itemDrop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
            ItemDrop.ItemData.SharedData? shared = itemDrop?.m_itemData?.m_shared;
            if (itemPrefab == null || shared == null)
            {
                continue;
            }

            string restoreKey = itemPrefab.name;
            if (!state.OriginalMaxQualities.ContainsKey(restoreKey))
            {
                state.OriginalMaxQualities[restoreKey] = shared.m_maxQuality;
            }

            shared.m_maxQuality = rule.MaxQuality;
            RulesBySharedName[shared.m_name] = rule;
        }
    }

    internal static bool TryGetItemQualityPreset(
        ItemDrop.ItemData? item,
        out MagicSummonQualityPreset preset)
    {
        preset = MagicSummonQualityPreset.None;
        if (!TryResolveItemRule(item, out QualityRule rule))
        {
            return false;
        }

        preset = rule.Preset;
        return preset != MagicSummonQualityPreset.None;
    }

    internal static void ApplyToZNetScene(
        ZNetScene scene,
        IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> summonConfigs)
    {
        if (scene == null)
        {
            return;
        }

        RebuildCoreRules(summonConfigs, ObjectDB.instance);
        RebuildLifetimeOverrides(summonConfigs, ObjectDB.instance);
        RulesBySpawnAbilityPrefab.Clear();

        if (_activeRules.Count == 0)
        {
            return;
        }

        foreach (QualityRule rule in _activeRules)
        {
            if (!TryResolveTargetSpawnAbility(scene, rule, out SpawnAbility? spawnAbility, out string targetName) ||
                spawnAbility == null)
            {
                continue;
            }

            RulesBySpawnAbilityPrefab[targetName] = rule;
            string prefabName = Utils.GetPrefabName(spawnAbility.gameObject);
            if (!string.IsNullOrWhiteSpace(prefabName))
            {
                RulesBySpawnAbilityPrefab[prefabName] = rule;
            }
        }
    }

    internal static SpawnAbilityRuntimeState? PrepareSpawnAbilityForRule(
        SpawnAbility spawnAbility,
        Character? owner,
        ItemDrop.ItemData item)
    {
        if (spawnAbility == null || item == null)
        {
            return null;
        }

        bool hasQualityRule = TryResolveRule(spawnAbility, item, out QualityRule rule);
        float lifetimeSeconds = GetBloodMagicSummonLifetimeSeconds(spawnAbility, owner, item);
        if (!hasQualityRule && lifetimeSeconds <= 0f)
        {
            if (PendingSpawnAbilityStates.TryGetValue(spawnAbility, out SpawnAbilityRuntimeState? pendingState))
            {
                pendingState.Restore(spawnAbility);
            }

            PendingSpawnAbilityStates.Remove(spawnAbility);
            ResetSpawnPrefabTags(spawnAbility);
            return null;
        }

        ZDOID ownerId = owner != null ? owner.GetZDOID() : ZDOID.None;
        int maxInstances = 0;
        int summonLevel = 1;
        string groupId = "";
        if (hasQualityRule)
        {
            int quality = Mathf.Clamp(item.m_quality, 1, rule.MaxQuality);
            summonLevel = rule.GetSummonLevel(quality);
            maxInstances = rule.GetMaxInstances(quality);
            groupId = rule.GroupId;
            TagSpawnPrefabs(spawnAbility, rule, maxInstances, summonLevel);
        }
        else
        {
            // Lifetime tracking is independent from quality scaling. Clear stale quality
            // metadata when the current item has no quality rule.
            ResetSpawnPrefabTags(spawnAbility);
        }

        SpawnAbilityRuntimeState state = SpawnAbilityRuntimeState.Capture(
            spawnAbility,
            groupId,
            maxInstances,
            ownerId,
            lifetimeSeconds);

        if (hasQualityRule)
        {
            spawnAbility.m_maxSpawned = 0;
            spawnAbility.m_setMaxInstancesFromWeaponLevel = false;
            spawnAbility.m_levelUpSettings = new List<SpawnAbility.LevelUpSettings>
            {
                new()
                {
                    m_skill = Skills.SkillType.BloodMagic,
                    m_skillLevel = 0,
                    m_setLevel = summonLevel,
                    m_maxSpawns = maxInstances
                }
            };
        }

        PendingSpawnAbilityStates.Remove(spawnAbility);
        PendingSpawnAbilityStates.Add(spawnAbility, state);
        return state;
    }

    internal static bool TryConsumePendingSpawnAbilityState(
        SpawnAbility spawnAbility,
        out SpawnAbilityRuntimeState? state)
    {
        state = null;
        if (spawnAbility == null || !PendingSpawnAbilityStates.TryGetValue(spawnAbility, out SpawnAbilityRuntimeState pendingState))
        {
            return false;
        }

        PendingSpawnAbilityStates.Remove(spawnAbility);
        state = pendingState;
        return true;
    }

    internal static IEnumerator WrapSpawnWithRestore(
        SpawnAbility spawnAbility,
        IEnumerator? spawnEnumerator,
        SpawnAbilityRuntimeState state)
    {
        ActiveSpawnAbilityStates.Remove(spawnAbility);
        ActiveSpawnAbilityStates.Add(spawnAbility, state);
        try
        {
            if (spawnEnumerator == null)
            {
                yield break;
            }

            while (spawnEnumerator.MoveNext())
            {
                yield return spawnEnumerator.Current;
            }
        }
        finally
        {
            ActiveSpawnAbilityStates.Remove(spawnAbility);
            RegisterNewSummonsAndEnforceLimit(state);
            RestoreSpawnAbilityAfterSpawn(spawnAbility, state);
        }
    }

    internal static void RegisterSpawnedCharacterLifetime(
        SpawnAbility spawnAbility,
        Character? character)
    {
        if (spawnAbility != null &&
            character != null &&
            ActiveSpawnAbilityStates.TryGetValue(spawnAbility, out SpawnAbilityRuntimeState state))
        {
            TryRegisterSummonLifetime(character, state);
        }
    }

    internal static bool TryGetSummonLifetime(
        Character? character,
        out float remainingSeconds)
    {
        remainingSeconds = 0f;
        ZNetView? view = character != null ? character.GetComponent<ZNetView>() : null;
        ZDO? zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        long expiryTicks = zdo?.GetLong(SummonLifetimeExpiryZdoKey, 0L) ?? 0L;
        if (expiryTicks <= 0L || ZNet.instance == null)
        {
            return false;
        }

        double seconds = (expiryTicks - (double)ZNet.instance.GetTime().Ticks) / TimeSpan.TicksPerSecond;
        remainingSeconds = Mathf.Max(0f, (float)seconds);
        return true;
    }

    internal static void RegisterPersistedSummonLifetime(ZNetView? view)
    {
        ZDO? zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        if (zdo == null || zdo.GetLong(SummonLifetimeExpiryZdoKey, 0L) <= 0L)
        {
            return;
        }

        EnsureLifetimeController(view!.GetComponent<Character>());
    }

    internal static bool TryExpireSummon(Character? character)
    {
        if (character == null || character.IsDead())
        {
            return true;
        }

        ZNetView? view = character.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner())
        {
            return false;
        }

        Tameable? tameable = character.GetComponent<Tameable>();
        if (tameable != null && TameableUnSummonMethod != null)
        {
            InvokeUnSummon(tameable);
            return true;
        }

        if (ZNetScene.instance == null)
        {
            return false;
        }

        ZNetScene.instance.Destroy(character.gameObject);
        return true;
    }

    private static void RestoreSpawnAbilityAfterSpawn(SpawnAbility spawnAbility, SpawnAbilityRuntimeState? state)
    {
        if (spawnAbility == null || state == null)
        {
            return;
        }

        state.Restore(spawnAbility);
    }

    internal static void EnforceSummonGroupLimit(Tameable summoned, ZDOID ownerId)
    {
        if (summoned == null)
        {
            return;
        }

        EnforceSummonGroupLimit(summoned.GetComponent<Character>(), ownerId);
    }

    private static void EnforceSummonGroupLimit(Character? summoned, ZDOID ownerId)
    {
        if (summoned == null)
        {
            return;
        }

        SummonQualityPresetTag groupTag = summoned.GetComponent<SummonQualityPresetTag>();
        if (groupTag == null ||
            string.IsNullOrWhiteSpace(groupTag.GroupId) ||
            !ActiveGroupIds.Contains(groupTag.GroupId))
        {
            return;
        }

        ZNetView summonedView = summoned.GetComponent<ZNetView>();
        if (summonedView == null || !summonedView.IsValid() || !summonedView.IsOwner())
        {
            return;
        }

        MarkSummonOwner(summoned, ownerId);
        EnforceSummonGroupLimit(groupTag.GroupId, ownerId, groupTag.MaxInstances, showMessage: true);
    }

    private static void RegisterNewSummonsAndEnforceLimit(SpawnAbilityRuntimeState state)
    {
        if (string.IsNullOrWhiteSpace(state.GroupId) ||
            state.OwnerId.IsNone())
        {
            return;
        }

        foreach (Character character in Character.GetAllCharacters())
        {
            if (character == null || state.ExistingSummonInstanceIds.Contains(character.GetInstanceID()))
            {
                continue;
            }

            SummonQualityPresetTag tag = character.GetComponent<SummonQualityPresetTag>();
            if (tag == null ||
                !string.Equals(tag.GroupId, state.GroupId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            MarkSummonOwner(character, state.OwnerId);
        }

        EnforceSummonGroupLimit(state.GroupId, state.OwnerId, state.MaxInstances, showMessage: true);
    }

    private static void EnforceSummonGroupLimit(string groupId, ZDOID ownerId, int maxInstances, bool showMessage)
    {
        if (string.IsNullOrWhiteSpace(groupId) || ownerId.IsNone())
        {
            return;
        }

        Player? owner = ResolveOwner(ownerId);
        List<Character> summons = new();
        foreach (Character character in Character.GetAllCharacters())
        {
            if (character == null || character.IsDead() || !IsMatchingSummonGroup(character, groupId))
            {
                continue;
            }

            if (IsMatchingSummonOwner(character, ownerId, owner))
            {
                summons.Add(character);
            }
        }

        summons.Sort(CompareOlderFirst);
        int removalsNeeded = summons.Count - Mathf.Max(1, maxInstances);
        for (int index = 0; index < removalsNeeded && index < summons.Count; index++)
        {
            RemoveSummon(summons[index]);
        }

        if (showMessage && removalsNeeded > 0 && owner != null && owner == Player.m_localPlayer)
        {
            owner.Message(MessageHud.MessageType.Center, "$hud_maxsummonsreached");
        }
    }

    private static void RebuildCoreRules(
        IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> summonConfigs,
        ObjectDB? objectDb)
    {
        _activeRules = BuildQualityRules(summonConfigs);
        AppendGlobalQualityRules(_activeRules, objectDb, summonConfigs);
        RulesByItemPrefab.Clear();
        ActiveGroupIds.Clear();

        foreach (QualityRule rule in _activeRules)
        {
            ActiveGroupIds.Add(rule.GroupId);
            RulesByItemPrefab[rule.ItemPrefabName] = rule;
        }
    }

    private static List<QualityRule> BuildQualityRules(
        IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> summonConfigs)
    {
        List<QualityRule> rules = new();
        foreach (NormalizedMagicSummonOverrideConfig config in summonConfigs.Values)
        {
            if (config == null || !config.HasQualityPreset)
            {
                continue;
            }

            string itemPrefabName = TrimOrEmpty(config.EntryId);

            rules.Add(new QualityRule(
                itemPrefabName,
                config.QualityPreset,
                Mathf.Clamp(config.MaxQuality, 1, 10)));
        }

        return rules;
    }

    private static void AppendGlobalQualityRules(
        List<QualityRule> rules,
        ObjectDB? objectDb,
        IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> summonConfigs)
    {
        MagicSummonQualityPreset globalPreset = GetConfiguredGlobalPreset();
        if (globalPreset == MagicSummonQualityPreset.None || objectDb?.m_items == null)
        {
            return;
        }

        HashSet<string> explicitItems = new(rules.Select(rule => rule.ItemPrefabName), StringComparer.OrdinalIgnoreCase);
        HashSet<string> summonOverrideItems = new(
            summonConfigs.Values
                .Where(static config => config != null && config.Summons.Count > 0)
                .Select(static config => TrimOrEmpty(config.EntryId)),
            StringComparer.OrdinalIgnoreCase);
        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null ||
                string.IsNullOrWhiteSpace(itemPrefab.name) ||
                explicitItems.Contains(itemPrefab.name))
            {
                continue;
            }

            ItemDrop? itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (!IsBloodMagicSummonItem(itemDrop, summonOverrideItems.Contains(itemPrefab.name)))
            {
                continue;
            }

            rules.Add(new QualityRule(
                itemPrefab.name,
                globalPreset,
                GlobalMaxQuality));
            explicitItems.Add(itemPrefab.name);
        }
    }

    private static MagicSummonQualityPreset GetConfiguredGlobalPreset()
    {
        return SecondaryAttacksPlugin.MagicSummonQualityPreset?.Value switch
        {
            SecondaryAttacksPlugin.MagicSummonQualityPresetSelection.CountByQuality => MagicSummonQualityPreset.CountByQuality,
            SecondaryAttacksPlugin.MagicSummonQualityPresetSelection.LevelByQuality => MagicSummonQualityPreset.LevelByQuality,
            _ => MagicSummonQualityPreset.None
        };
    }

    private static bool IsBloodMagicSummonItem(ItemDrop? itemDrop, bool hasSummonOverride)
    {
        ItemDrop.ItemData.SharedData? shared = itemDrop?.m_itemData?.m_shared;
        if (shared == null || shared.m_skillType != Skills.SkillType.BloodMagic)
        {
            return false;
        }

        return SummonSpawnAbilityResolver.TryResolve(shared, out SpawnAbility? spawnAbility, out _) &&
               (hasSummonOverride ||
                spawnAbility?.m_spawnPrefab?.Any(static prefab => prefab != null) == true);
    }

    private static bool IsBloodMagicSummonCast(
        SpawnAbility spawnAbility,
        Character? owner,
        ItemDrop.ItemData item)
    {
        return owner is Player &&
               item.m_shared?.m_skillType == Skills.SkillType.BloodMagic &&
               spawnAbility.m_spawnPrefab?.Any(static prefab =>
                   prefab != null && prefab.GetComponent<Character>() != null) == true;
    }

    private static float GetBloodMagicSummonLifetimeSeconds(
        SpawnAbility spawnAbility,
        Character? owner,
        ItemDrop.ItemData item)
    {
        if (!IsBloodMagicSummonCast(spawnAbility, owner, item))
        {
            return 0f;
        }

        int baseLifetimeSeconds = TryResolveLifetimeOverride(item, out int lifetimeOverrideSeconds)
            ? lifetimeOverrideSeconds
            : Math.Max(1, SecondaryAttacksPlugin.BloodMagicSummonLifetimeSeconds?.Value ?? 1200);
        float skillFactor = Mathf.Clamp01(owner!.GetSkillFactor(Skills.SkillType.BloodMagic));
        float skill100Multiplier = Mathf.Max(
            1f,
            SecondaryAttacksPlugin.BloodMagicSummonLifetimeSkill100Multiplier?.Value ?? 2f);
        return baseLifetimeSeconds * Mathf.Lerp(1f, skill100Multiplier, skillFactor);
    }

    private static bool TryResolveLifetimeOverride(
        ItemDrop.ItemData item,
        out int lifetimeSeconds)
    {
        string rawItemPrefabName = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
        if (!string.IsNullOrWhiteSpace(rawItemPrefabName) &&
            LifetimeOverridesByItemPrefab.TryGetValue(rawItemPrefabName, out lifetimeSeconds))
        {
            return true;
        }

        string itemPrefabName = item.m_dropPrefab != null ? Utils.GetPrefabName(item.m_dropPrefab) : "";
        if (!string.IsNullOrWhiteSpace(itemPrefabName) &&
            LifetimeOverridesByItemPrefab.TryGetValue(itemPrefabName, out lifetimeSeconds))
        {
            return true;
        }

        string sharedName = item.m_shared?.m_name ?? "";
        if (!string.IsNullOrWhiteSpace(sharedName) &&
            LifetimeOverridesBySharedName.TryGetValue(sharedName, out lifetimeSeconds))
        {
            return true;
        }

        lifetimeSeconds = 0;
        return false;
    }

    private static void RebuildLifetimeOverrides(
        IReadOnlyDictionary<string, NormalizedMagicSummonOverrideConfig> summonConfigs,
        ObjectDB? objectDb)
    {
        LifetimeOverridesByItemPrefab.Clear();
        LifetimeOverridesBySharedName.Clear();

        foreach (NormalizedMagicSummonOverrideConfig config in summonConfigs.Values)
        {
            if (config == null ||
                !config.LifetimeSeconds.HasValue ||
                string.IsNullOrWhiteSpace(config.EntryId))
            {
                continue;
            }

            string itemPrefabName = config.EntryId.Trim();
            int lifetimeSeconds = Math.Max(1, config.LifetimeSeconds.Value);
            LifetimeOverridesByItemPrefab[itemPrefabName] = lifetimeSeconds;

            GameObject? itemPrefab = objectDb != null ? objectDb.GetItemPrefab(itemPrefabName) : null;
            string sharedName = itemPrefab?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name ?? "";
            if (!string.IsNullOrWhiteSpace(sharedName))
            {
                LifetimeOverridesBySharedName[sharedName] = lifetimeSeconds;
            }
        }
    }

    private static void RestoreOriginalMaxQualities(ObjectDB objectDb, ObjectDbState state)
    {
        foreach ((string itemPrefabName, int maxQuality) in state.OriginalMaxQualities)
        {
            GameObject? itemPrefab = objectDb.GetItemPrefab(itemPrefabName);
            ItemDrop.ItemData.SharedData? shared = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
            if (shared != null)
            {
                shared.m_maxQuality = maxQuality;
            }
        }

        state.OriginalMaxQualities.Clear();
    }

    private static bool TryResolveRule(SpawnAbility spawnAbility, ItemDrop.ItemData item, out QualityRule rule)
    {
        if (TryResolveItemRule(item, out rule))
        {
            return true;
        }

        string spawnAbilityPrefabName = Utils.GetPrefabName(spawnAbility.gameObject);
        if (!string.IsNullOrWhiteSpace(spawnAbilityPrefabName) &&
            RulesBySpawnAbilityPrefab.TryGetValue(spawnAbilityPrefabName, out rule))
        {
            return true;
        }

        string rawSpawnAbilityName = spawnAbility.gameObject != null ? spawnAbility.gameObject.name : "";
        if (!string.IsNullOrWhiteSpace(rawSpawnAbilityName) &&
            RulesBySpawnAbilityPrefab.TryGetValue(rawSpawnAbilityName, out rule))
        {
            return true;
        }

        rule = null!;
        return false;
    }

    private static bool TryResolveItemRule(ItemDrop.ItemData? item, out QualityRule rule)
    {
        string itemPrefabName = item?.m_dropPrefab != null ? item.m_dropPrefab.name : "";
        if (!string.IsNullOrWhiteSpace(itemPrefabName) &&
            RulesByItemPrefab.TryGetValue(itemPrefabName, out rule))
        {
            return true;
        }

        string sharedName = item?.m_shared?.m_name ?? "";
        if (!string.IsNullOrWhiteSpace(sharedName) &&
            RulesBySharedName.TryGetValue(sharedName, out rule))
        {
            return true;
        }

        rule = null!;
        return false;
    }

    private static bool TryResolveTargetSpawnAbility(
        ZNetScene scene,
        QualityRule rule,
        out SpawnAbility? spawnAbility,
        out string targetName)
    {
        spawnAbility = null;
        targetName = "";

        GameObject? itemPrefab = ResolveItemPrefab(scene, rule.ItemPrefabName);
        ItemDrop? itemDrop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
        if (itemDrop?.m_itemData?.m_shared == null)
        {
            return false;
        }

        return SummonSpawnAbilityResolver.TryResolve(itemDrop, out spawnAbility, out targetName);
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

    private static void TagSpawnPrefabs(SpawnAbility spawnAbility, QualityRule rule, int maxInstances, int summonLevel)
    {
        GameObject[] spawnPrefabs = spawnAbility.m_spawnPrefab ?? Array.Empty<GameObject>();
        foreach (GameObject spawnPrefab in spawnPrefabs)
        {
            if (spawnPrefab == null)
            {
                continue;
            }

            SummonQualityPresetTag tag = spawnPrefab.GetComponent<SummonQualityPresetTag>();
            if (tag == null)
            {
                tag = spawnPrefab.AddComponent<SummonQualityPresetTag>();
            }

            tag.GroupId = rule.GroupId;
            tag.MaxInstances = Mathf.Max(1, maxInstances);
            tag.UsesLevelByQuality = rule.Preset == MagicSummonQualityPreset.LevelByQuality;
            tag.SummonLevel = tag.UsesLevelByQuality ? Mathf.Max(1, summonLevel) : 1;
        }
    }

    private static void ResetSpawnPrefabTags(SpawnAbility spawnAbility)
    {
        GameObject[] spawnPrefabs = spawnAbility.m_spawnPrefab ?? Array.Empty<GameObject>();
        foreach (GameObject spawnPrefab in spawnPrefabs)
        {
            SummonQualityPresetTag? tag = spawnPrefab != null
                ? spawnPrefab.GetComponent<SummonQualityPresetTag>()
                : null;
            if (tag == null)
            {
                continue;
            }

            tag.GroupId = "";
            tag.MaxInstances = 1;
            tag.UsesLevelByQuality = false;
            tag.SummonLevel = 1;
            tag.OwnerId = ZDOID.None;
        }
    }

    private static HashSet<int> SnapshotSummonInstanceIds(string groupId)
    {
        HashSet<int> instanceIds = new();
        foreach (Character character in Character.GetAllCharacters())
        {
            if (character != null && IsMatchingSummonGroup(character, groupId))
            {
                instanceIds.Add(character.GetInstanceID());
            }
        }

        return instanceIds;
    }

    private static void TryRegisterSummonLifetime(
        Character? character,
        SpawnAbilityRuntimeState state)
    {
        if (character == null || !state.HasLifetime)
        {
            return;
        }

        ZNetView? view = character.GetComponent<ZNetView>();
        ZDO? zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        if (zdo == null || !view!.IsOwner())
        {
            return;
        }

        if (zdo.GetLong(SummonLifetimeExpiryZdoKey, 0L) <= 0L && ZNet.instance != null)
        {
            long lifetimeTicks = Math.Max(
                1L,
                (long)(state.LifetimeSeconds * TimeSpan.TicksPerSecond));
            zdo.Set(SummonLifetimeExpiryZdoKey, ZNet.instance.GetTime().Ticks + lifetimeTicks);
        }

        EnsureLifetimeController(character);
    }

    internal static void EnsureLifetimeController(Character? character)
    {
        if (character == null ||
            character.GetComponent<ZNetView>() == null ||
            character.GetComponent<BloodMagicSummonLifetimeController>() != null)
        {
            return;
        }

        character.gameObject.AddComponent<BloodMagicSummonLifetimeController>();
    }

    private static bool IsMatchingSummonGroup(Character character, string groupId)
    {
        SummonQualityPresetTag tag = character.GetComponent<SummonQualityPresetTag>();
        return tag != null &&
               !string.IsNullOrWhiteSpace(tag.GroupId) &&
               string.Equals(tag.GroupId, groupId, StringComparison.OrdinalIgnoreCase);
    }

    private static void MarkSummonOwner(Character summon, ZDOID ownerId)
    {
        if (ownerId.IsNone())
        {
            return;
        }

        SummonQualityPresetTag tag = summon.GetComponent<SummonQualityPresetTag>();
        if (tag != null)
        {
            tag.OwnerId = ownerId;
        }

        ZNetView? view = summon.GetComponent<ZNetView>();
        if (view != null && view.IsValid() && view.IsOwner())
        {
            view.GetZDO().Set(SummonOwnerZdoKey, ownerId);
        }
    }

    private static bool IsMatchingSummonOwner(Character summon, ZDOID ownerId, Player? owner)
    {
        SummonQualityPresetTag tag = summon.GetComponent<SummonQualityPresetTag>();
        if (tag != null && !tag.OwnerId.IsNone() && tag.OwnerId == ownerId)
        {
            return true;
        }

        ZNetView? view = summon.GetComponent<ZNetView>();
        ZDO? zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        if (zdo != null)
        {
            ZDOID storedOwnerId = zdo.GetZDOID(SummonOwnerZdoKey);
            if (!storedOwnerId.IsNone() && storedOwnerId == ownerId)
            {
                if (tag != null)
                {
                    tag.OwnerId = storedOwnerId;
                }

                return true;
            }
        }

        return owner != null && IsFollowingOwner(summon.gameObject, owner);
    }

    private static int CompareOlderFirst(Character left, Character right)
    {
        float leftAge = GetTimeSinceSpawned(left);
        float rightAge = GetTimeSinceSpawned(right);
        return rightAge.CompareTo(leftAge);
    }

    private static float GetTimeSinceSpawned(Character character)
    {
        BaseAI? ai = character.GetComponent<BaseAI>();
        return ai != null ? (float)ai.GetTimeSinceSpawned().TotalSeconds : 0f;
    }

    private static void RemoveSummon(Character summon)
    {
        Tameable tameable = summon.GetComponent<Tameable>();
        if (tameable != null)
        {
            InvokeUnSummon(tameable);
            return;
        }

        ZNetView? view = summon.GetComponent<ZNetView>();
        if (view != null && view.IsValid() && view.IsOwner())
        {
            ZNetScene.instance.Destroy(summon.gameObject);
        }
    }

    private static Player? ResolveOwner(ZDOID ownerId)
    {
        GameObject? ownerObject = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(ownerId) : null;
        return ownerObject != null ? ownerObject.GetComponent<Player>() : null;
    }

    private static bool IsFollowingOwner(GameObject summon, Player owner)
    {
        MonsterAI monsterAi = summon.GetComponent<MonsterAI>();
        if (monsterAi != null && monsterAi.GetFollowTarget() == owner.gameObject)
        {
            return true;
        }

        ZNetView? view = summon.GetComponent<ZNetView>();
        ZDO? zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        return zdo != null && zdo.GetString(ZDOVars.s_follow) == owner.GetPlayerName();
    }

    private static void InvokeUnSummon(Tameable tameable)
    {
        if (TameableUnSummonMethod == null)
        {
            return;
        }

        TameableUnSummonMethod.Invoke(tameable, Array.Empty<object>());
    }

    private static string TrimOrEmpty(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value!.Trim();
    }

    private static string SanitizeGroupKey(string value)
    {
        char[] characters = value.ToCharArray();
        for (int index = 0; index < characters.Length; index++)
        {
            if (!char.IsLetterOrDigit(characters[index]))
            {
                characters[index] = '_';
            }
        }

        string sanitized = new string(characters).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "Summon" : sanitized;
    }

    private sealed class ObjectDbState
    {
        internal Dictionary<string, int> OriginalMaxQualities { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class SpawnAbilityRuntimeState
    {
        private SpawnAbilityRuntimeState(
            int maxSpawned,
            bool setMaxInstancesFromWeaponLevel,
            List<SpawnAbility.LevelUpSettings>? levelUpSettings,
            string groupId,
            int maxInstances,
            ZDOID ownerId,
            HashSet<int> existingSummonInstanceIds,
            float lifetimeSeconds)
        {
            MaxSpawned = maxSpawned;
            SetMaxInstancesFromWeaponLevel = setMaxInstancesFromWeaponLevel;
            LevelUpSettings = levelUpSettings;
            GroupId = groupId;
            MaxInstances = maxInstances;
            OwnerId = ownerId;
            ExistingSummonInstanceIds = existingSummonInstanceIds;
            LifetimeSeconds = lifetimeSeconds;
        }

        private int MaxSpawned { get; }

        private bool SetMaxInstancesFromWeaponLevel { get; }

        private List<SpawnAbility.LevelUpSettings>? LevelUpSettings { get; }

        internal string GroupId { get; }

        internal int MaxInstances { get; }

        internal ZDOID OwnerId { get; }

        internal HashSet<int> ExistingSummonInstanceIds { get; }

        internal float LifetimeSeconds { get; }

        internal bool HasLifetime => LifetimeSeconds > 0f;

        internal static SpawnAbilityRuntimeState Capture(
            SpawnAbility spawnAbility,
            string groupId,
            int maxInstances,
            ZDOID ownerId,
            float lifetimeSeconds)
        {
            float normalizedLifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
            return new SpawnAbilityRuntimeState(
                spawnAbility.m_maxSpawned,
                spawnAbility.m_setMaxInstancesFromWeaponLevel,
                spawnAbility.m_levelUpSettings,
                groupId,
                maxInstances,
                ownerId,
                SnapshotSummonInstanceIds(groupId),
                normalizedLifetimeSeconds);
        }

        internal void Restore(SpawnAbility spawnAbility)
        {
            spawnAbility.m_maxSpawned = MaxSpawned;
            spawnAbility.m_setMaxInstancesFromWeaponLevel = SetMaxInstancesFromWeaponLevel;
            spawnAbility.m_levelUpSettings = LevelUpSettings;
        }
    }

    private sealed class QualityRule
    {
        internal QualityRule(
            string itemPrefabName,
            MagicSummonQualityPreset preset,
            int maxQuality)
        {
            ItemPrefabName = itemPrefabName;
            Preset = preset;
            MaxQuality = maxQuality;
            GroupId = $"SecondaryAttacks.MagicSummon.{SanitizeGroupKey(itemPrefabName)}";
        }

        internal string ItemPrefabName { get; }

        internal MagicSummonQualityPreset Preset { get; }

        internal int MaxQuality { get; }

        internal string GroupId { get; }

        internal int GetSummonLevel(int itemQuality)
        {
            return Preset == MagicSummonQualityPreset.LevelByQuality
                ? Mathf.Clamp(itemQuality, 1, MaxQuality)
                : GlobalFixedSummonLevel;
        }

        internal int GetMaxInstances(int itemQuality)
        {
            return Preset == MagicSummonQualityPreset.CountByQuality
                ? Mathf.Clamp(itemQuality, 1, MaxQuality)
                : 1;
        }
    }
}

internal sealed class SummonQualityPresetTag : MonoBehaviour
{
    [SerializeField]
    internal string GroupId = "";

    [SerializeField]
    internal int MaxInstances = 1;

    [SerializeField]
    internal bool UsesLevelByQuality;

    [SerializeField]
    internal int SummonLevel = 1;

    [NonSerialized]
    internal ZDOID OwnerId = ZDOID.None;
}

internal sealed class BloodMagicSummonLifetimeController : MonoBehaviour
{
    private const float CheckIntervalSeconds = 0.5f;
    private Character? _character;
    private float _nextCheckTime;
    private bool _completed;

    private void Awake()
    {
        _character = GetComponent<Character>();
    }

    private void Update()
    {
        if (_completed || Time.unscaledTime < _nextCheckTime)
        {
            return;
        }

        _nextCheckTime = Time.unscaledTime + CheckIntervalSeconds;
        if (_character == null ||
            !MagicSummonQualityPresetSystem.TryGetSummonLifetime(
                _character,
                out float remainingSeconds))
        {
            _completed = true;
            Destroy(this);
            return;
        }

        if (remainingSeconds <= 0f)
        {
            _completed = MagicSummonQualityPresetSystem.TryExpireSummon(_character);
        }
    }
}

[HarmonyPatch(typeof(ZNetView), "Awake")]
internal static class ZNetViewAwakeBloodMagicSummonLifetimePatch
{
    private static void Postfix(ZNetView __instance)
    {
        MagicSummonQualityPresetSystem.RegisterPersistedSummonLifetime(__instance);
    }
}

[HarmonyPatch(typeof(SpawnAbility), "SetupAoe")]
internal static class SpawnAbilitySetupAoeBloodMagicSummonLifetimePatch
{
    private static void Prefix(SpawnAbility __instance, Character owner)
    {
        MagicSummonQualityPresetSystem.RegisterSpawnedCharacterLifetime(__instance, owner);
    }
}

[HarmonyPatch(typeof(SpawnAbility), nameof(SpawnAbility.Setup))]
internal static class SpawnAbilitySetupMagicSummonQualityPresetPatch
{
    private static void Prefix(
        SpawnAbility __instance,
        Character owner,
        ItemDrop.ItemData item)
    {
        MagicSummonQualityPresetSystem.PrepareSpawnAbilityForRule(__instance, owner, item);
    }
}

[HarmonyPatch(typeof(SpawnAbility), nameof(SpawnAbility.Spawn))]
internal static class SpawnAbilitySpawnMagicSummonQualityPresetPatch
{
    private static void Postfix(SpawnAbility __instance, ref IEnumerator __result)
    {
        if (MagicSummonQualityPresetSystem.TryConsumePendingSpawnAbilityState(__instance, out MagicSummonQualityPresetSystem.SpawnAbilityRuntimeState? state) &&
            state != null)
        {
            __result = MagicSummonQualityPresetSystem.WrapSpawnWithRestore(__instance, __result, state);
        }
    }
}

[HarmonyPatch(typeof(Tameable), "RPC_Command")]
internal static class TameableRpcCommandMagicSummonQualityLimitPatch
{
    private static void Postfix(Tameable __instance, ZDOID characterID)
    {
        MagicSummonQualityPresetSystem.EnforceSummonGroupLimit(__instance, characterID);
    }
}
