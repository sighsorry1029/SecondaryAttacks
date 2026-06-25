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
        state.RuntimeContext = SecondaryAttackRuntimeFacade.BeginProjectileHitContext(projectile, collider, hitPoint, water, normal);
        return true;
    }

    internal static void ProjectileOnHitPostfix(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal,
        ProjectileOnHitState state)
    {
        DirectWeaponHitContextSystem.End(state.DirectHitContext);
        if (state.RuntimeContext)
        {
            MeleeProjectileHitCascadeSystem.TryTrigger(projectile, collider, hitPoint, water, normal);
        }

        SecondaryAttackRuntimeFacade.EndProjectileHitContext(state.RuntimeContext);
        ProjectileRuntimeSystem.EndScatterRicochetDamageScale(state.ScatterRicochetDamageScope);
        MeleeProjectileHitCascadeSystem.DestroySpearRainFollowupAfterHit(projectile, collider, hitPoint, water, normal);
    }

    internal static void PlayerUpdatePostfix(Player player, bool primaryAttackHold, bool secondaryAttackHold, bool secondaryAttackPressed, ref bool blocking)
    {
        if (player == Player.m_localPlayer)
        {
            SecondaryAttackFacade.TryApplyPendingConfig();
            SecondaryAttackAdminAccessSystem.Update();
            MeleeBoomerangProjectileSystem.UpdateDeferredReturnAutoEquips(player);
            SecondaryAttackRuntimeFacade.TryUpdateSecondaryProjectileHoldRepeat(player, secondaryAttackHold);
            MeleePresetCooldownSystem.UpdateActiveCooldowns(player);
            RangedSecondaryCooldownSystem.UpdateActiveCooldowns(player);
            SneakAmbushChargeSystem.Update(player);
            SecondaryCooldownHudSystem.Update(player);
            BowSecondaryKeyHintSystem.RefreshKeyHintUi();
            SpinningSweepSystem.UpdateInput(player, secondaryAttackHold, primaryAttackHold);
            HarvestSweepSystem.UpdateInput(player, secondaryAttackHold, primaryAttackHold);
            StickyDetonatorSystem.UpdateInput(player, ref blocking);
        }
    }

    internal static void PlayerUpdatePlacementGhostPostfix(Player player)
    {
    }

    internal static bool PlayerTryPlacePiecePrefix(Player player, Piece piece, ref bool result)
    {
        return true;
    }

    internal static void PlayerTryPlacePiecePostfix(Player player, Piece piece, bool result)
    {
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
            CopiedThrowProjectileVisualSystem.EndBurst(state);
            return false;
        }

        return true;
    }

    internal static void AttackFireProjectileBurstPostfix(CopiedThrowProjectileVisualSystem.BurstScope state)
    {
        CopiedThrowProjectileVisualSystem.EndBurst(state);
    }
}
