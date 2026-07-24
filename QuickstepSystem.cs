using System;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class QuickstepSystem
{
    internal const float DashAcceleration = 200f;
    internal const float DashDuration = 0.25f;
    internal const float ShieldInvincibilityDuration = 0.15f;
    internal const float CooldownDuration = 0.5f;
    internal const float StaminaUsageFactor = 0.6f;

    private static bool _initialized;
    private static bool _externalQuickstepLoaded;
    private static bool _conflictWarningLogged;
    private static bool _disabledAfterRuntimeError;
    private static QuickstepController? _controller;

    internal static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _externalQuickstepLoaded = IsExternalQuickstepLoaded();
        _conflictWarningLogged = false;
        _disabledAfterRuntimeError = false;
        SecondaryAttacksPlugin.QuickstepEnabled.SettingChanged += OnEnabledSettingChanged;
        WarnAboutConflictIfNeeded();
    }

    internal static void Dispose()
    {
        if (_initialized)
        {
            SecondaryAttacksPlugin.QuickstepEnabled.SettingChanged -= OnEnabledSettingChanged;
        }

        _initialized = false;
        _controller?.ResetState(applyExitDamping: false, clearDodgeHandoff: true);
        _controller = null;
    }

    internal static bool IsActive(Player player)
    {
        return player != null &&
               _controller != null &&
               _controller.IsFor(player) &&
               _controller.Active;
    }

    internal static bool HandleUpdateDodge(Player player, float dt)
    {
        try
        {
            return HandleUpdateDodgeCore(player, dt);
        }
        catch (Exception exception)
        {
            DisableAfterRuntimeError(player, exception);
            return true;
        }
    }

    internal static void AdjustDodgeStaminaCost(Player player, ref float staminaUse)
    {
        if (player != null &&
            _controller != null &&
            _controller.IsFor(player) &&
            (_controller.Active || _controller.DodgeHandoffActive))
        {
            staminaUse = _controller.DodgeStaminaUse;
        }
    }

    internal static void ObserveDodgeUpdate(Player player)
    {
        if (_controller != null && _controller.IsFor(player))
        {
            _controller.UpdateDodgeHandoff();
        }
    }

    internal static void Stop(Player player, bool applyExitDamping = false)
    {
        if (_controller != null && _controller.IsFor(player))
        {
            _controller.Stop(applyExitDamping, startCooldown: false);
        }
    }

    internal static void Reset(Player player)
    {
        if (_controller == null || !_controller.IsFor(player))
        {
            return;
        }

        _controller.ResetState(applyExitDamping: false, clearDodgeHandoff: true);
    }

    internal static void NotifyControllerDestroyed(QuickstepController controller)
    {
        if (_controller == controller)
        {
            _controller = null;
        }
    }

    private static bool HandleUpdateDodgeCore(Player player, float dt)
    {
        QuickstepController? controller = _controller != null && _controller.IsFor(player)
            ? _controller
            : null;
        controller?.UpdateDodgeHandoff();
        controller?.RetryPendingCleanup();

        if (!IsFeatureAvailable(player))
        {
            Stop(player, applyExitDamping: true);
            return true;
        }

        if (!HasCharacterAuthority(player))
        {
            Stop(player);
            return true;
        }

        if (!HasEligibleEquippedWeapon(player))
        {
            if (_controller != null && _controller.IsFor(player) && _controller.Active)
            {
                _controller.Stop(applyExitDamping: true, startCooldown: true);
            }

            return true;
        }

        controller = GetExistingController(player);
        if (controller?.Active == true)
        {
            if (player.IsDead() || player.IsTeleporting())
            {
                controller.Stop(applyExitDamping: false, startCooldown: false);
                return true;
            }

            if (player.m_queuedDodgeTimer > 0f)
            {
                if (!CanHandoffToDodge(player))
                {
                    player.m_queuedDodgeTimer = 0f;
                }
                else
                {
                    float handoffStaminaUse = controller.DodgeStaminaUse;
                    if (!player.HaveStamina(handoffStaminaUse))
                    {
                        player.m_queuedDodgeTimer = 0f;
                        if (Hud.instance != null)
                        {
                            Hud.instance.StaminaBarEmptyFlash();
                        }
                    }
                    else
                    {
                        controller.Stop(applyExitDamping: true, startCooldown: true);
                        controller.BeginDodgeHandoff(handoffStaminaUse);
                        return true;
                    }
                }
            }

            controller.Tick(dt);
            return false;
        }

        if (player.InDodge() ||
            controller?.DodgeHandoffActive == true ||
            controller?.CoolingDown == true ||
            player.m_queuedDodgeTimer <= 0f ||
            !CanStart(player))
        {
            return true;
        }

        float staminaUse = Mathf.Max(0f, player.GetDodgeStaminaUse() * StaminaUsageFactor);
        if (!player.HaveStamina(staminaUse))
        {
            player.m_queuedDodgeTimer = Mathf.Max(0f, player.m_queuedDodgeTimer - Mathf.Max(0f, dt));
            if (Hud.instance != null)
            {
                Hud.instance.StaminaBarEmptyFlash();
            }

            return false;
        }

        controller ??= GetOrAddController(player);
        controller.Begin(
            player.m_queuedDodgeDir,
            HasShieldEquipped(player),
            staminaUse);
        return false;
    }

    private static bool IsFeatureAvailable(Player player)
    {
        if (!_initialized ||
            player == null ||
            player != Player.m_localPlayer ||
            _disabledAfterRuntimeError ||
            SecondaryAttacksPlugin.QuickstepEnabled.Value != SecondaryAttacksPlugin.Toggle.On)
        {
            return false;
        }

        if (!_externalQuickstepLoaded)
        {
            _externalQuickstepLoaded = IsExternalQuickstepLoaded();
        }

        if (!_externalQuickstepLoaded)
        {
            return true;
        }

        WarnAboutConflictIfNeeded();
        return false;
    }

    private static bool HasCharacterAuthority(Player player)
    {
        return player.m_nview != null &&
               player.m_nview.IsValid() &&
               player.m_nview.IsOwner();
    }

    private static bool HasEligibleEquippedWeapon(Player player)
    {
        ItemDrop.ItemData? equippedItem = player.GetRightItem() ?? player.GetLeftItem();
        if (equippedItem?.m_shared == null ||
            equippedItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield)
        {
            return false;
        }

        return equippedItem.m_shared.m_skillType is Skills.SkillType.Knives or Skills.SkillType.Unarmed;
    }

    private static bool HasShieldEquipped(Player player)
    {
        return player.GetLeftItem()?.m_shared?.m_itemType == ItemDrop.ItemData.ItemType.Shield;
    }

    private static bool CanStart(Player player)
    {
        return player.IsOnGround() &&
               !player.IsDead() &&
               !player.IsTeleporting() &&
               !player.InAttack() &&
               !player.IsEncumbered() &&
               !player.InDodge() &&
               !player.IsStaggering();
    }

    private static bool CanHandoffToDodge(Player player)
    {
        return player.IsOnGround() &&
               !player.IsDead() &&
               !player.IsTeleporting() &&
               !player.InAttack() &&
               !player.IsEncumbered() &&
               !player.IsStaggering();
    }

    private static QuickstepController? GetExistingController(Player player)
    {
        if (_controller != null && _controller.IsFor(player))
        {
            return _controller;
        }

        QuickstepController? existing = player.GetComponent<QuickstepController>();
        if (existing != null)
        {
            existing.Initialize(player);
            _controller = existing;
        }

        return existing;
    }

    private static QuickstepController GetOrAddController(Player player)
    {
        QuickstepController? controller = GetExistingController(player);
        if (controller != null)
        {
            return controller;
        }

        controller = player.gameObject.AddComponent<QuickstepController>();
        controller.Initialize(player);
        _controller = controller;
        return controller;
    }

    private static bool IsExternalQuickstepLoaded()
    {
        return Chainloader.PluginInfos.ContainsKey(SecondaryAttacksPlugin.QuickstepPluginGuid);
    }

    private static void WarnAboutConflictIfNeeded()
    {
        if (!_externalQuickstepLoaded ||
            _conflictWarningLogged ||
            SecondaryAttacksPlugin.QuickstepEnabled.Value != SecondaryAttacksPlugin.Toggle.On)
        {
            return;
        }

        _conflictWarningLogged = true;
        SecondaryAttacksPlugin.ModLogger.LogWarning(
            $"Integrated Quickstep is disabled because {SecondaryAttacksPlugin.QuickstepPluginGuid} is loaded. " +
            "Disable or remove one Quickstep implementation to avoid competing Player.UpdateDodge patches.");
    }

    private static void OnEnabledSettingChanged(object? sender, EventArgs eventArgs)
    {
        if (SecondaryAttacksPlugin.QuickstepEnabled.Value == SecondaryAttacksPlugin.Toggle.On)
        {
            if (!_externalQuickstepLoaded)
            {
                _externalQuickstepLoaded = IsExternalQuickstepLoaded();
            }

            WarnAboutConflictIfNeeded();
            return;
        }

        _controller?.ResetState(applyExitDamping: true, clearDodgeHandoff: false);
        // Keep an in-flight vanilla handoff scaled until its roll and possible stamina refund finish.
    }

    private static void DisableAfterRuntimeError(Player player, Exception exception)
    {
        _disabledAfterRuntimeError = true;
        Stop(player, applyExitDamping: true);
        SecondaryAttacksPlugin.ModLogger.LogError(
            $"Integrated Quickstep was disabled for this session after a runtime error. {exception}");
    }

}

