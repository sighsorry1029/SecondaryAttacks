using System.Diagnostics;

namespace SecondaryAttacks;

internal static class SecondaryAttackStartAttackDispatch
{
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Attack, ProjectilePresetCooldownFallbackState> ProjectilePresetCooldownFallbackAttacks = new();
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
            string originalCooldownFallbackPresetName = "")
        {
            ReloadCostContext = reloadCostContext;
            CooldownFallbackWeapon = cooldownFallbackWeapon;
            ConfiguredSecondaryAttack = configuredSecondaryAttack;
            SkipActiveRegistration = skipActiveRegistration;
            OriginalCooldownFallbackPresetName = originalCooldownFallbackPresetName;
        }

        internal SecondaryAttackManager.ReloadSecondaryResourceCostContext ReloadCostContext { get; }

        internal ItemDrop.ItemData? CooldownFallbackWeapon { get; }

        internal Attack? ConfiguredSecondaryAttack { get; }

        internal bool SkipActiveRegistration { get; }

        internal string OriginalCooldownFallbackPresetName { get; }

        internal bool IsOriginalCooldownFallback => !string.IsNullOrWhiteSpace(OriginalCooldownFallbackPresetName);

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
        ItemDrop.ItemData leftItem,
        ItemDrop.ItemData rightItem,
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
        SecondaryAttackAdrenalineSystem.EndConfiguredSecondaryStart(humanoid);
        if (secondaryAttack)
        {
            state.RestoreCooldownFallbackSecondary();
        }

        if (!secondaryAttack)
        {
            return;
        }

        LogStaffStartDebugIfNeeded(humanoid, result);
        if (!result || humanoid.m_currentAttack == null)
        {
            return;
        }

        if (state.IsOriginalCooldownFallback)
        {
            MarkProjectilePresetOriginalCooldownFallback(humanoid.m_currentAttack, state.OriginalCooldownFallbackPresetName);
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
            MeleeProjectileHitCascadeSystem.LogDebug("cooldown start probe skipped: current weapon is null.");
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

        if (IsProjectilePresetOriginalCooldownFallback(attack, out string originalFallbackPresetName))
        {
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"projectile burst cooldown skipped weapon={attack.m_weapon.m_dropPrefab?.name ?? "<unknown>"} preset={originalFallbackPresetName}: original secondary cooldown fallback attack.");
            return true;
        }

        ClearProjectilePresetCooldownFallback(attack);
        if (ProjectilePresetCooldownConsumedAttacks.TryGetValue(attack, out ProjectilePresetCooldownConsumedState? consumedState))
        {
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"projectile burst cooldown already consumed weapon={attack.m_weapon.m_dropPrefab?.name ?? "<unknown>"} preset={consumedState.PresetName}: allowing same attack burst.");
            return true;
        }

        SecondaryAttackManager.EnsureRuntimeWeaponDefinitionApplied(attack.m_weapon);
        if (!TryResolveProjectilePresetCooldown(
                attack.m_weapon,
                out string presetName,
                out MeleePresetCooldownDefinition? cooldown,
                out SecondaryAttackDefinition? definition))
        {
            return true;
        }

        if (IsSpearRainPreset(presetName))
        {
            bool cooldownReady = MeleePresetCooldownSystem.IsReady(attack.m_character, attack.m_weapon, presetName, cooldown!);
            bool pending = MeleeProjectileHitCascadeSystem.HasPendingSpearRain(attack.m_character, attack.m_weapon);
            if (cooldownReady && !pending)
            {
                MeleeProjectileHitCascadeSystem.LogDebug(
                    $"spearRain projectile burst armed weapon={attack.m_weapon.m_dropPrefab?.name ?? "<unknown>"} animation={attack.m_attackAnimation} projectile={attack.m_attackProjectile?.name ?? "<null>"} state=[{MeleePresetCooldownSystem.DescribeState(attack.m_character, attack.m_weapon, presetName, cooldown!)}].");
                return true;
            }

            attack.Stop();
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"spearRain projectile burst blocked weapon={attack.m_weapon.m_dropPrefab?.name ?? "<unknown>"} cooldownReady={cooldownReady} pending={pending} animation={attack.m_attackAnimation} projectile={attack.m_attackProjectile?.name ?? "<null>"} state=[{MeleePresetCooldownSystem.DescribeState(attack.m_character, attack.m_weapon, presetName, cooldown!)}].");
            return false;
        }

        if (MeleePresetCooldownSystem.TryConsume(
                attack.m_character,
                attack.m_weapon,
                presetName,
                cooldown!,
                out _))
        {
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"projectile burst cooldown consumed weapon={attack.m_weapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName} fallback={ResolveProjectilePresetCooldownFallback(definition!)} animation={attack.m_attackAnimation} projectile={attack.m_attackProjectile?.name ?? "<null>"} state=[{MeleePresetCooldownSystem.DescribeState(attack.m_character, attack.m_weapon, presetName, cooldown!)}].");
            MarkProjectilePresetCooldownConsumed(attack, presetName);
            return true;
        }

        MeleeProjectileHitCascadeSystem.LogDebug(
            $"projectile burst cooldown active weapon={attack.m_weapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName} fallback={ResolveProjectilePresetCooldownFallback(definition!)} animation={attack.m_attackAnimation} projectile={attack.m_attackProjectile?.name ?? "<null>"} state=[{MeleePresetCooldownSystem.DescribeState(attack.m_character, attack.m_weapon, presetName, cooldown!)}].");

        if (ProjectilePresetCooldownFallback.IsCopiedSecondary(ResolveProjectilePresetCooldownFallback(definition!)))
        {
            MarkProjectilePresetCooldownFallback(attack, presetName);
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"projectile burst cooldown fallback weapon={attack.m_weapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName}: using copied secondary without preset effects.");
            return true;
        }

        attack.Stop();
        MeleeProjectileHitCascadeSystem.LogDebug(
            $"projectile burst blocked weapon={attack.m_weapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName}: cooldown is active.");
        return false;
    }

    internal static bool ShouldSkipProjectilePresetEffectsForCooldown(Attack? attack, out string presetName)
    {
        presetName = "";
        if (attack == null ||
            !ProjectilePresetCooldownFallbackAttacks.TryGetValue(attack, out ProjectilePresetCooldownFallbackState? state))
        {
            return false;
        }

        presetName = state.PresetName;
        return true;
    }

    internal static bool IsProjectilePresetOriginalCooldownFallback(Attack? attack, out string presetName)
    {
        presetName = "";
        if (attack == null ||
            !ProjectilePresetOriginalCooldownFallbackAttacks.TryGetValue(attack, out ProjectilePresetOriginalCooldownFallbackState? state))
        {
            return false;
        }

        presetName = state.PresetName;
        return true;
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
                out string presetName,
                out MeleePresetCooldownDefinition? cooldown,
                out SecondaryAttackDefinition? definition))
        {
            LogCooldownStartProbeResolveMiss(currentWeapon);
            return false;
        }

        string cooldownFallback = ResolveProjectilePresetCooldownFallback(definition!);
        bool cooldownReady = MeleePresetCooldownSystem.IsReady(humanoid, currentWeapon, presetName, cooldown!);
        bool spearRainPending = IsSpearRainPreset(presetName) &&
                                MeleeProjectileHitCascadeSystem.HasPendingSpearRain(humanoid, currentWeapon);
        bool ready = cooldownReady && !spearRainPending;
        MeleeProjectileHitCascadeSystem.LogDebug(
            $"cooldown start probe weapon={currentWeapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName} fallback={cooldownFallback} cooldownReady={cooldownReady} pending={spearRainPending} ready={ready} currentSecondary={DescribeAttack(currentWeapon.m_shared?.m_secondaryAttack)} state=[{MeleePresetCooldownSystem.DescribeState(humanoid, currentWeapon, presetName, cooldown!)}].");
        if (ready)
        {
            if (ProjectilePresetCooldownFallback.UsesDynamicOriginalSecondary(definition!))
            {
                return TryPrepareDynamicProjectilePresetSecondary(
                    currentWeapon,
                    definition!,
                    presetName,
                    ref result,
                    out state,
                    out runOriginal);
            }

            return false;
        }

        if (ProjectilePresetCooldownFallback.IsCopiedSecondary(cooldownFallback))
        {
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"cooldown fallback weapon={currentWeapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName}: keeping copied secondary carrier.");
            return false;
        }

        if (ProjectilePresetCooldownFallback.UsesDynamicOriginalSecondary(definition!) &&
            TryPrepareDynamicOriginalSecondaryFallback(
                currentWeapon,
                definition!,
                presetName,
                out state))
        {
            return true;
        }

        if (!TryResolveOriginalSecondaryAttack(
                currentWeapon,
                definition!,
                secondaryBeforeRuntimeApply,
                definition!.ConfiguredSecondaryAttack ?? currentWeapon.m_shared?.m_secondaryAttack,
                out Attack? originalSecondaryAttack,
                out string originalSecondarySource))
        {
            result = false;
            runOriginal = false;
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"cooldown fallback blocked weapon={currentWeapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName}: original secondary attack could not be resolved. storedFallback={DescribeAttack(definition!.CooldownFallbackSecondaryAttack)} currentSecondary={DescribeAttack(currentWeapon.m_shared?.m_secondaryAttack)}.");
            return true;
        }

        if (currentWeapon.m_shared == null)
        {
            result = false;
            runOriginal = false;
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"cooldown fallback blocked weapon={currentWeapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName}: shared data is null.");
            return true;
        }

        Attack configuredSecondaryAttack = SecondaryAttackManager.CloneAttack(currentWeapon.m_shared.m_secondaryAttack);
        currentWeapon.m_shared.m_secondaryAttack = SecondaryAttackManager.CloneAttack(originalSecondaryAttack);
        state = new StartAttackState(
            SecondaryAttackManager.ReloadSecondaryResourceCostContext.Empty,
            currentWeapon,
            configuredSecondaryAttack,
            skipActiveRegistration: true,
            originalCooldownFallbackPresetName: presetName);
        MeleeProjectileHitCascadeSystem.LogDebug(
            $"cooldown fallback weapon={currentWeapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName}: using original secondary attack source={originalSecondarySource} original={DescribeAttack(originalSecondaryAttack)} configured={DescribeAttack(configuredSecondaryAttack)}.");
        return true;
    }

    private static bool TryPrepareDynamicProjectilePresetSecondary(
        ItemDrop.ItemData currentWeapon,
        SecondaryAttackDefinition definition,
        string presetName,
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
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"cooldown ready blocked weapon={currentWeapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName}: configured copied secondary attack could not be resolved. configured={DescribeAttack(definition.ConfiguredSecondaryAttack)} currentSecondary={DescribeAttack(currentWeapon.m_shared?.m_secondaryAttack)}.");
            return true;
        }

        Attack originalSecondaryAttack = SecondaryAttackManager.CloneAttack(currentWeapon.m_shared.m_secondaryAttack);
        currentWeapon.m_shared.m_secondaryAttack = SecondaryAttackManager.CloneAttack(definition.ConfiguredSecondaryAttack);
        state = new StartAttackState(
            SecondaryAttackManager.ReloadSecondaryResourceCostContext.Empty,
            currentWeapon,
            originalSecondaryAttack);
        MeleeProjectileHitCascadeSystem.LogDebug(
            $"cooldown ready weapon={currentWeapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName}: using copied secondary carrier configured={DescribeAttack(definition.ConfiguredSecondaryAttack)} restore={DescribeAttack(originalSecondaryAttack)}.");
        return true;
    }

    private static bool TryPrepareDynamicOriginalSecondaryFallback(
        ItemDrop.ItemData currentWeapon,
        SecondaryAttackDefinition definition,
        string presetName,
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
            originalCooldownFallbackPresetName: presetName);
        MeleeProjectileHitCascadeSystem.LogDebug(
            $"cooldown fallback weapon={currentWeapon.m_dropPrefab?.name ?? "<unknown>"} preset={presetName}: using current original secondary attack source=dynamicCurrent original={DescribeAttack(currentSecondaryAttack)} configured={DescribeAttack(definition.ConfiguredSecondaryAttack)}.");
        return true;
    }

    private static void LogCooldownStartProbeResolveMiss(ItemDrop.ItemData currentWeapon)
    {
        if (currentWeapon?.m_dropPrefab == null)
        {
            MeleeProjectileHitCascadeSystem.LogDebug("cooldown start probe skipped: weapon or drop prefab is null.");
            return;
        }

        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(currentWeapon, out SecondaryAttackDefinition definition))
        {
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"cooldown start probe skipped weapon={currentWeapon.m_dropPrefab.name}: no definition currentSecondary={DescribeAttack(currentWeapon.m_shared?.m_secondaryAttack)}.");
            return;
        }

        MeleeProjectileHitCascadeSystem.LogDebug(
            $"cooldown start probe skipped weapon={currentWeapon.m_dropPrefab.name}: behavior={definition.BehaviorType} boomerang={(definition.Boomerang != null)} onProjectileHit={definition.OnProjectileHit?.Preset ?? "<null>"} currentSecondary={DescribeAttack(currentWeapon.m_shared?.m_secondaryAttack)}.");
    }

    private static bool TryResolveProjectilePresetCooldown(
        ItemDrop.ItemData currentWeapon,
        out string presetName,
        out MeleePresetCooldownDefinition? cooldown,
        out SecondaryAttackDefinition? definition)
    {
        presetName = "";
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
            presetName = "boomerang";
            cooldown = resolvedDefinition.Boomerang.PresetCooldown;
            return true;
        }

        if (resolvedDefinition.OnProjectileHit == null ||
            (!resolvedDefinition.OnProjectileHit.Preset.Equals("impactBurst", System.StringComparison.OrdinalIgnoreCase) &&
             !resolvedDefinition.OnProjectileHit.Preset.Equals("spearRain", System.StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        presetName = resolvedDefinition.OnProjectileHit.Preset;
        cooldown = resolvedDefinition.OnProjectileHit.PresetCooldown;
        return true;
    }

    private static string ResolveProjectilePresetCooldownFallback(SecondaryAttackDefinition definition)
    {
        return ProjectilePresetCooldownFallback.OriginalSecondary;
    }

    private static bool IsSpearRainPreset(string presetName) =>
        presetName.Equals("spearRain", System.StringComparison.OrdinalIgnoreCase);

    private static void MarkProjectilePresetCooldownFallback(Attack attack, string presetName)
    {
        ProjectilePresetCooldownFallbackAttacks.Remove(attack);
        ProjectilePresetCooldownFallbackAttacks.Add(attack, new ProjectilePresetCooldownFallbackState(presetName));
    }

    private static void MarkProjectilePresetCooldownConsumed(Attack attack, string presetName)
    {
        ProjectilePresetCooldownConsumedAttacks.Remove(attack);
        ProjectilePresetCooldownConsumedAttacks.Add(attack, new ProjectilePresetCooldownConsumedState(presetName));
    }

    private static void MarkProjectilePresetOriginalCooldownFallback(Attack attack, string presetName)
    {
        ProjectilePresetOriginalCooldownFallbackAttacks.Remove(attack);
        ProjectilePresetOriginalCooldownFallbackAttacks.Add(attack, new ProjectilePresetOriginalCooldownFallbackState(presetName));
    }

    private static void ClearProjectilePresetCooldownFallback(Attack attack)
    {
        ProjectilePresetCooldownFallbackAttacks.Remove(attack);
    }

    private static bool TryResolveOriginalSecondaryAttack(
        ItemDrop.ItemData currentWeapon,
        SecondaryAttackDefinition definition,
        Attack? secondaryBeforeRuntimeApply,
        Attack? configuredSecondaryAttack,
        out Attack? originalSecondaryAttack,
        out string source)
    {
        originalSecondaryAttack = null;
        source = "";
        bool rejectCopiedCarrier = ShouldRejectMatchingCopiedCarrier(currentWeapon, definition, out string copiedSourcePrefab);
        if (HasUsableSecondaryAttack(secondaryBeforeRuntimeApply))
        {
            if (!ShouldSkipOriginalSecondaryCandidate(
                    secondaryBeforeRuntimeApply,
                    configuredSecondaryAttack,
                    rejectCopiedCarrier))
            {
                originalSecondaryAttack = SecondaryAttackManager.CloneAttack(secondaryBeforeRuntimeApply);
                source = "runtimeBeforeApply";
                return true;
            }

            LogSkippedOriginalSecondaryCandidate(
                currentWeapon,
                "runtimeBeforeApply",
                copiedSourcePrefab,
                secondaryBeforeRuntimeApply,
                configuredSecondaryAttack);
        }

        if (HasUsableSecondaryAttack(definition.CooldownFallbackSecondaryAttack))
        {
            if (!ShouldSkipOriginalSecondaryCandidate(
                    definition.CooldownFallbackSecondaryAttack,
                    configuredSecondaryAttack,
                    rejectCopiedCarrier))
            {
                originalSecondaryAttack = SecondaryAttackManager.CloneAttack(definition.CooldownFallbackSecondaryAttack);
                source = "definition";
                return true;
            }

            LogSkippedOriginalSecondaryCandidate(
                currentWeapon,
                "definition",
                copiedSourcePrefab,
                definition.CooldownFallbackSecondaryAttack,
                configuredSecondaryAttack);
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
                source = "objectDb";
                return true;
            }

            LogSkippedOriginalSecondaryCandidate(
                currentWeapon,
                "objectDb",
                copiedSourcePrefab,
                originalAttack,
                configuredSecondaryAttack);
        }

        return false;
    }

    private static bool ShouldRejectMatchingCopiedCarrier(
        ItemDrop.ItemData currentWeapon,
        SecondaryAttackDefinition definition,
        out string copiedSourcePrefab)
    {
        copiedSourcePrefab = "";
        if (currentWeapon?.m_dropPrefab == null ||
            definition.Behavior is not CopiedSecondaryBehavior copiedBehavior ||
            string.IsNullOrWhiteSpace(copiedBehavior.SourcePrefabName))
        {
            return false;
        }

        copiedSourcePrefab = copiedBehavior.SourcePrefabName.Trim();
        return !string.Equals(
            copiedSourcePrefab,
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

    private static void LogSkippedOriginalSecondaryCandidate(
        ItemDrop.ItemData? currentWeapon,
        string source,
        string copiedSourcePrefab,
        Attack? candidate,
        Attack? configuredSecondaryAttack)
    {
        MeleeProjectileHitCascadeSystem.LogDebug(
            $"cooldown fallback candidate skipped weapon={currentWeapon?.m_dropPrefab?.name ?? "<unknown>"} source={source}: candidate matches copied secondary carrier copiedSource={copiedSourcePrefab}. candidate={DescribeAttack(candidate)} configured={DescribeAttack(configuredSecondaryAttack)}.");
    }

    private static bool HasUsableSecondaryAttack(Attack? attack)
    {
        return attack != null && !string.IsNullOrWhiteSpace(attack.m_attackAnimation);
    }

    private static string DescribeAttack(Attack? attack)
    {
        if (attack == null)
        {
            return "<null>";
        }

        return
            $"animation={attack.m_attackAnimation ?? "<null>"}, type={attack.m_attackType}, projectile={attack.m_attackProjectile?.name ?? "<null>"}, consumeItem={attack.m_consumeItem}, projectiles={attack.m_projectiles}, bursts={attack.m_projectileBursts}";
    }

    [Conditional("SECONDARY_ATTACKS_DEBUG_LOGGING")]
    private static void LogStaffStartDebugIfNeeded(Humanoid humanoid, bool result)
    {
        ItemDrop.ItemData currentWeaponForDebug = humanoid.GetCurrentWeapon();
        SecondaryAttackManager.EnsureRuntimeWeaponDefinitionApplied(currentWeaponForDebug);
        if (currentWeaponForDebug == null ||
            !SecondaryAttackRuntimeFacade.TryGetDefinition(currentWeaponForDebug, out SecondaryAttackDefinition debugDefinition) ||
            (debugDefinition.BehaviorType != SecondaryAttackBehaviorType.SummonEmpower &&
             debugDefinition.BehaviorType != SecondaryAttackBehaviorType.ShieldConvert))
        {
            return;
        }

        SecondaryAttackManager.LogStaffDebug(
            $"Humanoid.StartAttack secondary result for '{debugDefinition.PrefabName}': started={result}, currentAttackNull={(humanoid.m_currentAttack == null)}.");
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

    private sealed class ProjectilePresetCooldownFallbackState
    {
        internal ProjectilePresetCooldownFallbackState(string presetName)
        {
            PresetName = presetName;
        }

        internal string PresetName { get; }
    }

    private sealed class ProjectilePresetCooldownConsumedState
    {
        internal ProjectilePresetCooldownConsumedState(string presetName)
        {
            PresetName = presetName;
        }

        internal string PresetName { get; }
    }

    private sealed class ProjectilePresetOriginalCooldownFallbackState
    {
        internal ProjectilePresetOriginalCooldownFallbackState(string presetName)
        {
            PresetName = presetName;
        }

        internal string PresetName { get; }
    }
}
