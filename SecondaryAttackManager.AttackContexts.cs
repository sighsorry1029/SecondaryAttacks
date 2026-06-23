using System;
using UnityEngine;

namespace SecondaryAttacks;

internal static partial class SecondaryAttackManager
{
    private sealed class BowSecondaryState
    {
        public string PrefabName { get; set; } = "";

        public bool PendingSecondary { get; set; }
    }

    private sealed class RuntimeWeaponDefinitionState
    {
        public int AppliedWorldRevision { get; set; } = -1;
    }

    internal sealed class ReloadSecondaryResourceCostContext
    {
        public static readonly ReloadSecondaryResourceCostContext Empty = new(0f, 0f);

        public ReloadSecondaryResourceCostContext(float staminaDelta, float eitrDelta)
        {
            StaminaDelta = staminaDelta;
            EitrDelta = eitrDelta;
        }

        public float StaminaDelta { get; }

        public float EitrDelta { get; }

        public bool HasDelta => Math.Abs(StaminaDelta) > 0.001f || Math.Abs(EitrDelta) > 0.001f;
    }

    public static bool BeginProjectileHitContext(Projectile projectile, Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
    {
        return SecondaryAttackRuntimeFacade.BeginProjectileHitContext(projectile, collider, hitPoint, water, normal);
    }

    public static void EndProjectileHitContext(bool active)
    {
        SecondaryAttackRuntimeFacade.EndProjectileHitContext(active);
    }

    public static bool TryGetProjectileHitAttackContext(
        out string weaponPrefabName,
        out bool secondaryAttack,
        out SecondaryAttackDefinition? definition,
        out bool disableCurrentAttackFallback)
    {
        return SecondaryAttackRuntimeFacade.TryGetProjectileHitAttackContext(
            out weaponPrefabName,
            out secondaryAttack,
            out definition,
            out disableCurrentAttackFallback);
    }

}
