using HarmonyLib;

namespace SecondaryAttacks;

internal static class DirectWeaponHitContextSystem
{
    private static int _directHitDepth;
    private static int _characterDamageDepth;

    internal static bool IsDirectWeaponHitActive => _directHitDepth > 0;

    internal static bool ShouldCountWeaponEffectHit =>
        _directHitDepth > 0 &&
        _characterDamageDepth == 1 &&
        !WeaponEffectManager.IsApplyingGeneratedEffectDamage &&
        !LaunchSlamSystem.IsApplyingLandingDamage &&
        !KnockbackChainSystem.IsApplyingChainDamage &&
        !MeleeProjectileHitCascadeSystem.IsApplyingImpactBurstDamage;

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

    private static bool IsSecondaryAttackProjectile(Projectile projectile)
    {
        return SecondaryAttackRuntimeContext.TryGetProjectileAttackAttribution(
                   projectile,
                   out ProjectileAttackAttribution? attribution) &&
               attribution is { SecondaryAttack: true } or { DisableCurrentAttackFallback: true };
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
    private static void Postfix(DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(__state);
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
    private static void Postfix(DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(__state);
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
    private static void Postfix(DirectWeaponHitContextSystem.Scope __state)
    {
        DirectWeaponHitContextSystem.End(__state);
    }
}
