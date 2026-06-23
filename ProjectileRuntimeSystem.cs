using UnityEngine;

namespace SecondaryAttacks;

internal static partial class ProjectileRuntimeSystem
{
    internal static string GetPresetName(SecondaryAttackPreset preset)
    {
        return preset switch
        {
            SecondaryAttackPreset.Barrage => "barrage",
            SecondaryAttackPreset.Volley => "volley",
            SecondaryAttackPreset.Piercing => "piercing",
            SecondaryAttackPreset.Scatter => "scatter",
            SecondaryAttackPreset.Spiral => "spiral",
            SecondaryAttackPreset.Sentinel => "sentinel",
            SecondaryAttackPreset.Meteor => "meteor",
            SecondaryAttackPreset.Burst => "burst",
            SecondaryAttackPreset.StickyDetonator => "stickyDetonator",
            SecondaryAttackPreset.OverchargedBomb => "overchargedBomb",
            _ => preset.ToString()
        };
    }

    internal static bool TryValidateConfiguredPayload(string weaponPrefabName, Attack primaryAttack, SecondaryAttackPreset preset, bool usesAmmo, out string reason)
    {
        reason = "";
        GameObject payloadPrefab = primaryAttack.m_attackProjectile;
        if (payloadPrefab == null)
        {
            if (usesAmmo)
            {
                return true;
            }

            reason = $"primary attack is marked projectile-based but has no projectile prefab for preset '{GetPresetName(preset)}'.";
            return false;
        }

        if (usesAmmo)
        {
            return true;
        }

        return TryValidatePayloadPrefab(weaponPrefabName, payloadPrefab, preset, out reason);
    }

    internal static bool TryHandleBurstPreset(Attack attack, SecondaryAttackDefinition definition, SecondaryAttackPreset preset)
    {
        switch (preset)
        {
            case SecondaryAttackPreset.Barrage:
                FireBarrage(attack, definition);
                return true;
            case SecondaryAttackPreset.Volley:
                return FireVolley(attack, definition);
            case SecondaryAttackPreset.Piercing:
                FirePiercingShot(attack, definition);
                return true;
            case SecondaryAttackPreset.Scatter:
                FireScatterRicochet(attack, definition);
                return true;
            case SecondaryAttackPreset.Spiral:
                FireSpiralBurst(attack, definition);
                return true;
            case SecondaryAttackPreset.Sentinel:
                FireSentinel(attack, definition);
                return true;
            case SecondaryAttackPreset.Meteor:
                FireMeteor(attack, definition);
                return true;
            case SecondaryAttackPreset.Burst:
                FireBurstFire(attack, definition);
                return true;
            case SecondaryAttackPreset.StickyDetonator:
                FireStickyDetonator(attack, definition);
                return true;
            case SecondaryAttackPreset.OverchargedBomb:
                return FireOverchargedBomb(attack, definition);
            default:
                return false;
        }
    }

    internal static bool TryHandleProjectilePresetHit(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal)
    {
        return TryHandleScatterRicochetProjectileHit(projectile, collider, hitPoint, water, normal) ||
               TryHandlePiercingShotProjectileHit(projectile, collider, hitPoint, water, normal);
    }

    private static bool UsesProjectilePayloadPreset(SecondaryAttackPreset preset)
    {
        return preset is SecondaryAttackPreset.Barrage
            or SecondaryAttackPreset.Volley
            or SecondaryAttackPreset.Piercing
            or SecondaryAttackPreset.Scatter
            or SecondaryAttackPreset.Spiral
            or SecondaryAttackPreset.Sentinel
            or SecondaryAttackPreset.Meteor
            or SecondaryAttackPreset.Burst
            or SecondaryAttackPreset.StickyDetonator
            or SecondaryAttackPreset.OverchargedBomb;
    }

    private static bool TryValidatePayloadPrefab(string weaponPrefabName, GameObject payloadPrefab, SecondaryAttackPreset preset, out string reason)
    {
        reason = "";
        string payloadName = payloadPrefab.name;
        Projectile? projectilePrefab = payloadPrefab.GetComponent<Projectile>();
        Aoe? aoePrefab = payloadPrefab.GetComponent<Aoe>();
        IProjectile? projectileInterface = payloadPrefab.GetComponent<IProjectile>();

        if (UsesProjectilePayloadPreset(preset))
        {
            if (aoePrefab != null)
            {
                reason = $"preset '{GetPresetName(preset)}' requires a Projectile payload, but '{payloadName}' is an Aoe prefab.";
                return false;
            }

            if (projectilePrefab == null)
            {
                reason = projectileInterface != null
                    ? $"preset '{GetPresetName(preset)}' requires a Projectile payload, but '{payloadName}' implements IProjectile without a Projectile component."
                    : $"preset '{GetPresetName(preset)}' requires a Projectile payload, but '{payloadName}' does not implement Projectile/IProjectile.";
                return false;
            }
        }

        if (HasUnregisteredZNetPrefab(payloadPrefab, out string registrationReason))
        {
            if (UsesProjectilePayloadPreset(preset) && projectilePrefab != null)
            {
                return true;
            }

            reason = $"payload '{payloadName}' is unsafe for preset '{GetPresetName(preset)}': {registrationReason}";
            return false;
        }

        return true;
    }

    private static bool HasUnregisteredZNetPrefab(GameObject payloadPrefab, out string reason)
    {
        reason = "";
        if (payloadPrefab.GetComponent<ZNetView>() == null || ZNetScene.instance == null)
        {
            return false;
        }

        if (ZNetScene.instance.GetPrefab(payloadPrefab.name) != null)
        {
            return false;
        }

        reason = "prefab has a ZNetView but is not registered in ZNetScene";
        return true;
    }
}
