using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using ServerSync;
using UnityEngine;

namespace SecondaryAttacks;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInDependency(SecondaryAttackCompat.MagicPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
public class SecondaryAttacksPlugin : BaseUnityPlugin
{
    internal const string ModName = "SecondaryAttacks";
    internal const string ModVersion = "1.1.2";
    internal const string Author = "sighsorry";
    private const string ModGUID = $"{Author}.{ModName}";
    private static string ConfigFileName = $"{ModGUID}.cfg";
    private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
    internal static string ConnectionError = "";
    private readonly Harmony _harmony = new(ModGUID);
    public static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    internal static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
    internal static PluginSettings Settings { get; } = new();
    internal static ConfigEntry<float> BloodMagicHealthCostSkillRaiseFactor => Settings.General.BloodMagicHealthCostSkillRaiseFactor;
    internal static ConfigEntry<Toggle> BloodMagicHealthCostUsesMaxHealth => Settings.General.BloodMagicHealthCostUsesMaxHealth;
    internal static ConfigEntry<MagicSummonQualityPresetSelection> MagicSummonQualityPreset => Settings.General.MagicSummonQualityPreset;
    internal static ConfigEntry<float> BackstabSneakSkillRaiseAmount => Settings.General.BackstabSneakSkillRaiseAmount;
    internal static ConfigEntry<float> SneakVisibilitySkillEffectFactor => Settings.General.SneakVisibilitySkillEffectFactor;
    internal static ConfigEntry<float> SneakMovementSpeedSkillFactor => Settings.General.SneakMovementSpeedSkillFactor;
    internal static ConfigEntry<RangedPresetSelection> FireballStaffPreset => Settings.Ranged.FireballStaffPreset;
    internal static ConfigEntry<RangedPresetSelection> RapidStaffPreset => Settings.Ranged.RapidStaffPreset;
    internal static ConfigEntry<RangedPresetSelection> LightningStaffPreset => Settings.Ranged.LightningStaffPreset;
    internal static ConfigEntry<RangedPresetSelection> BowPreset => Settings.Ranged.BowPreset;
    internal static ConfigEntry<RangedPresetSelection> CrossbowPreset => Settings.Ranged.CrossbowPreset;
    internal static ConfigEntry<BombPresetSelection> BombPreset => Settings.Ranged.BombPreset;
    internal static ConfigEntry<Toggle> SecondaryCooldownHudEnabled => Settings.Ui.SecondaryCooldownHudEnabled;
    internal static ConfigEntry<float> SecondaryCooldownHudPositionX => Settings.Ui.SecondaryCooldownHudPositionX;
    internal static ConfigEntry<float> SecondaryCooldownHudPositionY => Settings.Ui.SecondaryCooldownHudPositionY;
    internal static ConfigEntry<Toggle> AdminNoPresetCooldowns => Settings.Admin.AdminNoPresetCooldowns;
    private FileSystemWatcher? _watcher;
    private SecondaryAttackReloadDebouncer? _configReloadDebouncer;
    private readonly object _reloadLock = new();
    private string? _lastConfigFileText;
    private bool _suppressWorldApplySettingChange;

    public enum Toggle
    {
        On = 1,
        Off = 0
    }

    public enum RangedPresetSelection
    {
        Off = -1,
        Barrage = 0,
        Volley = 1,
        Piercing = 2,
        Scatter = 3,
        Spiral = 4,
        Sentinel = 5,
        Meteor = 6,
        Burst = 7
    }

    public enum BombPresetSelection
    {
        Off = -1,
        Auto = 0,
        StickyDetonator = 1,
        OverchargedBomb = 2
    }

    public enum MagicSummonQualityPresetSelection
    {
        Off,
        CountByQuality,
        LevelByQuality
    }

    public void Awake()
    {
        SecondaryAttackLocalization.Load();

        bool saveOnSet = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;

        Settings.Bind(this);
        RemoveLegacyHudScaleConfig();
        RegisterWorldApplySettingHandlers();
        _serverConfigLocked = Settings.General.LockConfiguration;
        _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
        Assembly assembly = Assembly.GetExecutingAssembly();
        _harmony.PatchAll(assembly);
        SecondaryAttackFacade.Initialize();
        SetupWatcher();

        Config.Save();
        _lastConfigFileText = ReadFileTextIfExists(ConfigFileFullPath);
        if (saveOnSet)
        {
            Config.SaveOnConfigSet = saveOnSet;
        }
    }

    private void OnDestroy()
    {
        UnregisterWorldApplySettingHandlers();
        SecondaryAttackFacade.Dispose();
        SaveWithRespectToConfigSet();
        _watcher?.Dispose();
        _configReloadDebouncer?.Dispose();
    }

    private void SetupWatcher()
    {
        _configReloadDebouncer = new SecondaryAttackReloadDebouncer(ReloadConfigValues, "SecondaryAttacks config reload");
        _watcher = new FileSystemWatcher(Paths.ConfigPath, ConfigFileName);
        _watcher.Changed += QueueConfigReload;
        _watcher.Created += QueueConfigReload;
        _watcher.Renamed += QueueConfigReload;
        _watcher.Deleted += QueueConfigReload;
        _watcher.Error += QueueConfigWatcherRecovery;
        _watcher.IncludeSubdirectories = false;
        _watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
        _watcher.EnableRaisingEvents = true;
    }

    private void QueueConfigReload(object sender, FileSystemEventArgs e)
    {
        _configReloadDebouncer?.Schedule();
    }

    private void QueueConfigWatcherRecovery(object sender, ErrorEventArgs e)
    {
        ModLogger.LogWarning($"Config file watcher reported an error; scheduling a full config rescan. {e.GetException().Message}");
        _configReloadDebouncer?.Schedule();
    }

    private bool ReloadConfigValues()
    {
        lock (_reloadLock)
        {
            if (!TryReadStableFileText(ConfigFileFullPath, out string? configFileText))
            {
                return false;
            }

            try
            {
                if (string.Equals(_lastConfigFileText, configFileText, StringComparison.Ordinal))
                {
                    return true;
                }

                RangedPresetSelection previousFireballStaffPreset = FireballStaffPreset.Value;
                RangedPresetSelection previousRapidStaffPreset = RapidStaffPreset.Value;
                RangedPresetSelection previousLightningStaffPreset = LightningStaffPreset.Value;
                RangedPresetSelection previousBowPreset = BowPreset.Value;
                RangedPresetSelection previousCrossbowPreset = CrossbowPreset.Value;
                BombPresetSelection previousBombPreset = BombPreset.Value;
                MagicSummonQualityPresetSelection previousMagicSummonQualityPreset = MagicSummonQualityPreset.Value;

                _suppressWorldApplySettingChange = true;
                try
                {
                    SaveWithRespectToConfigSet(true);
                }
                finally
                {
                    _suppressWorldApplySettingChange = false;
                }

                bool needsWorldReapply =
                    previousFireballStaffPreset != FireballStaffPreset.Value ||
                    previousRapidStaffPreset != RapidStaffPreset.Value ||
                    previousLightningStaffPreset != LightningStaffPreset.Value ||
                    previousBowPreset != BowPreset.Value ||
                    previousCrossbowPreset != CrossbowPreset.Value ||
                    previousBombPreset != BombPreset.Value ||
                    previousMagicSummonQualityPreset != MagicSummonQualityPreset.Value;
                if (needsWorldReapply)
                {
                    SecondaryAttackFacade.RequestCurrentWorldReapply();
                }

                _lastConfigFileText = configFileText;
                ModLogger.LogInfo("Configuration reload complete.");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.LogWarning($"Configuration reload attempt failed and will be retried: {ex.Message}");
                return false;
            }
        }
    }

    private static string? ReadFileTextIfExists(string path)
    {
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    private static bool TryReadStableFileText(string path, out string? fileText)
    {
        fileText = null;
        if (!File.Exists(path))
        {
            return false;
        }

        FileInfo before = new(path);
        before.Refresh();
        if (!before.Exists)
        {
            return false;
        }

        long length = before.Length;
        DateTime lastWriteTimeUtc = before.LastWriteTimeUtc;
        string candidate = File.ReadAllText(path);

        FileInfo after = new(path);
        after.Refresh();
        if (!after.Exists || after.Length != length || after.LastWriteTimeUtc != lastWriteTimeUtc)
        {
            return false;
        }

        fileText = candidate;
        return true;
    }

    private void SaveWithRespectToConfigSet(bool reload = false)
    {
        bool originalSaveOnSet = Config.SaveOnConfigSet;
        try
        {
            Config.SaveOnConfigSet = false;
            if (reload)
            {
                Config.Reload();
            }
            else
            {
                Config.Save();
            }
        }
        finally
        {
            Config.SaveOnConfigSet = originalSaveOnSet;
        }
    }

    private void RemoveLegacyHudScaleConfig()
    {
        const string group = "3 - UI";
        const string name = "Secondary Cooldown HUD Scale";
        ConfigDefinition definition = new(group, name);
        _ = Config.Bind(definition, 2f, new ConfigDescription("Legacy fixed HUD scale."));
        Config.Remove(definition);
    }

    private void RegisterWorldApplySettingHandlers()
    {
        FireballStaffPreset.SettingChanged += OnWorldApplySettingChanged;
        RapidStaffPreset.SettingChanged += OnWorldApplySettingChanged;
        LightningStaffPreset.SettingChanged += OnWorldApplySettingChanged;
        BowPreset.SettingChanged += OnWorldApplySettingChanged;
        CrossbowPreset.SettingChanged += OnWorldApplySettingChanged;
        BombPreset.SettingChanged += OnWorldApplySettingChanged;
        MagicSummonQualityPreset.SettingChanged += OnWorldApplySettingChanged;
    }

    private void UnregisterWorldApplySettingHandlers()
    {
        FireballStaffPreset.SettingChanged -= OnWorldApplySettingChanged;
        RapidStaffPreset.SettingChanged -= OnWorldApplySettingChanged;
        LightningStaffPreset.SettingChanged -= OnWorldApplySettingChanged;
        BowPreset.SettingChanged -= OnWorldApplySettingChanged;
        CrossbowPreset.SettingChanged -= OnWorldApplySettingChanged;
        BombPreset.SettingChanged -= OnWorldApplySettingChanged;
        MagicSummonQualityPreset.SettingChanged -= OnWorldApplySettingChanged;
    }

    private void OnWorldApplySettingChanged(object? sender, EventArgs e)
    {
        if (_suppressWorldApplySettingChange)
        {
            return;
        }

        SecondaryAttackFacade.RequestCurrentWorldReapply();
    }

    internal sealed class PluginSettings
    {
        internal GeneralSettings General { get; } = new();

        internal RangedSettings Ranged { get; } = new();

        internal UiSettings Ui { get; } = new();

        internal AdminSettings Admin { get; } = new();

        internal void Bind(SecondaryAttacksPlugin plugin)
        {
            General.Bind(plugin);
            Ranged.Bind(plugin);
            Ui.Bind(plugin);
            Admin.Bind(plugin);
        }
    }

    internal sealed class GeneralSettings
    {
        internal ConfigEntry<Toggle> LockConfiguration = null!;
        internal ConfigEntry<float> BloodMagicHealthCostSkillRaiseFactor = null!;
        internal ConfigEntry<Toggle> BloodMagicHealthCostUsesMaxHealth = null!;
        internal ConfigEntry<MagicSummonQualityPresetSelection> MagicSummonQualityPreset = null!;
        internal ConfigEntry<float> BackstabSneakSkillRaiseAmount = null!;
        internal ConfigEntry<float> SneakVisibilitySkillEffectFactor = null!;
        internal ConfigEntry<float> SneakMovementSpeedSkillFactor = null!;

        internal void Bind(SecondaryAttacksPlugin plugin)
        {
            const string group = "1 - General";
            LockConfiguration = plugin.config(group, "Lock Configuration", Toggle.On, new ConfigDescription("If on, the configuration is locked and can be changed by server admins only.", null, new ConfigurationManagerAttributes { Order = 700 }));
            SneakMovementSpeedSkillFactor = plugin.config(group, "Sneak Movement Speed Skill Factor", 1.0f, new ConfigDescription("Sneak movement speed multiplier at Sneak skill 100 while crouching. 1.0 keeps vanilla; 2.0 doubles crouched movement speed at Sneak 100, with lower Sneak levels linearly interpolated.", new AcceptableValueRange<float>(1f, 2f), new ConfigurationManagerAttributes { Order = 690 }), synchronizedSetting: true);
            SneakVisibilitySkillEffectFactor = plugin.config(group, "Sneak Visibility Skill Effect Factor", 1.0f, new ConfigDescription("Multiplier for the visibility reduction gained from Sneak skill while crouching. 1.0 keeps vanilla; 2.0 doubles only the skill-based reduction. Visibility is clamped to a fixed minimum of 0.1. At factor 1.0, Sneak 0 is 0.5 in darkness and 1.0 in bright light; Sneak 100 is 0.2 in darkness and 0.6 in bright light.", new AcceptableValueRange<float>(1f, 2f), new ConfigurationManagerAttributes { Order = 680 }), synchronizedSetting: true);
            BackstabSneakSkillRaiseAmount = plugin.config(group, "Backstab Sneak Skill Raise Amount", 1.0f, new ConfigDescription("Sneak skill raise amount awarded whenever any attack successfully triggers backstab damage. 0 disables this reward.", new AcceptableValueRange<float>(0f, 10f), new ConfigurationManagerAttributes { Order = 670 }), synchronizedSetting: true);
            MagicSummonQualityPreset = plugin.config(group, "Magic Summon Quality Preset", MagicSummonQualityPresetSelection.LevelByQuality, new ConfigDescription("Global quality preset for BloodMagic summon items whose primary or secondary projectile resolves to a SpawnAbility. Explicit summon blocks in SecondaryAttacks.BloodMagic.yml override this. Off disables automatic quality scaling; CountByQuality makes item quality raise active summon count; LevelByQuality makes item quality raise summoned creature level.", null, new ConfigurationManagerAttributes { Order = 660 }), synchronizedSetting: true);
            BloodMagicHealthCostUsesMaxHealth = plugin.config(group, "Blood Magic Health Cost Uses Max Health", Toggle.On, new ConfigDescription("If on, Blood Magic attack health percentage costs are calculated from max health at cast time instead of current health. Flat health cost and Blood Magic skill cost reduction are unchanged.", null, new ConfigurationManagerAttributes { Order = 650 }), synchronizedSetting: true);
            BloodMagicHealthCostSkillRaiseFactor = plugin.config(group, "Blood Magic Health Cost Skill Raise Factor", 0.01f, new ConfigDescription("Additional Blood Magic skill raise amount per actual consumed health. Vanilla Blood Magic skill gain always remains active. 0 disables only this custom health-cost skill gain. Example: consuming 160 health and 0.01 factor awards 1.6 extra raise amount.", new AcceptableValueRange<float>(0f, 0.1f), new ConfigurationManagerAttributes { Order = 640 }), synchronizedSetting: true);
        }
    }

    internal sealed class RangedSettings
    {
        internal ConfigEntry<RangedPresetSelection> FireballStaffPreset = null!;
        internal ConfigEntry<RangedPresetSelection> RapidStaffPreset = null!;
        internal ConfigEntry<RangedPresetSelection> LightningStaffPreset = null!;
        internal ConfigEntry<RangedPresetSelection> BowPreset = null!;
        internal ConfigEntry<RangedPresetSelection> CrossbowPreset = null!;
        internal ConfigEntry<BombPresetSelection> BombPreset = null!;

        internal void Bind(SecondaryAttacksPlugin plugin)
        {
            const string group = "2 - Ranged";
            const string descriptionSuffix = "Explicit prefab entries in SecondaryAttacks.Ranged.yml override this automatic group preset. Select Off to disable automatic assignment for this group.";
            FireballStaffPreset = plugin.config(group, "Fireball Staff Preset", RangedPresetSelection.Sentinel, $"Default ranged preset for ElementalMagic items whose primary attack animation is staff_fireball. {descriptionSuffix}", synchronizedSetting: true);
            RapidStaffPreset = plugin.config(group, "Rapidfire Staff Preset", RangedPresetSelection.Spiral, $"Default ranged preset for ElementalMagic items whose primary attack animation is staff_rapidfire. {descriptionSuffix}", synchronizedSetting: true);
            LightningStaffPreset = plugin.config(group, "Reload Staff Preset", RangedPresetSelection.Burst, $"Default ranged preset for ElementalMagic items whose primary attack animation is staff_lightningshot. {descriptionSuffix}", synchronizedSetting: true);
            BowPreset = plugin.config(group, "Bow Preset", RangedPresetSelection.Barrage, $"Default ranged preset for bow items. {descriptionSuffix}", synchronizedSetting: true);
            CrossbowPreset = plugin.config(group, "Crossbow Preset", RangedPresetSelection.Burst, $"Default ranged preset for reload-based crossbow-style projectile items. {descriptionSuffix}", synchronizedSetting: true);
            BombPreset = plugin.config(group, "Bomb Preset", BombPresetSelection.Auto, "Default ranged preset for throw_bomb projectile items. Auto uses overchargedBomb when the primary projectile itself has AOE or spawns an Aoe prefab on hit, and stickyDetonator otherwise. Explicit prefab entries in SecondaryAttacks.Ranged.yml override this automatic group preset. Select Off to disable automatic bomb assignment.", synchronizedSetting: true);
        }
    }

    internal sealed class UiSettings
    {
        internal ConfigEntry<Toggle> SecondaryCooldownHudEnabled = null!;
        internal ConfigEntry<float> SecondaryCooldownHudPositionX = null!;
        internal ConfigEntry<float> SecondaryCooldownHudPositionY = null!;

        internal void Bind(SecondaryAttacksPlugin plugin)
        {
            const string group = "3 - UI";
            SecondaryCooldownHudEnabled = plugin.config(group, "Secondary Cooldown HUD Enabled", Toggle.On, "If on, secondary attack cooldowns and charge progress are shown in a dedicated HUD block. Off hides this display without changing cooldown behavior.", synchronizedSetting: false);
            SecondaryCooldownHudPositionX = plugin.config(group, "Secondary Cooldown HUD Position X", 0.615f, new ConfigDescription("Client-side normalized horizontal position for the secondary cooldown HUD. 0 is left, 1 is right. Open inventory to preview the configured position.", new AcceptableValueRange<float>(0f, 1f)), synchronizedSetting: false);
            SecondaryCooldownHudPositionY = plugin.config(group, "Secondary Cooldown HUD Position Y", 0.22f, new ConfigDescription("Client-side normalized vertical position for the secondary cooldown HUD. 0 is bottom, 1 is top. Open inventory to preview the configured position.", new AcceptableValueRange<float>(0f, 1f)), synchronizedSetting: false);
        }
    }

    internal sealed class AdminSettings
    {
        internal ConfigEntry<Toggle> AdminNoPresetCooldowns = null!;

        internal void Bind(SecondaryAttacksPlugin plugin)
        {
            const string group = "4 - Admin";
            AdminNoPresetCooldowns = plugin.config(
                group,
                "Admin No Preset Cooldowns",
                Toggle.Off,
                "Client-side admin convenience. If on, host or server-admin players use SecondaryAttacks presets without preset cooldowns. This does not change server-synced YAML values and does not remove internal hit throttles.",
                synchronizedSetting: false);
        }
    }


    #region ConfigOptions

    private static ConfigEntry<Toggle> _serverConfigLocked = null!;

    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
        //var configEntry = Config.Bind(group, name, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
    {
        return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }

    private class ConfigurationManagerAttributes
    {
        [UsedImplicitly] public int? Order = null!;
        [UsedImplicitly] public bool? Browsable = null!;
        [UsedImplicitly] public string? Category = null!;
        [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
    }

    class AcceptableShortcuts() : AcceptableValueBase(typeof(KeyboardShortcut))
    {
        public override object Clamp(object value) => value;
        public override bool IsValid(object value) => true;

        public override string ToDescriptionString() => $"# Acceptable values: {string.Join(", ", UnityInput.Current.SupportedKeyCodes)}";
    }

    #endregion
}

public static class KeyboardExtensions
{
    extension(KeyboardShortcut shortcut)
    {
        public bool IsKeyDown()
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }

        public bool IsKeyHeld()
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }
    }
}

public static class ToggleExtentions
{
    extension(SecondaryAttacksPlugin.Toggle value)
    {
        public bool IsOn()
        {
            return value == SecondaryAttacksPlugin.Toggle.On;
        }

        public bool IsOff()
        {
            return value == SecondaryAttacksPlugin.Toggle.Off;
        }
    }
}
