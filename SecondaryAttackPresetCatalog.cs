using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecondaryAttacks;

internal enum SecondaryAttackPresetGroup
{
    Ranged,
    Melee,
    BloodMagic,
    SummonQuality
}

internal static class SecondaryAttackPresetCatalog
{
    internal static IReadOnlyList<SecondaryAttackPresetInfo> Entries { get; } =
    [
        new("barrage", "barrage", SecondaryAttackPresetGroup.Ranged, "BowHuntsman", "Barrage", "Fires multiple projectiles across a spread or a laterally spaced formation."),
        new("volley", "volley", SecondaryAttackPresetGroup.Ranged, "CrossbowArbalest", "Volley", "Launches multiple projectiles in arcs so they land around the aimed point."),
        new("piercing", "piercing", SecondaryAttackPresetGroup.Ranged, "StaffLightning", "Piercing", "Fires the weapon's full projectile pattern through multiple characters, with damage falling after each hit."),
        new("scatter", "scatter", SecondaryAttackPresetGroup.Ranged, "StaffClusterbomb", "Scatter", "Fires a projectile that splits into a cone of ricocheting projectiles when it first hits a non-character surface."),
        new("spiral", "spiral", SecondaryAttackPresetGroup.Ranged, "StaffIceShards", "Spiral", "Fires projectiles in a rotating spiral around the aim direction."),
        new("sentinel", "sentinel", SecondaryAttackPresetGroup.Ranged, "StaffFireball", "Sentinel", "Creates orbiting projectiles that seek nearby enemies and launch themselves at a target."),
        new("meteor", "meteor", SecondaryAttackPresetGroup.Ranged, "StaffFireball", "Meteor", "Calls down projectiles from above across an area around the aimed point."),
        new("burst", "burst", SecondaryAttackPresetGroup.Ranged, "CrossbowArbalest", "Burst", "Repeats the weapon's full primary projectile pattern through successive firing animations."),
        new("stickyDetonator", "sticky_detonator", SecondaryAttackPresetGroup.Ranged, "BombOoze", "Sticky Detonator", "Throws charges that stick where they hit. Use Block to detonate all active charges."),
        new("overchargedBomb", "overcharged_bomb", SecondaryAttackPresetGroup.Ranged, "BombBile", "Overcharged Bomb", "Can consume additional bombs to launch a stronger bomb with increased damage and blast radius."),

        new("sneakAmbush", "sneak_ambush", SecondaryAttackPresetGroup.Melee, "KnifeWood", "Sneak Ambush", "Builds preparation while sneaking, charging faster with higher Sneak skill. The next valid secondary hit releases smoke that clears nearby enemies' awareness; more preparation increases its radius, how long affected enemies cannot sense you, and the reduction to their backstab cooldown."),
        new("riftTrail", "rift_trail", SecondaryAttackPresetGroup.Melee, "SwordWood", "Rift Trail", "Leaves the sword's swing trail active, repeatedly damaging enemies within the swept path."),
        new("cleavingThrust", "cleaving_thrust", SecondaryAttackPresetGroup.Melee, "THSwordWood", "Cleaving Thrust", "Extends a greatsword thrust into a long cleave that strikes every valid target along its path."),
        new("launchSlam", "launch_slam", SecondaryAttackPresetGroup.Melee, "MaceWood", "Launch Slam", "Launches the secondary-hit target upward, then deals area damage when it lands."),
        new("aftershock", "aftershock", SecondaryAttackPresetGroup.Melee, "SledgeWood", "Aftershock", "Follows the initial area slam with weakening shockwaves that advance forward."),
        new("knockbackChain", "knockback_chain", SecondaryAttackPresetGroup.Melee, "FistBjornClaw", "Knockback Chain", "Greatly increases kick knockback. Collisions pass diminishing damage and force through nearby enemies."),
        new("boomerang", "boomerang", SecondaryAttackPresetGroup.Melee, "AxeBronze", "Boomerang", "Throws the weapon along a curved return path, hitting multiple targets before returning to the wielder."),
        new("impactBurst", "impact_burst", SecondaryAttackPresetGroup.Melee, "Battleaxe", "Impact Burst", "Throws the weapon and releases a damaging, forceful burst around its impact point."),
        new("spearRain", "spear_rain", SecondaryAttackPresetGroup.Melee, "SpearWood", "Spear Rain", "Calls down additional spears after the thrown spear hits a character, guiding them toward the struck target."),
        new("spinningSweep", "spinning_sweep", SecondaryAttackPresetGroup.Melee, "AtgeirWood", "Spinning Sweep", "Sustains a repeating polearm spin until cancelled, the weapon changes, or the next attack cost cannot be paid."),
        new("fractureLine", "fracture_line", SecondaryAttackPresetGroup.Melee, "PickaxeAntler", "Fracture Line", "Opens a crack along the ground that repeatedly damages enemies and destructibles standing over it."),
        new("harvestSweep", "harvest_sweep", SecondaryAttackPresetGroup.Melee, "Scythe", "Harvest Sweep", "Sustains a repeating scythe sweep that harvests nearby crops and pickables."),

        new("summonEmpower", "summon_empower", SecondaryAttackPresetGroup.BloodMagic, "StaffSkeleton", "Summon Empower", "Temporarily increases matching nearby summons' non-crouching movement, acceleration, and turning speeds and accelerates their attack animations and AI attack cadence."),
        new("shieldConvert", "shield_convert", SecondaryAttackPresetGroup.BloodMagic, "StaffShield", "Shield Convert", "Removes active shields from the wielder and nearby non-hostile targets, restoring health from their remaining shield strength."),

        new("countByQuality", "count_by_quality", SecondaryAttackPresetGroup.SummonQuality, "StaffRedTroll", "Count by Quality", "Weapon quality increases the maximum number of simultaneous summons while summon level stays fixed."),
        new("levelByQuality", "level_by_quality", SecondaryAttackPresetGroup.SummonQuality, "StaffSkeleton", "Level by Quality", "Weapon quality increases summon level while the maximum number of simultaneous summons stays fixed.")
    ];

