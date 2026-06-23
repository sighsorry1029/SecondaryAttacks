using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackRuntimeFacade
{
    internal const string ReloadLoadedCustomDataKey = "SecondaryAttacks.ReloadLoaded";

    private const string StaffRapidFireAnimation = "staff_rapidfire";
    private const float FallbackHoldRepeatInterval = 0.2f;

    [Conditional("SECONDARY_ATTACKS_DEBUG_LOGGING")]
    internal static void LogRangedDebug(string message)
    {
    }

    internal static string DescribeItemForRangedDebug(ItemDrop.ItemData? item)
    {
        if (item == null)
        {
            return "<null>";
        }

        string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : "<no-prefab>";
        string sharedName = item.m_shared?.m_name ?? "<no-shared>";
        string itemType = item.m_shared != null ? item.m_shared.m_itemType.ToString() : "<no-type>";
        string ammoType = item.m_shared != null ? item.m_shared.m_ammoType : "";
        string loaded = item.m_customData != null && item.m_customData.TryGetValue(ReloadLoadedCustomDataKey, out string loadedValue)
            ? loadedValue
            : "0";
        return $"{prefab}/{sharedName} type={itemType} stack={item.m_stack} quality={item.m_quality} durability={item.m_durability:0.###} ammoType={ammoType} loadedData={loaded}";
    }

    internal static string DescribeAttackForRangedDebug(Attack? attack)
    {
        if (attack == null)
        {
            return "<null>";
        }

        string loaded = attack.m_character is Player player && attack.m_weapon != null
            ? (player.m_weaponLoaded == attack.m_weapon).ToString()
            : attack.m_character?.IsWeaponLoaded().ToString() ?? "<no-character>";
        return
            $"animation={attack.m_attackAnimation ?? "<null>"} type={attack.m_attackType} projectile={attack.m_attackProjectile?.name ?? "<null>"} consumeItem={attack.m_consumeItem} requiresReload={attack.m_requiresReload} loaded={loaded} projectiles={attack.m_projectiles} bursts={attack.m_projectileBursts} perBurst={attack.m_perBurstResourceUsage} weapon=[{DescribeItemForRangedDebug(attack.m_weapon)}] ammo=[{DescribeItemForRangedDebug(attack.m_ammoItem)}] lastAmmo=[{DescribeItemForRangedDebug(attack.m_lastUsedAmmo)}]";
    }

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

    internal static bool TryGetCurrentWeaponDefinition(out SecondaryAttackDefinition definition, out bool secondaryAttack)
    {
        definition = null!;
        secondaryAttack = false;
        Player? localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            return false;
        }

        Attack? currentAttack = ((Humanoid)localPlayer).m_currentAttack;
        if (currentAttack?.m_weapon?.m_dropPrefab == null)
        {
            return false;
        }

        secondaryAttack = ((Humanoid)localPlayer).m_currentAttackIsSecondary;
        return SecondaryAttackFacade.CurrentAppliedWorldSnapshot.DefinitionsByPrefabName.TryGetValue(currentAttack.m_weapon.m_dropPrefab.name, out definition!);
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

        if (projectileBehavior.AmmoConsumption <= 0 || string.IsNullOrWhiteSpace(weapon.m_shared.m_ammoType))
        {
            return true;
        }

        if (humanoid.GetInventory() != null &&
            CountAmmo(humanoid.GetInventory(), weapon.m_shared.m_ammoType) >= projectileBehavior.AmmoConsumption)
        {
            return true;
        }

        humanoid.Message(MessageHud.MessageType.Center, "$msg_outof " + weapon.m_shared.m_ammoType);
        return false;
    }

    internal static bool BeginProjectileHitContext(Projectile projectile, Collider collider, UnityEngine.Vector3 hitPoint, bool water, UnityEngine.Vector3 normal)
    {
        if (projectile == null || collider == null)
        {
            return false;
        }

        ProjectileAttackAttribution? attribution = null;
        SecondaryAttackRuntimeContext.TryGetProjectileAttackAttribution(projectile, out attribution);
        SecondaryAttackRuntimeContext.PushProjectileHitContext(new ProjectileHitContext(projectile, collider, hitPoint, water, normal, attribution));
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

        if (!SecondaryAttackRuntimeContext.TryPeekProjectileHitContext(out ProjectileHitContext? context) || context == null)
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
                : SecondaryAttackAdrenalineSystem.ResolveDefinitionFactor(definition);
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

        GreatSwordSkillScalingSystem.ApplyTrailScaleForActiveDefinition(attack, definition);
        if (!needsActiveAttack)
        {
            return;
        }

        ActiveSecondaryAttack activeAttack = new(definition);
        SecondaryAttackRuntimeContext.SetActiveAttack(attack, activeAttack);
        SecondaryAttackAdrenalineSystem.Reset(attack);
        if (definition.CleavingThrust != null)
        {
            GreatSwordSkillScalingSystem.ApplyTrailScaleForActiveDefinition(attack, definition);
        }

        if (definition.RiftTrail != null)
        {
            RiftTrailSystem.BeginSampling(attack, definition);
        }

        if (definition.BehaviorType == SecondaryAttackBehaviorType.CopiedSecondary &&
            (definition.OnProjectileHit != null || definition.Boomerang != null))
        {
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"active copied attack registered weapon={weapon.m_dropPrefab.name} preset={definition.OnProjectileHit?.Preset ?? "boomerang"} animation={attack.m_attackAnimation} attackType={attack.m_attackType} projectile={attack.m_attackProjectile?.name ?? "<null>"}.");
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
                    activeAttack.Definition.CleavingThrust.PresetCooldown,
                    out _))
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
                    activeAttack.Definition.Aftershock.PresetCooldown,
                    out _))
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

        TriggerConfiguredAttack(attack, activeAttack);
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
                activeAttack.Definition.RiftTrail.PresetCooldown,
                out _))
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
                activeAttack.Definition.FractureLine.PresetCooldown,
                out _))
        {
            return;
        }

        activeAttack.Triggered = true;
        FractureLineSystem.Trigger(attack, activeAttack.Definition);
    }

    private static bool TriggerCleavingThrust(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        if (!ConsumeConfiguredAmmo(attack, activeAttack.Definition))
        {
            return false;
        }

        activeAttack.Triggered = true;
        GreatSwordSkillScalingSystem.ApplyCleavingThrustTrailScaleForTriggeredAttack(attack, activeAttack.Definition);
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
        if ((consumeRepeatedStartResources && !TryConsumeRepeatedProjectileStartResources(attack)) ||
            !TriggerConfiguredAttack(attack, activeAttack))
        {
            attack.Stop();
        }
    }

    private static bool TriggerConfiguredAttack(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        if (!ConsumeConfiguredAmmo(attack, activeAttack.Definition))
        {
            return false;
        }

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
        }

        ApplyAttackTriggerSideEffects(attack);
        return true;
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
            LogRangedDebug($"attack side effect consumeItem before attack={DescribeAttackForRangedDebug(attack)}");
            attack.ConsumeItem();
            LogRangedDebug($"attack side effect consumeItem after attack={DescribeAttackForRangedDebug(attack)}");
        }

        if (attack.m_requiresReload)
        {
            if (ProjectileRuntimeSystem.ShouldDeferBurstFireReloadReset(attack))
            {
                LogRangedDebug($"attack side effect reset loaded deferred attack={DescribeAttackForRangedDebug(attack)}");
                return;
            }

            LogRangedDebug($"attack side effect reset loaded before attack={DescribeAttackForRangedDebug(attack)}");
            attack.m_character.ResetLoadedWeapon();
            LogRangedDebug($"attack side effect reset loaded after attack={DescribeAttackForRangedDebug(attack)}");
        }
    }

    private static bool IsHoldRepeatProjectileAttack(Attack attack, ActiveSecondaryAttack activeAttack)
    {
        return activeAttack.Definition.BehaviorType == SecondaryAttackBehaviorType.Projectile &&
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

        float attackStamina = attack.GetAttackStamina();
        if (attackStamina > 0f)
        {
            if (!attack.m_character.HaveStamina(attackStamina))
            {
                if (attack.m_character.IsPlayer())
                {
                    Hud.instance?.StaminaBarEmptyFlash();
                }

                return false;
            }

            attack.m_character.UseStamina(attackStamina);
        }

        float attackEitr = attack.GetAttackEitr();
        if (attackEitr > 0f)
        {
            if (!attack.m_character.HaveEitr(attackEitr))
            {
                return false;
            }

            attack.m_character.UseEitr(attackEitr);
        }

        float attackHealth = attack.GetAttackHealth();
        if (attackHealth > 0f)
        {
            if (!attack.m_character.HaveHealth(attackHealth) && attack.m_attackHealthLowBlockUse)
            {
                if (attack.m_character.IsPlayer())
                {
                    Hud.instance?.FlashHealthBar();
                }

                return false;
            }

            attack.m_character.UseHealth(Mathf.Min(attack.m_character.GetHealth() - 1f, attackHealth));
        }

        return true;
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
        if (!RangedSecondaryCooldownSystem.CanUse(attack, projectileBehavior))
        {
            if (!burstFireControllerActive)
            {
                attack.Stop();
            }

            return true;
        }

        if (!ProjectileRuntimeSystem.CanStartBurstPreset(attack, activeAttack.Definition, projectileBehavior.Preset))
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
            RangedSecondaryCooldownSystem.StartCooldown(attack, projectileBehavior);
        }

        return handled;
    }

    private static bool ConsumeConfiguredAmmo(Attack attack, SecondaryAttackDefinition definition)
    {
        attack.m_ammoItem = null;
        attack.m_lastUsedAmmo = null;
        ProjectileSecondaryBehavior? projectileBehavior = definition.Behavior as ProjectileSecondaryBehavior;

        if (string.IsNullOrWhiteSpace(attack.m_weapon.m_shared.m_ammoType))
        {
            LogRangedDebug($"ammo skipped no ammo type definition={definition.PrefabName} attack={DescribeAttackForRangedDebug(attack)}");
            return true;
        }

        int ammoCountBefore = CountAmmo(attack.m_character.GetInventory(), attack.m_weapon.m_shared.m_ammoType);
        LogRangedDebug($"ammo check definition={definition.PrefabName} ammoType={attack.m_weapon.m_shared.m_ammoType} configuredConsumption={projectileBehavior?.AmmoConsumption ?? 0} countBefore={ammoCountBefore} attack={DescribeAttackForRangedDebug(attack)}");

        ItemDrop.ItemData ammoItem = Attack.FindAmmo(attack.m_character, attack.m_weapon);
        if (ammoItem != null && !IsAmmoItemForType(ammoItem, attack.m_weapon.m_shared.m_ammoType))
        {
            LogRangedDebug($"ammo selected item rejected because it is not an ammo item definition={definition.PrefabName} selected=[{DescribeItemForRangedDebug(ammoItem)}]");
            ammoItem = attack.m_character.GetInventory()
                .GetAllItems()
                .FirstOrDefault(item => IsAmmoItemForType(item, attack.m_weapon.m_shared.m_ammoType));
        }

        if (ammoItem == null)
        {
            LogRangedDebug($"ammo failed no matching ammo definition={definition.PrefabName} ammoType={attack.m_weapon.m_shared.m_ammoType} countBefore={ammoCountBefore} attack={DescribeAttackForRangedDebug(attack)}");
            attack.m_character.Message(MessageHud.MessageType.Center, "$msg_outof " + attack.m_weapon.m_shared.m_ammoType);
            return false;
        }

        attack.m_ammoItem = ammoItem;
        attack.m_lastUsedAmmo = ammoItem;
        LogRangedDebug($"ammo selected definition={definition.PrefabName} ammo=[{DescribeItemForRangedDebug(ammoItem)}] attack={DescribeAttackForRangedDebug(attack)}");

        if (projectileBehavior == null || projectileBehavior.AmmoConsumption <= 0)
        {
            return true;
        }

        if (ammoCountBefore < projectileBehavior.AmmoConsumption)
        {
            LogRangedDebug($"ammo failed insufficient definition={definition.PrefabName} ammoType={attack.m_weapon.m_shared.m_ammoType} required={projectileBehavior.AmmoConsumption} countBefore={ammoCountBefore}");
            attack.m_character.Message(MessageHud.MessageType.Center, "$msg_outof " + attack.m_weapon.m_shared.m_ammoType);
            return false;
        }

        RemoveAmmo(attack.m_character.GetInventory(), attack.m_weapon.m_shared.m_ammoType, projectileBehavior.AmmoConsumption);
        int ammoCountAfter = CountAmmo(attack.m_character.GetInventory(), attack.m_weapon.m_shared.m_ammoType);
        LogRangedDebug($"ammo consumed definition={definition.PrefabName} ammoType={attack.m_weapon.m_shared.m_ammoType} consumed={projectileBehavior.AmmoConsumption} countBefore={ammoCountBefore} countAfter={ammoCountAfter} attack={DescribeAttackForRangedDebug(attack)}");

        return true;
    }

    private static int CountAmmo(Inventory inventory, string ammoType)
    {
        return inventory.GetAllItems()
            .Where(item => IsAmmoItemForType(item, ammoType))
            .Sum(item => item.m_stack);
    }

    private static void RemoveAmmo(Inventory inventory, string ammoType, int amount)
    {
        foreach (ItemDrop.ItemData item in inventory.GetAllItems().Where(item => IsAmmoItemForType(item, ammoType)).ToList())
        {
            if (amount <= 0)
            {
                return;
            }

            int removeCount = Mathf.Min(item.m_stack, amount);
            LogRangedDebug($"ammo remove item=[{DescribeItemForRangedDebug(item)}] removeCount={removeCount} remainingBefore={amount}");
            inventory.RemoveItem(item, removeCount);
            amount -= removeCount;
        }
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

        float attackStamina = attack.GetAttackStamina();
        if (attackStamina > 0f)
        {
            if (!attack.m_character.HaveStamina(attackStamina))
            {
                attack.Stop();
                return false;
            }

            attack.m_character.UseStamina(attackStamina);
        }

        float attackEitr = attack.GetAttackEitr();
        if (attackEitr > 0f)
        {
            if (!attack.m_character.HaveEitr(attackEitr))
            {
                attack.Stop();
                return false;
            }

            attack.m_character.UseEitr(attackEitr);
        }

        float attackHealth = attack.GetAttackHealth();
        if (attackHealth > 0f)
        {
            if (!attack.m_character.HaveHealth(attackHealth) && attack.m_attackHealthLowBlockUse)
            {
                attack.Stop();
                return false;
            }

            attack.m_character.UseHealth(Mathf.Min(attack.m_character.GetHealth() - 1f, attackHealth));
        }

        return true;
    }
}