internal sealed class QuickstepController : MonoBehaviour
{
    private static readonly int EquippingAnimatorHash = ZSyncAnimation.GetHash("equipping");

    private Player? _player;
    private QuickstepPhase _phase;
    private Vector3 _direction;
    private Vector3 _startingVelocity;
    private float _dashElapsed;
    private float _activeElapsed;
    private float _readyAt;
    private float _dodgeStaminaUse;
    private float _originalAnimatorSpeed = 1f;
    private float _appliedAnimatorSpeed = 1f;
    private bool _hasShield;
    private bool _originalCrouchToggled;
    private bool _originalCrouchAnimator;
    private bool _appliedCrouchToggled;
    private bool _crouchTouched;
    private bool _originalEquipping;
    private bool _equippingTouched;
    private bool _animatorSpeedTouched;
    private bool _dodgeStateTouched;
    private bool _invincibleApplied;
    private bool _dodgeHandoff;
    private bool _dodgeHandoffObserved;
    private float _dodgeHandoffObserveDeadline;
    private float _dodgeHandoffHardDeadline;
    private bool _cleanupErrorLogged;

    internal bool Active { get; private set; }

    internal bool CoolingDown => Time.time < _readyAt;

    internal bool DodgeHandoffActive => _dodgeHandoff;