    private static readonly Dictionary<string, SecondaryAttackPresetInfo> EntriesByKey = BuildEntriesByKey();

    internal static bool TryGet(string presetKey, out SecondaryAttackPresetInfo info)
    {
        return EntriesByKey.TryGetValue(presetKey ?? "", out info!);
    }

    internal static Sprite? ResolveIcon(string presetKey)
    {
        if (ObjectDB.instance == null || !TryGet(presetKey, out SecondaryAttackPresetInfo info))
        {
            return null;
        }

        ItemDrop? itemDrop = ObjectDB.instance.GetItemPrefab(info.IconPrefabName)?.GetComponent<ItemDrop>();
        return itemDrop?.m_itemData?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }

    private static Dictionary<string, SecondaryAttackPresetInfo> BuildEntriesByKey()
    {
        Dictionary<string, SecondaryAttackPresetInfo> entries = new(StringComparer.OrdinalIgnoreCase);
        foreach (SecondaryAttackPresetInfo entry in Entries)
        {
            entries[entry.Key] = entry;
        }

        return entries;
    }
}

internal sealed class SecondaryAttackPresetInfo
{
    internal SecondaryAttackPresetInfo(
        string key,
        string tokenKey,
        SecondaryAttackPresetGroup group,
        string iconPrefabName,
        string fallbackName,
        string fallbackDescription)
    {
        Key = key;
        Group = group;
        IconPrefabName = iconPrefabName;
        NameToken = $"$sa_compendium_preset_{tokenKey}_name";
        DescriptionToken = $"$sa_compendium_preset_{tokenKey}_description";
        FallbackName = fallbackName;
        FallbackDescription = fallbackDescription;
    }

    internal string Key { get; }

    internal SecondaryAttackPresetGroup Group { get; }

    internal string IconPrefabName { get; }

    internal string NameToken { get; }

    internal string DescriptionToken { get; }

    internal string FallbackName { get; }

    internal string FallbackDescription { get; }
}
