using System;
using System.Linq;

namespace SecondaryAttacks;

internal enum BloodMagicAutomaticWeaponFamily
{
    None,
    Summon,
    Shield
}

internal enum RangedAutomaticWeaponFamily
{
    None,
    Bomb,
    FireballStaff,
    RapidStaff,
    ReloadStaff,
    Crossbow,
    Bow
}

internal enum MeleeAutomaticWeaponFamily
{
    None,
    Knives,
    TwoHandedSword,
    OneHandedSword,
    OneHandedClub,
    Unarmed,
    Sledge,
    Polearm,
    Farming,
    Pickaxe,
    Spear,
    Battleaxe,
    OneHandedAxe
}

internal static class SecondaryAttackWeaponFamilyResolver
{
    internal const string BloodSummonCooldownGroup = "family:blood:summon";
    internal const string BloodShieldCooldownGroup = "family:blood:shield";

    internal static string ResolveAutomaticCooldownGroup(
        string prefabName,
        ItemDrop itemDrop)
    {
        BloodMagicAutomaticWeaponFamily bloodMagicFamily = ResolveBloodMagicFamily(prefabName, itemDrop);
        if (bloodMagicFamily != BloodMagicAutomaticWeaponFamily.None)
        {
            return bloodMagicFamily switch
            {
                BloodMagicAutomaticWeaponFamily.Summon => BloodSummonCooldownGroup,
                BloodMagicAutomaticWeaponFamily.Shield => BloodShieldCooldownGroup,
                _ => ""
            };
        }

        RangedAutomaticWeaponFamily rangedFamily = ResolveRangedFamily(itemDrop);
        if (rangedFamily != RangedAutomaticWeaponFamily.None)
        {
            return rangedFamily switch
            {
                RangedAutomaticWeaponFamily.Bomb => "family:ranged:bomb",
                RangedAutomaticWeaponFamily.FireballStaff => "family:ranged:staff-fireball",
                RangedAutomaticWeaponFamily.RapidStaff => "family:ranged:staff-rapid",
                RangedAutomaticWeaponFamily.ReloadStaff => "family:ranged:staff-reload",
                RangedAutomaticWeaponFamily.Crossbow => "family:ranged:crossbow",
                RangedAutomaticWeaponFamily.Bow => "family:ranged:bow",
                _ => ""
            };
        }

        return ResolveMeleeFamily(itemDrop) switch
        {
            MeleeAutomaticWeaponFamily.Knives => "family:melee:knives",
            MeleeAutomaticWeaponFamily.TwoHandedSword => "family:melee:sword-2h",
            MeleeAutomaticWeaponFamily.OneHandedSword => "family:melee:sword-1h",
            MeleeAutomaticWeaponFamily.OneHandedClub => "family:melee:club-1h",
            MeleeAutomaticWeaponFamily.Unarmed => "family:melee:unarmed",
            MeleeAutomaticWeaponFamily.Sledge => "family:melee:sledge",
            MeleeAutomaticWeaponFamily.Polearm => "family:melee:polearm",
            MeleeAutomaticWeaponFamily.Farming => "family:melee:farming",
            MeleeAutomaticWeaponFamily.Pickaxe => "family:melee:pickaxe",
            MeleeAutomaticWeaponFamily.Spear => "family:melee:spear",
            MeleeAutomaticWeaponFamily.Battleaxe => "family:melee:battleaxe",
            MeleeAutomaticWeaponFamily.OneHandedAxe => "family:melee:axe-1h",
            _ => ""
        };
    }

    internal static BloodMagicAutomaticWeaponFamily ResolveBloodMagicFamily(
        string prefabName,
        ItemDrop itemDrop)
    {
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        Attack? primaryAttack = sharedData?.m_attack;
        if (sharedData == null ||
            primaryAttack == null ||
            sharedData.m_skillType != Skills.SkillType.BloodMagic)
        {
            return BloodMagicAutomaticWeaponFamily.None;
        }

        if (IsDefaultShieldConvertWeapon(prefabName))
        {
            return BloodMagicAutomaticWeaponFamily.Shield;
        }

        return IsDefaultSummonEmpowerWeapon(primaryAttack)
            ? BloodMagicAutomaticWeaponFamily.Summon
            : BloodMagicAutomaticWeaponFamily.None;
    }

