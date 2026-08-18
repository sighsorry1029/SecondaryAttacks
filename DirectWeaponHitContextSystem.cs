using HarmonyLib;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class DirectWeaponHitContextSystem
{
    private static int _directHitDepth;
    private static int _characterDamageDepth;

    private static bool ShouldHandleDirectWeaponHit =>
        _directHitDepth > 0 &&
        _characterDamageDepth == 1 &&
        !SecondaryAttackRuntimeContext.IsGeneratedDamageActive;

    internal static void ApplyDirectWeaponHitEffects(Character target, ref HitData hit)
    {
        if (!ShouldHandleDirectWeaponHit ||
            (Object)(object)hit.GetAttacker() != (Object)(object)Player.m_localPlayer ||
            !TryGetEffectiveAttackContext(
                out Player localPlayer,
                out bool secondaryAttack,
                out SecondaryAttackDefinition? definition))
        {
            return;
        }

        if (definition?.SneakAmbush != null)
        {
            SneakAmbushSystem.TryTriggerForSecondaryHit(localPlayer, target, secondaryAttack, definition);
        }

        if (!KnockbackChainSystem.TryApplyForSecondaryHit(localPlayer, target, secondaryAttack, definition, ref hit))
        {
            LaunchSlamSystem.TryApplyForSecondaryHit(localPlayer, target, secondaryAttack, definition, ref hit);
        }
    }

    internal static Scope BeginAttackHit(Attack attack)
    {
        if (attack?.m_character != Player.m_localPlayer)
        {
            return default;
        }

        _directHitDepth++;
        return new Scope(ScopeKind.DirectHit);
    }

    internal static Scope BeginProjectileHit(Projectile projectile)
    {
        if (projectile == null ||
            ProjectileAccess.GetOwner(projectile) != Player.m_localPlayer ||
            IsSecondaryAttackProjectile(projectile))
        {
            return default;
        }

        _directHitDepth++;
        return new Scope(ScopeKind.DirectHit);
    }

    internal static Scope BeginCharacterDamage()
    {
        _characterDamageDepth++;
        return new Scope(ScopeKind.CharacterDamage);
    }

    internal static void End(Scope scope)
    {
        switch (scope.Kind)
        {
            case ScopeKind.DirectHit when _directHitDepth > 0:
                _directHitDepth--;
                break;
            case ScopeKind.CharacterDamage when _characterDamageDepth > 0:
                _characterDamageDepth--;
                break;
        }
    }

    internal static void End(ref Scope scope)
    {
        End(scope);
        scope = default;
    }

    private static bool IsSecondaryAttackProjectile(Projectile projectile)
    {
        return SecondaryAttackRuntimeContext.TryGetProjectileAttackAttribution(
                   projectile,
                   out ProjectileAttackAttribution? attribution) &&
               attribution is { SecondaryAttack: true } or { DisableCurrentAttackFallback: true };
    }

    private static bool TryGetEffectiveAttackContext(
        out Player localPlayer,
        out bool secondaryAttack,
        out SecondaryAttackDefinition? definition)
    {
        localPlayer = Player.m_localPlayer;
        secondaryAttack = false;
        definition = null;
        if (localPlayer == null)
        {
            return false;
        }

        if (SecondaryAttackRuntimeFacade.TryGetProjectileHitAttackContext(
                out _,
                out secondaryAttack,
                out definition,
                out bool disableCurrentAttackFallback))
        {
            return true;
        }

        if (disableCurrentAttackFallback)
        {
            return false;
        }

        Attack? currentAttack = ((Humanoid)localPlayer).m_currentAttack;
        if (currentAttack?.m_weapon?.m_dropPrefab == null)
        {
            return false;
        }

        secondaryAttack = ((Humanoid)localPlayer).m_currentAttackIsSecondary;
        SecondaryAttackRuntimeFacade.TryGetDefinition(currentAttack.m_weapon, out definition!);
        return true;
    }

    internal readonly struct Scope
    {
        internal Scope(ScopeKind kind)
        {
            Kind = kind;
        }

        internal ScopeKind Kind { get; }
    }

    internal enum ScopeKind
    {
        None,
        DirectHit,
        CharacterDamage
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoMeleeAttack))]
internal static class AttackDoMeleeAttackDirectWeaponHitPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Attack __instance, out DirectWeaponHitContextSystem.Scope __state)
    {
        __state = DirectWeaponHitContextSystem.BeginAttackHit(__instance);
    }

    [HarmonyPriority(Priority.First)]
    private static void Postfix(ref DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(ref __state);
    }

    private static void Finalizer(ref DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(ref __state);
    }
}

[HarmonyPatch(typeof(Attack), nameof(Attack.DoAreaAttack))]
internal static class AttackDoAreaAttackDirectWeaponHitPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(Attack __instance, out DirectWeaponHitContextSystem.Scope __state)
    {
        __state = DirectWeaponHitContextSystem.BeginAttackHit(__instance);
    }

    [HarmonyPriority(Priority.First)]
    private static void Postfix(ref DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(ref __state);
    }

    private static void Finalizer(ref DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(ref __state);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
internal static class CharacterDamageDirectWeaponHitDepthPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(out DirectWeaponHitContextSystem.Scope __state)
    {
        __state = DirectWeaponHitContextSystem.BeginCharacterDamage();
    }

    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ref DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(ref __state);
    }

    private static void Finalizer(ref DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(ref __state);
    }
}

[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
internal static class CharacterDamageDirectWeaponHitEffectsPatch
{
    [HarmonyPriority(Priority.Normal)]
    private static void Prefix(Character __instance, ref HitData hit)
    {
        DirectWeaponHitContextSystem.ApplyDirectWeaponHitEffects(__instance, ref hit);
    }
}
