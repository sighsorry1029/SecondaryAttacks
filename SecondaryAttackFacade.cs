using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private static readonly Dictionary<SecondaryAttackYamlDomainId, CustomSyncedValue<string>> SyncedYamlValues = new();
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

    internal static SecondaryAttackCompiledSnapshot CurrentCompiledSnapshot => _currentCompiledSnapshot;

    internal static SecondaryAttackAppliedWorldSnapshot CurrentAppliedWorldSnapshot => _currentAppliedWorldSnapshot;

    public static void Initialize()
    {
        _applyRetryDebouncer = new SecondaryAttackReloadDebouncer(
            RetryFailedPendingApply,
            "SecondaryAttacks pending world apply");
        SecondaryAttackConfigLoader.EnsureLocalFilesExist();
        InitializeSyncedYamlValues();

        RefreshYamlAuthorityMode(force: true);
    }

    public static void Dispose()
    {
        DisposeSyncedYamlValues();

        _watcher?.Dispose();
        _watcher = null;
        _yamlReloadDebouncer?.Dispose();
        _yamlReloadDebouncer = null;
        _applyRetryDebouncer?.Dispose();
        _applyRetryDebouncer = null;
    }

    public static void ApplyToObjectDb(ObjectDB objectDb, bool emitMissingWarnings)
    {
        RefreshYamlAuthorityMode();
        ApplyCompiledSnapshotToObjectDb(objectDb, _currentCompiledSnapshot, emitMissingWarnings);
        _hasPendingWorldReapply = false;
        ResetApplyFailure(PendingWorldApplyFailure);
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
            ApplyCompiledSnapshotToObjectDb(objectDb, snapshot, emitMissingWarnings);
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

        _hasPendingWorldReapply = false;
        ResetApplyFailure(PendingConfigApplyFailure);
        ResetApplyFailure(PendingWorldApplyFailure);
    }

    internal static void ApplyPendingConfigToZNetScene(ZNetScene scene, bool emitMissingWarnings)
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
        ObjectDB? objectDb = ObjectDB.instance;

        try
        {
            ApplyCompiledSnapshotToZNetScene(scene, snapshot, emitMissingWarnings);
            if (objectDb != null)
            {
                ApplyCompiledSnapshotToObjectDb(objectDb, snapshot, emitMissingWarnings, applyZNetScene: false);
            }
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
        if (SyncedYamlValues.Count == SecondaryAttackYamlDomainRegistry.Domains.Count)
        {
            _suppressSyncedYamlChanged = true;
            try
            {
                foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
                {
                    SyncedYamlValues[domain.Id].AssignLocalValue(yamlTexts.Get(domain.Id));
                }
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

        ApplyYamlTexts(ReadSyncedYamlTexts());
    }

    private static void RefreshYamlAuthorityMode(bool force = false)
    {
        YamlAuthorityMode nextMode = DetermineYamlAuthorityMode();
        if (!force && nextMode == _yamlAuthorityMode)
        {
            return;
        }

        _yamlAuthorityMode = nextMode;
        switch (nextMode)
        {
            case YamlAuthorityMode.LocalFiles:
                SetupWatcher();
                ReloadLocalYaml();
                SecondaryAttacksPlugin.ModLogger.LogInfo("SecondaryAttacks YAML authority mode: LocalFiles.");
                break;
            case YamlAuthorityMode.SyncedOnly:
                DisposeWatcher();
                if (AnySyncedYamlHasValue())
                {
                    ApplyYamlTexts(ReadSyncedYamlTexts());
                }
                else
                {
                    _pendingCompiledSnapshot = null;
                    _pendingYamlFingerprint = null;
                    _hasPendingConfig = false;
                    _hasPendingWorldReapply = false;
                    _currentCompiledSnapshot = SecondaryAttackCompiledSnapshot.Empty;
                    _currentYamlFingerprint = string.Empty;
                    _currentAppliedWorldSnapshot = SecondaryAttackAppliedWorldSnapshot.Empty;
                    if (ZNetScene.instance != null)
                    {
                        ApplyCompiledSnapshotToZNetScene(ZNetScene.instance, _currentCompiledSnapshot, emitMissingWarnings: true);
                    }

                    SecondaryAttackManager.RefreshLocalPlayerRuntimeWeaponDefinitions();
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

    private static void InitializeSyncedYamlValues()
    {
        DisposeSyncedYamlValues();
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            CustomSyncedValue<string> syncedValue = new(SecondaryAttacksPlugin.ConfigSync, domain.SyncedIdentifier, "");
            syncedValue.ValueChanged += OnSyncedYamlChanged;
            SyncedYamlValues[domain.Id] = syncedValue;
        }
    }

    private static void DisposeSyncedYamlValues()
    {
        foreach (CustomSyncedValue<string> syncedValue in SyncedYamlValues.Values)
        {
            syncedValue.ValueChanged -= OnSyncedYamlChanged;
        }

        SyncedYamlValues.Clear();
    }

    private static SecondaryAttackYamlTexts ReadSyncedYamlTexts()
    {
        Dictionary<SecondaryAttackYamlDomainId, string> texts = new();
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            texts[domain.Id] = SyncedYamlValues.TryGetValue(domain.Id, out CustomSyncedValue<string>? syncedValue)
                ? syncedValue.Value
                : string.Empty;
        }

        return new SecondaryAttackYamlTexts(texts);
    }

    private static bool AnySyncedYamlHasValue()
    {
        return SyncedYamlValues.Values.Any(syncedValue => !string.IsNullOrEmpty(syncedValue.Value));
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
            return;
        }

        if (_hasPendingConfig && string.Equals(_pendingYamlFingerprint, fingerprint, StringComparison.Ordinal))
        {
            CommitPendingConfig(force: false);
            return;
        }

        if (!SecondaryAttackConfigLoader.TryCompileSnapshot(_nextSnapshotId++, yamlTexts, out SecondaryAttackCompiledSnapshot? snapshot))
        {
            return;
        }

        StageConfig(snapshot!, fingerprint);
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
            ApplyCompiledSnapshotToObjectDb(objectDb, snapshot, emitMissingWarnings: true);
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
            ApplyCompiledSnapshotToObjectDb(ObjectDB.instance, _currentCompiledSnapshot, emitMissingWarnings: true);
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

    private static void ApplyCompiledSnapshotToObjectDb(
        ObjectDB objectDb,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings,
        bool applyZNetScene = true)
    {
        if (applyZNetScene && ZNetScene.instance != null)
        {
            ApplyCompiledSnapshotToZNetScene(ZNetScene.instance, compiledSnapshot, emitMissingWarnings);
        }

        _currentAppliedWorldSnapshot = SecondaryAttackWorldApplySystem.Apply(objectDb, compiledSnapshot, emitMissingWarnings);
        SecondaryAttackManager.RefreshLocalPlayerRuntimeWeaponDefinitions();
    }

    private static void ApplyCompiledSnapshotToZNetScene(
        ZNetScene scene,
        SecondaryAttackCompiledSnapshot compiledSnapshot,
        bool emitMissingWarnings)
    {
        SecondaryAttackWorldApplySystem.ApplyToZNetScene(scene, compiledSnapshot, emitMissingWarnings);
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
