using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackRuntimeWeaponRebind
{
    public static void Apply(ItemDrop.ItemData? weapon, SecondaryAttackAppliedWorldSnapshot appliedWorldSnapshot)
    {
        if (weapon?.m_dropPrefab == null)
        {
            return;
        }

        int currentApplyRevision = appliedWorldSnapshot.ApplyRevision;
        if (SecondaryAttackManager.GetRuntimeWeaponAppliedWorldRevision(weapon) == currentApplyRevision)
        {
            return;
        }

        ItemDrop? prefabItemDrop = weapon.m_dropPrefab.GetComponent<ItemDrop>();
        if (prefabItemDrop == null)
        {
            return;
        }

        if (appliedWorldSnapshot.DefinitionsByPrefabName.TryGetValue(weapon.m_dropPrefab.name, out SecondaryAttackDefinition definition) &&
            definition.AppliesSecondaryOverride &&
            ObjectDB.instance != null)
        {
            Attack sourceAttack = SecondaryAttackManager.ResolveSourceAttack(ObjectDB.instance, prefabItemDrop, definition);
            Attack configuredSecondaryAttack = SecondaryAttackManager.BuildSecondaryAttack(sourceAttack, definition);
            SecondaryAttackManager.NormalizeCopiedProjectileAim(configuredSecondaryAttack, definition);
            weapon.m_shared.m_secondaryAttack = ProjectilePresetCooldownFallback.UsesDynamicOriginalSecondary(definition)
                ? SecondaryAttackManager.CloneAttack(definition.CooldownFallbackSecondaryAttack ?? prefabItemDrop.m_itemData?.m_shared?.m_secondaryAttack)
                : configuredSecondaryAttack;
        }
        else
        {
            weapon.m_shared.m_secondaryAttack = SecondaryAttackManager.CloneAttack(prefabItemDrop.m_itemData?.m_shared?.m_secondaryAttack);
        }

        SecondaryAttackManager.SetRuntimeWeaponAppliedWorldRevision(weapon, currentApplyRevision);
    }

    public static void RefreshLocalPlayerInventory(SecondaryAttackAppliedWorldSnapshot appliedWorldSnapshot)
    {
        Player? localPlayer = Player.m_localPlayer;
        Inventory? inventory = localPlayer?.GetInventory();
        if (inventory == null)
        {
            return;
        }

        foreach (ItemDrop.ItemData item in inventory.GetAllItems())
        {
            Apply(item, appliedWorldSnapshot);
        }
    }
}
