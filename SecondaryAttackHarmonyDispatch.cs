using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackHarmonyDispatch
{
    internal struct ProjectileOnHitState
    {
        internal bool RuntimeContext;
        internal DirectWeaponHitContextSystem.Scope DirectHitContext;
        internal ProjectileRuntimeSystem.ScatterRicochetDamageScope ScatterRicochetDamageScope;
    }

    internal static bool ProjectileOnHitPrefix(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal,
        out ProjectileOnHitState state)
    {
        state = default;
        if (StickyDetonatorSystem.TryHandleProjectileHit(projectile, collider, hitPoint, water, normal))
        {
            return false;
        }

        if (MeleeBoomerangProjectileSystem.TryHandleProjectileHit(projectile, collider, hitPoint, water, normal))
        {
            return false;
        }

        if (ProjectileRuntimeSystem.TryHandleProjectilePresetHit(projectile, collider, hitPoint, water, normal))
        {
            return false;
        }

        if (MeleeProjectileHitCascadeSystem.ShouldIgnoreOnProjectileHitSourceHit(projectile, collider))
        {
            return false;
        }

        SecondaryAttackAdrenalineSystem.TryGrantAttackUseAdrenalineOnProjectileHit(projectile, collider);
        state.ScatterRicochetDamageScope = ProjectileRuntimeSystem.BeginScatterRicochetDamageScale(projectile, collider, water, normal);
        state.DirectHitContext = DirectWeaponHitContextSystem.BeginProjectileHit(projectile);
        state.RuntimeContext = SecondaryAttackRuntimeFacade.BeginProjectileHitContext(projectile, collider);
        return true;
    }

    internal static void ProjectileOnHitPostfix(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal,
        ref ProjectileOnHitState state)
    {
        DirectWeaponHitContextSystem.End(ref state.DirectHitContext);
        try
        {
            if (state.RuntimeContext)
            {
                MeleeProjectileHitCascadeSystem.TryTrigger(projectile, collider, hitPoint, water, normal);
            }
        }
        finally
        {
            CleanupProjectileOnHitState(ref state);
        }

        MeleeProjectileHitCascadeSystem.DestroySpearRainFollowupAfterHit(projectile);
    }

    internal static void ProjectileOnHitFinalizer(ref ProjectileOnHitState state)
    {
        DirectWeaponHitContextSystem.End(ref state.DirectHitContext);
        CleanupProjectileOnHitState(ref state);
    }

    private static void CleanupProjectileOnHitState(ref ProjectileOnHitState state)
    {
        SecondaryAttackRuntimeFacade.EndProjectileHitContext(state.RuntimeContext);
        state.RuntimeContext = false;
        ProjectileRuntimeSystem.EndScatterRicochetDamageScale(state.ScatterRicochetDamageScope);
        state.ScatterRicochetDamageScope = default;
    }

    internal static void PlayerUpdatePostfix(Player player, bool primaryAttackHold, bool secondaryAttackHold, ref bool blocking)
    {
        if (player == Player.m_localPlayer)
        {
            SecondaryAttackFacade.TryApplyPendingConfig();
            MeleeBoomerangProjectileSystem.UpdateDeferredReturnAutoEquips(player);
            SecondaryAttackRuntimeFacade.TryUpdateSecondaryProjectileHoldRepeat(player, secondaryAttackHold);
            SecondaryCooldownGroupSystem.UpdateActiveCooldowns(player);
            SneakAmbushChargeSystem.Update(player);
            SecondaryCooldownHudSystem.Update(player);
            SecondaryAttackKeyHintSystem.RefreshKeyHintUi();
            SpinningSweepSystem.UpdateInput(player, secondaryAttackHold, primaryAttackHold);
            HarvestSweepSystem.UpdateInput(player, secondaryAttackHold, primaryAttackHold);
            StickyDetonatorSystem.UpdateInput(player, ref blocking);
        }
    }

    internal static bool AttackFireProjectileBurstPrefix(
        Attack attack,
        out CopiedThrowProjectileVisualSystem.BurstScope state)
    {
        state = CopiedThrowProjectileVisualSystem.BeginBurst(attack);
        if (SecondaryAttackRuntimeFacade.TryHandleCustomProjectileBurst(attack))
        {
            return false;
        }

        if (state.Active && !SecondaryAttackStartAttackDispatch.TryConsumeProjectilePresetCooldownAtBurst(attack))
        {
            CopiedThrowProjectileVisualSystem.EndBurst(ref state);
            return false;
        }

        return true;
    }

    internal static void AttackFireProjectileBurstPostfix(ref CopiedThrowProjectileVisualSystem.BurstScope state)
    {
        CopiedThrowProjectileVisualSystem.EndBurst(ref state);
    }
}
