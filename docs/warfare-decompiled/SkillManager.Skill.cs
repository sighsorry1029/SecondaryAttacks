using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace SkillManager;

[<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(1)]
[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(0)]
[PublicAPI]
public class Skill
{
	[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(0)]
	public static class LocalizationCache
	{
		private static readonly Dictionary<string, Localization> localizations = new Dictionary<string, Localization>();

		internal static void LocalizationPostfix(Localization __instance, string language)
		{
			string key = localizations.FirstOrDefault([<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)] (KeyValuePair<string, Localization> l) => l.Value == __instance).Key;
			if (key != null)
			{
				localizations.Remove(key);
			}
			if (!localizations.ContainsKey(language))
			{
				localizations.Add(language, __instance);
			}
		}

		public static Localization ForLanguage([<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)] string language = null)
		{
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0030: Expected O, but got Unknown
			if (localizations.TryGetValue(language ?? PlayerPrefs.GetString("language", "English"), out var value))
			{
				return value;
			}
			value = new Localization();
			if (language != null)
			{
				value.SetupLanguage(language);
			}
			return value;
		}
	}

	[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(0)]
	[PublicAPI]
	public class LocalizeKey
	{
		private static readonly List<LocalizeKey> keys = new List<LocalizeKey>();

		public readonly string Key;

		public readonly Dictionary<string, string> Localizations = new Dictionary<string, string>();

		public LocalizeKey(string key)
		{
			Key = key.Replace("$", "");
			keys.Add(this);
		}

		public void Alias(string alias)
		{
			Localizations.Clear();
			if (!alias.Contains("$"))
			{
				alias = "$" + alias;
			}
			Localizations["alias"] = alias;
			Localization.instance.AddWord(Key, Localization.instance.Localize(alias));
		}

		public LocalizeKey English(string key)
		{
			return addForLang("English", key);
		}

		public LocalizeKey Swedish(string key)
		{
			return addForLang("Swedish", key);
		}

		public LocalizeKey French(string key)
		{
			return addForLang("French", key);
		}

		public LocalizeKey Italian(string key)
		{
			return addForLang("Italian", key);
		}

		public LocalizeKey German(string key)
		{
			return addForLang("German", key);
		}

		public LocalizeKey Spanish(string key)
		{
			return addForLang("Spanish", key);
		}

		public LocalizeKey Russian(string key)
		{
			return addForLang("Russian", key);
		}

		public LocalizeKey Romanian(string key)
		{
			return addForLang("Romanian", key);
		}

		public LocalizeKey Bulgarian(string key)
		{
			return addForLang("Bulgarian", key);
		}

		public LocalizeKey Macedonian(string key)
		{
			return addForLang("Macedonian", key);
		}

		public LocalizeKey Finnish(string key)
		{
			return addForLang("Finnish", key);
		}

		public LocalizeKey Danish(string key)
		{
			return addForLang("Danish", key);
		}

		public LocalizeKey Norwegian(string key)
		{
			return addForLang("Norwegian", key);
		}

		public LocalizeKey Icelandic(string key)
		{
			return addForLang("Icelandic", key);
		}

		public LocalizeKey Turkish(string key)
		{
			return addForLang("Turkish", key);
		}

		public LocalizeKey Lithuanian(string key)
		{
			return addForLang("Lithuanian", key);
		}

		public LocalizeKey Czech(string key)
		{
			return addForLang("Czech", key);
		}

		public LocalizeKey Hungarian(string key)
		{
			return addForLang("Hungarian", key);
		}

		public LocalizeKey Slovak(string key)
		{
			return addForLang("Slovak", key);
		}

		public LocalizeKey Polish(string key)
		{
			return addForLang("Polish", key);
		}

		public LocalizeKey Dutch(string key)
		{
			return addForLang("Dutch", key);
		}

		public LocalizeKey Portuguese_European(string key)
		{
			return addForLang("Portuguese_European", key);
		}

		public LocalizeKey Portuguese_Brazilian(string key)
		{
			return addForLang("Portuguese_Brazilian", key);
		}

		public LocalizeKey Chinese(string key)
		{
			return addForLang("Chinese", key);
		}

		public LocalizeKey Japanese(string key)
		{
			return addForLang("Japanese", key);
		}

		public LocalizeKey Korean(string key)
		{
			return addForLang("Korean", key);
		}

		public LocalizeKey Hindi(string key)
		{
			return addForLang("Hindi", key);
		}

		public LocalizeKey Thai(string key)
		{
			return addForLang("Thai", key);
		}

		public LocalizeKey Abenaki(string key)
		{
			return addForLang("Abenaki", key);
		}

		public LocalizeKey Croatian(string key)
		{
			return addForLang("Croatian", key);
		}

		public LocalizeKey Georgian(string key)
		{
			return addForLang("Georgian", key);
		}

		public LocalizeKey Greek(string key)
		{
			return addForLang("Greek", key);
		}

		public LocalizeKey Serbian(string key)
		{
			return addForLang("Serbian", key);
		}

		public LocalizeKey Ukrainian(string key)
		{
			return addForLang("Ukrainian", key);
		}

		private LocalizeKey addForLang(string lang, string value)
		{
			Localizations[lang] = value;
			if (Localization.instance.GetSelectedLanguage() == lang)
			{
				Localization.instance.AddWord(Key, value);
			}
			else if (lang == "English" && !Localization.instance.m_translations.ContainsKey(Key))
			{
				Localization.instance.AddWord(Key, value);
			}
			return this;
		}

		[HarmonyPriority(300)]
		internal static void AddLocalizedKeys(Localization __instance, string language)
		{
			foreach (LocalizeKey key in keys)
			{
				string value2;
				if (key.Localizations.TryGetValue(language, out var value) || key.Localizations.TryGetValue("English", out value))
				{
					__instance.AddWord(key.Key, value);
				}
				else if (key.Localizations.TryGetValue("alias", out value2))
				{
					__instance.AddWord(key.Key, Localization.instance.Localize(value2));
				}
			}
		}
	}

	[<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)]
	private class ConfigurationManagerAttributes
	{
		[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
		[UsedImplicitly]
		public string Category;
	}

	[HarmonyPatch(typeof(Skills), "IsSkillValid")]
	[<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)]
	private static class Patch_Skills_IsSkillValid
	{
		private static void Postfix(SkillType type, ref bool __result)
		{
			//IL_000f: Unknown result type (might be due to invalid IL or missing references)
			if (!__result && skills.ContainsKey(type))
			{
				__result = true;
			}
		}
	}

	private static readonly Dictionary<SkillType, Skill> skills;

	internal static readonly Dictionary<string, Skill> skillByName;

	private readonly string skillName;

	private readonly string internalSkillName;

	private readonly SkillDef skillDef;

	public readonly LocalizeKey Name;

	public readonly LocalizeKey Description;

	private float skillEffectFactor = 1f;

	private int skillLoss = 5;

	public bool Configurable = false;

	private static bool InitializedTerminal;

	[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
	private static Localization _english;

	[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
	private static BaseUnityPlugin _plugin;

	private static bool hasConfigSync;

	[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
	private static object _configSync;

	public float SkillGainFactor
	{
		get
		{
			return skillDef.m_increseStep;
		}
		set
		{
			skillDef.m_increseStep = value;
			this.SkillGainFactorChanged?.Invoke(value);
		}
	}

	public float SkillEffectFactor
	{
		get
		{
			return skillEffectFactor;
		}
		set
		{
			skillEffectFactor = value;
			this.SkillEffectFactorChanged?.Invoke(value);
		}
	}

	public int SkillLoss
	{
		get
		{
			return skillLoss;
		}
		set
		{
			skillLoss = value;
			this.SkillLossChanged?.Invoke(value);
		}
	}

	private static Localization english => _english ?? (_english = LocalizationCache.ForLanguage("English"));

	private static BaseUnityPlugin plugin
	{
		get
		{
			//IL_0041: Unknown result type (might be due to invalid IL or missing references)
			//IL_0046: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Expected O, but got Unknown
			object obj = _plugin;
			if (obj == null)
			{
				BaseUnityPlugin val = (BaseUnityPlugin)Chainloader.ManagerObject.GetComponent((Type)Assembly.GetExecutingAssembly().DefinedTypes.First([<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)] (TypeInfo t) => t.IsClass && typeof(BaseUnityPlugin).IsAssignableFrom(t)));
				_plugin = val;
				obj = (object)val;
			}
			return (BaseUnityPlugin)obj;
		}
	}

	[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
	private static object configSync
	{
		[<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(2)]
		get
		{
			if (_configSync == null && hasConfigSync)
			{
				Type type = Assembly.GetExecutingAssembly().GetType("ServerSync.ConfigSync");
				if ((object)type != null)
				{
					_configSync = Activator.CreateInstance(type, plugin.Info.Metadata.GUID + " SkillManager");
					type.GetField("CurrentVersion").SetValue(_configSync, plugin.Info.Metadata.Version.ToString());
					type.GetProperty("IsLocked").SetValue(_configSync, true);
				}
				else
				{
					hasConfigSync = false;
				}
			}
			return _configSync;
		}
	}

	[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
	[method: <852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(2)]
	[field: <12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
	public event Action<float> SkillGainFactorChanged;

	[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
	[method: <852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(2)]
	[field: <12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
	public event Action<float> SkillEffectFactorChanged;

	[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
	[method: <852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(2)]
	[field: <12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)]
	public event Action<float> SkillLossChanged;

	public Skill(string englishName, string icon)
		: this(englishName, loadSprite(icon, 64, 64))
	{
	}

	public unsafe Skill(string englishName, Sprite icon)
	{
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_005e: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0076: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		//IL_008d: Expected O, but got Unknown
		SkillType val = fromName(englishName);
		string text = new Regex("[^a-zA-Z]").Replace(englishName, "_");
		skills[val] = this;
		skillByName[englishName] = this;
		skillDef = new SkillDef
		{
			m_description = "$skilldesc_" + text,
			m_icon = icon,
			m_increseStep = 1f,
			m_skill = val
		};
		internalSkillName = text;
		skillName = englishName;
		Name = new LocalizeKey("skill_" + ((object)(*(SkillType*)(&val))/*cast due to .constrained prefix*/).ToString()).English(englishName);
		Description = new LocalizeKey("skilldesc_" + text);
	}

	public static SkillType fromName(string englishName)
	{
		return (SkillType)Math.Abs(StringExtensionMethods.GetStableHashCode(englishName));
	}

	static Skill()
	{
		//IL_0026: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Expected O, but got Unknown
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Expected O, but got Unknown
		//IL_0096: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a3: Expected O, but got Unknown
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Expected O, but got Unknown
		//IL_010d: Unknown result type (might be due to invalid IL or missing references)
		//IL_011b: Expected O, but got Unknown
		//IL_014a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0157: Expected O, but got Unknown
		//IL_0185: Unknown result type (might be due to invalid IL or missing references)
		//IL_01a0: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ad: Expected O, but got Unknown
		//IL_01ad: Expected O, but got Unknown
		//IL_01dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_01e9: Expected O, but got Unknown
		//IL_0217: Unknown result type (might be due to invalid IL or missing references)
		//IL_0234: Unknown result type (might be due to invalid IL or missing references)
		//IL_023f: Expected O, but got Unknown
		//IL_023f: Expected O, but got Unknown
		skills = new Dictionary<SkillType, Skill>();
		skillByName = new Dictionary<string, Skill>();
		InitializedTerminal = false;
		hasConfigSync = true;
		Harmony val = new Harmony("org.bepinex.helpers.skillmanager");
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(FejdStartup), "Awake", (Type[])null, (Type[])null), (HarmonyMethod)null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Skill), "Patch_FejdStartup", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(Skills), "GetSkillDef", (Type[])null, (Type[])null), (HarmonyMethod)null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Skill), "Patch_Skills_GetSkillDef", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(Skills), "CheatRaiseSkill", (Type[])null, (Type[])null), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Skill), "Patch_Skills_CheatRaiseskill", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(Skills), "CheatResetSkill", (Type[])null, (Type[])null), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Skill), "Patch_Skills_CheatResetSkill", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(Localization), "LoadCSV", (Type[])null, (Type[])null), (HarmonyMethod)null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(LocalizeKey), "AddLocalizedKeys", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(Terminal), "InitTerminal", (Type[])null, (Type[])null), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Skill), "Patch_Terminal_InitTerminal_Prefix", (Type[])null, (Type[])null)), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Skill), "Patch_Terminal_InitTerminal", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(Localization), "SetupLanguage", (Type[])null, (Type[])null), (HarmonyMethod)null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(LocalizationCache), "LocalizationPostfix", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
		val.Patch((MethodBase)AccessTools.DeclaredMethod(typeof(Skills), "OnDeath", (Type[])null, (Type[])null), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Skill), "Patch_Skills_OnDeath_Prefix", (Type[])null, (Type[])null)), (HarmonyMethod)null, (HarmonyMethod)null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Skill), "Patch_Skills_OnDeath_Finalizer", (Type[])null, (Type[])null)), (HarmonyMethod)null);
	}

	private static void Patch_FejdStartup()
	{
		//IL_00de: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Expected O, but got Unknown
		//IL_0163: Unknown result type (might be due to invalid IL or missing references)
		//IL_016d: Expected O, but got Unknown
		//IL_01e1: Unknown result type (might be due to invalid IL or missing references)
		//IL_01eb: Expected O, but got Unknown
		foreach (Skill skill in skills.Values)
		{
			if (skill.Configurable)
			{
				string key = skill.Name.Key;
				string text = new Regex("['[\"\\]]").Replace(english.Localize(key), "").Trim();
				string category = Localization.instance.Localize(key).Trim();
				ConfigEntry<float> skillGain = config(text, "Skill gain factor", skill.SkillGainFactor, new ConfigDescription("The rate at which you gain experience for the skill.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0.01f, 5f), new object[1]
				{
					new ConfigurationManagerAttributes
					{
						Category = category
					}
				}));
				skill.SkillGainFactor = skillGain.Value;
				skillGain.SettingChanged += [<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)] (object _, EventArgs _) =>
				{
					skill.SkillGainFactor = skillGain.Value;
				};
				ConfigEntry<float> skillEffect = config(text, "Skill effect factor", skill.SkillEffectFactor, new ConfigDescription("The power of the skill, based on the default power.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0.01f, 5f), new object[1]
				{
					new ConfigurationManagerAttributes
					{
						Category = category
					}
				}));
				skill.SkillEffectFactor = skillEffect.Value;
				skillEffect.SettingChanged += [<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)] (object _, EventArgs _) =>
				{
					skill.SkillEffectFactor = skillEffect.Value;
				};
				ConfigEntry<int> skillLoss = config(text, "Skill loss", skill.skillLoss, new ConfigDescription("How much experience to lose on death.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(0, 100), new object[1]
				{
					new ConfigurationManagerAttributes
					{
						Category = category
					}
				}));
				skill.skillLoss = skillLoss.Value;
				skillLoss.SettingChanged += [<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)] (object _, EventArgs _) =>
				{
					skill.skillLoss = skillLoss.Value;
				};
			}
		}
	}

	private static void Patch_Skills_GetSkillDef([<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)] ref SkillDef __result, List<SkillDef> ___m_skills, SkillType type)
	{
		//IL_0005: Unknown result type (might be due to invalid IL or missing references)
		if (__result == null)
		{
			SkillDef val = GetSkillDef(type);
			if (val != null)
			{
				___m_skills.Add(val);
				__result = val;
			}
		}
	}

	private unsafe static bool Patch_Skills_CheatRaiseskill(Skills __instance, string name, float value, Player ___m_player)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0025: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		foreach (SkillType key in skills.Keys)
		{
			Skill skill = skills[key];
			if (string.Equals(skill.internalSkillName, name, StringComparison.CurrentCultureIgnoreCase))
			{
				Skill skill2 = __instance.GetSkill(key);
				skill2.m_level += value;
				skill2.m_level = Mathf.Clamp(skill2.m_level, 0f, 100f);
				((Character)___m_player).Message((MessageType)1, "Skill increased " + Localization.instance.Localize("$skill_" + ((object)(*(SkillType*)(&key))/*cast due to .constrained prefix*/).ToString()) + ": " + (int)skill2.m_level, 0, skill2.m_info.m_icon);
				Console.instance.Print("Skill " + skill.internalSkillName + " = " + skill2.m_level);
				return false;
			}
		}
		return true;
	}

	private static bool Patch_Skills_CheatResetSkill(Skills __instance, string name)
	{
		//IL_0016: Unknown result type (might be due to invalid IL or missing references)
		//IL_001b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0022: Unknown result type (might be due to invalid IL or missing references)
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		foreach (SkillType key in skills.Keys)
		{
			Skill skill = skills[key];
			if (string.Equals(skill.internalSkillName, name, StringComparison.CurrentCultureIgnoreCase))
			{
				__instance.ResetSkill(key);
				Console.instance.Print("Skill " + skill.internalSkillName + " reset");
				return false;
			}
		}
		return true;
	}

	private static void Patch_Skills_OnDeath_Prefix(Skills __instance, [<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(new byte[] { 2, 1 })] ref Dictionary<SkillType, Skill> __state)
	{
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
		if (__state == null)
		{
			__state = new Dictionary<SkillType, Skill>();
		}
		foreach (KeyValuePair<SkillType, Skill> skill in skills)
		{
			if (__instance.m_skillData.TryGetValue(skill.Key, out var value))
			{
				__state[skill.Key] = value;
				if (skill.Value.skillLoss > 0)
				{
					Skill obj = value;
					obj.m_level -= value.m_level * (float)skill.Value.SkillLoss / 100f;
					value.m_accumulator = 0f;
				}
				__instance.m_skillData.Remove(skill.Key);
			}
		}
	}

	private static void Patch_Skills_OnDeath_Finalizer(Skills __instance, [<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(new byte[] { 2, 1 })] ref Dictionary<SkillType, Skill> __state)
	{
		//IL_002a: Unknown result type (might be due to invalid IL or missing references)
		if (__state == null)
		{
			return;
		}
		foreach (KeyValuePair<SkillType, Skill> item in __state)
		{
			__instance.m_skillData[item.Key] = item.Value;
		}
		__state = null;
	}

	private static void Patch_Terminal_InitTerminal_Prefix()
	{
		InitializedTerminal = Terminal.m_terminalInitialized;
	}

	private static void Patch_Terminal_InitTerminal()
	{
		if (!InitializedTerminal)
		{
			AddSkill(Terminal.commands["raiseskill"]);
			AddSkill(Terminal.commands["resetskill"]);
		}
		static void AddSkill(ConsoleCommand command)
		{
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0025: Expected O, but got Unknown
			ConsoleOptionsFetcher fetcher = command.m_tabOptionsFetcher;
			command.m_tabOptionsFetcher = (ConsoleOptionsFetcher)([<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)] () =>
			{
				List<string> list = fetcher.Invoke();
				list.AddRange(skills.Values.Select([<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)] (Skill skill) => skill.internalSkillName));
				return list;
			});
		}
	}

	[<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(2)]
	private static SkillDef GetSkillDef(SkillType skillType)
	{
		//IL_0006: Unknown result type (might be due to invalid IL or missing references)
		//IL_001d: Unknown result type (might be due to invalid IL or missing references)
		if (!skills.ContainsKey(skillType))
		{
			return null;
		}
		Skill skill = skills[skillType];
		return skill.skillDef;
	}

	private static byte[] ReadEmbeddedFileBytes(string name)
	{
		using MemoryStream memoryStream = new MemoryStream();
		Assembly.GetExecutingAssembly().GetManifestResourceStream(Assembly.GetExecutingAssembly().GetName().Name + "." + name).CopyTo(memoryStream);
		return memoryStream.ToArray();
	}

	private static Texture2D loadTexture(string name)
	{
		//IL_0003: Unknown result type (might be due to invalid IL or missing references)
		//IL_0009: Expected O, but got Unknown
		Texture2D val = new Texture2D(0, 0);
		ImageConversion.LoadImage(val, ReadEmbeddedFileBytes("icons." + name));
		return val;
	}

	private static Sprite loadSprite(string name, int width, int height)
	{
		//IL_0014: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		return Sprite.Create(loadTexture(name), new Rect(0f, 0f, (float)width, (float)height), Vector2.zero);
	}

	private static ConfigEntry<T> config<[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)] T>(string group, string name, T value, ConfigDescription description)
	{
		ConfigEntry<T> val = plugin.Config.Bind<T>(group, name, value, description);
		configSync?.GetType().GetMethod("AddConfigEntry").MakeGenericMethod(typeof(T))
			.Invoke(configSync, new object[1] { val });
		return val;
	}

	private static ConfigEntry<T> config<[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(2)] T>(string group, string name, T value, string description)
	{
		//IL_000a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0014: Expected O, but got Unknown
		return config(group, name, value, new ConfigDescription(description, (AcceptableValueBase)null, Array.Empty<object>()));
	}
}
You are not using the latest version of the tool, please update.
Latest version is '10.0.1.8346' (yours is '9.1.0.7988')
