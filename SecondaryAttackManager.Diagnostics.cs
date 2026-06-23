using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace SecondaryAttacks;

internal static partial class SecondaryAttackManager
{
    public static void DumpPlayerAnimatorReferences(Player player)
    {
        if (_animatorDumpWritten)
        {
            return;
        }

        try
        {
            Animator? animator = player.GetComponentsInChildren<Animator>(true).FirstOrDefault();
            if (animator == null)
            {
                return;
            }

            SortedSet<string> triggerLines = new(StringComparer.Ordinal);
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Trigger &&
                    !string.IsNullOrWhiteSpace(parameter.name))
                {
                    triggerLines.Add(parameter.name);
                }
            }

            PlayerAnimatorTriggers.Clear();
            foreach (string triggerLine in triggerLines)
            {
                PlayerAnimatorTriggers.Add(triggerLine);
            }

            _animatorDumpWritten = true;
            WriteAnimationReferenceFile();
        }
        catch (Exception exception)
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Failed to write {SecondaryAttackYamlDomainRegistry.AnimationReferenceFileName}: {exception.Message}");
        }
    }

    public static void DumpCustomAnimationReferences(Player player)
    {
        if (_customAnimationDumpWritten)
        {
            return;
        }

        try
        {
            _customAnimationDumpWritten = true;
            WriteAnimationReferenceFile();
        }
        catch (Exception exception)
        {
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Failed to write {SecondaryAttackYamlDomainRegistry.AnimationReferenceFileName}: {exception.Message}");
        }
    }

    private static void WriteAnimationReferenceFile()
    {
        Directory.CreateDirectory(SecondaryAttackYamlDomainRegistry.ConfigDirectoryPath);

        StringBuilder builder = new();
        builder.AppendLine("SecondaryAttacks animation reference dump");
        builder.AppendLine("This file is informational only; SecondaryAttacks does not read it as config.");
        builder.AppendLine();
        builder.AppendLine("[Vanilla Animations]");
        builder.AppendLine();
        builder.AppendLine("[Player Animator Triggers]");
        if (PlayerAnimatorTriggers.Count == 0)
        {
            builder.AppendLine("<empty>");
        }
        else
        {
            foreach (string trigger in PlayerAnimatorTriggers)
            {
                builder.AppendLine($"- {trigger}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("[Mod Animations]");
        builder.AppendLine();
        AppendAnimationReplaceManagerDump(builder);
        AppendPixMovementSlideDump(builder);

        File.WriteAllText(SecondaryAttackYamlDomainRegistry.AnimationReferenceFilePath, builder.ToString());
    }

    private static void AppendAnimationReplaceManagerDump(StringBuilder builder)
    {
        const string typeName = "KG_Managers.AnimationReplaceManager";

        builder.AppendLine("[KG_Managers.AnimationReplaceManager]");
        Type? managerType = FindLoadedType(typeName);
        if (managerType == null)
        {
            builder.AppendLine("Loaded: false");
            builder.AppendLine();
            return;
        }

        AssemblyName assemblyName = managerType.Assembly.GetName();
        builder.AppendLine("Loaded: true");
        builder.AppendLine($"Assembly: {assemblyName.Name} {assemblyName.Version}");
        builder.AppendLine();

        AppendAnimationSetsDump(builder, managerType);
        builder.AppendLine();
    }

    private static void AppendPixMovementSlideDump(StringBuilder builder)
    {
        const string typeName = "Pix.Movement.Slide";

        builder.AppendLine("[Pix.Movement.Slide]");
        Type? slideType = FindLoadedType(typeName);
        if (slideType == null)
        {
            builder.AppendLine("Loaded: false");
            builder.AppendLine();
            return;
        }

        AssemblyName assemblyName = slideType.Assembly.GetName();
        builder.AppendLine("Loaded: true");
        builder.AppendLine($"Assembly: {assemblyName.Name} {assemblyName.Version}");

        object? triggerEntry = GetStaticFieldValue(slideType, "CfgAnimTriggerName");
        object? triggerValue = GetInstancePropertyValue(triggerEntry, "Value");
        if (triggerValue is string trigger && !string.IsNullOrWhiteSpace(trigger))
        {
            builder.AppendLine($"Trigger: {trigger}");
        }

        RuntimeAnimatorController? controller = GetStaticFieldValue(slideType, "_slideController") as RuntimeAnimatorController;
        builder.AppendLine();
        builder.AppendLine("[Animation Sets]");
        if (controller == null)
        {
            builder.AppendLine("<missing>");
            builder.AppendLine();
            return;
        }

        AppendStringList(builder, GetRuntimeAnimatorControllerClipNames(controller), "");
        builder.AppendLine();
    }

    private static void AppendAnimationSetsDump(StringBuilder builder, Type managerType)
    {
        builder.AppendLine("[Animation Sets]");
        object? value = GetStaticFieldValue(managerType, "AllAnimationSets");
        if (value is not IEnumerable sets)
        {
            builder.AppendLine("<missing>");
            return;
        }

        int index = 0;
        foreach (object? set in sets)
        {
            if (set is string)
            {
                continue;
            }

            IEnumerable<object> names = set is IEnumerable enumerable
                ? enumerable.Cast<object>()
                : Enumerable.Empty<object>();
            builder.AppendLine($"Set {index}:");
            AppendStringList(builder, names.Select(static name => name?.ToString() ?? "<null>"), "  ");
            index++;
        }

        if (index == 0)
        {
            builder.AppendLine("<empty>");
        }
    }

    private static void AppendStringList(StringBuilder builder, IEnumerable<string> values, string indent)
    {
        List<string> normalizedValues = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(static value => value, StringComparer.Ordinal)
            .ToList();
        if (normalizedValues.Count == 0)
        {
            builder.AppendLine($"{indent}<empty>");
            return;
        }

        foreach (string value in normalizedValues)
        {
            builder.AppendLine($"{indent}- {value}");
        }
    }

    private static object? GetStaticFieldValue(Type type, string fieldName)
    {
        FieldInfo? field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        return field?.GetValue(null);
    }

    private static object? GetInstancePropertyValue(object? instance, string propertyName)
    {
        if (instance == null)
        {
            return null;
        }

        PropertyInfo? property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        return property?.GetValue(instance);
    }

    private static IEnumerable<string> GetRuntimeAnimatorControllerClipNames(RuntimeAnimatorController controller)
    {
        return controller.animationClips
            .Where(static clip => clip != null)
            .Select(static clip => clip.name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct()
            .OrderBy(static name => name, StringComparer.Ordinal);
    }

    private static Type? FindLoadedType(string fullTypeName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(fullTypeName, throwOnError: false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    private static bool ShouldDumpRuntimeProfile(string prefabName)
    {
        return string.Equals(prefabName, "StaffDeathcallerPoisonDO", StringComparison.OrdinalIgnoreCase)
               || string.Equals(prefabName, "StaffStormcallerShockerDO", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool ShouldDumpRuntimeProfileForProjectileRuntime(string prefabName)
    {
        return ShouldDumpRuntimeProfile(prefabName);
    }

    internal static bool TryMarkRuntimeDumpReported(string dumpKey)
    {
        return ReportedRuntimeDumps.Add(dumpKey);
    }

    internal static void DumpRuntimeWeaponProfile(string weaponPrefabName, ItemDrop.ItemData.SharedData sharedData)
    {
        if (!ShouldDumpRuntimeProfile(weaponPrefabName))
        {
            return;
        }

        string key = "weapon|" + weaponPrefabName;
        if (!ReportedRuntimeDumps.Add(key))
        {
            return;
        }

        Attack? primaryAttack = sharedData.m_attack;
        if (primaryAttack == null)
        {
            SecondaryAttacksPlugin.ModLogger.LogInfo($"[RuntimeDump] {weaponPrefabName} primary attack is <null>.");
            return;
        }

        GameObject? payloadPrefab = primaryAttack.m_attackProjectile;
        Projectile? projectile = payloadPrefab != null ? payloadPrefab.GetComponent<Projectile>() : null;
        Aoe? aoe = payloadPrefab != null ? payloadPrefab.GetComponent<Aoe>() : null;
        string payloadType = projectile != null ? "Projectile" : aoe != null ? "Aoe" : payloadPrefab != null && payloadPrefab.GetComponent<IProjectile>() != null ? "IProjectileOnly" : "None";

        SecondaryAttacksPlugin.ModLogger.LogInfo(
            "[RuntimeDump] "
            + weaponPrefabName
            + " primary"
            + $" type={primaryAttack.m_attackType}"
            + $" projectile={payloadPrefab?.name ?? "<null>"}"
            + $" payloadType={payloadType}"
            + $" projectiles={primaryAttack.m_projectiles}"
            + $" bursts={primaryAttack.m_projectileBursts}"
            + $" burstInterval={primaryAttack.m_burstInterval}"
            + $" vel={primaryAttack.m_projectileVel}"
            + $" velMin={primaryAttack.m_projectileVelMin}"
            + $" randomVelocity={primaryAttack.m_randomVelocity}"
            + $" accuracy={primaryAttack.m_projectileAccuracy}"
            + $" accuracyMin={primaryAttack.m_projectileAccuracyMin}"
            + $" launchAngle={primaryAttack.m_launchAngle}"
            + $" destroyPrevious={primaryAttack.m_destroyPreviousProjectile}"
            + $" requiresReload={primaryAttack.m_requiresReload}"
            + $" attackHitNoise={primaryAttack.m_attackHitNoise}");

        SecondaryAttacksPlugin.ModLogger.LogInfo(
            "[RuntimeDump] "
            + weaponPrefabName
            + " payloadComponents="
            + DescribeComponents(payloadPrefab));

        if (projectile != null)
        {
            SecondaryAttacksPlugin.ModLogger.LogInfo(
                "[RuntimeDump] "
                + weaponPrefabName
                + " payload"
                + $" gravity={projectile.m_gravity}"
                + $" drag={projectile.m_drag}"
                + $" ttl={projectile.m_ttl}"
                + $" rayRadius={projectile.m_rayRadius}"
                + $" aoe={projectile.m_aoe}"
                + $" attackForce={projectile.m_attackForce}"
                + $" hitNoise={projectile.m_hitNoise}"
                + $" canHitWater={projectile.m_canHitWater}"
                + $" blockable={projectile.m_blockable}"
                + $" dodgeable={projectile.m_dodgeable}"
                + $" stayStatic={projectile.m_stayAfterHitStatic}"
                + $" stayDynamic={projectile.m_stayAfterHitDynamic}"
                + $" stayTTL={projectile.m_stayTTL}"
                + $" attachToRigidBody={projectile.m_attachToRigidBody}"
                + $" spawnOnHit={projectile.m_spawnOnHit?.name ?? "<null>"}"
                + $" spawnOnHitChance={projectile.m_spawnOnHitChance}");

            SecondaryAttacksPlugin.ModLogger.LogInfo(
                "[RuntimeDump] "
                + weaponPrefabName
                + " payloadFlags"
                + $" type={projectile.m_type}"
                + $" adrenaline={projectile.m_adrenaline}"
                + $" backstabBonus={projectile.m_backstabBonus}"
                + $" statusEffect={projectile.m_statusEffect}"
                + $" doOwnerRaytest={projectile.m_doOwnerRaytest}"
                + $" attachToClosestBone={projectile.m_attachToClosestBone}"
                + $" attachPenetration={projectile.m_attachPenetration}"
                + $" hideOnHit={projectile.m_hideOnHit?.name ?? "<null>"}"
                + $" stopEmittersOnHit={projectile.m_stopEmittersOnHit}"
                + $" bounce={projectile.m_bounce}"
                + $" bounceOnWater={projectile.m_bounceOnWater}"
                + $" bouncePower={projectile.m_bouncePower}"
                + $" bounceRoughness={projectile.m_bounceRoughness}"
                + $" maxBounces={projectile.m_maxBounces}"
                + $" minBounceVel={projectile.m_minBounceVel}"
                + $" respawnItemOnHit={projectile.m_respawnItemOnHit}"
                + $" spawnOnTtl={projectile.m_spawnOnTtl}"
                + $" spawnCount={projectile.m_spawnCount}"
                + $" randomSpawnOnHitCount={projectile.m_randomSpawnOnHitCount}"
                + $" randomSpawnSkipLava={projectile.m_randomSpawnSkipLava}"
                + $" showBreakMessage={projectile.m_showBreakMessage}"
                + $" staticHitOnly={projectile.m_staticHitOnly}"
                + $" groundHitOnly={projectile.m_groundHitOnly}"
                + $" spawnOffset={projectile.m_spawnOffset}"
                + $" copyProjectileRotation={projectile.m_copyProjectileRotation}"
                + $" spawnRandomRotation={projectile.m_spawnRandomRotation}"
                + $" spawnFacingRotation={projectile.m_spawnFacingRotation}"
                + $" spawnProjectileNewVelocity={projectile.m_spawnProjectileNewVelocity}"
                + $" spawnProjectileMinVel={projectile.m_spawnProjectileMinVel}"
                + $" spawnProjectileMaxVel={projectile.m_spawnProjectileMaxVel}"
                + $" spawnProjectileRandomDir={projectile.m_spawnProjectileRandomDir}"
                + $" spawnProjectileHemisphereDir={projectile.m_spawnProjectileHemisphereDir}"
                + $" projectilesInheritHitData={projectile.m_projectilesInheritHitData}"
                + $" onlySpawnedProjectilesDealDamage={projectile.m_onlySpawnedProjectilesDealDamage}"
                + $" divideDamageBetweenProjectiles={projectile.m_divideDamageBetweenProjectiles}"
                + $" rotateVisual={projectile.m_rotateVisual}"
                + $" rotateVisualY={projectile.m_rotateVisualY}"
                + $" rotateVisualZ={projectile.m_rotateVisualZ}"
                + $" visual={projectile.m_visual?.name ?? "<null>"}"
                + $" canChangeVisuals={projectile.m_canChangeVisuals}"
                + $" skill={projectile.m_skill}"
                + $" raiseSkillAmount={projectile.m_raiseSkillAmount}");

            SecondaryAttacksPlugin.ModLogger.LogInfo(
                "[RuntimeDump] "
                + weaponPrefabName
                + " payloadHierarchy="
                + DescribeHierarchy(payloadPrefab));
        }
    }


    private static string DescribeComponents(GameObject? gameObject)
    {
        if (gameObject == null)
        {
            return "<null>";
        }

        Component[] components = gameObject.GetComponents<Component>();
        if (components.Length == 0)
        {
            return "<none>";
        }

        return string.Join(", ", components.Select(component => component == null ? "<missing>" : component.GetType().FullName ?? component.GetType().Name));
    }

    private static string DescribeHierarchy(GameObject? gameObject)
    {
        if (gameObject == null)
        {
            return "<null>";
        }

        List<string> nodes = new();
        CollectHierarchyDescriptions(gameObject.transform, string.Empty, nodes);
        return nodes.Count == 0 ? "<none>" : string.Join(" | ", nodes);
    }

    private static void CollectHierarchyDescriptions(Transform node, string parentPath, List<string> nodes)
    {
        string path = string.IsNullOrEmpty(parentPath) ? node.name : parentPath + "/" + node.name;
        Component[] components = node.GetComponents<Component>();
        string componentList = string.Join(", ", components
            .Where(component => component != null && component is not Transform)
            .Select(component => component.GetType().FullName ?? component.GetType().Name));

        nodes.Add(string.IsNullOrEmpty(componentList) ? path : $"{path}[{componentList}]");

        for (int childIndex = 0; childIndex < node.childCount; childIndex++)
        {
            CollectHierarchyDescriptions(node.GetChild(childIndex), path, nodes);
        }
    }

}

internal static class SecondaryAttackPerformanceLog
{
    internal const bool Enabled = false;

    internal static Stopwatch? Start()
    {
        return null;
    }

    [Conditional("SECONDARY_ATTACKS_PERF_LOGGING")]
    internal static void Stop(Stopwatch? stopwatch, string scope, string details)
    {
    }

    [Conditional("SECONDARY_ATTACKS_PERF_LOGGING")]
    internal static void Stop(Stopwatch? stopwatch, string scope, Func<string> details)
    {
    }
}
