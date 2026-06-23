using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class SecondaryAttackNamedEffectSystem
{
    private const float EffectLifetime = 10f;
    private static readonly Dictionary<string, GameObject> PrefabCache = new();

    internal static bool Create(string? prefabName, Vector3 position, Quaternion rotation, string context)
    {
        if (string.IsNullOrWhiteSpace(prefabName))
        {
            return false;
        }

        string trimmedPrefabName = prefabName!.Trim();
        if (!TryResolvePrefab(trimmedPrefabName, out GameObject? resolvedPrefab))
        {
            if (SecondaryAttackManager.TryMarkCompatibilityWarningReported($"named_effect_missing_{context}_{trimmedPrefabName}"))
            {
                SecondaryAttacksPlugin.ModLogger.LogWarning($"Configured effect prefab '{trimmedPrefabName}' was not found for {context}.");
            }

            return false;
        }

        GameObject prefab = resolvedPrefab!;
        GameObject instance = Object.Instantiate(prefab, position, rotation);
        Object.Destroy(instance, EffectLifetime);
        return true;
    }

    private static bool TryResolvePrefab(string prefabName, out GameObject? prefab)
    {
        if (PrefabCache.TryGetValue(prefabName, out prefab))
        {
            if (prefab != null)
            {
                return true;
            }

            PrefabCache.Remove(prefabName);
        }

        prefab = ZNetScene.instance?.GetPrefab(prefabName);
        if (prefab == null)
        {
            return false;
        }

        PrefabCache[prefabName] = prefab;
        return true;
    }

    internal static Quaternion RotationFromNormal(Vector3 normal)
    {
        return normal.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(normal.normalized)
            : Quaternion.identity;
    }
}
