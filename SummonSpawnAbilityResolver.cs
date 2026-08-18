using UnityEngine;

namespace SecondaryAttacks;

internal static class SummonSpawnAbilityResolver
{
    internal static bool TryResolve(
        ZNetScene? scene,
        string? itemPrefabName,
        out ItemDrop? itemDrop,
        out SpawnAbility? spawnAbility,
        out string targetName)
    {
        itemDrop = ResolveItemDrop(scene, itemPrefabName);
        spawnAbility = null;
        targetName = "";
        return itemDrop?.m_itemData?.m_shared != null &&
               TryResolve(itemDrop, out spawnAbility, out targetName);
    }

    internal static bool TryResolve(
        ItemDrop? itemDrop,
        out SpawnAbility? spawnAbility,
        out string targetName)
    {
        return TryResolve(itemDrop?.m_itemData?.m_shared, out spawnAbility, out targetName);
    }

    internal static bool TryResolve(
        ItemDrop.ItemData.SharedData? shared,
        out SpawnAbility? spawnAbility,
        out string targetName)
    {
        spawnAbility = null;
        targetName = "";
        return shared != null &&
               (TryResolveFromAttack(shared.m_attack, out spawnAbility, out targetName) ||
                TryResolveFromAttack(shared.m_secondaryAttack, out spawnAbility, out targetName));
    }

    private static bool TryResolveFromAttack(
        Attack? attack,
        out SpawnAbility? spawnAbility,
        out string targetName)
    {
        spawnAbility = null;
        targetName = "";
        if (attack == null)
        {
            return false;
        }

        GameObject? attackProjectile = attack.m_attackProjectile;
        IProjectile? projectile = attackProjectile != null
            ? attackProjectile.GetComponent<IProjectile>()
            : null;
        if (projectile is SpawnAbility directSpawnAbility)
        {
            spawnAbility = directSpawnAbility;
            targetName = attackProjectile!.name;
            return true;
        }

        if (projectile is Projectile baseProjectile &&
            TryResolveSpawnOnHit(baseProjectile.m_spawnOnHit, out spawnAbility, out targetName))
        {
            return true;
        }

        return attack.m_spawnOnHitChance > 0f &&
               TryResolveSpawnOnHit(attack.m_spawnOnHit, out spawnAbility, out targetName);
    }

    private static bool TryResolveSpawnOnHit(
        GameObject? prefab,
        out SpawnAbility? spawnAbility,
        out string targetName)
    {
        spawnAbility = prefab != null
            ? prefab.GetComponentInChildren<IProjectile>() as SpawnAbility
            : null;
        if (spawnAbility != null)
        {
            targetName = prefab!.name;
            return true;
        }

        targetName = prefab != null ? prefab.name : "";
        return false;
    }

    private static ItemDrop? ResolveItemDrop(ZNetScene? scene, string? itemPrefabName)
    {
        if (string.IsNullOrWhiteSpace(itemPrefabName))
        {
            return null;
        }

        GameObject? itemPrefab = ObjectDB.instance != null
            ? ObjectDB.instance.GetItemPrefab(itemPrefabName)
            : null;
        if (itemPrefab == null)
        {
            itemPrefab = scene != null ? scene.GetPrefab(itemPrefabName) : null;
        }

        return itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
    }
}
