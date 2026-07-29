using System;
using System.IO;
using BepInEx;
using ServerSync;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackFacade
{
    private sealed class ApplyFailureState
    {
        internal int ConsecutiveFailures;
        internal DateTime NextAttemptUtc = DateTime.MinValue;
        internal DateTime NextLogUtc = DateTime.MinValue;
        internal string Signature = string.Empty;
        internal int SuppressedLogCount;
    }

    private enum YamlAuthorityMode
    {
        LocalFiles,
        SyncedOnly
    }

    private static readonly TimeSpan ApplyFailureLogInterval = TimeSpan.FromSeconds(10d);
    private static readonly object ReloadLock = new();
    private static readonly ApplyFailureState PendingConfigApplyFailure = new();
    private static readonly ApplyFailureState PendingWorldApplyFailure = new();
    private static FileSystemWatcher? _watcher;
    private static SecondaryAttackReloadDebouncer? _yamlReloadDebouncer;
    private static SecondaryAttackReloadDebouncer? _applyRetryDebouncer;
    private static CustomSyncedValue<string>? _syncedYamlEnvelope;
    private static SecondaryAttackCompiledSnapshot _currentCompiledSnapshot = SecondaryAttackCompiledSnapshot.Empty;
    private static SecondaryAttackCompiledSnapshot? _pendingCompiledSnapshot;
    private static SecondaryAttackAppliedWorldSnapshot _currentAppliedWorldSnapshot = SecondaryAttackAppliedWorldSnapshot.Empty;
    private static bool _hasPendingConfig;
    private static bool _hasPendingWorldReapply;
    private static int _nextSnapshotId = 1;
    private static bool _suppressSyncedYamlChanged;
    private static YamlAuthorityMode _yamlAuthorityMode;
    private static string _currentYamlFingerprint = string.Empty;
    private static string? _pendingYamlFingerprint;

    internal static SecondaryAttackAppliedWorldSnapshot CurrentAppliedWorldSnapshot => _currentAppliedWorldSnapshot;

    public static void Initialize()
    {
        _applyRetryDebouncer = new SecondaryAttackReloadDebouncer(
            RetryFailedPendingApply,
            "SecondaryAttacks pending world apply");
        SecondaryAttackConfigLoader.EnsureLocalFilesExist();
        InitializeSyncedYamlEnvelope();

        RefreshYamlAuthorityMode(force: true);
    }

    public static void Dispose()
    {
        DisposeSyncedYamlEnvelope();

        _watcher?.Dispose();
        _watcher = null;
        _yamlReloadDebouncer?.Dispose();
        _yamlReloadDebouncer = null;
        _applyRetryDebouncer?.Dispose();
        _applyRetryDebouncer = null;
    }

    internal static void TryApplyPendingConfig()
    {
        RefreshYamlAuthorityMode();
        if (_hasPendingConfig)
        {
            CommitPendingConfig(force: false);
            return;
        }

        CommitPendingWorldReapply(force: false);
    }

    internal static void RequestCurrentWorldReapply()
    {
        lock (ReloadLock)
        {
            StageWorldReapply();
        }
    }

    internal static void ApplyPendingConfigToObjectDb(ObjectDB objectDb, bool emitMissingWarnings)
    {
        ApplyPendingConfigToWorld(ZNetScene.instance, objectDb, emitMissingWarnings);
    }

    internal static void ApplyPendingConfigToZNetScene(ZNetScene scene, bool emitMissingWarnings)
    {
        ApplyPendingConfigToWorld(scene, ObjectDB.instance, emitMissingWarnings);
    }

    private static void ApplyPendingConfigToWorld(
        ZNetScene? scene,
        ObjectDB? objectDb,
        bool emitMissingWarnings)
    {
        RefreshYamlAuthorityMode();
        bool applyingPendingConfig = _hasPendingConfig && _pendingCompiledSnapshot != null;
        SecondaryAttackCompiledSnapshot snapshot = applyingPendingConfig
            ? _pendingCompiledSnapshot!
            : _currentCompiledSnapshot;
        string fingerprint = applyingPendingConfig
            ? _pendingYamlFingerprint ?? _currentYamlFingerprint
            : _currentYamlFingerprint;
        ApplyFailureState failureState = applyingPendingConfig
            ? PendingConfigApplyFailure
            : PendingWorldApplyFailure;
        try
        {
            ApplyCompiledSnapshotToWorld(scene, objectDb, snapshot, emitMissingWarnings);
        }
        catch (Exception exception)
        {
            if (!applyingPendingConfig)
            {
                _hasPendingWorldReapply = true;
            }

            RecordApplyFailure(
                applyingPendingConfig ? "staged YAML configuration" : "world configuration",
                exception,
                failureState);
            return;
        }

        if (applyingPendingConfig)
        {
            CompletePendingConfigCommit(snapshot, fingerprint);
        }

        if (objectDb != null)
        {
            _hasPendingWorldReapply = false;
        }

        ResetApplyFailure(PendingConfigApplyFailure);
        ResetApplyFailure(PendingWorldApplyFailure);
    }

    private static void SetupWatcher()
    {
        if (_watcher != null)
        {
            return;
        }

        Directory.CreateDirectory(SecondaryAttackYamlDomainRegistry.ConfigDirectoryPath);
        _yamlReloadDebouncer = new SecondaryAttackReloadDebouncer(
            ReloadLocalYamlFromWatcher,
            "SecondaryAttacks YAML reload");
        _watcher = new FileSystemWatcher(SecondaryAttackYamlDomainRegistry.ConfigDirectoryPath, "SecondaryAttacks.*.yml");
        _watcher.Changed += OnYamlFileChanged;
        _watcher.Created += OnYamlFileChanged;
        _watcher.Deleted += OnYamlFileChanged;
        _watcher.Renamed += OnYamlFileChanged;
        _watcher.Error += OnYamlWatcherError;
        _watcher.IncludeSubdirectories = false;
        _watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        _watcher.EnableRaisingEvents = true;
    }

    private static void OnYamlFileChanged(object sender, FileSystemEventArgs e)
    {
        if (_yamlAuthorityMode != YamlAuthorityMode.LocalFiles)
        {
            return;
        }

        _yamlReloadDebouncer?.Schedule();
    }

    private static void OnYamlWatcherError(object sender, ErrorEventArgs e)
    {
        if (_yamlAuthorityMode != YamlAuthorityMode.LocalFiles)
        {
            return;
        }

        SecondaryAttacksPlugin.ModLogger.LogError(
            $"SecondaryAttacks YAML file watcher error; scheduling a full reload. {e.GetException()}");
        _yamlReloadDebouncer?.Schedule();
    }

    private static bool ReloadLocalYamlFromWatcher()
    {
        lock (ReloadLock)
        {
            try
            {
                if (!SecondaryAttackConfigLoader.TryReadStableLocalYamlTexts(out SecondaryAttackYamlTexts? yamlTexts))
                {
                    return false;
                }

                PublishAndApplyLocalYaml(yamlTexts!);
                return true;
            }
            catch (Exception exception)
            {
                SecondaryAttacksPlugin.ModLogger.LogError($"Error reloading SecondaryAttacks YAML configuration: {exception.Message}");
                return false;
            }
        }
    }

    private static void ReloadLocalYaml()
    {
        if (_yamlAuthorityMode != YamlAuthorityMode.LocalFiles)
        {
            return;
        }

        SecondaryAttackConfigLoader.EnsureLocalFilesExist();
        SecondaryAttackYamlTexts yamlTexts = SecondaryAttackConfigLoader.ReadLocalYamlTexts();
        PublishAndApplyLocalYaml(yamlTexts);
    }

    private static void PublishAndApplyLocalYaml(SecondaryAttackYamlTexts yamlTexts)
    {
        if (_syncedYamlEnvelope != null)
        {
            _suppressSyncedYamlChanged = true;
            try
            {
                _syncedYamlEnvelope.AssignLocalValue(yamlTexts.ToEnvelope());
            }
            finally
            {
                _suppressSyncedYamlChanged = false;
            }
        }

        ApplyYamlTexts(yamlTexts);
    }

    private static void OnSyncedYamlChanged()
    {
        if (_suppressSyncedYamlChanged)
        {
            return;
        }

        if (TryReadSyncedYamlTexts(out SecondaryAttackYamlTexts? yamlTexts, out _))
        {
            ApplyYamlTexts(yamlTexts!);
        }
    }

    private static void RefreshYamlAuthorityMode(bool force = false)
    {
        YamlAuthorityMode nextMode = DetermineYamlAuthorityMode();
        if (!force && nextMode == _yamlAuthorityMode)
        {
            return;
        }

        _yamlAuthorityMode = nextMode;
        DiscardPendingConfig();
        switch (nextMode)
        {
            case YamlAuthorityMode.LocalFiles:
                SetupWatcher();
                ReloadLocalYaml();
                SecondaryAttacksPlugin.ModLogger.LogInfo("SecondaryAttacks YAML authority mode: LocalFiles.");
                break;
            case YamlAuthorityMode.SyncedOnly:
                DisposeWatcher();
                if (TryReadSyncedYamlTexts(
                        out SecondaryAttackYamlTexts? yamlTexts,
                        out bool hasSyncedYamlEnvelope))
                {
                    ApplyYamlTexts(yamlTexts!);
                }
                else if (!hasSyncedYamlEnvelope)
                {
                    _currentCompiledSnapshot = SecondaryAttackCompiledSnapshot.Empty;
                    _currentYamlFingerprint = string.Empty;
                    _hasPendingWorldReapply = true;
                    ResetApplyFailure(PendingWorldApplyFailure);
                }

                SecondaryAttacksPlugin.ModLogger.LogInfo("SecondaryAttacks YAML authority mode: SyncedOnly.");
                break;
        }
    }

    private static YamlAuthorityMode DetermineYamlAuthorityMode()
    {
        return ZNet.instance != null && !ZNet.instance.IsServer()
            ? YamlAuthorityMode.SyncedOnly
            : YamlAuthorityMode.LocalFiles;
    }

    private static void InitializeSyncedYamlEnvelope()
    {
        DisposeSyncedYamlEnvelope();
        _syncedYamlEnvelope = new CustomSyncedValue<string>(
            SecondaryAttacksPlugin.ConfigSync,
            SecondaryAttackYamlDomainRegistry.SyncedYamlEnvelopeIdentifier,
            "");
        _syncedYamlEnvelope.ValueChanged += OnSyncedYamlChanged;
    }

    private static void DisposeSyncedYamlEnvelope()
    {
        if (_syncedYamlEnvelope == null)
        {
            return;
        }

        _syncedYamlEnvelope.ValueChanged -= OnSyncedYamlChanged;
        _syncedYamlEnvelope = null;
    }

    private static bool TryReadSyncedYamlTexts(
        out SecondaryAttackYamlTexts? yamlTexts,
        out bool hasValue)
    {
        yamlTexts = null;
        string envelope = _syncedYamlEnvelope?.Value ?? string.Empty;
        hasValue = !string.IsNullOrEmpty(envelope);
        if (!hasValue)
        {
            return false;
        }

        if (SecondaryAttackYamlTexts.TryFromEnvelope(envelope, out yamlTexts))
        {
            return true;
        }

        SecondaryAttacksPlugin.ModLogger.LogError(
            "Ignoring malformed synchronized SecondaryAttacks YAML envelope.");
        return false;
    }

    private static void DisposeWatcher()
    {
        if (_watcher == null)
        {
            return;
        }

        _watcher.Dispose();
        _watcher = null;
        _yamlReloadDebouncer?.Dispose();
        _yamlReloadDebouncer = null;
    }

    private static void ApplyYamlTexts(SecondaryAttackYamlTexts yamlTexts)
    {
        string fingerprint = yamlTexts.GetContentFingerprint();
        if (string.Equals(_currentYamlFingerprint, fingerprint, StringComparison.Ordinal))
        {
            DiscardPendingConfig();
            return;
        }

        if (_hasPendingConfig && string.Equals(_pendingYamlFingerprint, fingerprint, StringComparison.Ordinal))
        {
            CommitPendingConfig(force: false);
            return;
        }

        DiscardPendingConfig();
        if (!SecondaryAttackConfigLoader.TryCompileSnapshot(_nextSnapshotId++, yamlTexts, out SecondaryAttackCompiledSnapshot? snapshot))
        {
            return;
        }

        StageConfig(snapshot!, fingerprint);
    }

    private static void DiscardPendingConfig()
    {
        _pendingCompiledSnapshot = null;
        _pendingYamlFingerprint = null;
        _hasPendingConfig = false;
        ResetApplyFailure(PendingConfigApplyFailure);
    }

    private static void StageConfig(SecondaryAttackCompiledSnapshot snapshot, string fingerprint)
    {
        _pendingCompiledSnapshot = snapshot;
        _pendingYamlFingerprint = fingerprint;
        _hasPendingConfig = true;
        ResetApplyFailure(PendingConfigApplyFailure);
        CommitPendingConfig(force: false);
    }

    private static void StageWorldReapply()
    {
        _hasPendingWorldReapply = true;
        ResetApplyFailure(PendingWorldApplyFailure);
        if (_hasPendingConfig)
        {
            CommitPendingConfig(force: false);
            return;
        }

        CommitPendingWorldReapply(force: false);
    }

    private static bool CommitPendingConfig(bool force)
    {
        if (!_hasPendingConfig || _pendingCompiledSnapshot == null)
        {
            return false;
        }

        if (!force && !CanApplyPendingConfigNow())
        {
            return false;
        }

        if (!force && !CanAttemptApply(PendingConfigApplyFailure))
        {
            return false;
        }

        ObjectDB? objectDb = ObjectDB.instance;
        if (objectDb == null)
        {
            return false;
        }

        SecondaryAttackCompiledSnapshot snapshot = _pendingCompiledSnapshot;
        string fingerprint = _pendingYamlFingerprint ?? _currentYamlFingerprint;
        try
        {
            ApplyCompiledSnapshotToWorld(
                ZNetScene.instance,
                objectDb,
                snapshot,
                emitMissingWarnings: true);
        }
        catch (Exception exception)
        {
            RecordApplyFailure("staged YAML configuration", exception, PendingConfigApplyFailure);
            return false;
        }

        CompletePendingConfigCommit(snapshot, fingerprint);
        _hasPendingWorldReapply = false;
        ResetApplyFailure(PendingConfigApplyFailure);
        ResetApplyFailure(PendingWorldApplyFailure);
        return true;
    }

    private static bool CommitPendingWorldReapply(bool force)
    {
        if (!_hasPendingWorldReapply)
        {
            return false;
        }

        if (!force && !CanApplyPendingConfigNow())
        {
            return false;
        }

        if (!force && !CanAttemptApply(PendingWorldApplyFailure))
        {
            return false;
        }

        if (ObjectDB.instance == null)
        {
            return false;
        }

        try
        {
            ApplyCompiledSnapshotToWorld(
                ZNetScene.instance,
                ObjectDB.instance,
                _currentCompiledSnapshot,
                emitMissingWarnings: true);
        }
        catch (Exception exception)
        {
            RecordApplyFailure("world configuration", exception, PendingWorldApplyFailure);
            return false;
        }

        _hasPendingWorldReapply = false;
        ResetApplyFailure(PendingWorldApplyFailure);
        SecondaryAttacksPlugin.ModLogger.LogInfo("Applied staged world-apply config changes.");
        return true;
    }

    private static void ApplyCompiledSnapshotToWorld(
        ZNetScene? scene,
        ObjectDB? objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        SecondaryAttackCompiledSnapshot rollbackSnapshot =
            _currentAppliedWorldSnapshot.CompiledSnapshot;
        try
        {
            if (scene != null)
            {
                SecondaryAttackWorldApplySystem.ApplyToZNetScene(
                    scene,
                    compiledSnapshot,
                    emitMissingWarnings);
            }

            if (objectDb != null)
            {
                _currentAppliedWorldSnapshot =
                    SecondaryAttackWorldApplySystem.Apply(objectDb, compiledSnapshot, emitMissingWarnings);
                SecondaryAttackManager.RefreshLocalPlayerRuntimeWeaponDefinitions();
            }
        }
        catch (Exception applyException)
        {
            TryCompensateFailedWorldApply(
                scene,
                objectDb,
                rollbackSnapshot,
                applyException);
            throw;
        }
    }

    private static void TryCompensateFailedWorldApply(
        ZNetScene? scene,
        ObjectDB? objectDb,
        SecondaryAttackCompiledSnapshot rollbackSnapshot,
        Exception applyException)
    {
        try
        {
            if (scene != null)
            {
                SecondaryAttackWorldApplySystem.ApplyToZNetScene(
                    scene,
                    rollbackSnapshot,
                    emitMissingWarnings: false);
            }

            if (objectDb != null)
            {
                _currentAppliedWorldSnapshot =
                    SecondaryAttackWorldApplySystem.Apply(
                        objectDb,
                        rollbackSnapshot,
                        emitMissingWarnings: false);
                SecondaryAttackManager.RefreshLocalPlayerRuntimeWeaponDefinitions();
            }
        }
        catch (Exception compensationException)
        {
            SecondaryAttacksPlugin.ModLogger.LogError(
                "Failed to restore the previously applied SecondaryAttacks world after an apply error. " +
                $"Apply error: {applyException}\nCompensation error: {compensationException}");
        }
    }

    private static bool CanApplyPendingConfigNow()
    {
        Player? localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            return true;
        }

        Attack? currentAttack = ((Humanoid)localPlayer).m_currentAttack;
        return currentAttack == null || currentAttack.IsDone();
    }

    private static void CompletePendingConfigCommit(
        SecondaryAttackCompiledSnapshot snapshot,
        string fingerprint)
    {
        if (!_hasPendingConfig || !ReferenceEquals(_pendingCompiledSnapshot, snapshot))
        {
            return;
        }

        _currentCompiledSnapshot = snapshot;
        _currentYamlFingerprint = fingerprint;
        _pendingCompiledSnapshot = null;
        _pendingYamlFingerprint = null;
        _hasPendingConfig = false;
        SecondaryAttacksPlugin.ModLogger.LogInfo("Applied staged YAML config changes.");
    }

    private static bool CanAttemptApply(ApplyFailureState state)
    {
        return DateTime.UtcNow >= state.NextAttemptUtc;
    }

    private static void RecordApplyFailure(
        string operation,
        Exception exception,
        ApplyFailureState state)
    {
        DateTime now = DateTime.UtcNow;
        state.ConsecutiveFailures++;
        int retryStep = Math.Min(state.ConsecutiveFailures - 1, 4);
        double retryDelaySeconds = Math.Min(0.25d * (1 << retryStep), 4d);
        state.NextAttemptUtc = now.AddSeconds(retryDelaySeconds);
        if (state.ConsecutiveFailures == 1)
        {
            _applyRetryDebouncer?.Schedule();
        }

        string signature = $"{exception.GetType().FullName}: {exception.Message}";
        if (!string.Equals(signature, state.Signature, StringComparison.Ordinal) || now >= state.NextLogUtc)
        {
            string suppressedSuffix = state.SuppressedLogCount > 0
                ? $" ({state.SuppressedLogCount} repeated failures suppressed.)"
                : string.Empty;
            SecondaryAttacksPlugin.ModLogger.LogError(
                $"Failed to apply {operation}; the pending change will be retried.{suppressedSuffix}\n{exception}");
            state.Signature = signature;
            state.NextLogUtc = now + ApplyFailureLogInterval;
            state.SuppressedLogCount = 0;
            return;
        }

        state.SuppressedLogCount++;
    }

    private static bool RetryFailedPendingApply()
    {
        lock (ReloadLock)
        {
            if (!CanApplyPendingConfigNow() || ObjectDB.instance == null)
            {
                return true;
            }

            if (_hasPendingConfig)
            {
                bool applied = CommitPendingConfig(force: false);
                return applied || PendingConfigApplyFailure.ConsecutiveFailures == 0;
            }

            if (_hasPendingWorldReapply)
            {
                bool applied = CommitPendingWorldReapply(force: false);
                return applied || PendingWorldApplyFailure.ConsecutiveFailures == 0;
            }

            return true;
        }
    }

    private static void ResetApplyFailure(ApplyFailureState state)
    {
        state.ConsecutiveFailures = 0;
        state.NextAttemptUtc = DateTime.MinValue;
        state.NextLogUtc = DateTime.MinValue;
        state.Signature = string.Empty;
        state.SuppressedLogCount = 0;
    }

}