    internal float DodgeStaminaUse => _dodgeStaminaUse;

    private void Awake()
    {
        _player = GetComponent<Player>();
    }

    private void OnDisable()
    {
        ResetState(applyExitDamping: false, clearDodgeHandoff: true);
    }

    private void OnDestroy()
    {
        ResetState(applyExitDamping: false, clearDodgeHandoff: true);
        QuickstepSystem.NotifyControllerDestroyed(this);
    }

    internal void Initialize(Player player)
    {
        _player = player;
    }

    internal bool IsFor(Player player)
    {
        return _player == player;
    }

    internal void Begin(
        Vector3 direction,
        bool hasShield,
        float staminaUse)
    {
        Player player = RequirePlayer();
        if (Active)
        {
            return;
        }

        RetryPendingCleanup();
        if (CleanupPending)
        {
            throw new InvalidOperationException("Quickstep could not restore the previous player state.");
        }

        ClearDodgeHandoff();
        Vector3 horizontalDirection = direction;
        horizontalDirection.y = 0f;
        if (horizontalDirection.sqrMagnitude <= 0.0001f)
        {
            horizontalDirection = player.transform.forward;
            horizontalDirection.y = 0f;
        }

        _direction = horizontalDirection.normalized;
        _hasShield = hasShield;
        _dodgeStaminaUse = staminaUse;
        _dashElapsed = 0f;
        _activeElapsed = 0f;
        _startingVelocity = player.m_body.linearVelocity;
        _originalCrouchToggled = player.m_crouchToggled;
        _originalCrouchAnimator = player.m_animator.GetBool(Player.s_crouching);
        _originalEquipping = player.m_zanim.m_animator.GetBool(EquippingAnimatorHash);
        _originalAnimatorSpeed = player.m_zanim.m_animator.speed;
        _appliedAnimatorSpeed = _originalAnimatorSpeed;
        _crouchTouched = false;
        _equippingTouched = false;
        _animatorSpeedTouched = false;
        _dodgeStateTouched = false;
        _invincibleApplied = false;
        _cleanupErrorLogged = false;

        bool wasCrouching = player.IsCrouching();
        bool wasBlocking = player.IsBlocking();
        _appliedCrouchToggled = !wasCrouching;
        Active = true;

        player.ClearActionQueue();
        player.m_queuedDodgeTimer = 0f;
        player.m_inDodge = true;
        player.m_beenHitWhileDodging = false;
        _dodgeStateTouched = true;
        SetInvincible(value: true);

        if (!wasCrouching)
        {
            SetAnimatorSpeed(_originalAnimatorSpeed * 3f);
            SetEquipping(value: true);
        }

        if (wasBlocking)
        {
            ClearBlockingState();
            _phase = QuickstepPhase.AfterBlocking;
        }
        else
        {
            ApplyOppositeCrouch();
            _phase = QuickstepPhase.AfterCrouch;
        }

        player.AddNoise(3f);
        player.UseStamina(staminaUse);
        player.UpdateBodyFriction();
        player.m_dodgeEffects.Create(player.transform.position, Quaternion.identity, player.transform);
    }

