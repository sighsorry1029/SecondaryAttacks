using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackAdrenalineSystem
{
    private const string StaffLightningShotAnimation = "staff_lightningshot";

    private sealed class AttackAdrenalineState
    {
        internal readonly HashSet<string> GrantedKeys = new();
    }

    private sealed class AttackUseAdrenalineProjectileHitState
    {
        internal AttackUseAdrenalineProjectileHitState(float baseAdrenaline)
        {
            BaseAdrenaline = Mathf.Max(0f, baseAdrenaline);
        }

        internal float BaseAdrenaline { get; }

        internal bool Granted { get; set; }
    }

    private sealed class ProjectileUseAdrenalineProjectileHitState
    {
        internal ProjectileUseAdrenalineProjectileHitState(Attack attack, AttackUseAdrenalineProjectileHitState attackState)
        {
            Attack = attack;
            AttackState = attackState;
        }

        internal Attack Attack { get; }

        internal AttackUseAdrenalineProjectileHitState AttackState { get; }
    }

    private sealed class PendingSecondaryStartState
    {
        internal ItemDrop.ItemData? Weapon { get; set; }
    }

    private static readonly ConditionalWeakTable<Attack, AttackAdrenalineState> AttackStates = new();
    private static readonly ConditionalWeakTable<Attack, AttackUseAdrenalineProjectileHitState> AttackUseAdrenalineProjectileHitStates = new();
    private static readonly ConditionalWeakTable<Projectile, ProjectileUseAdrenalineProjectileHitState> ProjectileUseAdrenalineProjectileHitStates = new();
    private static readonly ConditionalWeakTable<Humanoid, PendingSecondaryStartState> PendingSecondaryStarts = new();

    [ThreadStatic] private static Attack? ScopedAttack;
    [ThreadStatic] private static float ScopedFactor;
    [ThreadStatic] private static string? ScopedKey;
    [ThreadStatic] private static bool ScopedOnce;

    internal static void BeginConfiguredSecondaryStart(Humanoid humanoid, ItemDrop.ItemData weapon)
    {
        if (humanoid == null || weapon == null)
        {
            return;
        }

        PendingSecondaryStartState state = PendingSecondaryStarts.GetValue(humanoid, _ => new PendingSecondaryStartState());
        state.Weapon = weapon;
    }

    internal static void EndConfiguredSecondaryStart(Humanoid humanoid)
    {
        if (humanoid != null)
        {
            PendingSecondaryStarts.Remove(humanoid);
        }
    }

    internal static bool ShouldSuppressAttackUseAdrenaline(Attack attack)
    {
        if (attack?.m_character is not Humanoid humanoid ||
            attack.m_weapon == null ||
            !PendingSecondaryStarts.TryGetValue(humanoid, out PendingSecondaryStartState? state))
        {
            return false;
        }

        return MatchesWeapon(state.Weapon, attack.m_weapon) &&
               SecondaryAttackRuntimeFacade.TryGetDefinition(attack.m_weapon, out SecondaryAttackDefinition definition) &&
               HasAdrenalineManagedSecondary(definition);
    }

    internal static bool TryBeginAttackUseAdrenalineProjectileHitConversion(Attack attack)
    {
        if (!ShouldConvertAttackUseAdrenalineToProjectileHit(attack))
        {
            return false;
        }

        AttackUseAdrenalineProjectileHitStates.Remove(attack);
        AttackUseAdrenalineProjectileHitStates.Add(attack, new AttackUseAdrenalineProjectileHitState(attack.m_attackUseAdrenaline));
        return true;
    }

    internal static void TryApplyAttackUseAdrenalineProjectileHitConversion(Projectile projectile, ItemDrop.ItemData item)
    {
        if (projectile == null ||
            item == null ||
            ProjectileAccess.GetOwner(projectile) is not Humanoid owner ||
            owner.m_currentAttack == null ||
            owner.m_currentAttackIsSecondary ||
            !MatchesWeapon(owner.m_currentAttack.m_weapon, item) ||
            !IsStaffLightningShotAttack(owner.m_currentAttack) ||
            !AttackUseAdrenalineProjectileHitStates.TryGetValue(owner.m_currentAttack, out AttackUseAdrenalineProjectileHitState? attackState))
        {
            return;
        }

        ProjectileUseAdrenalineProjectileHitStates.Remove(projectile);
        ProjectileUseAdrenalineProjectileHitStates.Add(projectile, new ProjectileUseAdrenalineProjectileHitState(owner.m_currentAttack, attackState));
        projectile.m_adrenaline = 0f;
    }

    internal static void TryGrantAttackUseAdrenalineOnProjectileHit(Projectile projectile, Collider collider)
    {
        if (projectile == null ||
            collider == null ||
            !ProjectileUseAdrenalineProjectileHitStates.TryGetValue(projectile, out ProjectileUseAdrenalineProjectileHitState? state) ||
            state.AttackState.Granted)
        {
            return;
        }

        Character? owner = ProjectileAccess.GetOwner(projectile);
        Character? target = ProjectileRuntimeSystem.GetHitCharacter(collider);
        if (owner == null ||
            target == null ||
            target.m_enemyAdrenalineMultiplier <= 0f ||
            !BaseAI.IsEnemy(owner, target))
        {
            return;
        }

        state.AttackState.Granted = true;
        using (BeginScope(
                   state.Attack,
                   1f,
                   "attackUseProjectileHit:" + StaffLightningShotAnimation,
                   once: false))
        {
            owner.AddAdrenaline(state.AttackState.BaseAdrenaline * target.m_enemyAdrenalineMultiplier);
        }
    }

    internal static void Reset(Attack attack)
    {
        if (attack != null)
        {
            AttackStates.Remove(attack);
            AttackUseAdrenalineProjectileHitStates.Remove(attack);
        }
    }

    internal static bool TryModify(Character character, ref float amount)
    {
        if (character == null || amount <= 0f)
        {
            return true;
        }

        Attack? attack = ScopedAttack;
        float factor = ScopedFactor;
        string key = ScopedKey ?? "";
        bool once = ScopedOnce;
        if (attack == null)
        {
            attack = (character as Humanoid)?.m_currentAttack;
            if (attack == null ||
                !SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) ||
                activeAttack == null)
            {
                return true;
            }

            bool projectileHit = IsSecondaryProjectileHitContext();
            if (projectileHit)
            {
                return true;
            }

            if (activeAttack.Definition.Behavior is ProjectileSecondaryBehavior && !projectileHit)
            {
                amount = 0f;
                return false;
            }

            factor = ResolveFactor(activeAttack);
            key = ResolveKey(activeAttack);
            once = true;
        }

        factor = Mathf.Max(0f, factor);
        if (factor <= 0f)
        {
            amount = 0f;
            return false;
        }

        if (once && !TryMarkGranted(attack, key))
        {
            amount = 0f;
            return false;
        }

        amount *= factor;
        return true;
    }

    internal static AdrenalineScope BeginScope(Attack attack, float factor, string key, bool once = true)
    {
        Attack? previousAttack = ScopedAttack;
        float previousFactor = ScopedFactor;
        string? previousKey = ScopedKey;
        bool previousOnce = ScopedOnce;
        ScopedAttack = attack;
        ScopedFactor = Mathf.Max(0f, factor);
        ScopedKey = key;
        ScopedOnce = once;
        return new AdrenalineScope(previousAttack, previousFactor, previousKey, previousOnce);
    }

    internal static bool TryGrantOnce(Attack attack, Character target, float factor, string key)
    {
        if (attack?.m_character == null ||
            target == null ||
            target.m_enemyAdrenalineMultiplier <= 0f ||
            attack.m_attackAdrenaline <= 0f)
        {
            return false;
        }

        using (BeginScope(attack, factor, key))
        {
            attack.m_character.AddAdrenaline(attack.m_attackAdrenaline * target.m_enemyAdrenalineMultiplier);
        }

        return true;
    }

    internal static bool TryGrantOnce(Attack attack, float enemyAdrenalineMultiplier, float factor, string key)
    {
        if (attack?.m_character == null ||
            enemyAdrenalineMultiplier <= 0f ||
            attack.m_attackAdrenaline <= 0f)
        {
            return false;
        }

        using (BeginScope(attack, factor, key))
        {
            attack.m_character.AddAdrenaline(attack.m_attackAdrenaline * enemyAdrenalineMultiplier);
        }

        return true;
    }

    internal static bool TryGrantOnceRaw(Attack attack, Character target, float baseAdrenaline, float factor, string key)
    {
        if (attack?.m_character == null ||
            target == null ||
            target.m_enemyAdrenalineMultiplier <= 0f ||
            baseAdrenaline <= 0f)
        {
            return false;
        }

        using (BeginScope(attack, factor, key))
        {
            attack.m_character.AddAdrenaline(baseAdrenaline * target.m_enemyAdrenalineMultiplier);
        }

        return true;
    }

    internal static void ApplyProjectileFactor(Projectile projectile, Attack? attack, float factor)
    {
        if (projectile == null)
        {
            return;
        }

        float baseAdrenaline = projectile.m_adrenaline;
        if (baseAdrenaline <= 0f && attack != null)
        {
            baseAdrenaline = attack.m_attackAdrenaline > 0f
                ? attack.m_attackAdrenaline
                : attack.m_attackUseAdrenaline;
        }

        projectile.m_adrenaline = Mathf.Max(0f, baseAdrenaline * Mathf.Max(0f, factor));
    }

    internal static float ResolveFactor(ActiveSecondaryAttack activeAttack)
    {
        SecondaryAttackDefinition definition = activeAttack.Definition;
        switch (definition.Behavior)
        {
            case ProjectileSecondaryBehavior projectile:
                return projectile.AdrenalineFactor;
        }

        return ResolveDefinitionFactor(definition);
    }

    internal static float ResolveDefinitionFactor(SecondaryAttackDefinition definition)
    {
        return 1f;
    }

    private static bool HasAdrenalineManagedSecondary(SecondaryAttackDefinition definition)
    {
        return definition.BehaviorType != SecondaryAttackBehaviorType.EffectOnly ||
               definition.CleavingThrust != null ||
               definition.LaunchSlam != null ||
               definition.KnockbackChain != null ||
               definition.Aftershock != null ||
               definition.RiftTrail != null ||
               definition.FractureLine != null ||
               definition.OnProjectileHit != null ||
               definition.Boomerang != null ||
               definition.SpinningSweep != null;
    }

    private static string ResolveKey(ActiveSecondaryAttack activeAttack)
    {
        return activeAttack.Definition.PrefabName + "|" + activeAttack.Definition.BehaviorType;
    }

    private static bool IsSecondaryProjectileHitContext()
    {
        return SecondaryAttackRuntimeContext.TryPeekProjectileHitContext(out ProjectileHitContext? context) &&
               context?.Attribution?.SecondaryAttack == true;
    }

    private static bool TryMarkGranted(Attack attack, string key)
    {
        if (attack == null)
        {
            return true;
        }

        AttackAdrenalineState state = AttackStates.GetValue(attack, _ => new AttackAdrenalineState());
        return state.GrantedKeys.Add(key);
    }

    private static bool MatchesWeapon(ItemDrop.ItemData? left, ItemDrop.ItemData? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        if (ReferenceEquals(left, right))
        {
            return true;
        }

        return left.m_dropPrefab != null &&
               right.m_dropPrefab != null &&
               left.m_dropPrefab.name == right.m_dropPrefab.name;
    }

    private static bool ShouldConvertAttackUseAdrenalineToProjectileHit(Attack attack)
    {
        return attack != null &&
               attack.m_attackUseAdrenaline > 0f &&
               IsStaffLightningShotAttack(attack);
    }

    private static bool IsStaffLightningShotAttack(Attack attack)
    {
        return string.Equals(
            attack?.m_attackAnimation,
            StaffLightningShotAnimation,
            StringComparison.OrdinalIgnoreCase);
    }

    internal readonly struct AdrenalineScope : System.IDisposable
    {
        private readonly Attack? _previousAttack;
        private readonly float _previousFactor;
        private readonly string? _previousKey;
        private readonly bool _previousOnce;

        internal AdrenalineScope(Attack? previousAttack, float previousFactor, string? previousKey, bool previousOnce)
        {
            _previousAttack = previousAttack;
            _previousFactor = previousFactor;
            _previousKey = previousKey;
            _previousOnce = previousOnce;
        }

        public void Dispose()
        {
            ScopedAttack = _previousAttack;
            ScopedFactor = _previousFactor;
            ScopedKey = _previousKey;
            ScopedOnce = _previousOnce;
        }
    }
}
