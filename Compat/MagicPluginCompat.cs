using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class MagicPluginCompat
{
    internal const string MagicPluginGuid = "blacks7ar.MagicPlugin";

    private const string MagicPluginTypeName = "MagicPlugin.Plugin";
    private const string ConfigSetupTypeName = "MagicPlugin.Functions.ConfigSetup";
    private static readonly FloatConfigAccessor MagicVelocity = new(null, 2f);
    private static readonly FloatConfigAccessor MagicAccuracy = new(null, 0f);
    private static readonly FloatConfigAccessor SurtlingStaffAoe = new(null, 3f);
    private static readonly FloatConfigAccessor EikthyrStaffAoe = new(null, 3f);
    private static readonly FloatConfigAccessor LightningStaffAoe = new(null, 3f);
    private static readonly FloatConfigAccessor PoisonStaffAoe = new(null, 3f);
    private static readonly FloatConfigAccessor ArcticStaffAoe = new(null, 3f);
    private static readonly FloatConfigAccessor ModersAoeRange = new(null, 5f);
    private static readonly FloatConfigAccessor ModersChopDamage = new(null, 50f);
    private static readonly FloatConfigAccessor ModersFrostDamage = new(null, 120f);
    private static readonly FloatConfigAccessor ModersPickaxeDamage = new(null, 50f);
    private static readonly FloatConfigAccessor ModersPierceDamage = new(null, 40f);
    private static readonly FloatConfigAccessor YagluthAoeRange = new(null, 5f);
    private static readonly FloatConfigAccessor YagluthBluntDamage = new(null, 40f);
    private static readonly FloatConfigAccessor YagluthChopDamage = new(null, 50f);
    private static readonly FloatConfigAccessor YagluthFireDamage = new(null, 120f);
    private static readonly FloatConfigAccessor YagluthPickaxeDamage = new(null, 50f);
    private const string PlayerPatchTypeName = "MagicPlugin.Patches.PlayerPatch";
    private static bool _projectilePatchInstalled;
    private static bool _teleportPatchInstalled;
    private static bool _summonCleanupPatchInstalled;
    private static bool _reportedRuntimeError;
    private static bool _reportedTeleportRuntimeError;
    private static bool _reportedSummonCleanupRuntimeError;
    private static object? _magicPluginInstance;
    private static Type? _magicPluginType;
    private static bool _magicSummonReferenceFieldsInitialized;
    private static readonly List<FieldInfo> MagicSummonReferenceFields = new();

    internal static void TryInstall(Harmony harmony)
    {
        if (!Chainloader.PluginInfos.TryGetValue(MagicPluginGuid, out var pluginInfo))
        {
            return;
        }

        TryInstallProjectilePatch(harmony, pluginInfo);
        TryInstallTeleportPatch(harmony, pluginInfo);
        TryInstallSummonCleanupPatch(harmony, pluginInfo);
    }

    private static void TryInstallProjectilePatch(Harmony harmony, PluginInfo pluginInfo)
    {
        if (_projectilePatchInstalled)
        {
            return;
        }

        MethodInfo? original = AccessTools.DeclaredMethod(typeof(Attack), nameof(Attack.FireProjectileBurst));
        if (original == null)
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning("MagicPlugin compatibility skipped: Attack.FireProjectileBurst was not found.");
            return;
        }

        int magicPrefixCount = CountMagicPluginPrefixes(original);
        if (magicPrefixCount == 0)
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning("MagicPlugin compatibility skipped: MagicPlugin FireProjectileBurst prefix was not found.");
            return;
        }

        Assembly? magicAssembly = pluginInfo.Instance?.GetType().Assembly ?? FindAssemblyWithType(MagicPluginTypeName);
        Type? pluginType = magicAssembly?.GetType(MagicPluginTypeName, throwOnError: false);
        Type? configSetupType = magicAssembly?.GetType(ConfigSetupTypeName, throwOnError: false);
        if (pluginType == null || configSetupType == null)
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning("MagicPlugin compatibility skipped: MagicPlugin config types were not found.");
            return;
        }

        UpdateAccessor(pluginType, "_magicVelocity", MagicVelocity);
        UpdateAccessor(pluginType, "_magciAccuracy", MagicAccuracy);
        UpdateAccessor(configSetupType, "_surtlingStaffAOE", SurtlingStaffAoe);
        UpdateAccessor(configSetupType, "_eikthyrStaffAOE", EikthyrStaffAoe);
        UpdateAccessor(configSetupType, "_lightningStaffAOE", LightningStaffAoe);
        UpdateAccessor(configSetupType, "_poisonStaffAOE", PoisonStaffAoe);
        UpdateAccessor(configSetupType, "_arcticStaffAOE", ArcticStaffAoe);
        UpdateAccessor(configSetupType, "_modersAoeRange", ModersAoeRange);
        UpdateAccessor(configSetupType, "_modersChopDamage", ModersChopDamage);
        UpdateAccessor(configSetupType, "_modersFrostDamage", ModersFrostDamage);
        UpdateAccessor(configSetupType, "_modersPickaxeDamage", ModersPickaxeDamage);
        UpdateAccessor(configSetupType, "_modersPierceDamage", ModersPierceDamage);
        UpdateAccessor(configSetupType, "_yagluthAoeRange", YagluthAoeRange);
        UpdateAccessor(configSetupType, "_yagluthBluntDamage", YagluthBluntDamage);
        UpdateAccessor(configSetupType, "_yagluthChopDamage", YagluthChopDamage);
        UpdateAccessor(configSetupType, "_yagluthFireDamage", YagluthFireDamage);
        UpdateAccessor(configSetupType, "_yagluthPickaxeDamage", YagluthPickaxeDamage);

        harmony.Unpatch(original, HarmonyPatchType.Prefix, MagicPluginGuid);
        HarmonyMethod replacementPrefix = new(typeof(MagicPluginCompat), nameof(FireProjectileBurstPrefix))
        {
            priority = Priority.Normal
        };
        harmony.Patch(original, prefix: replacementPrefix);
        _projectilePatchInstalled = true;

        SecondaryAttacksPlugin.ModLogger.LogInfo($"Installed MagicPlugin FireProjectileBurst compatibility patch for MagicPlugin {pluginInfo.Metadata.Version}; removed {magicPrefixCount} event-registering prefix(es).");
    }

    private static void TryInstallTeleportPatch(Harmony harmony, PluginInfo pluginInfo)
    {
        if (_teleportPatchInstalled)
        {
            return;
        }

        MethodInfo? original = AccessTools.DeclaredMethod(typeof(Player), nameof(Player.TeleportTo), new[] { typeof(Vector3), typeof(Quaternion), typeof(bool) }) ??
                               AccessTools.DeclaredMethod(typeof(Player), nameof(Player.TeleportTo));
        if (original == null)
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning("MagicPlugin teleport compatibility skipped: Player.TeleportTo was not found.");
            return;
        }

        List<MethodInfo> magicPostfixes = FindMagicPluginTeleportPostfixes(original);
        if (magicPostfixes.Count == 0)
        {
            return;
        }

        foreach (MethodInfo postfix in magicPostfixes)
        {
            harmony.Unpatch(original, postfix);
        }

        HarmonyMethod replacementPostfix = new(typeof(MagicPluginCompat), nameof(TeleportToPostfix))
        {
            priority = Priority.Normal
        };
        harmony.Patch(original, postfix: replacementPostfix);
        _teleportPatchInstalled = true;

        SecondaryAttacksPlugin.ModLogger.LogInfo($"Installed MagicPlugin safe teleport compatibility patch for MagicPlugin {pluginInfo.Metadata.Version}; removed {magicPostfixes.Count} unsafe TeleportTo postfix(es).");
    }

    private static void TryInstallSummonCleanupPatch(Harmony harmony, PluginInfo pluginInfo)
    {
        if (_summonCleanupPatchInstalled)
        {
            return;
        }

        Assembly? magicAssembly = pluginInfo.Instance?.GetType().Assembly ?? FindAssemblyWithType(MagicPluginTypeName);
        Type? pluginType = magicAssembly?.GetType(MagicPluginTypeName, throwOnError: false);
        MethodInfo? update = pluginType != null ? AccessTools.DeclaredMethod(pluginType, "Update") : null;
        MethodInfo? characterOnDestroy = AccessTools.DeclaredMethod(typeof(Character), "OnDestroy");
        if (pluginType == null || update == null || characterOnDestroy == null)
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning("MagicPlugin summon cleanup compatibility skipped: required runtime methods were not found.");
            return;
        }

        _magicPluginInstance = pluginInfo.Instance;
        _magicPluginType = pluginType;
        RebuildMagicSummonReferenceFields(pluginType);

        HarmonyMethod updatePrefix = new(typeof(MagicPluginCompat), nameof(MagicPluginUpdatePrefix))
        {
            priority = Priority.First
        };
        HarmonyMethod destroyPrefix = new(typeof(MagicPluginCompat), nameof(CharacterOnDestroyPrefix))
        {
            priority = Priority.First
        };

        harmony.Patch(update, prefix: updatePrefix);
        harmony.Patch(characterOnDestroy, prefix: destroyPrefix);
        _summonCleanupPatchInstalled = true;

        SecondaryAttacksPlugin.ModLogger.LogInfo($"Installed MagicPlugin summon cleanup compatibility patch for MagicPlugin {pluginInfo.Metadata.Version}; tracking {MagicSummonReferenceFields.Count} possible summon reference field(s).");
    }

    private static bool FireProjectileBurstPrefix(Attack __instance)
    {
        try
        {
            ApplyFireProjectileBurstAdjustments(__instance);
        }
        catch (Exception ex)
        {
            if (!_reportedRuntimeError)
            {
                _reportedRuntimeError = true;
                SecondaryAttacksPlugin.ModLogger.LogWarning($"MagicPlugin compatibility failed while applying projectile adjustments: {ex.Message}");
            }
        }

        return true;
    }

    private static void ApplyFireProjectileBurstAdjustments(Attack attack)
    {
        if (attack == null || attack.m_character is not Player player)
        {
            return;
        }

        ItemDrop.ItemData? weapon = attack.m_weapon;
        ItemDrop.ItemData.SharedData? shared = weapon?.m_shared;
        if (shared == null || shared.m_skillType != Skills.SkillType.ElementalMagic)
        {
            return;
        }

        string weaponName = shared.m_name ?? "";
        if (weaponName.EndsWith("scepter") ||
            weaponName.Contains("flamestaff") ||
            weaponName.Contains("thunderstaff") ||
            (shared.m_ammoType?.Contains("ammo_spells") ?? false))
        {
            return;
        }

        if (shared.m_secondaryAttack?.m_attackProjectile != null &&
            shared.m_secondaryAttack.m_attackProjectile.name == "BDS_DvergerStaffFire_clusterbomb_projectile")
        {
            return;
        }

        if (weaponName == "$bmp_arcticstaff" || weaponName == "$item_stafficeshards")
        {
            attack.m_projectileVel *= 1.2f;
            attack.m_projectileVelMin *= 1.2f;
            attack.m_projectileAccuracy *= 0f;
            attack.m_projectileAccuracyMin *= 0f;
        }
        else
        {
            attack.m_projectileVel *= MagicVelocity.Value;
            attack.m_projectileVelMin *= MagicVelocity.Value;
            attack.m_projectileAccuracy *= MagicAccuracy.Value;
            attack.m_projectileAccuracyMin *= MagicAccuracy.Value;
        }

        GameObject? projectilePrefab = attack.m_attackProjectile;
        if (projectilePrefab == null)
        {
            return;
        }

        string projectileName = projectilePrefab.name ?? "";
        string lowerProjectileName = projectileName.ToLowerInvariant();
        float elementalSkillFactor = player.GetSkillFactor(Skills.SkillType.ElementalMagic);
        if (lowerProjectileName.Contains("surtlingstaff"))
        {
            ApplyProjectileAoe(projectilePrefab, SurtlingStaffAoe, elementalSkillFactor);
        }

        if (lowerProjectileName.Contains("eikthyrsstaff"))
        {
            ApplyProjectileAoe(projectilePrefab, EikthyrStaffAoe, elementalSkillFactor);
        }

        if (lowerProjectileName.Contains("lightningstaff"))
        {
            ApplyProjectileAoe(projectilePrefab, LightningStaffAoe, elementalSkillFactor);
        }

        if (lowerProjectileName.Contains("poisonstaff"))
        {
            ApplyProjectileAoe(projectilePrefab, PoisonStaffAoe, elementalSkillFactor);
        }

        if (lowerProjectileName.Contains("arcticstaff"))
        {
            ApplyProjectileAoe(projectilePrefab, ArcticStaffAoe, elementalSkillFactor);
        }

        if (projectileName == "bmp_modersheritage_projectile")
        {
            ApplyModersHeritage(projectilePrefab, elementalSkillFactor);
        }

        if (projectileName == "bmp_yagluthsheritage_projectile")
        {
            ApplyYagluthsHeritage(projectilePrefab, elementalSkillFactor);
        }
    }

    private static void ApplyProjectileAoe(GameObject projectilePrefab, FloatConfigAccessor aoeConfig, float elementalSkillFactor)
    {
        Projectile? projectile = projectilePrefab.GetComponent<Projectile>();
        if (projectile != null)
        {
            projectile.m_aoe = aoeConfig.Value + 2f * elementalSkillFactor;
        }
    }

    private static void ApplyModersHeritage(GameObject projectilePrefab, float elementalSkillFactor)
    {
        Projectile? iceblast = TryGetSpawnedProjectile(projectilePrefab);
        if (iceblast == null)
        {
            return;
        }

        iceblast.m_aoe = ModersAoeRange.Value + 2f * elementalSkillFactor;
        iceblast.m_damage.m_chop = ModersChopDamage.Value;
        iceblast.m_damage.m_frost = ModersFrostDamage.Value + ModersFrostDamage.Value * elementalSkillFactor;
        iceblast.m_damage.m_pickaxe = ModersPickaxeDamage.Value;
        iceblast.m_damage.m_pierce = ModersPierceDamage.Value;
    }

    private static void ApplyYagluthsHeritage(GameObject projectilePrefab, float elementalSkillFactor)
    {
        Projectile? meteors = TryGetSpawnedProjectile(projectilePrefab);
        if (meteors == null)
        {
            return;
        }

        meteors.m_aoe = YagluthAoeRange.Value + 2f * elementalSkillFactor;
        meteors.m_damage.m_blunt = YagluthBluntDamage.Value;
        meteors.m_damage.m_chop = YagluthChopDamage.Value;
        meteors.m_damage.m_fire = YagluthFireDamage.Value + YagluthFireDamage.Value * elementalSkillFactor;
        meteors.m_damage.m_pickaxe = YagluthPickaxeDamage.Value;
    }

    private static Projectile? TryGetSpawnedProjectile(GameObject projectilePrefab)
    {
        Projectile? projectile = projectilePrefab.GetComponent<Projectile>();
        GameObject? spawnOnHit = projectile?.m_spawnOnHit;
        SpawnAbility? spawnAbility = spawnOnHit?.GetComponent<SpawnAbility>();
        if (spawnAbility?.m_spawnPrefab == null || spawnAbility.m_spawnPrefab.Length == 0)
        {
            return null;
        }

        return spawnAbility.m_spawnPrefab[0]?.GetComponent<Projectile>();
    }

    private static void TeleportToPostfix(Player __instance, ref Vector3 pos, ref Quaternion rot)
    {
        try
        {
            TeleportFollowingMagicSummons(__instance, pos, rot);
        }
        catch (Exception ex)
        {
            if (!_reportedTeleportRuntimeError)
            {
                _reportedTeleportRuntimeError = true;
                SecondaryAttacksPlugin.ModLogger.LogWarning($"MagicPlugin teleport compatibility failed while moving followed summons: {ex.Message}");
            }
        }
    }

    private static void TeleportFollowingMagicSummons(Player player, Vector3 pos, Quaternion rot)
    {
        if (player == null)
        {
            return;
        }

        GameObject owner = player.gameObject;
        if (owner == null)
        {
            return;
        }

        Vector3 summonPosition = pos + player.transform.forward;
        foreach (Character character in Character.GetAllCharacters())
        {
            if (!IsFollowedMagicSummon(character, owner))
            {
                continue;
            }

            Transform transform = character.transform;
            transform.position = summonPosition;
            transform.rotation = rot;
        }
    }

    private static bool IsFollowedMagicSummon(Character character, GameObject owner)
    {
        if (character == null || character.gameObject == null)
        {
            return false;
        }

        ZNetView? nview = character.m_nview;
        if (nview == null || !nview.IsValid())
        {
            return false;
        }

        string name = character.gameObject.name ?? "";
        if (!name.StartsWith("BMP_", StringComparison.Ordinal) || !character.IsTamed())
        {
            return false;
        }

        Tameable? tameable = character.GetComponent<Tameable>();
        MonsterAI? monsterAi = tameable != null ? tameable.m_monsterAI : null;
        GameObject? followTarget = monsterAi != null ? monsterAi.GetFollowTarget() : null;
        return followTarget == owner;
    }

    private static void MagicPluginUpdatePrefix(object __instance)
    {
        if (__instance != null)
        {
            _magicPluginInstance = __instance;
        }

        TryPruneMagicPluginSummonReferences(null);
    }

    private static void CharacterOnDestroyPrefix(Character __instance)
    {
        TryPruneMagicPluginSummonReferences(__instance);
    }

    private static void TryPruneMagicPluginSummonReferences(Character? target)
    {
        try
        {
            PruneMagicPluginSummonReferences(target);
        }
        catch (Exception ex)
        {
            if (!_reportedSummonCleanupRuntimeError)
            {
                _reportedSummonCleanupRuntimeError = true;
                SecondaryAttacksPlugin.ModLogger.LogWarning($"MagicPlugin summon cleanup compatibility failed while pruning stale summon references: {ex.Message}");
            }
        }
    }

    private static int PruneMagicPluginSummonReferences(Character? target)
    {
        object? pluginInstance = _magicPluginInstance;
        Type? pluginType = pluginInstance?.GetType() ?? _magicPluginType;
        if (pluginType == null)
        {
            return 0;
        }

        if (!_magicSummonReferenceFieldsInitialized || _magicPluginType != pluginType)
        {
            _magicPluginType = pluginType;
            RebuildMagicSummonReferenceFields(pluginType);
        }

        int removed = 0;
        foreach (FieldInfo field in MagicSummonReferenceFields)
        {
            object? owner = field.IsStatic ? null : pluginInstance;
            if (!field.IsStatic && owner == null)
            {
                continue;
            }

            object? value = field.GetValue(owner);
            removed += PruneMagicSummonReferenceValue(field, owner, value, target);
        }

        return removed;
    }

    private static void RebuildMagicSummonReferenceFields(Type pluginType)
    {
        MagicSummonReferenceFields.Clear();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (FieldInfo field in pluginType.GetFields(flags))
        {
            if (CanContainCharacterReference(field.FieldType))
            {
                MagicSummonReferenceFields.Add(field);
            }
        }

        _magicSummonReferenceFieldsInitialized = true;
    }

    private static bool CanContainCharacterReference(Type type)
    {
        if (typeof(Character).IsAssignableFrom(type))
        {
            return true;
        }

        Type? elementType = type.IsArray ? type.GetElementType() : null;
        if (elementType != null && typeof(Character).IsAssignableFrom(elementType))
        {
            return true;
        }

        if (HasCharacterGenericArgument(type))
        {
            return true;
        }

        foreach (Type interfaceType in type.GetInterfaces())
        {
            if (HasCharacterGenericArgument(interfaceType))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasCharacterGenericArgument(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            if (typeof(Character).IsAssignableFrom(argument))
            {
                return true;
            }
        }

        return false;
    }

    private static int PruneMagicSummonReferenceValue(FieldInfo field, object? owner, object? value, Character? target)
    {
        if (value == null)
        {
            return 0;
        }

        if (value is Character character)
        {
            if (ShouldPruneCharacterReference(character, target) && !field.IsInitOnly)
            {
                field.SetValue(owner, null);
                return 1;
            }

            return 0;
        }

        if (value is Array array)
        {
            return PruneArray(array, target);
        }

        if (value is IList list)
        {
            return PruneList(list, target);
        }

        if (value is IDictionary dictionary)
        {
            return PruneDictionary(dictionary, target);
        }

        if (value is IEnumerable enumerable)
        {
            return PruneEnumerableWithRemove(value, enumerable, target);
        }

        return 0;
    }

    private static int PruneArray(Array array, Character? target)
    {
        int removed = 0;
        for (int index = 0; index < array.Length; index++)
        {
            object? item = array.GetValue(index);
            if (item is Character character && ShouldPruneCharacterReference(character, target))
            {
                array.SetValue(null, index);
                removed++;
            }
        }

        return removed;
    }

    private static int PruneList(IList list, Character? target)
    {
        int removed = 0;
        for (int index = list.Count - 1; index >= 0; index--)
        {
            object? item = list[index];
            if (item == null || item is Character character && ShouldPruneCharacterReference(character, target))
            {
                list.RemoveAt(index);
                removed++;
            }
        }

        return removed;
    }

    private static int PruneDictionary(IDictionary dictionary, Character? target)
    {
        List<object> keysToRemove = new();
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is Character keyCharacter && ShouldPruneCharacterReference(keyCharacter, target) ||
                entry.Value is Character valueCharacter && ShouldPruneCharacterReference(valueCharacter, target))
            {
                keysToRemove.Add(entry.Key);
            }
        }

        foreach (object key in keysToRemove)
        {
            dictionary.Remove(key);
        }

        return keysToRemove.Count;
    }

    private static int PruneEnumerableWithRemove(object collection, IEnumerable enumerable, Character? target)
    {
        MethodInfo? removeMethod = collection.GetType().GetMethod(
            "Remove",
            BindingFlags.Instance | BindingFlags.Public,
            null,
            new[] { typeof(Character) },
            null);
        if (removeMethod == null)
        {
            return 0;
        }

        List<Character> charactersToRemove = new();
        foreach (object? item in enumerable)
        {
            if (item is Character character && ShouldPruneCharacterReference(character, target))
            {
                charactersToRemove.Add(character);
            }
        }

        int removed = 0;
        foreach (Character character in charactersToRemove)
        {
            object? result = removeMethod.Invoke(collection, new object[] { character });
            if (result is not bool removedItem || removedItem)
            {
                removed++;
            }
        }

        return removed;
    }

    private static bool ShouldPruneCharacterReference(Character character, Character? target)
    {
        if (!ReferenceEquals(target, null) && ReferenceEquals(character, target))
        {
            return true;
        }

        return character == null;
    }

    private static int CountMagicPluginPrefixes(MethodBase original)
    {
        Patches? patchInfo = Harmony.GetPatchInfo(original);
        if (patchInfo == null)
        {
            return 0;
        }

        int count = 0;
        foreach (Patch prefix in patchInfo.Prefixes)
        {
            if (prefix.owner == MagicPluginGuid)
            {
                count++;
            }
        }

        return count;
    }

    private static List<MethodInfo> FindMagicPluginTeleportPostfixes(MethodBase original)
    {
        List<MethodInfo> postfixes = [];
        Patches? patchInfo = Harmony.GetPatchInfo(original);
        if (patchInfo == null)
        {
            return postfixes;
        }

        foreach (Patch postfix in patchInfo.Postfixes)
        {
            if (postfix.owner == MagicPluginGuid &&
                postfix.PatchMethod.DeclaringType?.FullName == PlayerPatchTypeName &&
                postfix.PatchMethod.Name.Contains("TeleportTo_Postfix"))
            {
                postfixes.Add(postfix.PatchMethod);
            }
        }

        return postfixes;
    }

    private static Assembly? FindAssemblyWithType(string fullTypeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.GetType(fullTypeName, throwOnError: false) != null)
            {
                return assembly;
            }
        }

        return null;
    }

    private static void UpdateAccessor(Type ownerType, string fieldName, FloatConfigAccessor accessor)
    {
        FieldInfo? field = AccessTools.Field(ownerType, fieldName);
        if (field?.GetValue(null) is ConfigEntry<float> entry)
        {
            accessor.Entry = entry;
        }
    }

    private sealed class FloatConfigAccessor
    {
        private readonly float _fallback;

        internal FloatConfigAccessor(ConfigEntry<float>? entry, float fallback)
        {
            Entry = entry;
            _fallback = fallback;
        }

        internal ConfigEntry<float>? Entry { get; set; }

        internal float Value => Entry?.Value ?? _fallback;
    }
}
