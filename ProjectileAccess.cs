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
    private static readonly FieldInfo? ChangedVisualField = AccessTools.Field(typeof(Projectile), "m_changedVisual");
    private static readonly MethodInfo? UpdateVisualMethod = AccessTools.Method(typeof(Projectile), "UpdateVisual", System.Type.EmptyTypes);
    private static readonly System.Type? EquipmentVisualType = typeof(ItemDrop).Assembly.GetType("IEquipmentVisual");
    private static readonly MethodInfo? EquipmentVisualSetup = EquipmentVisualType?.GetMethod("Setup", new[] { typeof(int) });

    internal static void SetEquipmentVisualVariant(UnityEngine.GameObject visual, int variant)
    {
        if (EquipmentVisualType == null || EquipmentVisualSetup == null) return;
        UnityEngine.Component? component = visual.GetComponentInChildren(EquipmentVisualType);
        if (component != null) EquipmentVisualSetup.Invoke(component, new object[] { variant });
    }

    internal static bool CanResetVisual => ChangedVisualField?.FieldType == typeof(bool);

    internal static void ResetVisual(Projectile projectile)
    {
        ChangedVisualField?.SetValue(projectile, false);
    }

    internal static bool HasChangedVisual(Projectile projectile) => ChangedVisualField?.GetValue(projectile) is true;

    internal static void RefreshVisual(Projectile projectile) => UpdateVisualMethod?.Invoke(projectile, null);

    // Capture the resolved equipment appearance before the thrown item is unequipped.
    // This is presentation data; it never replaces the weapon or its recoverable ItemData.
    internal static long CaptureEquippedAppearance(Humanoid owner, ItemDrop.ItemData weapon)
    {
        if (owner == null || weapon?.m_dropPrefab == null || !IsMeleeAppearanceItem(weapon)) return 0L;
        ZNetView? view = owner.GetComponent<ZNetView>();
        if (view == null || !view.IsValid() || !view.IsOwner()) return 0L;
        ZDO zdo = view.GetZDO();
        if (zdo == null) return 0L;
        int hash;
        int variant;
        int originalVariant;
        if (ReferenceEquals(owner.LeftItem, weapon))
        {
            hash = zdo.GetInt(ZDOVars.s_leftItem);
            variant = zdo.GetInt(ZDOVars.s_leftItemVariant);
            originalVariant = weapon.m_variant;
        }
        else if (ReferenceEquals(owner.RightItem, weapon))
        {
            hash = zdo.GetInt(ZDOVars.s_rightItem);
            variant = 0;
            originalVariant = 0;
        }
        else return 0L;

        // An empty/hidden hand is not an invisible projectile request.
        if (hash == 0 || (hash == weapon.m_dropPrefab.name.GetStableHashCode() && variant == originalVariant)) return 0L;
        return PackAppearance(hash, variant);
    }

    internal static bool IsMeleeAppearanceItem(ItemDrop.ItemData weapon)
    {
        ItemDrop.ItemData.SharedData? shared = weapon?.m_shared;
        return shared != null &&
               shared.m_skillType != Skills.SkillType.ElementalMagic &&
               shared.m_skillType != Skills.SkillType.BloodMagic &&
               shared.m_itemType is ItemDrop.ItemData.ItemType.OneHandedWeapon or
                   ItemDrop.ItemData.ItemType.TwoHandedWeapon or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
    }

    internal static long PackAppearance(int prefabHash, int variant) =>
        unchecked((long)(((ulong)(uint)prefabHash << 32) | (uint)System.Math.Max(0, variant)));

    internal static int AppearancePrefabHash(long appearance) => unchecked((int)(appearance >> 32));

    internal static int AppearanceVariant(long appearance) => unchecked((int)(uint)appearance);

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