    internal static RangedAutomaticWeaponFamily ResolveRangedFamily(ItemDrop itemDrop)
    {
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        Attack? primaryAttack = sharedData?.m_attack;
        if (sharedData == null ||
            primaryAttack == null ||
            IsAmmoItemType(sharedData.m_itemType) ||
            string.IsNullOrWhiteSpace(primaryAttack.m_attackAnimation))
        {
            return RangedAutomaticWeaponFamily.None;
        }

        if (IsBombProjectileAttack(primaryAttack))
        {
            return RangedAutomaticWeaponFamily.Bomb;
        }

        if (sharedData.m_skillType == Skills.SkillType.ElementalMagic)
        {
            string animation = primaryAttack.m_attackAnimation ?? "";
            if (string.Equals(animation, "staff_fireball", StringComparison.OrdinalIgnoreCase))
            {
                return RangedAutomaticWeaponFamily.FireballStaff;
            }

            if (string.Equals(animation, "staff_rapidfire", StringComparison.OrdinalIgnoreCase))
            {
                return RangedAutomaticWeaponFamily.RapidStaff;
            }

            if (string.Equals(animation, "staff_lightningshot", StringComparison.OrdinalIgnoreCase))
            {
                return RangedAutomaticWeaponFamily.ReloadStaff;
            }
        }

        if (sharedData.m_skillType == Skills.SkillType.Crossbows ||
            (primaryAttack.m_requiresReload &&
             primaryAttack.m_attackType == Attack.AttackType.Projectile &&
             !string.IsNullOrWhiteSpace(sharedData.m_ammoType)))
        {
            return RangedAutomaticWeaponFamily.Crossbow;
        }

        return sharedData.m_itemType == ItemDrop.ItemData.ItemType.Bow ||
               sharedData.m_skillType == Skills.SkillType.Bows
            ? RangedAutomaticWeaponFamily.Bow
            : RangedAutomaticWeaponFamily.None;
    }

    internal static MeleeAutomaticWeaponFamily ResolveMeleeFamily(ItemDrop itemDrop)
    {
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        if (IsDefaultSpearRainWeapon(sharedData))
        {
            return MeleeAutomaticWeaponFamily.Spear;
        }

        if (IsDefaultImpactBurstWeapon(itemDrop))
        {
            return MeleeAutomaticWeaponFamily.Battleaxe;
        }

        if (IsDefaultBoomerangWeapon(sharedData))
        {
            return MeleeAutomaticWeaponFamily.OneHandedAxe;
        }

        if (IsDefaultSpinningSweepWeapon(sharedData))
        {
            return MeleeAutomaticWeaponFamily.Polearm;
        }

        if (IsDefaultCleavingThrustWeapon(sharedData))
        {
            return MeleeAutomaticWeaponFamily.TwoHandedSword;
        }

        if (IsDefaultRiftTrailWeapon(sharedData))
        {
            return MeleeAutomaticWeaponFamily.OneHandedSword;
        }

        if (IsDefaultLaunchSlamWeapon(sharedData))
        {
            return MeleeAutomaticWeaponFamily.OneHandedClub;
        }

        if (IsDefaultKnockbackChainWeapon(sharedData))
        {
            return MeleeAutomaticWeaponFamily.Unarmed;
        }

        if (IsDefaultAftershockWeapon(itemDrop))
        {
            return MeleeAutomaticWeaponFamily.Sledge;
        }

        if (IsDefaultFractureLineWeapon(sharedData))
        {
            return MeleeAutomaticWeaponFamily.Pickaxe;
        }

        if (IsDefaultSneakAmbushWeapon(sharedData))
        {
            return MeleeAutomaticWeaponFamily.Knives;
        }

        return IsDefaultHarvestSweepWeapon(sharedData)
            ? MeleeAutomaticWeaponFamily.Farming
            : MeleeAutomaticWeaponFamily.None;
    }