    internal void Tick(float dt)
    {
        if (!Active)
        {
            return;
        }

        float safeDt = Mathf.Max(0f, dt);
        UpdateShieldInvincibility(safeDt);
        switch (_phase)
        {
            case QuickstepPhase.AfterBlocking:
                ApplyOppositeCrouch();
                _phase = QuickstepPhase.AfterCrouch;
                return;
            case QuickstepPhase.AfterCrouch:
                SetEquipping(value: false);
                _phase = QuickstepPhase.AfterEquipping;
                return;
            case QuickstepPhase.AfterEquipping:
                SetAnimatorSpeed(_originalAnimatorSpeed * 1.5f);
                _phase = QuickstepPhase.Dashing;
                break;
            case QuickstepPhase.Dashing:
                break;
            default:
                return;
        }

        TickDash(safeDt);
    }

    internal void Stop(bool applyExitDamping, bool startCooldown)
    {
        if (!Active && !CleanupPending)
        {
            return;
        }

        Active = false;
        _phase = QuickstepPhase.None;
        if (startCooldown)
        {
            _readyAt = Time.time + QuickstepSystem.CooldownDuration;
        }

        Exception? cleanupException = null;
        try
        {
            ClearOwnedDodgeState();
        }
        catch (Exception exception)
        {
            cleanupException = exception;
        }

        try
        {
            RestoreVisualState();
        }
        catch (Exception exception)
        {
            cleanupException ??= exception;
        }

        if (applyExitDamping)
        {
            try
            {
                ApplyExitDamping();
            }
            catch (Exception exception)
            {
                cleanupException ??= exception;
            }
        }

        if (cleanupException != null && !_cleanupErrorLogged)
        {
            _cleanupErrorLogged = true;
            SecondaryAttacksPlugin.ModLogger.LogError(
                $"Quickstep cleanup completed with an error: {cleanupException}");
        }
        else if (!CleanupPending)
        {
            _cleanupErrorLogged = false;
        }
    }

    private void ClearCooldown()
    {
        _readyAt = 0f;
    }

    internal void ResetState(bool applyExitDamping, bool clearDodgeHandoff)
    {
        Stop(applyExitDamping, startCooldown: false);
        ClearCooldown();
        if (clearDodgeHandoff)
        {
            ClearDodgeHandoff();
        }
    }

