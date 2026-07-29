using UnityEngine;

namespace SecondaryAttacks;

internal static partial class ProjectileRuntimeSystem
{
    internal static string GetPresetName(SecondaryAttackPreset preset)
    {
        return SecondaryAttackPresetCatalog.GetKey(preset);
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

    internal static bool TryValidateBurstPresetPayload(
        Attack attack,
        SecondaryAttackDefinition definition,
        SecondaryAttackPreset preset,
        ItemDrop.ItemData? ammoItem)
    {
        if (!UsesProjectilePayloadPreset(preset))
        {
            ReportCompatibilityIssue(
                attack,
                definition,
                $"unsupported projectile preset '{GetPresetName(preset)}'.");
            return false;
        }

        GameObject? payloadPrefab = ResolveEffectiveProjectilePayload(attack, ammoItem);
        if (payloadPrefab == null)
        {
            ReportCompatibilityIssue(
                attack,
                definition,
                $"no effective projectile payload was found for preset '{GetPresetName(preset)}'.");
            return false;
        }

        string weaponPrefabName =
            attack.m_weapon?.m_dropPrefab != null
                ? attack.m_weapon.m_dropPrefab.name
                : definition.PrefabName;
        if (TryValidatePayloadPrefab(
                weaponPrefabName,
                payloadPrefab,
                preset,
                out string reason))
        {
            return true;
        }

        ReportCompatibilityIssue(attack, definition, reason);
        return false;
    }

    private static GameObject? ResolveEffectiveProjectilePayload(
        Attack attack,
        ItemDrop.ItemData? ammoItem)
    {
        GameObject? ammoProjectile =
            ammoItem?.m_shared?.m_attack?.m_attackProjectile;
        return ammoProjectile != null
            ? ammoProjectile
            : attack.m_attackProjectile;
    }

    internal static bool TryHandleBurstPreset(Attack attack, SecondaryAttackDefinition definition, SecondaryAttackPreset preset)
    {
        switch (preset)
        {
            case SecondaryAttackPreset.Barrage:
                return FireBarrage(attack, definition);
            case SecondaryAttackPreset.Volley:
                return FireVolley(attack, definition);
            case SecondaryAttackPreset.Piercing:
                return FirePiercingShot(attack, definition);
            case SecondaryAttackPreset.Scatter:
                return FireScatterRicochet(attack, definition);
            case SecondaryAttackPreset.Spiral:
                return FireSpiralBurst(attack, definition);
            case SecondaryAttackPreset.Sentinel:
                return FireSentinel(attack, definition);
            case SecondaryAttackPreset.Meteor:
                return FireMeteor(attack, definition);
            case SecondaryAttackPreset.Burst:
                return FireBurstFire(attack, definition);
            case SecondaryAttackPreset.StickyDetonator:
                return FireStickyDetonator(attack, definition);
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
        return TryHandleScatterRicochetProjectileHit(projectile, collider, hitPoint, normal) ||
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
