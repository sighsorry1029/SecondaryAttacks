namespace SecondaryAttacks;

public static class WarfareTweaksBridge
{
    public static bool IsGeneratedDamageActive => SecondaryAttackRuntimeContext.IsGeneratedDamageActive;

    public static bool ShouldSuppressProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return false;
        }

        return SecondaryAttackRuntimeContext.TryGetProjectileAttackAttribution(
                   projectile,
                   out ProjectileAttackAttribution? attribution) &&
               attribution != null &&
               (attribution.SecondaryAttack || attribution.DisableCurrentAttackFallback);
    }
}
