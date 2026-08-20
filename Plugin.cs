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
[BepInDependency(SecondaryAttacksPlugin.MagicPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(SecondaryAttacksPlugin.QuickstepPluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(SecondaryAttacksPlugin.CreatureManagerGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(SecondaryAttacksPlugin.CreatureLevelControlGuid, BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency(SecondaryAttacksPlugin.StarLevelSystemGuid, BepInDependency.DependencyFlags.SoftDependency)]
public class SecondaryAttacksPlugin : BaseUnityPlugin
{
    internal const string MagicPluginGuid = "blacks7ar.MagicPlugin";
    internal const string QuickstepPluginGuid = "shudnal.Quickstep";
    internal const string CreatureManagerGuid = "sighsorry.CreatureManager";
    internal const string CreatureLevelControlGuid = "org.bepinex.plugins.creaturelevelcontrol";
    internal const string StarLevelSystemGuid = "MidnightsFX.StarLevelSystem";
    internal const string ModName = "SecondaryAttacks";
    internal const string ModVersion = "1.2.1";
    internal const string Author = "sighsorry";
    private const string ModGUID = $"{Author}.{ModName}";
    private static string ConfigFileName = $"{ModGUID}.cfg";
    private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
    private readonly Harmony _harmony = new(ModGUID);
    public static readonly ManualLogSource ModLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);
    internal static readonly ConfigSync ConfigSync = new(ModGUID)
    {
        DisplayName = ModName,
        CurrentVersion = ModVersion,
        MinimumRequiredVersion = ModVersion
    };
    internal static ConfigEntry<float> BloodMagicHealthCostSkillRaiseFactor { get; private set; } = null!;
    internal static ConfigEntry<Toggle> BloodMagicHealthCostUsesMaxHealth { get; private set; } = null!;
    internal static ConfigEntry<MagicSummonQualityPresetSelection> MagicSummonQualityPreset { get; private set; } = null!;
    internal static ConfigEntry<int> BloodMagicSummonLifetimeSeconds { get; private set; } = null!;
    internal static ConfigEntry<float> BloodMagicSummonLifetimeSkill100Multiplier { get; private set; } = null!;
    internal static ConfigEntry<float> BackstabSneakSkillRaiseAmount { get; private set; } = null!;
    internal static ConfigEntry<float> SneakVisibilitySkillEffectFactor { get; private set; } = null!;
    internal static ConfigEntry<float> SneakMovementSpeedSkillFactor { get; private set; } = null!;
    internal static ConfigEntry<Toggle> KeepCrouchingDuringElementalDamageOverTime { get; private set; } = null!;
    internal static ConfigEntry<RangedPresetSelection> FireballStaffPreset { get; private set; } = null!;
    internal static ConfigEntry<RangedPresetSelection> RapidStaffPreset { get; private set; } = null!;
    internal static ConfigEntry<RangedPresetSelection> LightningStaffPreset { get; private set; } = null!;
    internal static ConfigEntry<RangedPresetSelection> BowPreset { get; private set; } = null!;
    internal static ConfigEntry<RangedPresetSelection> CrossbowPreset { get; private set; } = null!;
    internal static ConfigEntry<BombPresetSelection> BombPreset { get; private set; } = null!;
    internal static ConfigEntry<Toggle> SecondaryCooldownHudEnabled { get; private set; } = null!;
    internal static ConfigEntry<Toggle> SecondaryAttackTooltipsEnabled { get; private set; } = null!;
    internal static ConfigEntry<float> SecondaryCooldownHudPositionX { get; private set; } = null!;
    internal static ConfigEntry<float> SecondaryCooldownHudPositionY { get; private set; } = null!;
    internal static ConfigEntry<Toggle> AdminNoPresetCooldowns { get; private set; } = null!;
    internal static ConfigEntry<Toggle> QuickstepEnabled { get; private set; } = null!;
    private FileSystemWatcher? _watcher;
    private SecondaryAttackReloadDebouncer? _configReloadDebouncer;
    private readonly object _reloadLock = new();
    private string? _lastConfigFileText;
    private bool _suppressSettingChangeSideEffects;
    private bool _worldApplySettingChangeDirty;
    private bool _summonLifetimeSettingChangeDirty;

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
        try
        {
            BindGeneralSettings();
            BindBloodMagicSettings();
            BindRangedSettings();
            BindUiSettings();
            QuickstepSystem.Initialize();
            SummonQualityHudCompatibility.Initialize();
            RegisterWorldApplySettingHandlers();
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SecondaryAttackFacade.Initialize();
            SetupWatcher();

            Config.Save();
            _lastConfigFileText = ReadFileTextIfExists(ConfigFileFullPath);
        }
        finally
        {
            Config.SaveOnConfigSet = saveOnSet;
        }
    }

    private void OnDestroy()
    {
        _watcher?.Dispose();
        _watcher = null;
        _configReloadDebouncer?.Dispose();
        _configReloadDebouncer = null;
        QuickstepSystem.Dispose();
        UnregisterWorldApplySettingHandlers();
        SecondaryAttackFacade.Dispose();
        SaveWithRespectToConfigSet();
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

                _worldApplySettingChangeDirty = false;
                _summonLifetimeSettingChangeDirty = false;
                _suppressSettingChangeSideEffects = true;
                try
                {
                    SaveWithRespectToConfigSet(true);
                }
                finally
                {
                    _suppressSettingChangeSideEffects = false;
                }

                FlushReloadSettingChanges();

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

    private void RegisterWorldApplySettingHandlers()
    {
        FireballStaffPreset.SettingChanged += OnWorldApplySettingChanged;
        RapidStaffPreset.SettingChanged += OnWorldApplySettingChanged;
        LightningStaffPreset.SettingChanged += OnWorldApplySettingChanged;
        BowPreset.SettingChanged += OnWorldApplySettingChanged;
        CrossbowPreset.SettingChanged += OnWorldApplySettingChanged;
        BombPreset.SettingChanged += OnWorldApplySettingChanged;
        MagicSummonQualityPreset.SettingChanged += OnWorldApplySettingChanged;
        BloodMagicSummonLifetimeSeconds.SettingChanged += OnSummonLifetimeSettingChanged;
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
        BloodMagicSummonLifetimeSeconds.SettingChanged -= OnSummonLifetimeSettingChanged;
    }

    private void OnWorldApplySettingChanged(object? sender, EventArgs e)
    {
        if (_suppressSettingChangeSideEffects)
        {
            _worldApplySettingChangeDirty = true;
            return;
        }

        SecondaryAttackFacade.RequestCurrentWorldReapply();
    }

    private void OnSummonLifetimeSettingChanged(object? sender, EventArgs e)
    {
        if (_suppressSettingChangeSideEffects)
        {
            _summonLifetimeSettingChangeDirty = true;
            return;
        }

        MagicSummonQualityPresetSystem.RefreshLoadedSummonLifetimeState();
    }

    private void FlushReloadSettingChanges()
    {
        bool reapplyWorld = _worldApplySettingChangeDirty;
        bool refreshSummonLifetime = _summonLifetimeSettingChangeDirty;
        _worldApplySettingChangeDirty = false;
        _summonLifetimeSettingChangeDirty = false;

        if (reapplyWorld)
        {
            SecondaryAttackFacade.RequestCurrentWorldReapply();
        }

        if (refreshSummonLifetime)
        {
            MagicSummonQualityPresetSystem.RefreshLoadedSummonLifetimeState();
        }
    }

    private void BindGeneralSettings()
    {
        const string group = "1 - General";
        _ = ConfigSync.AddLockingConfigEntry(config(group, "Lock Configuration", Toggle.On, new ConfigDescription("If on, the configuration is locked and can be changed by server admins only.", null, new ConfigurationManagerAttributes { Order = 700 })));
        AdminNoPresetCooldowns = config(group, "Admin No Preset Cooldowns", Toggle.Off, new ConfigDescription("Client-side admin convenience. If on, host or server-admin players use SecondaryAttacks presets without preset cooldowns. This does not change server-synced YAML values and does not remove internal hit throttles.", null, new ConfigurationManagerAttributes { Order = 690 }), synchronizedSetting: false);
        QuickstepEnabled = config(group, "Quickstep Enabled", Toggle.Off, new ConfigDescription("Enables the fixed quickstep for equipped Knives and Unarmed weapons. Bare fists are excluded. Quickstep uses 200 horizontal acceleration for 0.25 seconds, full-duration invincibility without a shield, 0.15 seconds of invincibility with a shield, and a 0.5-second cooldown. Quickstep and its double-press dodge handoff each consume 60% of the current dodge stamina cost.", null, new ConfigurationManagerAttributes { Order = 680 }), synchronizedSetting: true);
        BackstabSneakSkillRaiseAmount = config(group, "Backstab Sneak Skill Raise Amount", 1.0f, new ConfigDescription("Sneak skill raise amount awarded whenever any attack successfully triggers backstab damage. 0 disables this reward.", new AcceptableValueRange<float>(0f, 10f), new ConfigurationManagerAttributes { Order = 670 }), synchronizedSetting: true);
        SneakVisibilitySkillEffectFactor = config(group, "Sneak Visibility Skill Effect Factor", 1.0f, new ConfigDescription("Multiplier for the visibility reduction gained from Sneak skill while crouching. 1.0 keeps vanilla; 2.0 doubles only the skill-based reduction. Visibility is clamped to a fixed minimum of 0.1. At factor 1.0, Sneak 0 is 0.5 in darkness and 1.0 in bright light; Sneak 100 is 0.2 in darkness and 0.6 in bright light.", new AcceptableValueRange<float>(1f, 2f), new ConfigurationManagerAttributes { Order = 660 }), synchronizedSetting: true);
        SneakMovementSpeedSkillFactor = config(group, "Sneak Movement Speed Skill Factor", 1.0f, new ConfigDescription("Sneak movement speed multiplier at Sneak skill 100 while crouching. 1.0 keeps vanilla; 2.0 doubles crouched movement speed at Sneak 100, with lower Sneak levels linearly interpolated.", new AcceptableValueRange<float>(1f, 2f), new ConfigurationManagerAttributes { Order = 650 }), synchronizedSetting: true);
        KeepCrouchingDuringElementalDamageOverTime = config(group, "Keep Crouching During Elemental Damage Over Time", Toggle.Off, new ConfigDescription("If on, periodic Fire, Spirit, and Poison damage-over-time ticks do not cancel the player's crouch toggle. Direct hits, lethal damage, stagger, and knockback retain vanilla behavior.", null, new ConfigurationManagerAttributes { Order = 640 }), synchronizedSetting: true);
    }

    private void BindBloodMagicSettings()
    {
        const string group = "2 - Blood Magic";
        MagicSummonQualityPreset = config(group, "Magic Summon Quality Preset", MagicSummonQualityPresetSelection.LevelByQuality, new ConfigDescription("Global quality preset for BloodMagic summon items whose primary or secondary projectile resolves to a SpawnAbility. Explicit summon.qualityPreset values in SecondaryAttacks.BloodMagic.yml override this. Off disables automatic quality scaling; CountByQuality makes item quality raise active summon count; LevelByQuality makes item quality raise summoned creature level.", null, new ConfigurationManagerAttributes { Order = 700 }), synchronizedSetting: true);
        BloodMagicSummonLifetimeSeconds = config(group, "Blood Magic Summon Lifetime Seconds", 1200, new ConfigDescription("Base lifetime in whole seconds for creatures summoned by player-used Blood Magic staves. 0 disables summon lifetime assignment, restoration, expiration, and HUD timers, including staff-specific YAML overrides. Positive values enable the feature. The configured base is assigned only to newly created summons; existing assigned deadlines are not recalculated.", new AcceptableNonNegativeInt(), new ConfigurationManagerAttributes { Order = 690 }), synchronizedSetting: true);
        BloodMagicSummonLifetimeSkill100Multiplier = config(group, "Blood Magic Summon Lifetime Multiplier At Skill 100", 2.0f, new ConfigDescription("Multiplier applied to the base summon lifetime at Blood Magic skill 100. Skill 0 uses the base lifetime, with intermediate skill levels interpolated linearly.", new AcceptableValueRange<float>(1f, 10f), new ConfigurationManagerAttributes { Order = 680 }), synchronizedSetting: true);
        BloodMagicHealthCostUsesMaxHealth = config(group, "Blood Magic Health Cost Uses Max Health", Toggle.On, new ConfigDescription("If on, Blood Magic attack health percentage costs are calculated from max health at cast time instead of current health. Flat health cost and Blood Magic skill cost reduction are unchanged.", null, new ConfigurationManagerAttributes { Order = 670 }), synchronizedSetting: true);
        BloodMagicHealthCostSkillRaiseFactor = config(group, "Blood Magic Health Cost Skill Raise Factor", 0.01f, new ConfigDescription("Additional Blood Magic skill raise amount per actual consumed health. Vanilla Blood Magic skill gain always remains active. 0 disables only this custom health-cost skill gain. Example: consuming 160 health and 0.01 factor awards 1.6 extra raise amount.", new AcceptableValueRange<float>(0f, 0.1f), new ConfigurationManagerAttributes { Order = 660 }), synchronizedSetting: true);
    }

    private void BindRangedSettings()
    {
        const string group = "3 - Ranged";
        const string descriptionSuffix = "Explicit prefab entries in SecondaryAttacks.Ranged.yml override this automatic group preset. Select Off to disable automatic assignment for this group.";
        FireballStaffPreset = config(group, "Fireball Staff Preset", RangedPresetSelection.Sentinel, $"Default ranged preset for ElementalMagic items whose primary attack animation is staff_fireball. {descriptionSuffix}", synchronizedSetting: true);
        RapidStaffPreset = config(group, "Rapidfire Staff Preset", RangedPresetSelection.Spiral, $"Default ranged preset for ElementalMagic items whose primary attack animation is staff_rapidfire. {descriptionSuffix}", synchronizedSetting: true);
        LightningStaffPreset = config(group, "Reload Staff Preset", RangedPresetSelection.Burst, $"Default ranged preset for ElementalMagic items whose primary attack animation is staff_lightningshot. {descriptionSuffix}", synchronizedSetting: true);
        BowPreset = config(group, "Bow Preset", RangedPresetSelection.Barrage, $"Default ranged preset for bow items. {descriptionSuffix}", synchronizedSetting: true);
        CrossbowPreset = config(group, "Crossbow Preset", RangedPresetSelection.Burst, $"Default ranged preset for reload-based crossbow-style projectile items. {descriptionSuffix}", synchronizedSetting: true);
        BombPreset = config(group, "Bomb Preset", BombPresetSelection.Auto, "Default ranged preset for throw_bomb projectile items. Auto uses overchargedBomb when the primary projectile itself has AOE or spawns an Aoe prefab on hit, and stickyDetonator otherwise. Explicit prefab entries in SecondaryAttacks.Ranged.yml override this automatic group preset. Select Off to disable automatic bomb assignment.", synchronizedSetting: true);
    }

    private void BindUiSettings()
    {
        const string group = "4 - UI";
        SecondaryCooldownHudEnabled = config(group, "Secondary Cooldown HUD Enabled", Toggle.On, "If on, secondary attack cooldowns and charge progress are shown in a dedicated HUD block. Off hides this display without changing cooldown behavior.", synchronizedSetting: false);
        SecondaryAttackTooltipsEnabled = config(group, "Secondary Attack Tooltips Enabled", Toggle.On, "If on, weapons with an applied SecondaryAttacks preset show its localized name and description in item tooltips. Off hides only this client-side tooltip section.", synchronizedSetting: false);
        SecondaryCooldownHudPositionX = config(group, "Secondary Cooldown HUD Position X", 0.615f, new ConfigDescription("Client-side normalized horizontal position for the secondary cooldown HUD. 0 is left, 1 is right. Open inventory to preview the configured position.", new AcceptableValueRange<float>(0f, 1f)), synchronizedSetting: false);
        SecondaryCooldownHudPositionY = config(group, "Secondary Cooldown HUD Position Y", 0.22f, new ConfigDescription("Client-side normalized vertical position for the secondary cooldown HUD. 0 is bottom, 1 is top. Open inventory to preview the configured position.", new AcceptableValueRange<float>(0f, 1f)), synchronizedSetting: false);
    }

    #region ConfigOptions

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

    private sealed class AcceptableNonNegativeInt : AcceptableValueBase
    {
        internal AcceptableNonNegativeInt() : base(typeof(int))
        {
        }

        public override object Clamp(object value)
        {
            return Math.Max(0, (int)value);
        }

        public override bool IsValid(object value)
        {
            return value is int intValue && intValue >= 0;
        }

        public override string ToDescriptionString()
        {
            return "# Acceptable values: Non-negative whole numbers (0 or greater)";
        }
    }

    private class ConfigurationManagerAttributes
    {
        [UsedImplicitly] public int? Order = null!;
        [UsedImplicitly] public bool? Browsable = null!;
        [UsedImplicitly] public string? Category = null!;
        [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
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