    internal void RetryPendingCleanup()
    {
        if (!Active && CleanupPending)
        {
            Stop(applyExitDamping: false, startCooldown: false);
        }
    }

    internal void BeginDodgeHandoff(float staminaUse)
    {
        _dodgeStaminaUse = Mathf.Max(0f, staminaUse);
        _dodgeHandoff = true;
        _dodgeHandoffObserved = false;
        _dodgeHandoffObserveDeadline = Time.time + 0.6f;
        _dodgeHandoffHardDeadline = Time.time + 3f;
    }

    internal void UpdateDodgeHandoff()
    {
        if (!_dodgeHandoff || _player == null)
        {
            return;
        }

        if (_player.IsDead() ||
            _player.IsTeleporting() ||
            Time.time >= _dodgeHandoffHardDeadline)
        {
            ClearDodgeHandoff();
            return;
        }

        bool inDodge = _player.InDodge();
        if (_dodgeHandoffObserved)
        {
            if (inDodge || _player.m_dodgeInvincible)
            {
                return;
            }

            ClearDodgeHandoff();
            return;
        }

        if (inDodge || _player.m_dodgeInvincible)
        {
            _dodgeHandoffObserved = true;
            return;
        }

        if (_player.m_queuedDodgeTimer <= 0f ||
            Time.time >= _dodgeHandoffObserveDeadline)
        {
            ClearDodgeHandoff();
        }
    }

    private void ClearDodgeHandoff()
    {
        _dodgeHandoff = false;
        _dodgeHandoffObserved = false;
        _dodgeHandoffObserveDeadline = 0f;
        _dodgeHandoffHardDeadline = 0f;
        _dodgeStaminaUse = 0f;
    }

    private void TickDash(float dt)
    {
        Player player = RequirePlayer();
        if (dt <= 0f)
        {
            return;
        }

        float remaining = Mathf.Max(0f, QuickstepSystem.DashDuration - _dashElapsed);
        float appliedFraction = Mathf.Clamp01(remaining / dt);
        if (appliedFraction > 0f)
        {
            player.m_body.AddForce(
                _direction * (QuickstepSystem.DashAcceleration * appliedFraction),
                ForceMode.Acceleration);
        }

        _dashElapsed += Mathf.Min(dt, remaining);

        if (_dashElapsed >= QuickstepSystem.DashDuration)
        {
            Stop(applyExitDamping: true, startCooldown: true);
        }
    }

    private void ApplyOppositeCrouch()
    {
        Player player = RequirePlayer();
        player.SetCrouch(_appliedCrouchToggled);
        player.m_zanim.SetBool(Player.s_crouching, _appliedCrouchToggled);
        _crouchTouched = true;
    }

    private void SetEquipping(bool value)
    {
        Player player = RequirePlayer();
        player.m_zanim.SetBool(EquippingAnimatorHash, value);
        _equippingTouched = true;
    }

    private void SetAnimatorSpeed(float speed)
    {
        Player player = RequirePlayer();
        _appliedAnimatorSpeed = speed;
        player.m_zanim.SetSpeed(speed);
        _animatorSpeedTouched = true;
    }

    private void SetInvincible(bool value)
    {
        Player player = RequirePlayer();
        if (_invincibleApplied == value &&
            player.m_dodgeInvincible == value &&
            player.m_dodgeInvincibleCached == value)
        {
            return;
        }

        player.m_dodgeInvincible = value;
        player.m_dodgeInvincibleCached = value;
        if (player.m_nview != null &&
            player.m_nview.IsValid() &&
            player.m_nview.IsOwner() &&
            player.m_nview.GetZDO() != null)
        {
            player.m_nview.GetZDO().Set(ZDOVars.s_dodgeinv, value);
        }

        _invincibleApplied = value;
    }

    private void ClearBlockingState()
    {
        Player player = RequirePlayer();
        player.m_internalBlockingState = false;
        if (player.m_nview != null &&
            player.m_nview.IsValid() &&
            player.m_nview.IsOwner() &&
            player.m_nview.GetZDO() != null)
        {
            player.m_nview.GetZDO().Set(ZDOVars.s_isBlockingHash, false);
        }

        player.m_zanim.SetBool(Humanoid.s_blocking, false);
    }

