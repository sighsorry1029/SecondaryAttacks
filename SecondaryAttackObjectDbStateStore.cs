using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackObjectDbStateStore
{
    private static readonly ConditionalWeakTable<ObjectDB, Dictionary<string, OriginalWeaponState>> Snapshots = new();

    public static void Capture(ObjectDB objectDb)
    {
        Dictionary<string, OriginalWeaponState> snapshots = Snapshots.GetValue(
            objectDb,
            _ => new Dictionary<string, OriginalWeaponState>(StringComparer.OrdinalIgnoreCase));

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

            ItemDrop.ItemData.SharedData sharedData = itemDrop.m_itemData.m_shared;
            if (!snapshots.ContainsKey(itemPrefab.name))
            {
                snapshots[itemPrefab.name] = new OriginalWeaponState(
                    SecondaryAttackManager.CloneAttack(sharedData.m_secondaryAttack),
                    sharedData.m_equipStatusEffect,
                    sharedData.m_buildBlockCharges,
                    sharedData.m_maxBlockCharges,
                    sharedData.m_blockChargeDecayTime,
                    sharedData.m_blockChargeBlockingDecayMult);
            }
        }
    }

    public static void Restore(ObjectDB objectDb)
    {
        if (!Snapshots.TryGetValue(objectDb, out Dictionary<string, OriginalWeaponState>? snapshots))
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

            if (snapshots.TryGetValue(itemPrefab.name, out OriginalWeaponState snapshot))
            {
                ItemDrop.ItemData.SharedData sharedData = itemDrop.m_itemData.m_shared;
                sharedData.m_secondaryAttack = SecondaryAttackManager.CloneAttack(snapshot.OriginalSecondaryAttack);
                sharedData.m_equipStatusEffect = snapshot.OriginalEquipStatusEffect;
                sharedData.m_buildBlockCharges = snapshot.OriginalBuildBlockCharges;
                sharedData.m_maxBlockCharges = snapshot.OriginalMaxBlockCharges;
                sharedData.m_blockChargeDecayTime = snapshot.OriginalBlockChargeDecayTime;
                sharedData.m_blockChargeBlockingDecayMult = snapshot.OriginalBlockChargeBlockingDecayFactor;
            }
        }
    }

    public static bool TryGetOriginalSecondaryAttack(ObjectDB objectDb, string prefabName, out Attack? attack)
    {
        attack = null;
        if (objectDb == null ||
            string.IsNullOrWhiteSpace(prefabName) ||
            !Snapshots.TryGetValue(objectDb, out Dictionary<string, OriginalWeaponState>? snapshots) ||
            !snapshots.TryGetValue(prefabName.Trim(), out OriginalWeaponState snapshot))
        {
            return false;
        }

        attack = SecondaryAttackManager.CloneAttack(snapshot.OriginalSecondaryAttack);
        return true;
    }

    private sealed class OriginalWeaponState
    {
        public OriginalWeaponState(
            Attack originalSecondaryAttack,
            StatusEffect? originalEquipStatusEffect,
            bool originalBuildBlockCharges,
            int originalMaxBlockCharges,
            float originalBlockChargeDecayTime,
            float originalBlockChargeBlockingDecayFactor)
        {
            OriginalSecondaryAttack = originalSecondaryAttack;
            OriginalEquipStatusEffect = originalEquipStatusEffect;
            OriginalBuildBlockCharges = originalBuildBlockCharges;
            OriginalMaxBlockCharges = originalMaxBlockCharges;
            OriginalBlockChargeDecayTime = originalBlockChargeDecayTime;
            OriginalBlockChargeBlockingDecayFactor = originalBlockChargeBlockingDecayFactor;
        }

        public Attack OriginalSecondaryAttack { get; }

        public StatusEffect? OriginalEquipStatusEffect { get; }

        public bool OriginalBuildBlockCharges { get; }

        public int OriginalMaxBlockCharges { get; }

        public float OriginalBlockChargeDecayTime { get; }

        public float OriginalBlockChargeBlockingDecayFactor { get; }
    }
}
