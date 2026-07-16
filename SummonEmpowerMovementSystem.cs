using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

[HarmonyPatch]
internal static class CharacterSummonEmpowerMovementPatch
{
    private static readonly IReadOnlyDictionary<string, HashSet<string>> ExpectedFieldsByMethod =
        new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["UpdateWalking"] = new(StringComparer.Ordinal)
            {
                nameof(Character.m_walkSpeed),
                nameof(Character.m_speed),
                nameof(Character.m_runSpeed),
                nameof(Character.m_acceleration),
                nameof(Character.m_turnSpeed),
                nameof(Character.m_runTurnSpeed),
            },
            ["UpdateFlying"] = new(StringComparer.Ordinal)
            {
                nameof(Character.m_flySlowSpeed),
                nameof(Character.m_flyFastSpeed),
                nameof(Character.m_acceleration),
                nameof(Character.m_flyTurnSpeed),
            },
            ["UpdateSwimming"] = new(StringComparer.Ordinal)
            {
                nameof(Character.m_swimSpeed),
                nameof(Character.m_swimAcceleration),
                nameof(Character.m_swimTurnSpeed),
            },
        };

    private static readonly MethodInfo ApplyMovementFactorMethod =
        AccessTools.Method(typeof(CharacterSummonEmpowerMovementPatch), nameof(ApplyMovementFactor))!;

    private static IReadOnlyList<MethodBase>? _targetMethods;

    [ThreadStatic]
    private static float _activeMovementFactor;

    private static bool Prepare()
    {
        return GetTargetMethods().Count > 0;
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        return GetTargetMethods();
    }

    private static IReadOnlyList<MethodBase> GetTargetMethods()
    {
        if (_targetMethods != null)
        {
            return _targetMethods;
        }

        List<MethodBase> targets = new();
        foreach (string methodName in ExpectedFieldsByMethod.Keys)
        {
            MethodInfo? method = FindTargetMethod(methodName);
            if (method != null)
            {
                targets.Add(method);
                continue;
            }

            SecondaryAttackWarningLog.WarnOnce(
                $"summon-empower-movement-target-{methodName}",
                $"Summon Empower could not patch Character.{methodName}(float); " +
                "that movement mode will not receive its speed bonus.");
        }

        if (targets.Count == 0)
        {
            SecondaryAttackWarningLog.WarnOnce(
                "summon-empower-movement-no-targets",
                "Summon Empower could not find Valheim's character movement methods; movement bonuses are disabled for this game version.");
        }

        _targetMethods = targets;
        return _targetMethods;
    }

    private static MethodInfo? FindTargetMethod(string methodName)
    {
        return AccessTools.DeclaredMethod(typeof(Character), methodName, new[] { typeof(float) });
    }

    [HarmonyPriority(Priority.First)]
    private static void Prefix(Character __instance, out float __state)
    {
        __state = _activeMovementFactor;
        _activeMovementFactor = 1f;

        if (__instance == null ||
            __instance.IsPlayer() ||
            !StaffRuntimeSystem.TryGetSummonEmpower(__instance, out float moveSpeedFactor, out _, out _) ||
            Mathf.Approximately(moveSpeedFactor, 1f))
        {
            return;
        }

        _activeMovementFactor = moveSpeedFactor;
    }

    [HarmonyPriority(Priority.Last)]
    private static Exception? Finalizer(Exception? __exception, float __state)
    {
        _activeMovementFactor = __state;
        return __exception;
    }

    [HarmonyPriority(Priority.Last)]
    private static IEnumerable<CodeInstruction> Transpiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase __originalMethod)
    {
        if (!ExpectedFieldsByMethod.TryGetValue(__originalMethod.Name, out HashSet<string> expectedFields))
        {
            return instructions;
        }

        List<CodeInstruction> original = instructions.ToList();
        List<CodeInstruction> rewritten = new();
        HashSet<string> patchedFields = new(StringComparer.Ordinal);
        foreach (CodeInstruction instruction in original)
        {
            rewritten.Add(instruction);
            if (instruction.opcode != OpCodes.Ldfld ||
                instruction.operand is not FieldInfo field ||
                field.DeclaringType != typeof(Character) ||
                field.FieldType != typeof(float) ||
                !expectedFields.Contains(field.Name))
            {
                continue;
            }

            // The field read leaves the current vanilla/CLLC value on the stack.
            // Multiplying it here keeps the temporary buff as an isolated overlay.
            rewritten.Add(new CodeInstruction(OpCodes.Call, ApplyMovementFactorMethod));
            patchedFields.Add(field.Name);
        }

        string[] missingFields = expectedFields
            .Except(patchedFields)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        foreach (string missingField in missingFields)
        {
            SecondaryAttackWarningLog.WarnOnce(
                $"summon-empower-movement-{__originalMethod.Name}-{missingField}",
                $"Summon Empower could not patch Character.{__originalMethod.Name}'s {missingField} read; " +
                "that movement mode's Empower bonus has been disabled for this game version.");
        }

        return missingFields.Length == 0 ? rewritten : original;
    }

    private static float ApplyMovementFactor(float value)
    {
        return _activeMovementFactor > 0f ? value * _activeMovementFactor : value;
    }
}