    private void ClearOwnedDodgeState()
    {
        if (_player == null || !_dodgeStateTouched)
        {
            return;
        }

        bool shouldWriteInvincibility = _invincibleApplied ||
                                        _player.m_dodgeInvincible ||
                                        _player.m_dodgeInvincibleCached;
        _player.m_dodgeInvincible = false;
        _player.m_dodgeInvincibleCached = false;
        _player.m_inDodge = false;
        _player.m_beenHitWhileDodging = false;
        if (_player.m_nview != null &&
            _player.m_nview.IsValid() &&
            _player.m_nview.IsOwner() &&
            _player.m_nview.GetZDO() != null &&
            shouldWriteInvincibility)
        {
            _player.m_nview.GetZDO().Set(ZDOVars.s_dodgeinv, false);
        }

        _invincibleApplied = false;
        _dodgeStateTouched = false;
    }

    private void RestoreVisualState()
    {
        if (_player == null)
        {
            return;
        }

        if (_equippingTouched)
        {
            _player.m_zanim.SetBool(EquippingAnimatorHash, _originalEquipping);
            _equippingTouched = false;
        }

        if (_crouchTouched && _player.m_crouchToggled == _appliedCrouchToggled)
        {
            _player.m_zanim.SetBool(Player.s_crouching, _originalCrouchAnimator);
            _player.SetCrouch(_originalCrouchToggled);
        }

        _crouchTouched = false;
        if (_animatorSpeedTouched &&
            Mathf.Approximately(_player.m_zanim.m_animator.speed, _appliedAnimatorSpeed))
        {
            _player.m_zanim.SetSpeed(_originalAnimatorSpeed);
        }

        _animatorSpeedTouched = false;
    }

    private void ApplyExitDamping()
    {
        if (_player == null || _player.m_body == null)
        {
            return;
        }

        Vector3 current = _player.m_body.linearVelocity;
        Vector3 damped = Vector3.Lerp(current, _startingVelocity, 0.5f) * 0.3f;
        damped.y = current.y;
        _player.m_body.linearVelocity = damped;
    }

    private void UpdateShieldInvincibility(float dt)
    {
        if (!_hasShield || !_invincibleApplied)
        {
            return;
        }

        _activeElapsed += dt;
        if (_activeElapsed >= QuickstepSystem.ShieldInvincibilityDuration)
        {
            SetInvincible(value: false);
        }
    }

    private Player RequirePlayer()
    {
        return _player ?? throw new InvalidOperationException("Quickstep controller is not attached to a Player.");
    }

    private bool CleanupPending =>
        _dodgeStateTouched ||
        _crouchTouched ||
        _equippingTouched ||
        _animatorSpeedTouched;

    private enum QuickstepPhase
    {
        None,
        AfterBlocking,
        AfterCrouch,
        AfterEquipping,
        Dashing
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.UpdateDodge))]
internal static class PlayerUpdateDodgeQuickstepPatch
{
    private static bool Prefix(Player __instance, float dt)
    {
        return QuickstepSystem.HandleUpdateDodge(__instance, dt);
    }

    private static void Postfix(Player __instance)
    {
        QuickstepSystem.ObserveDodgeUpdate(__instance);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.GetDodgeStaminaUse))]
internal static class PlayerGetDodgeStaminaUseQuickstepPatch
{
    private static void Postfix(Player __instance, ref float __result)
    {
        QuickstepSystem.AdjustDodgeStaminaCost(__instance, ref __result);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
internal static class PlayerOnDeathQuickstepPatch
{
    private static void Prefix(Player __instance)
    {
        QuickstepSystem.Reset(__instance);
    }
}

[HarmonyPatch(typeof(Player), nameof(Player.OnSneaking))]
internal static class PlayerOnSneakingQuickstepPatch
{
    private static void Prefix(Player __instance, ref float dt)
    {
        if (QuickstepSystem.IsActive(__instance))
        {
            dt = 0f;
        }
    }
}
