using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackProjectileToolTierSystem
{
    internal static void ApplyCurrentProjectileHitToolTierIfNeeded(HitData? hit)
    {
        if (hit == null ||
            !SecondaryAttackRuntimeContext.TryPeekProjectileHitContext(out ProjectileHitContext? context) ||
            context?.Projectile == null ||
            !ShouldApplyToProjectile(context.Attribution))
        {
            return;
        }

        ApplyToHitData(hit, context.Projectile, ProjectileAccess.GetWeapon(context.Projectile));
    }

    internal static void ApplyToHitData(HitData? hit, Projectile? projectile, ItemDrop.ItemData? weapon)
    {
        if (hit == null)
        {
            return;
        }

        short toolTier = ResolveToolTier(projectile, weapon);
        byte itemWorldLevel = ResolveItemWorldLevel(projectile, weapon);
        if (toolTier > hit.m_toolTier)
        {
            hit.m_toolTier = toolTier;
        }

        if (itemWorldLevel > hit.m_itemWorldLevel)
        {
            hit.m_itemWorldLevel = itemWorldLevel;
        }
    }

    private static bool ShouldApplyToProjectile(ProjectileAttackAttribution? attribution)
    {
        return attribution?.SecondaryAttack == true ||
               attribution?.Definition?.BehaviorType is SecondaryAttackBehaviorType.Projectile
                   or SecondaryAttackBehaviorType.CopiedSecondary;
    }

    private static short ResolveToolTier(Projectile? projectile, ItemDrop.ItemData? weapon)
    {
        short toolTier = 0;
        HitData? originalHitData = projectile != null ? ProjectileAccess.GetOriginalHitData(projectile) : null;
        if (originalHitData != null)
        {
            toolTier = originalHitData.m_toolTier;
        }

        ItemDrop.ItemData? projectileWeapon = projectile != null ? ProjectileAccess.GetWeapon(projectile) : null;
        if (projectileWeapon?.m_shared != null)
        {
            toolTier = (short)Mathf.Max(toolTier, projectileWeapon.m_shared.m_toolTier);
        }

        if (weapon?.m_shared != null)
        {
            toolTier = (short)Mathf.Max(toolTier, weapon.m_shared.m_toolTier);
        }

        return toolTier;
    }

    private static byte ResolveItemWorldLevel(Projectile? projectile, ItemDrop.ItemData? weapon)
    {
        byte itemWorldLevel = 0;
        HitData? originalHitData = projectile != null ? ProjectileAccess.GetOriginalHitData(projectile) : null;
        if (originalHitData != null)
        {
            itemWorldLevel = originalHitData.m_itemWorldLevel;
        }

        ItemDrop.ItemData? projectileWeapon = projectile != null ? ProjectileAccess.GetWeapon(projectile) : null;
        if (projectileWeapon != null)
        {
            itemWorldLevel = (byte)Mathf.Max(itemWorldLevel, projectileWeapon.m_worldLevel);
        }

        if (weapon != null)
        {
            itemWorldLevel = (byte)Mathf.Max(itemWorldLevel, weapon.m_worldLevel);
        }

        return itemWorldLevel;
    }
}
