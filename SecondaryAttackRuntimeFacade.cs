using System;
using System.Linq;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackRuntimeFacade
{
    private const string StaffRapidFireAnimation = "staff_rapidfire";
    private const float FallbackHoldRepeatInterval = 0.2f;

    internal static bool TryGetDefinition(ItemDrop.ItemData weapon, out SecondaryAttackDefinition definition)
    {
        definition = null!;
        if (weapon?.m_dropPrefab == null)
        {
            return false;
        }

        return SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.TryGetValue(weapon.m_dropPrefab.name, out definition!);
    }

    internal static bool TryGetDefinition(string weaponPrefabName, out SecondaryAttackDefinition definition)
    {
        return SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.TryGetValue(weaponPrefabName, out definition!);
    }

    internal static bool ShouldHandleBowDraw(ItemDrop.ItemData weapon)
    {
        return weapon != null &&
               weapon.m_shared.m_attack.m_bowDraw &&
               TryGetDefinition(weapon, out SecondaryAttackDefinition definition) &&
               definition.BehaviorType == SecondaryAttackBehaviorType.Projectile;
    }

    internal static bool CanStartConfiguredSecondary(Humanoid humanoid, ItemDrop.ItemData weapon)
    {
        if (!TryGetDefinition(weapon, out SecondaryAttackDefinition definition))
        {
            return true;
        }

        if (definition.Behavior is not ProjectileSecondaryBehavior projectileBehavior)
        {
            return true;
        }

        if (!OverchargedBombSystem.CanStart(humanoid, weapon, projectileBehavior))
        {
            return false;
        }

        if (projectileBehavior.Preset == SecondaryAttackPreset.Burst &&
            weapon.m_shared.m_attack.m_requiresReload &&
            humanoid is Player player &&
            player.m_weaponLoaded != weapon)
        {
            return false;
        }

        string ammoType = weapon.m_shared.m_ammoType;
        Inventory? inventory = humanoid.GetInventory();
        ConfiguredAmmoContext ammoContext = default;
        if (!string.IsNullOrWhiteSpace(ammoType) &&
            !TrySelectConfiguredAmmo(
                humanoid,
                weapon,
                inventory,
                ammoType,
                projectileBehavior.AmmoConsumption,
                out ammoContext))
        {
            return false;
        }

        ItemDrop.ItemData? ammoItem = ammoContext.AmmoItem;

        Attack? configuredAttack = weapon.m_shared.m_secondaryAttack;
        if (configuredAttack == null)
        {
            return true;
        }

        return ProjectileRuntimeSystem.TryValidateBurstPresetPayload(
            configuredAttack,
            definition,
            projectileBehavior.Preset,
            ammoItem);
    }

    internal static bool BeginProjectileHitContext(Projectile projectile, Collider collider)
    {
        if (projectile == null || collider == null)
        {
            return false;
        }

        ProjectileAttackAttribution? attribution = null;
        SecondaryAttackRuntimeContext.TryGetProjectileAttackAttribution(projectile, out attribution);
        SecondaryAttackRuntimeContext.PushProjectileHitContext(new ProjectileHitContext(projectile, attribution));
        return true;
    }

    internal static void EndProjectileHitContext(bool active)
    {
        if (!active)
        {
            return;
        }

        SecondaryAttackRuntimeContext.PopProjectileHitContext();
    }

    internal static bool TryGetProjectileHitAttackContext(
        out string weaponPrefabName,
        out bool secondaryAttack,
        out SecondaryAttackDefinition? definition,
        out bool disableCurrentAttackFallback)
    {
        weaponPrefabName = string.Empty;
        secondaryAttack = false;
        definition = null;
        disableCurrentAttackFallback = false;

        if (!SecondaryAttackRuntimeContext.TryPeekProjectileHitContext(out ProjectileHitContext context))
        {
            return false;
        }

        ProjectileAttackAttribution? attribution = context.Attribution;
        if (attribution == null)
        {
            return false;
        }

        disableCurrentAttackFallback = attribution.DisableCurrentAttackFallback;
        definition = attribution.Definition;
        weaponPrefabName = attribution.WeaponPrefabName;
        secondaryAttack = attribution.SecondaryAttack;
        return !string.IsNullOrEmpty(weaponPrefabName) || definition != null;
    }

    internal static bool TryResolveProjectileAttackAttributionData(
        Attack attack,
        out string weaponPrefabName,
        out bool secondaryAttack,
        out SecondaryAttackDefinition? definition)
    {
        weaponPrefabName = attack.m_weapon?.m_dropPrefab?.name ?? string.Empty;
        secondaryAttack = false;
        definition = null;

        if (attack.m_weapon?.m_dropPrefab == null)
        {
            return false;
        }

        if (SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) && activeAttack != null)
        {
            definition = activeAttack.Definition;
            weaponPrefabName = activeAttack.Definition.PrefabName;
            secondaryAttack = true;
            return true;
        }

        if (TryGetDefinition(attack.m_weapon, out SecondaryAttackDefinition resolvedDefinition))
        {
            definition = resolvedDefinition;
        }

        return true;
    }

    internal static void SetProjectileAttackAttribution(
        Projectile projectile,
        string weaponPrefabName,
        bool secondaryAttack,
        SecondaryAttackDefinition? definition,
        bool disableCurrentAttackFallback)
    {
        SecondaryAttackRuntimeContext.SetProjectileAttackAttribution(
            projectile,
            new ProjectileAttackAttribution(weaponPrefabName, secondaryAttack, definition, disableCurrentAttackFallback));
    }

    internal static void TryApplyProjectileSetupAttribution(Projectile projectile, ItemDrop.ItemData item)
    {
        if (projectile == null ||
            item?.m_dropPrefab == null ||
            ProjectileAccess.GetOwner(projectile) != Player.m_localPlayer ||
            !TryGetDefinition(item, out SecondaryAttackDefinition definition))
        {
            return;
        }

        bool secondaryAttack = false;
        Player? localPlayer = Player.m_localPlayer;
        Attack? currentAttack = localPlayer != null ? ((Humanoid)localPlayer).m_currentAttack : null;
        if (currentAttack?.m_weapon?.m_dropPrefab != null &&
            string.Equals(currentAttack.m_weapon.m_dropPrefab.name, item.m_dropPrefab.name, StringComparison.OrdinalIgnoreCase))
        {
            secondaryAttack = ((Humanoid)localPlayer!).m_currentAttackIsSecondary;
        }

        if (secondaryAttack)
        {
            float adrenalineFactor = definition.Behavior is ProjectileSecondaryBehavior projectileBehavior
                ? projectileBehavior.AdrenalineFactor
                : 1f;
            SecondaryAttackAdrenalineSystem.ApplyProjectileFactor(projectile, currentAttack, adrenalineFactor);
        }

        SetProjectileAttackAttribution(
            projectile,
            definition.PrefabName,
            secondaryAttack,
            definition,
            disableCurrentAttackFallback: false);
    }

    internal static void RegisterActiveAttack(Attack attack, ItemDrop.ItemData weapon)
    {
        if (!TryGetDefinition(weapon, out SecondaryAttackDefinition definition))
        {
            return;
        }

        bool needsActiveAttack = definition.BehaviorType == SecondaryAttackBehaviorType.Projectile ||
                                 definition.BehaviorType == SecondaryAttackBehaviorType.SummonEmpower ||
                                 definition.BehaviorType == SecondaryAttackBehaviorType.ShieldConvert ||
                                 definition.SneakAmbush != null ||
                                 definition.CleavingThrust != null ||
                                 definition.LaunchSlam != null ||
                                 definition.KnockbackChain != null ||
                                 definition.Aftershock != null ||
                                 definition.RiftTrail != null ||
                                 definition.FractureLine != null ||
                                 definition.HarvestSweep != null ||
                                 definition.SpinningSweep != null ||
                                 (definition.BehaviorType == SecondaryAttackBehaviorType.CopiedSecondary && definition.Boomerang != null) ||
                                 (definition.BehaviorType == SecondaryAttackBehaviorType.CopiedSecondary && definition.OnProjectileHit != null);
        if (definition.SpinningSweep != null)
        {
            SpinningSweepSystem.TryStart(attack, definition);
        }
        else if (definition.HarvestSweep != null)
        {
            HarvestSweepSystem.TryStart(attack, definition);
        }

        if (!needsActiveAttack)
        {
            return;
        }

        ActiveSecondaryAttack activeAttack = new(definition);
        SecondaryAttackRuntimeContext.SetActiveAttack(attack, activeAttack);
        SecondaryAttackAdrenalineSystem.Reset(attack);
        if (definition.CleavingThrust != null)
        {
            CleavingThrustTrailVisualSystem.BeginCleavingThrustVisualSession(attack, definition);
        }

        if (definition.RiftTrail != null)
        {
            RiftTrailSystem.BeginSampling(attack, definition);
        }

    }

    internal static bool TryHandleCustomAttackTrigger(Attack attack)
    {
        if (!SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) || activeAttack == null)
        {
            return false;
        }

        if (attack.m_character.IsStaggering())
        {
            return true;
        }

        if (activeAttack.Definition.CleavingThrust != null)
        {
            if (!CleavingThrustSystem.CanHandle(attack))
            {
                return false;
            }

            if (!MeleePresetCooldownSystem.TryConsume(
                    attack.m_character,
                    attack.m_weapon,
                    "cleavingThrust",
                    activeAttack.Definition.CleavingThrust.PresetCooldown))
            {
                return false;
            }

            if (!TriggerCleavingThrust(attack, activeAttack))
            {
                attack.Stop();
            }

            return true;
        }

        if (activeAttack.Definition.Aftershock != null)
        {
            if (!AftershockSystem.CanHandle(attack, activeAttack.Definition))
            {
                return false;
            }

            if (!MeleePresetCooldownSystem.TryConsume(
                    attack.m_character,
                    attack.m_weapon,
                    "aftershock",
                    activeAttack.Definition.Aftershock.PresetCooldown))
            {
                return false;
            }

            activeAttack.Triggered = true;
            AftershockSystem.Trigger(attack, activeAttack.Definition);
            return true;
        }

        if (activeAttack.Definition.BehaviorType == SecondaryAttackBehaviorType.CopiedSecondary)
        {
            return false;
        }

        if (activeAttack.Definition.BehaviorType == SecondaryAttackBehaviorType.SummonEmpower ||
            activeAttack.Definition.BehaviorType == SecondaryAttackBehaviorType.ShieldConvert)
        {
            if (!activeAttack.Triggered &&
                !StaffRuntimeSystem.TryTriggerStaffSpecialFromRuntimeFacade(attack, activeAttack))
            {
                attack.Stop();
            }

            return true;
        }

        bool isBurstTrigger =
            activeAttack.Definition.Behavior is ProjectileSecondaryBehavior { Preset: SecondaryAttackPreset.Burst };
        if (isBurstTrigger)
        {
            if (activeAttack.BurstTriggerHandled)
            {
                return true;
            }

            activeAttack.BurstTriggerHandled = true;
        }

        if (!TriggerConfiguredAttack(attack, activeAttack))
        {
            attack.Stop();
        }

        return true;
    }

    internal static void TryTriggerRiftTrailAfterAttack(Attack attack)
    {
        if (!SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) ||
            activeAttack == null ||
            activeAttack.Triggered ||
            activeAttack.Definition.RiftTrail == null)
        {
            return;
        }

        if (!RiftTrailSystem.CanHandle(attack, activeAttack.Definition))
        {
            return;
        }

        if (!MeleePresetCooldownSystem.TryConsume(
                attack.m_character,
                attack.m_weapon,
                "riftTrail",
                activeAttack.Definition.RiftTrail.PresetCooldown))
        {
            return;
        }

        activeAttack.Triggered = true;
        RiftTrailSystem.Trigger(attack, activeAttack.Definition);
    }

    internal static void TryTriggerFractureLineAfterAttack(Attack attack)
    {
        if (!SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) ||
            activeAttack == null ||
            activeAttack.Triggered ||
            activeAttack.Definition.FractureLine == null)
        {
            return;
        }

        if (!FractureLineSystem.CanHandle(attack, activeAttack.Definition))
        {
            return;
        }

        if (!MeleePresetCooldownSystem.TryConsume(
                attack.m_character,
                attack.m_weapon,
                "fractureLine",
                activeAttack.Definition.FractureLine.PresetCooldown))
        {
            return;
        }

        activeAttack.Triggered = true;
        FractureLineSystem.Trigger(attack, activeAttack.Definition);
    }

    private static bool TriggerCleavingThrust(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        if (!TryPrepareConfiguredAmmo(
                attack,
                activeAttack.Definition,
                out ConfiguredAmmoContext ammoContext))
        {
            return false;
        }

        CommitConfiguredAmmo(attack, ammoContext);
        activeAttack.Triggered = true;
        CleavingThrustSystem.Trigger(attack, activeAttack.Definition);
        ApplyAttackTriggerSideEffects(attack);
        return true;
    }

    internal static void TryUpdateSecondaryProjectileHoldRepeat(Player player, bool secondaryAttackHold)
    {
        if (!secondaryAttackHold ||
            player == null ||
            player != Player.m_localPlayer ||
            player.IsDead())
        {
            return;
        }

        Attack? attack = ((Humanoid)player).m_currentAttack;
        if (attack == null || !((Humanoid)player).m_currentAttackIsSecondary)
        {
            return;
        }

        if (!SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) ||
            activeAttack == null ||
            !IsHoldRepeatProjectileAttack(attack, activeAttack))
        {
            return;
        }

        if (!player.InAttack() || attack.m_character == null || attack.m_character.IsStaggering())
        {
            return;
        }

        float repeatInterval = ResolveHoldRepeatInterval(attack, activeAttack);
        if (activeAttack.NextHoldRepeatTime <= 0f)
        {
            activeAttack.NextHoldRepeatTime = Time.time + repeatInterval;
            return;
        }

        if (Time.time < activeAttack.NextHoldRepeatTime)
        {
            return;
        }

        bool consumeRepeatedStartResources = activeAttack.ProjectileTriggered;
        activeAttack.NextHoldRepeatTime = Time.time + repeatInterval;
        if (!TryPrepareConfiguredAttack(
                attack,
                activeAttack,
                out ConfiguredAmmoContext ammoContext) ||
            (consumeRepeatedStartResources &&
             !TryConsumeRepeatedProjectileStartResources(attack)) ||
            !TriggerPreparedConfiguredAttack(attack, activeAttack, ammoContext))
        {
            attack.Stop();
        }
    }

    internal static bool TryExecuteBurstFireRepeat(
        Attack attack,
        out Vector3 spawnPoint,
        out Vector3 rawAimDirection)
    {
        spawnPoint = Vector3.zero;
        rawAimDirection = Vector3.zero;
        if (!SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) ||
            activeAttack == null ||
            !activeAttack.BurstRuntimeStarted ||
            activeAttack.Definition.Behavior is not ProjectileSecondaryBehavior
            {
                Preset: SecondaryAttackPreset.Burst
            } ||
            !ProjectileRuntimeSystem.IsBurstFireControllerActive(attack))
        {
            return false;
        }

        if (TryPrepareConfiguredAttack(
                attack,
                activeAttack,
                out ConfiguredAmmoContext ammoContext))
        {
            if (!ConsumePerBurstResourcesIfNeeded(attack))
            {
                return false;
            }

            ProjectileRuntimeSystem.OrientPlayerBodyToCurrentAim(attack);
            CommitConfiguredAmmo(attack, ammoContext);
            attack.ProjectileAttackTriggered();
            activeAttack.ProjectileTriggered = true;

            if (!ProjectileRuntimeSystem.TryFireBurstShot(
                    attack,
                    activeAttack.Definition,
                    out spawnPoint,
                    out rawAimDirection))
            {
                return false;
            }

            ApplyAttackTriggerSideEffects(attack);
            return true;
        }

        attack.Stop();
        return false;
    }

    private static bool TriggerConfiguredAttack(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        if (!TryPrepareConfiguredAttack(
                attack,
                activeAttack,
                out ConfiguredAmmoContext ammoContext))
        {
            return false;
        }

        return TriggerPreparedConfiguredAttack(attack, activeAttack, ammoContext);
    }

    private static bool TryPrepareConfiguredAttack(
        Attack attack,
        ActiveSecondaryAttack activeAttack,
        out ConfiguredAmmoContext ammoContext)
    {
        ammoContext = default;
        if (!IsSupportedConfiguredAttackType(attack.m_attackType))
        {
            return false;
        }

        ProjectileSecondaryBehavior? projectileBehavior =
            activeAttack.Definition.Behavior as ProjectileSecondaryBehavior;
        if (attack.m_attackType == Attack.AttackType.Projectile &&
            projectileBehavior != null &&
            !ProjectileRuntimeSystem.IsBurstFireControllerActive(attack) &&
            !RangedSecondaryCooldownSystem.CanUse(attack, projectileBehavior))
        {
            return false;
        }

        if (!TryPrepareConfiguredAmmo(
                attack,
                activeAttack.Definition,
                out ammoContext))
        {
            return false;
        }

        if (attack.m_attackType != Attack.AttackType.Projectile ||
            projectileBehavior == null)
        {
            return true;
        }

        if (!ProjectileRuntimeSystem.CanStartBurstPreset(
                attack,
                activeAttack.Definition,
                projectileBehavior.Preset,
                ammoContext.AmmoItem))
        {
            return false;
        }

        return !attack.m_perBurstResourceUsage ||
               HasAttackResources(
                   attack,
                   stopAttackOnFailure: false,
                   flashHudOnFailure: true);
    }

    private static bool TriggerPreparedConfiguredAttack(
        Attack attack,
        ActiveSecondaryAttack activeAttack,
        ConfiguredAmmoContext ammoContext)
    {
        if (attack.m_attackType == Attack.AttackType.Projectile &&
            activeAttack.Definition.Behavior is ProjectileSecondaryBehavior
            {
                Preset: SecondaryAttackPreset.Burst
            })
        {
            ProjectileRuntimeSystem.OrientPlayerBodyToCurrentAim(attack);
        }

        CommitConfiguredAmmo(attack, ammoContext);
        switch (attack.m_attackType)
        {
            case Attack.AttackType.Horizontal:
            case Attack.AttackType.Vertical:
                attack.DoMeleeAttack();
                break;
            case Attack.AttackType.Area:
                attack.DoAreaAttack();
                break;
            case Attack.AttackType.Projectile:
                attack.ProjectileAttackTriggered();
                activeAttack.ProjectileTriggered = true;
                UpdateHoldRepeatAfterProjectileTrigger(attack, activeAttack);
                break;
            case Attack.AttackType.None:
                attack.DoNonAttack();
                break;
            default:
                return false;
        }

        ApplyAttackTriggerSideEffects(attack);
        return true;
    }

    private static bool IsSupportedConfiguredAttackType(Attack.AttackType attackType)
    {
        return attackType is
            Attack.AttackType.Horizontal or
            Attack.AttackType.Vertical or
            Attack.AttackType.Area or
            Attack.AttackType.Projectile or
            Attack.AttackType.None;
    }

    private static void ApplyAttackTriggerSideEffects(Attack attack)
    {
        if (attack.m_toggleFlying)
        {
            if (attack.m_character.IsFlying())
            {
                attack.m_character.Land();
            }
            else
            {
                attack.m_character.TakeOff();
            }
        }

        if (attack.m_recoilPushback != 0f)
        {
            attack.m_character.ApplyPushback(-attack.m_character.transform.forward, attack.m_recoilPushback);
        }

        if (attack.m_selfDamage > 0)
        {
            HitData selfHit = new();
            selfHit.m_damage.m_damage = attack.m_selfDamage;
            attack.m_character.Damage(selfHit);
        }

        if (attack.m_consumeItem)
        {
            attack.ConsumeItem();
        }

        if (attack.m_requiresReload)
        {
            if (ProjectileRuntimeSystem.ShouldDeferBurstFireReloadReset(attack))
            {
                return;
            }
            attack.m_character.ResetLoadedWeapon();
        }
    }

    private static bool IsHoldRepeatProjectileAttack(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        return activeAttack.Definition.BehaviorType == SecondaryAttackBehaviorType.Projectile &&
               activeAttack.Definition.Behavior is not ProjectileSecondaryBehavior { Preset: SecondaryAttackPreset.Burst } &&
               attack.m_attackType == Attack.AttackType.Projectile &&
               attack.m_loopingAttack &&
               string.Equals(attack.m_attackAnimation, StaffRapidFireAnimation, StringComparison.Ordinal);
    }

    private static void UpdateHoldRepeatAfterProjectileTrigger(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        if (!IsHoldRepeatProjectileAttack(attack, activeAttack))
        {
            return;
        }

        activeAttack.NextHoldRepeatTime = Time.time + ResolveHoldRepeatInterval(attack, activeAttack);
    }

    private static float ResolveHoldRepeatInterval(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        if (activeAttack.Definition.Behavior is ProjectileSecondaryBehavior projectileBehavior)
        {
            return Mathf.Max(0.01f, projectileBehavior.HoldRepeatInterval);
        }

        return FallbackHoldRepeatInterval;
    }

    private static bool TryConsumeRepeatedProjectileStartResources(Attack attack)
    {
        if (attack.m_perBurstResourceUsage)
        {
            return true;
        }

        return TryConsumeAttackResources(attack, stopAttackOnFailure: false, flashHudOnFailure: true);
    }

    private static bool TryConsumeAttackResources(
        Attack attack,
        bool stopAttackOnFailure,
        bool flashHudOnFailure)
    {
        if (!HasAttackResources(attack, stopAttackOnFailure, flashHudOnFailure))
        {
            return false;
        }

        float attackStamina = attack.GetAttackStamina();
        float attackEitr = attack.GetAttackEitr();
        float attackHealth = attack.GetAttackHealth();

        if (attackStamina > 0f)
        {
            attack.m_character.UseStamina(attackStamina);
        }

        if (attackEitr > 0f)
        {
            attack.m_character.UseEitr(attackEitr);
        }

        if (attackHealth > 0f)
        {
            attack.m_character.UseHealth(Mathf.Min(attack.m_character.GetHealth() - 1f, attackHealth));
        }

        return true;
    }

    private static bool HasAttackResources(
        Attack attack,
        bool stopAttackOnFailure,
        bool flashHudOnFailure)
    {
        float attackStamina = attack.GetAttackStamina();
        float attackEitr = attack.GetAttackEitr();
        float attackHealth = attack.GetAttackHealth();

        if (attackStamina > 0f && !attack.m_character.HaveStamina(attackStamina))
        {
            return HandleResourceFailure(
                attack,
                stopAttackOnFailure,
                flashStamina: flashHudOnFailure);
        }

        if (attackEitr > 0f && !attack.m_character.HaveEitr(attackEitr))
        {
            return HandleResourceFailure(attack, stopAttackOnFailure);
        }

        if (attackHealth > 0f &&
            !attack.m_character.HaveHealth(attackHealth) &&
            attack.m_attackHealthLowBlockUse)
        {
            return HandleResourceFailure(
                attack,
                stopAttackOnFailure,
                flashHealth: flashHudOnFailure);
        }

        return true;
    }

    private static bool HandleResourceFailure(
        Attack attack,
        bool stopAttack,
        bool flashStamina = false,
        bool flashHealth = false)
    {
        if (stopAttack)
        {
            attack.Stop();
        }
        else if (attack.m_character.IsPlayer())
        {
            if (flashStamina)
            {
                Hud.instance?.StaminaBarEmptyFlash();
            }
            else if (flashHealth)
            {
                Hud.instance?.FlashHealthBar();
            }
        }

        return false;
    }

    internal static bool TryHandleCustomProjectileBurst(Attack attack)
    {
        if (!SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) || activeAttack == null)
        {
            return false;
        }

        if (activeAttack.Definition.Behavior is not ProjectileSecondaryBehavior projectileBehavior)
        {
            return false;
        }

        bool burstFireControllerActive = ProjectileRuntimeSystem.IsBurstFireControllerActive(attack);
        bool isBurstPreset = projectileBehavior.Preset == SecondaryAttackPreset.Burst;
        if (isBurstPreset && activeAttack.BurstRuntimeStarted)
        {
            return true;
        }

        if (!RangedSecondaryCooldownSystem.CanUse(attack, projectileBehavior))
        {
            if (!burstFireControllerActive)
            {
                attack.Stop();
            }

            return true;
        }

        if (!ProjectileRuntimeSystem.CanStartBurstPreset(
                attack,
                activeAttack.Definition,
                projectileBehavior.Preset,
                attack.m_ammoItem))
        {
            attack.Stop();
            return true;
        }

        if (!ConsumePerBurstResourcesIfNeeded(attack))
        {
            return true;
        }

        bool handled = ProjectileRuntimeSystem.TryHandleBurstPreset(attack, activeAttack.Definition, projectileBehavior.Preset);
        if (handled)
        {
            if (isBurstPreset)
            {
                activeAttack.BurstRuntimeStarted = true;
            }

            RangedSecondaryCooldownSystem.StartCooldown(attack, projectileBehavior);
        }
        else if (!burstFireControllerActive)
        {
            attack.Stop();
        }

        return true;
    }

    private static bool TryPrepareConfiguredAmmo(
        Attack attack,
        SecondaryAttackDefinition definition,
        out ConfiguredAmmoContext context)
    {
        context = default;
        ProjectileSecondaryBehavior? projectileBehavior = definition.Behavior as ProjectileSecondaryBehavior;
        string ammoType = attack.m_weapon.m_shared.m_ammoType;
        if (string.IsNullOrWhiteSpace(ammoType))
        {
            return true;
        }

        Inventory? inventory = attack.m_character.GetInventory();
        return TrySelectConfiguredAmmo(
            attack.m_character,
            attack.m_weapon,
            inventory,
            ammoType,
            projectileBehavior?.AmmoConsumption ?? 0,
            out context);
    }

    private static bool TrySelectConfiguredAmmo(
        Humanoid character,
        ItemDrop.ItemData weapon,
        Inventory? inventory,
        string ammoType,
        int configuredRemovalCount,
        out ConfiguredAmmoContext context)
    {
        context = default;
        ItemDrop.ItemData? ammoItem =
            FindConfiguredAmmo(character, weapon, inventory, ammoType);
        int removalCount = Mathf.Max(0, configuredRemovalCount);
        if (ammoItem == null ||
            removalCount > 0 &&
            (inventory == null || CountAmmo(inventory, ammoItem) < removalCount))
        {
            character.Message(
                MessageHud.MessageType.Center,
                "$msg_outof " + ammoType);
            return false;
        }

        context = new ConfiguredAmmoContext(inventory, ammoItem, removalCount);
        return true;
    }

    private static ItemDrop.ItemData? FindConfiguredAmmo(
        Humanoid character,
        ItemDrop.ItemData weapon,
        Inventory? inventory,
        string ammoType)
    {
        ItemDrop.ItemData? ammoItem = Attack.FindAmmo(character, weapon);
        if (ammoItem != null && IsAmmoItemForType(ammoItem, ammoType))
        {
            return ammoItem;
        }

        return inventory?.GetAllItems()
            .FirstOrDefault(item => IsAmmoItemForType(item, ammoType));
    }

    private static void CommitConfiguredAmmo(
        Attack attack,
        ConfiguredAmmoContext context)
    {
        attack.m_ammoItem = context.AmmoItem;
        attack.m_lastUsedAmmo = context.AmmoItem;
        if (context.Inventory == null ||
            context.AmmoItem == null ||
            context.RemovalCount <= 0)
        {
            return;
        }

        RemoveAmmo(
            context.Inventory,
            context.AmmoItem,
            context.RemovalCount);
    }

    private static int CountAmmo(
        Inventory inventory,
        ItemDrop.ItemData selectedAmmo)
    {
        return inventory.GetAllItems()
            .Where(item => IsSameAmmoPrefab(item, selectedAmmo))
            .Sum(item => item.m_stack);
    }

    private static void RemoveAmmo(
        Inventory inventory,
        ItemDrop.ItemData selectedAmmo,
        int amount)
    {
        int selectedRemoval = Mathf.Min(selectedAmmo.m_stack, amount);
        if (selectedRemoval > 0)
        {
            inventory.RemoveItem(selectedAmmo, selectedRemoval);
            amount -= selectedRemoval;
        }

        foreach (ItemDrop.ItemData item in inventory.GetAllItems()
                     .Where(item => !ReferenceEquals(item, selectedAmmo) &&
                                    IsSameAmmoPrefab(item, selectedAmmo))
                     .ToList())
        {
            if (amount <= 0)
            {
                return;
            }

            int removeCount = Mathf.Min(item.m_stack, amount);
            inventory.RemoveItem(item, removeCount);
            amount -= removeCount;
        }
    }

    private static bool IsSameAmmoPrefab(
        ItemDrop.ItemData? candidate,
        ItemDrop.ItemData? selectedAmmo)
    {
        if (candidate == null || selectedAmmo == null)
        {
            return false;
        }

        if (ReferenceEquals(candidate, selectedAmmo))
        {
            return true;
        }

        return candidate.m_dropPrefab != null &&
               selectedAmmo.m_dropPrefab != null &&
               string.Equals(
                   candidate.m_dropPrefab.name,
                   selectedAmmo.m_dropPrefab.name,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAmmoItemForType(ItemDrop.ItemData? item, string ammoType)
    {
        if (item?.m_shared == null || item.m_shared.m_ammoType != ammoType)
        {
            return false;
        }

        return item.m_shared.m_itemType is ItemDrop.ItemData.ItemType.Ammo or ItemDrop.ItemData.ItemType.AmmoNonEquipable;
    }

    private static bool ConsumePerBurstResourcesIfNeeded(Attack attack)
    {
        if (!attack.m_perBurstResourceUsage)
        {
            return true;
        }

        return TryConsumeAttackResources(attack, stopAttackOnFailure: true, flashHudOnFailure: false);
    }

    private readonly struct ConfiguredAmmoContext
    {
        internal ConfiguredAmmoContext(
            Inventory? inventory,
            ItemDrop.ItemData? ammoItem,
            int removalCount)
        {
            Inventory = inventory;
            AmmoItem = ammoItem;
            RemovalCount = removalCount;
        }

        internal Inventory? Inventory { get; }

        internal ItemDrop.ItemData? AmmoItem { get; }

        internal int RemovalCount { get; }
    }
}
