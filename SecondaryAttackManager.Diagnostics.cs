using System;
using System.Collections;
using System.Collections.Generic;
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

}
