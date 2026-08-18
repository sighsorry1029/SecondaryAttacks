using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackRuntimeContext
{
    private static readonly ConditionalWeakTable<Attack, ActiveSecondaryAttack> ActiveAttacks = new();
    private static readonly ConditionalWeakTable<Projectile, ProjectileAttackAttribution> ProjectileAttackAttributions = new();
    private static readonly List<ProjectileHitContext> ActiveProjectileHitContexts = new();
    private static int _generatedDamageDepth;

    internal static bool IsGeneratedDamageActive => _generatedDamageDepth > 0;

    internal static GeneratedDamageScope BeginGeneratedDamage()
    {
        _generatedDamageDepth++;
        return new GeneratedDamageScope(active: true);
    }

    private static void EndGeneratedDamage()
    {
        if (_generatedDamageDepth > 0)
        {
            _generatedDamageDepth--;
        }
    }

    internal static void SetActiveAttack(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        ActiveAttacks.Remove(attack);
        ActiveAttacks.Add(attack, activeAttack);
    }

    internal static bool TryGetActiveAttack(Attack attack, out ActiveSecondaryAttack? activeAttack)
    {
        return ActiveAttacks.TryGetValue(attack, out activeAttack);
    }

    internal static void SetProjectileAttackAttribution(Projectile projectile, ProjectileAttackAttribution attribution)
    {
        ProjectileAttackAttributions.Remove(projectile);
        ProjectileAttackAttributions.Add(projectile, attribution);
    }

    internal static bool TryGetProjectileAttackAttribution(Projectile projectile, out ProjectileAttackAttribution? attribution)
    {
        return ProjectileAttackAttributions.TryGetValue(projectile, out attribution);
    }

    internal static void PushProjectileHitContext(ProjectileHitContext context)
    {
        ActiveProjectileHitContexts.Add(context);
    }

    internal static void PopProjectileHitContext()
    {
        if (ActiveProjectileHitContexts.Count == 0)
        {
            return;
        }

        ActiveProjectileHitContexts.RemoveAt(ActiveProjectileHitContexts.Count - 1);
    }

    internal static bool TryPeekProjectileHitContext(out ProjectileHitContext context)
    {
        if (ActiveProjectileHitContexts.Count == 0)
        {
            context = default;
            return false;
        }

        context = ActiveProjectileHitContexts[ActiveProjectileHitContexts.Count - 1];
        return true;
    }

    internal struct GeneratedDamageScope : System.IDisposable
    {
        private bool _active;

        internal GeneratedDamageScope(bool active)
        {
            _active = active;
        }

        public void Dispose()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            EndGeneratedDamage();
        }
    }
}

internal sealed class ActiveSecondaryAttack
{
    public ActiveSecondaryAttack(SecondaryAttackDefinition definition)
    {
        Definition = definition;
    }

    public SecondaryAttackDefinition Definition { get; }

    public bool Triggered { get; set; }

    public bool ProjectileTriggered { get; set; }

    public bool BurstTriggerHandled { get; set; }

    public bool BurstRuntimeStarted { get; set; }

    public float NextHoldRepeatTime { get; set; }
}

internal sealed class ProjectileAttackAttribution
{
    public ProjectileAttackAttribution(
        string weaponPrefabName,
        bool secondaryAttack,
        SecondaryAttackDefinition? definition,
        bool disableCurrentAttackFallback)
    {
        WeaponPrefabName = weaponPrefabName;
        SecondaryAttack = secondaryAttack;
        Definition = definition;
        DisableCurrentAttackFallback = disableCurrentAttackFallback;
    }

    public string WeaponPrefabName { get; }

    public bool SecondaryAttack { get; }

    public SecondaryAttackDefinition? Definition { get; }

    public bool DisableCurrentAttackFallback { get; }
}

internal readonly struct ProjectileHitContext
{
    public ProjectileHitContext(
        Projectile projectile,
        ProjectileAttackAttribution? attribution)
    {
        Projectile = projectile;
        Attribution = attribution;
    }

    public Projectile Projectile { get; }

    public ProjectileAttackAttribution? Attribution { get; }
}
