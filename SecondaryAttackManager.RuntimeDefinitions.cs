using UnityEngine;

namespace SecondaryAttacks;

internal static partial class SecondaryAttackManager
{
    public static void EnsureRuntimeWeaponDefinitionApplied(ItemDrop.ItemData? weapon)
    {
        SecondaryAttackRuntimeWeaponRebind.Apply(weapon, SecondaryAttackFacade.CurrentAppliedWorldSnapshot);
    }

    internal static void RefreshLocalPlayerRuntimeWeaponDefinitions()
    {
        SecondaryAttackRuntimeWeaponRebind.RefreshLocalPlayerInventory(SecondaryAttackFacade.CurrentAppliedWorldSnapshot);
    }
}
