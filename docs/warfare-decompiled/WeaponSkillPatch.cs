using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using JetBrains.Annotations;
using SkillManager;
using UnityEngine;

namespace Warfare;

[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(0)]
[<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(1)]
public class WeaponSkillPatch
{
	[HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
	[<12d738c4-d00c-401c-b127-bd32667a54bc>Nullable(0)]
	public static class Attack_FireProjectileBurst_Patch
	{
		[<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)]
		[HarmonyPatch(typeof(Attack), "GetAttackStamina")]
		private static class Attack_GetAttackStamina_Patch_Throwing
		{
			[<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(1)]
			private static void Postfix(Attack __instance, ref float __result)
			{
				if (ThrowableWeapons.Contains(__instance.m_weapon.m_shared.m_name))
				{
					float skillFactor = ((Character)(object)Player.m_localPlayer).GetSkillFactor("Throwing");
					__result *= 1f - skillFactor;
				}
			}
		}

		[<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)]
		[HarmonyPatch(typeof(Attack), "GetAttackStamina")]
		private static class Attack_GetAttackStamina_Patch_Scythes
		{
			[<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(1)]
			private static void Postfix(Attack __instance, ref float __result)
			{
				if (ScytheWeapons.Contains(__instance.m_weapon.m_shared.m_name))
				{
					float skillFactor = ((Character)(object)Player.m_localPlayer).GetSkillFactor("Scythes");
					__result *= 1f - skillFactor;
				}
			}
		}

		private static List<string> ThrowableWeapons = new List<string> { "$throw_axe_flint_TW", "$throw_axe_bronze_TW", "$throw_axe_iron_TW", "$throw_axe_silver_TW", "$throw_axe_blackmetal_TW", "$throw_axe_dvergr_TW", "$throw_axe_njord_TW", "$throw_axe_surtr_TW" };

		private static List<string> ScytheWeapons = new List<string> { "$dual_scythebloodthirst_TW", "$scythe_vampiric_TW" };

		private static void Modify(Attack a, HitData hit, ref float projVelocity, ref float projectileAccuracy)
		{
			//IL_0040: Unknown result type (might be due to invalid IL or missing references)
			//IL_0045: Unknown result type (might be due to invalid IL or missing references)
			if (!((Object)(object)a.m_character != (Object)(object)Player.m_localPlayer) && ThrowableWeapons.Contains(a.m_weapon.m_shared.m_name))
			{
				hit.m_skill = fromName("Throwing");
				float skillFactor = ((Character)(object)Player.m_localPlayer).GetSkillFactor("Throwing");
				projVelocity *= 1f + skillFactor;
				projectileAccuracy *= 1f + skillFactor;
			}
		}

		[UsedImplicitly]
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code)
		{
			//IL_0003: Unknown result type (might be due to invalid IL or missing references)
			//IL_0009: Expected O, but got Unknown
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			//IL_0039: Expected O, but got Unknown
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			//IL_0077: Expected O, but got Unknown
			//IL_0091: Unknown result type (might be due to invalid IL or missing references)
			//IL_0097: Expected O, but got Unknown
			//IL_00b1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b7: Expected O, but got Unknown
			//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d7: Expected O, but got Unknown
			//IL_0101: Unknown result type (might be due to invalid IL or missing references)
			//IL_0107: Expected O, but got Unknown
			CodeMatcher val = new CodeMatcher(code, (ILGenerator)null);
			val.MatchForward(false, (CodeMatch[])(object)new CodeMatch[1]
			{
				new CodeMatch((Func<CodeInstruction, bool>)([<852c3678-ed26-475b-a49b-ff27c8cecffc>NullableContext(0)] (CodeInstruction ins) => ins.opcode == OpCodes.Stloc_S && ins.operand.StlocIndex(22)), (string)null)
			});
			if (val.IsInvalid)
			{
				return code;
			}
			byte b = 21;
			byte b2 = 3;
			byte b3 = 4;
			val.Advance(1);
			val.InsertAndAdvance((CodeInstruction[])(object)new CodeInstruction[1]
			{
				new CodeInstruction(OpCodes.Ldarg_0, (object)null)
			});
			val.InsertAndAdvance((CodeInstruction[])(object)new CodeInstruction[1]
			{
				new CodeInstruction(OpCodes.Ldloc_S, (object)b)
			});
			val.InsertAndAdvance((CodeInstruction[])(object)new CodeInstruction[1]
			{
				new CodeInstruction(OpCodes.Ldloca_S, (object)b2)
			});
			val.InsertAndAdvance((CodeInstruction[])(object)new CodeInstruction[1]
			{
				new CodeInstruction(OpCodes.Ldloca_S, (object)b3)
			});
			val.InsertAndAdvance((CodeInstruction[])(object)new CodeInstruction[1]
			{
				new CodeInstruction(OpCodes.Call, (object)AccessTools.Method(typeof(Attack_FireProjectileBurst_Patch), "Modify", (Type[])null, (Type[])null))
			});
			return val.Instructions();
		}
	}

	public static SkillType fromName(string englishName)
	{
		return (SkillType)Math.Abs(StringExtensionMethods.GetStableHashCode(englishName));
	}
}
You are not using the latest version of the tool, please update.
Latest version is '10.0.1.8346' (yours is '9.1.0.7988')
