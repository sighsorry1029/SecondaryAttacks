using System.Reflection;
using HarmonyLib;

namespace SecondaryAttacks;

internal static class ProjectileAccess
{
    private static readonly FieldInfo? WeaponField = AccessTools.Field(typeof(Projectile), "m_weapon");
    private static readonly FieldInfo? OwnerField = AccessTools.Field(typeof(Projectile), "m_owner");
    private static readonly FieldInfo? OriginalHitDataField = AccessTools.Field(typeof(Projectile), "m_originalHitData");
    private static readonly FieldInfo? VelocityField = AccessTools.Field(typeof(Projectile), "m_vel");
    private static readonly FieldInfo? DidHitField = AccessTools.Field(typeof(Projectile), "m_didHit");

    internal static ItemDrop.ItemData? GetWeapon(Projectile projectile)
    {
        return WeaponField?.GetValue(projectile) as ItemDrop.ItemData;
    }

    internal static Character? GetOwner(Projectile projectile)
    {
        return OwnerField?.GetValue(projectile) as Character;
    }

    internal static HitData? GetOriginalHitData(Projectile projectile)
    {
        return OriginalHitDataField?.GetValue(projectile) as HitData;
    }

    internal static UnityEngine.Vector3 GetVelocity(Projectile projectile)
    {
        return VelocityField?.GetValue(projectile) is UnityEngine.Vector3 velocity ? velocity : UnityEngine.Vector3.zero;
    }

    internal static void SetVelocity(Projectile projectile, UnityEngine.Vector3 velocity)
    {
        VelocityField?.SetValue(projectile, velocity);
    }

    internal static void SetDidHit(Projectile projectile, bool didHit)
    {
        DidHitField?.SetValue(projectile, didHit);
    }

    internal static void SuppressItemDrops(Projectile projectile)
    {
        projectile.m_respawnItemOnHit = false;
        projectile.m_spawnItem = null;
        projectile.m_spawnOnTtl = false;
    }
}
