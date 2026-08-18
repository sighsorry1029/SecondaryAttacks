
namespace SecondaryAttacks;

internal static class SecondaryAttackStartAttackDispatch
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Attack, ProjectilePresetCooldownConsumedState> ProjectilePresetCooldownConsumedAttacks = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Attack, ProjectilePresetOriginalCooldownFallbackState> ProjectilePresetOriginalCooldownFallbackAttacks = new();

    internal readonly struct StartAttackState
    {
        internal static readonly StartAttackState Empty = new(SecondaryAttackManager.ReloadSecondaryResourceCostContext.Empty);

        internal StartAttackState(
            SecondaryAttackManager.ReloadSecondaryResourceCostContext reloadCostContext,
            ItemDrop.ItemData? cooldownFallbackWeapon = null,
            Attack? configuredSecondaryAttack = null,
            bool skipActiveRegistration = false,
            bool originalCooldownFallback = false)
        {
            ReloadCostContext = reloadCostContext;
            CooldownFallbackWeapon = cooldownFallbackWeapon;
            ConfiguredSecondaryAttack = configuredSecondaryAttack;
            SkipActiveRegistration = skipActiveRegistration;
            IsOriginalCooldownFallback = originalCooldownFallback;
        }

        internal SecondaryAttackManager.ReloadSecondaryResourceCostContext ReloadCostContext { get; }

        internal ItemDrop.ItemData? CooldownFallbackWeapon { get; }

        internal Attack? ConfiguredSecondaryAttack { get; }

        internal bool SkipActiveRegistration { get; }

        internal bool IsOriginalCooldownFallback { get; }

        internal void RestoreCooldownFallbackSecondary()
        {
            if (CooldownFallbackWeapon?.m_shared == null || ConfiguredSecondaryAttack == null)
            {
                return;
            }

            CooldownFallbackWeapon.m_shared.m_secondaryAttack = ConfiguredSecondaryAttack;
        }
    }

    internal static bool Prefix(
        Humanoid humanoid,
        bool secondaryAttack,
        ref bool result,
        out StartAttackState state)
    {
        state = StartAttackState.Empty;

        if (!secondaryAttack)
        {
            return true;
        }

        return TryPrepareConfiguredSecondaryStart(humanoid, ref result, out state);
    }

    internal static void Postfix(
        Humanoid humanoid,
        bool secondaryAttack,
        bool result,
        StartAttackState state)
    {
        Cleanup(humanoid, secondaryAttack, state);

        if (!secondaryAttack)
        {
            return;
        }
        if (!result || humanoid.m_currentAttack == null)
        {
            return;
        }

        if (state.IsOriginalCooldownFallback)
        {
            MarkProjectilePresetOriginalCooldownFallback(humanoid.m_currentAttack);
        }

        if (humanoid is Player player)
        {
            SneakAmbushChargeSystem.BeginSecondaryAttack(player, humanoid.GetCurrentWeapon());
        }

        if (state.SkipActiveRegistration)
        {
            return;
        }

        SecondaryAttackManager.ApplyReloadSecondaryResourceCost(humanoid, state.ReloadCostContext);
        RegisterActiveAttackIfNeeded(humanoid);
    }

    internal static void Cleanup(
        Humanoid humanoid,
        bool secondaryAttack,
        StartAttackState state)
    {
        try
        {
            SecondaryAttackAdrenalineSystem.EndConfiguredSecondaryStart(humanoid);
        }
        finally
        {
            if (secondaryAttack)
            {
                state.RestoreCooldownFallbackSecondary();
            }
        }
    }

    private static bool TryPrepareConfiguredSecondaryStart(
        Humanoid humanoid,
        ref bool result,
        out StartAttackState state)
    {
        state = StartAttackState.Empty;
        ItemDrop.ItemData currentWeapon = humanoid.GetCurrentWeapon();
        Attack? secondaryBeforeRuntimeApply = currentWeapon?.m_shared?.m_secondaryAttack;
        SecondaryAttackManager.EnsureRuntimeWeaponDefinitionApplied(currentWeapon);
        if (currentWeapon == null)
        {
            return true;
        }

        if (TryPrepareOriginalSecondaryForProjectilePresetCooldown(
                humanoid,
                currentWeapon,
                secondaryBeforeRuntimeApply,
                ref result,
                out state,
                out bool runOriginalCooldownFallback))
        {
            return runOriginalCooldownFallback;
        }

        if (!RangedSecondaryCooldownSystem.CanStart(humanoid, currentWeapon))
        {
            result = false;
            return false;
        }

        if (!StaffRuntimeSystem.CanStartStaffSpecial(humanoid, currentWeapon))
        {
            result = false;
            return false;
        }

        if (!SecondaryAttackRuntimeFacade.CanStartConfiguredSecondary(humanoid, currentWeapon))
        {
            result = false;
            return false;
        }

        if (!SecondaryAttackManager.TryPrepareReloadSecondaryResourceCost(
                humanoid,
                currentWeapon,
                out SecondaryAttackManager.ReloadSecondaryResourceCostContext reloadCostContext))
        {
            result = false;
            return false;
        }

        state = new StartAttackState(reloadCostContext);
        SecondaryAttackAdrenalineSystem.BeginConfiguredSecondaryStart(humanoid, currentWeapon);
        return true;
    }

    internal static bool TryConsumeProjectilePresetCooldownAtBurst(Attack attack)
    {
        if (attack?.m_character == null || attack.m_weapon == null)
        {
            return true;
        }

        if (IsProjectilePresetOriginalCooldownFallback(attack))
        {
            return true;
        }

        if (ProjectilePresetCooldownConsumedAttacks.TryGetValue(attack, out _))
        {
            return true;
        }

        SecondaryAttackManager.EnsureRuntimeWeaponDefinitionApplied(attack.m_weapon);
        if (!TryResolveProjectilePresetCooldown(
                attack.m_weapon,
                out MeleeSpecialPreset preset,
                out MeleePresetCooldownDefinition? cooldown,
                out SecondaryAttackDefinition? definition))
        {
            return true;
        }

        string presetName = SecondaryAttackPresetCatalog.GetKey(preset)!;
        if (preset == MeleeSpecialPreset.SpearRain)
        {
            bool cooldownReady = MeleePresetCooldownSystem.IsReady(
                attack.m_character,
                presetName,
                cooldown!);
            bool pending = MeleeProjectileHitCascadeSystem.HasPendingSpearRain(attack.m_character);
            if (cooldownReady && !pending)
            {
                return true;
            }

            attack.Stop();
            return false;
        }

        if (MeleePresetCooldownSystem.TryConsume(
                attack.m_character,
                attack.m_weapon,
                presetName,
                cooldown!))
        {
            MarkProjectilePresetCooldownConsumed(attack);
            return true;
        }

        attack.Stop();
        return false;
    }

    internal static bool IsProjectilePresetOriginalCooldownFallback(Attack? attack)
    {
        return attack != null && ProjectilePresetOriginalCooldownFallbackAttacks.TryGetValue(attack, out _);
    }

    private static bool TryPrepareOriginalSecondaryForProjectilePresetCooldown(
        Humanoid humanoid,
        ItemDrop.ItemData currentWeapon,
        Attack? secondaryBeforeRuntimeApply,
        ref bool result,
        out StartAttackState state,
        out bool runOriginal)
    {
        state = StartAttackState.Empty;
        runOriginal = true;
        if (!TryResolveProjectilePresetCooldown(
                currentWeapon,
                out MeleeSpecialPreset preset,
                out MeleePresetCooldownDefinition? cooldown,
                out SecondaryAttackDefinition? definition))
        {
            return false;
        }

        string presetName = SecondaryAttackPresetCatalog.GetKey(preset)!;
        bool cooldownReady = MeleePresetCooldownSystem.IsReady(
            humanoid,
            presetName,
            cooldown!);
        bool spearRainPending = preset == MeleeSpecialPreset.SpearRain &&
                                MeleeProjectileHitCascadeSystem.HasPendingSpearRain(humanoid);
        bool ready = cooldownReady && !spearRainPending;
        if (ready)
        {
            if (ProjectilePresetCooldownPolicy.UsesDynamicOriginalSecondary(definition!))
            {
                return TryPrepareDynamicProjectilePresetSecondary(
                    currentWeapon,
                    definition!,
                    ref result,
                    out state,
                    out runOriginal);
            }

            return false;
        }

        if (ProjectilePresetCooldownPolicy.UsesDynamicOriginalSecondary(definition!) &&
            TryPrepareDynamicOriginalSecondaryFallback(
                currentWeapon,
                definition!,
                out state))
        {
            return true;
        }

        if (!TryResolveOriginalSecondaryAttack(
                currentWeapon,
                definition!,
                secondaryBeforeRuntimeApply,
                definition!.ConfiguredSecondaryAttack ?? currentWeapon.m_shared?.m_secondaryAttack,
                out Attack? originalSecondaryAttack))
        {
            result = false;
            runOriginal = false;
            return true;
        }

        if (currentWeapon.m_shared == null)
        {
            result = false;
            runOriginal = false;
            return true;
        }

        Attack configuredSecondaryAttack = SecondaryAttackManager.CloneAttack(currentWeapon.m_shared.m_secondaryAttack);
        currentWeapon.m_shared.m_secondaryAttack = SecondaryAttackManager.CloneAttack(originalSecondaryAttack);
        state = new StartAttackState(
            SecondaryAttackManager.ReloadSecondaryResourceCostContext.Empty,
            currentWeapon,
            configuredSecondaryAttack,
            skipActiveRegistration: true,
            originalCooldownFallback: true);
        return true;
    }

    private static bool TryPrepareDynamicProjectilePresetSecondary(
        ItemDrop.ItemData currentWeapon,
        SecondaryAttackDefinition definition,
        ref bool result,
        out StartAttackState state,
        out bool runOriginal)
    {
        state = StartAttackState.Empty;
        runOriginal = true;
        if (currentWeapon.m_shared == null ||
            !HasUsableSecondaryAttack(definition.ConfiguredSecondaryAttack))
        {
            result = false;
            runOriginal = false;
            return true;
        }

        Attack originalSecondaryAttack = SecondaryAttackManager.CloneAttack(currentWeapon.m_shared.m_secondaryAttack);
        currentWeapon.m_shared.m_secondaryAttack = SecondaryAttackManager.CloneAttack(definition.ConfiguredSecondaryAttack);
        state = new StartAttackState(
            SecondaryAttackManager.ReloadSecondaryResourceCostContext.Empty,
            currentWeapon,
            originalSecondaryAttack);
        return true;
    }

    private static bool TryPrepareDynamicOriginalSecondaryFallback(
        ItemDrop.ItemData currentWeapon,
        SecondaryAttackDefinition definition,
        out StartAttackState state)
    {
        state = StartAttackState.Empty;
        Attack? currentSecondaryAttack = currentWeapon.m_shared?.m_secondaryAttack;
        if (!HasUsableSecondaryAttack(currentSecondaryAttack) ||
            HasSameAttackShape(currentSecondaryAttack, definition.ConfiguredSecondaryAttack))
        {
            return false;
        }

        state = new StartAttackState(
            SecondaryAttackManager.ReloadSecondaryResourceCostContext.Empty,
            skipActiveRegistration: true,
            originalCooldownFallback: true);
        return true;
    }

    private static bool TryResolveProjectilePresetCooldown(
        ItemDrop.ItemData currentWeapon,
        out MeleeSpecialPreset preset,
        out MeleePresetCooldownDefinition? cooldown,
        out SecondaryAttackDefinition? definition)
    {
        preset = MeleeSpecialPreset.None;
        cooldown = null;
        definition = null;
        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(currentWeapon, out SecondaryAttackDefinition resolvedDefinition) ||
            resolvedDefinition.Behavior is not CopiedSecondaryBehavior)
        {
            return false;
        }

        definition = resolvedDefinition;
        if (resolvedDefinition.Boomerang != null)
        {
            preset = MeleeSpecialPreset.Boomerang;
            cooldown = resolvedDefinition.Boomerang.PresetCooldown;
            return true;
        }

        if (resolvedDefinition.OnProjectileHit == null ||
            (resolvedDefinition.OnProjectileHit.Preset != MeleeSpecialPreset.ImpactBurst &&
             resolvedDefinition.OnProjectileHit.Preset != MeleeSpecialPreset.SpearRain))
        {
            return false;
        }

        preset = resolvedDefinition.OnProjectileHit.Preset;
        cooldown = resolvedDefinition.OnProjectileHit.PresetCooldown;
        return true;
    }

    private static void MarkProjectilePresetCooldownConsumed(Attack attack)
    {
        ProjectilePresetCooldownConsumedAttacks.Remove(attack);
        ProjectilePresetCooldownConsumedAttacks.Add(attack, new ProjectilePresetCooldownConsumedState());
    }

    private static void MarkProjectilePresetOriginalCooldownFallback(Attack attack)
    {
        ProjectilePresetOriginalCooldownFallbackAttacks.Remove(attack);
        ProjectilePresetOriginalCooldownFallbackAttacks.Add(attack, new ProjectilePresetOriginalCooldownFallbackState());
    }

    private static bool TryResolveOriginalSecondaryAttack(
        ItemDrop.ItemData currentWeapon,
        SecondaryAttackDefinition definition,
        Attack? secondaryBeforeRuntimeApply,
        Attack? configuredSecondaryAttack,
        out Attack? originalSecondaryAttack)
    {
        originalSecondaryAttack = null;
        bool rejectCopiedCarrier = ShouldRejectMatchingCopiedCarrier(currentWeapon, definition);
        if (HasUsableSecondaryAttack(secondaryBeforeRuntimeApply))
        {
            if (!ShouldSkipOriginalSecondaryCandidate(
                    secondaryBeforeRuntimeApply,
                    configuredSecondaryAttack,
                    rejectCopiedCarrier))
            {
                originalSecondaryAttack = SecondaryAttackManager.CloneAttack(secondaryBeforeRuntimeApply);
                return true;
            }
        }

        if (HasUsableSecondaryAttack(definition.CooldownFallbackSecondaryAttack))
        {
            if (!ShouldSkipOriginalSecondaryCandidate(
                    definition.CooldownFallbackSecondaryAttack,
                    configuredSecondaryAttack,
                    rejectCopiedCarrier))
            {
                originalSecondaryAttack = SecondaryAttackManager.CloneAttack(definition.CooldownFallbackSecondaryAttack);
                return true;
            }
        }

        if (ObjectDB.instance != null &&
            currentWeapon?.m_dropPrefab != null &&
            currentWeapon.m_shared != null &&
            SecondaryAttackObjectDbStateStore.TryGetOriginalSecondaryAttack(
                ObjectDB.instance,
                currentWeapon.m_dropPrefab.name,
                out Attack? originalAttack) &&
            HasUsableSecondaryAttack(originalAttack))
        {
            if (!ShouldSkipOriginalSecondaryCandidate(originalAttack, configuredSecondaryAttack, rejectCopiedCarrier))
            {
                originalSecondaryAttack = originalAttack;
                return true;
            }
        }

        return false;
    }

    private static bool ShouldRejectMatchingCopiedCarrier(
        ItemDrop.ItemData currentWeapon,
        SecondaryAttackDefinition definition)
    {
        if (currentWeapon?.m_dropPrefab == null ||
            definition.Behavior is not CopiedSecondaryBehavior copiedBehavior ||
            string.IsNullOrWhiteSpace(copiedBehavior.SourcePrefabName))
        {
            return false;
        }

        return !string.Equals(
            copiedBehavior.SourcePrefabName.Trim(),
            currentWeapon.m_dropPrefab.name,
            System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldSkipOriginalSecondaryCandidate(
        Attack? candidate,
        Attack? configuredSecondaryAttack,
        bool rejectCopiedCarrier) =>
        rejectCopiedCarrier && HasSameAttackShape(candidate, configuredSecondaryAttack);

    private static bool HasSameAttackShape(Attack? left, Attack? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        return left.m_attackType == right.m_attackType &&
               string.Equals(
                   left.m_attackAnimation?.Trim() ?? "",
                   right.m_attackAnimation?.Trim() ?? "",
                   System.StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   left.m_attackProjectile?.name ?? "",
                   right.m_attackProjectile?.name ?? "",
                   System.StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsableSecondaryAttack(Attack? attack)
    {
        return attack != null && !string.IsNullOrWhiteSpace(attack.m_attackAnimation);
    }

    private static void RegisterActiveAttackIfNeeded(Humanoid humanoid)
    {
        ItemDrop.ItemData currentWeapon = humanoid.GetCurrentWeapon();
        if (currentWeapon == null)
        {
            return;
        }

        SecondaryAttackRuntimeFacade.RegisterActiveAttack(humanoid.m_currentAttack, currentWeapon);
    }

    private sealed class ProjectilePresetCooldownConsumedState
    {
    }

    private sealed class ProjectilePresetOriginalCooldownFallbackState
    {
    }
}
