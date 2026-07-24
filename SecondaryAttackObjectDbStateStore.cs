using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackObjectDbStateStore
{
    private static readonly ConditionalWeakTable<ObjectDB, Dictionary<string, Attack>> Snapshots = new();

    public static void CaptureSecondaryAttack(
        ObjectDB objectDb,
        string prefabName,
        Attack? secondaryAttack)
    {
        if (objectDb == null || string.IsNullOrWhiteSpace(prefabName))
        {
            return;
        }

        Dictionary<string, Attack> snapshots = Snapshots.GetValue(
            objectDb,
            _ => new Dictionary<string, Attack>(StringComparer.OrdinalIgnoreCase));
        string snapshotKey = prefabName.Trim();
        if (!snapshots.ContainsKey(snapshotKey))
        {
            snapshots[snapshotKey] = SecondaryAttackManager.CloneAttack(secondaryAttack);
        }
    }

    public static void Restore(ObjectDB objectDb)
    {
        if (!Snapshots.TryGetValue(objectDb, out Dictionary<string, Attack>? snapshots))
        {
            return;
        }

        foreach (GameObject itemPrefab in objectDb.m_items)
        {
            if (itemPrefab == null)
            {
                continue;
            }

            ItemDrop itemDrop = itemPrefab.GetComponent<ItemDrop>();
            if (itemDrop == null)
            {
                continue;
            }

            if (snapshots.TryGetValue(itemPrefab.name, out Attack? originalSecondaryAttack))
            {
                ItemDrop.ItemData.SharedData sharedData = itemDrop.m_itemData.m_shared;
                sharedData.m_secondaryAttack = SecondaryAttackManager.CloneAttack(originalSecondaryAttack);
            }
        }

        snapshots.Clear();
    }

    public static bool TryGetOriginalSecondaryAttack(ObjectDB objectDb, string prefabName, out Attack? attack)
    {
        attack = null;
        if (objectDb == null ||
            string.IsNullOrWhiteSpace(prefabName) ||
            !Snapshots.TryGetValue(objectDb, out Dictionary<string, Attack>? snapshots) ||
            !snapshots.TryGetValue(prefabName.Trim(), out Attack? originalSecondaryAttack))
        {
            return false;
        }

        attack = SecondaryAttackManager.CloneAttack(originalSecondaryAttack);
        return true;
    }
}