    internal static bool IsDefaultShieldConvertWeapon(string prefabName)
    {
        return string.Equals(prefabName, "StaffShield", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDefaultSummonEmpowerWeapon(Attack primaryAttack)
    {
        if (primaryAttack.m_attackProjectile == null)
        {
            return false;
        }

        SpawnAbility? spawnAbility = primaryAttack.m_attackProjectile.GetComponent<SpawnAbility>();
        return spawnAbility?.m_spawnPrefab != null &&
               spawnAbility.m_spawnPrefab.Any(spawnPrefab => spawnPrefab != null);
    }

    internal static bool IsBombProjectileAttack(Attack primaryAttack)
    {
        return primaryAttack.m_attackProjectile != null &&
               primaryAttack.m_attackProjectile.GetComponent<Projectile>() != null &&
               (primaryAttack.m_attackType == Attack.AttackType.Projectile ||
                primaryAttack.m_attackProjectile.GetComponent<IProjectile>() != null) &&
               string.Equals(primaryAttack.m_attackAnimation, "throw_bomb", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDefaultSneakAmbushWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Knives;
    }

    internal static bool IsDefaultCleavingThrustWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Swords &&
               sharedData.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon;
    }

    internal static bool IsDefaultRiftTrailWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Swords &&
               sharedData.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon &&
               sharedData.m_secondaryAttack != null &&
               string.Equals(sharedData.m_secondaryAttack.m_attackAnimation, "sword_secondary", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDefaultLaunchSlamWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Clubs &&
               sharedData.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon;
    }

    internal static bool IsDefaultKnockbackChainWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Unarmed &&
               sharedData.m_secondaryAttack != null &&
               string.Equals(sharedData.m_secondaryAttack.m_attackAnimation, "unarmed_kick", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDefaultAftershockWeapon(ItemDrop itemDrop)
    {
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        string prefabName = itemDrop.name ?? "";
        return sharedData?.m_skillType == Skills.SkillType.Clubs &&
               sharedData.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon &&
               prefabName.Contains("Sledge", StringComparison.OrdinalIgnoreCase) &&
               (sharedData.m_attack?.m_attackType == Attack.AttackType.Area ||
                sharedData.m_secondaryAttack?.m_attackType == Attack.AttackType.Area);
    }

    internal static bool IsDefaultSpinningSweepWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Polearms &&
               sharedData.m_secondaryAttack != null &&
               string.Equals(sharedData.m_secondaryAttack.m_attackAnimation, "atgeir_secondary", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDefaultHarvestSweepWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Farming;
    }

    internal static bool IsDefaultFractureLineWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Pickaxes;
    }

    internal static bool IsDefaultSpearRainWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        Attack? secondaryAttack = sharedData?.m_secondaryAttack;
        return sharedData?.m_skillType == Skills.SkillType.Spears &&
               secondaryAttack != null &&
               !string.IsNullOrWhiteSpace(secondaryAttack.m_attackAnimation);
    }

    internal static bool IsDefaultImpactBurstWeapon(ItemDrop itemDrop)
    {
        ItemDrop.ItemData.SharedData? sharedData = itemDrop.m_itemData?.m_shared;
        if (sharedData?.m_skillType != Skills.SkillType.Axes ||
            sharedData.m_itemType != ItemDrop.ItemData.ItemType.TwoHandedWeapon)
        {
            return false;
        }

        string prefabName = itemDrop.name ?? "";
        if (prefabName.Contains("DualAxe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string secondaryAnimation = sharedData.m_secondaryAttack?.m_attackAnimation ?? "";
        if (!string.Equals(secondaryAnimation, "battleaxe_secondary", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (prefabName.Contains("Battleaxe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string primaryAnimation = sharedData.m_attack?.m_attackAnimation ?? "";
        return (primaryAnimation.Contains("battleaxe", StringComparison.OrdinalIgnoreCase) ||
                secondaryAnimation.Contains("battleaxe", StringComparison.OrdinalIgnoreCase)) &&
               !primaryAnimation.Contains("dualaxe", StringComparison.OrdinalIgnoreCase) &&
               !secondaryAnimation.Contains("dualaxe", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsDefaultBoomerangWeapon(ItemDrop.ItemData.SharedData? sharedData)
    {
        return sharedData?.m_skillType == Skills.SkillType.Axes &&
               sharedData.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon;
    }

    private static bool IsAmmoItemType(ItemDrop.ItemData.ItemType itemType)
    {
        return itemType is ItemDrop.ItemData.ItemType.Ammo or ItemDrop.ItemData.ItemType.AmmoNonEquipable;
    }
}
