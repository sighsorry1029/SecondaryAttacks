# Warfare Skill Analysis

Source DLL copied from:

`C:/Users/blizz/AppData/Roaming/com.kesomannen.gale/valheim/profiles/adminadmin/BepInEx/plugins/Therzie-Warfare/Warfare.dll`

Local copy:

`Libs/Warfare.dll`

Selected decompile output:

- `docs/warfare-decompiled/class-list.txt`
- `docs/warfare-decompiled/WarfarePlugin.cs`
- `docs/warfare-decompiled/WeaponSkillPatch.cs`
- `docs/warfare-decompiled/SkillManager.Skill.cs`
- `docs/warfare-decompiled/SkillManager.SkillExtensions.cs`

## Custom Skill Registration

Warfare registers two custom skills with the embedded `SkillManager.Skill` helper:

- `Throwing`
  - Korean name: `던지기`
  - Korean description: `투척 무기의 피해량과 정확도가 증가합니다.`
  - Stable hash / `Skills.SkillType`: `99738506`
- `Scythes`
  - Korean name: `낫`
  - Korean description: `낫 무기로 인한 피해량이 증가합니다.`
  - Stable hash / `Skills.SkillType`: `1328437209`

The skill type is generated as:

```csharp
(Skills.SkillType)Math.Abs(StringExtensionMethods.GetStableHashCode(skillName))
```

Warfare then assigns the generated skill type directly to each item's `m_shared.m_skillType`.

## Throwing Skill

Assigned items:

- `ThrowAxeFlint_TW`
- `ThrowAxeBronze_TW`
- `ThrowAxeIron_TW`
- `ThrowAxeSilver_TW`
- `ThrowAxeBlackmetal_TW`
- `ThrowAxeDvergr_TW`

The `WeaponSkillPatch` lists two additional names for skill stamina handling:

- `$throw_axe_njord_TW`
- `$throw_axe_surtr_TW`

Those are likely added by Warfare Fire And Ice or another Therzie module, not by the base Warfare item registration shown in this DLL.

Behavior:

- `Attack.GetAttackStamina` postfix checks the localized shared name list and multiplies stamina cost by `1 - ThrowingSkillFactor`.
- `Attack.FireProjectileBurst` transpiler calls `Modify(Attack, HitData, ref projVelocity, ref projectileAccuracy)`.
- For local player throwing axes, `Modify`:
  - forces `hit.m_skill = Throwing`
  - multiplies projectile velocity by `1 + ThrowingSkillFactor`
  - multiplies projectile accuracy by `1 + ThrowingSkillFactor`
- Damage still comes mostly from vanilla attack flow because the weapon shared skill type is already set to `Throwing`, so `Attack.FireProjectileBurst` computes damage skill factor from Throwing.

SecondaryAttacks implication:

- Keeping `m_shared.m_skillType == Throwing` is important.
- Our Warfare throwable durability conversion should not replace the skill type.
- Vanilla primary/secondary projectile attacks still receive Warfare's velocity/accuracy transpiler when they use `Attack.FireProjectileBurst`.
- Custom projectile runtimes that bypass `Attack.FireProjectileBurst` will not automatically receive Warfare's velocity/accuracy multiplier unless we add explicit compatibility.

## Scythes Skill

Assigned items in base Warfare:

- `DualScytheBloodthirst_TW`
- `ScytheVampiric_TW`

Behavior:

- Warfare assigns `m_shared.m_skillType = Scythes`.
- `Attack.GetAttackStamina` postfix checks the localized shared name list and multiplies stamina cost by `1 - ScythesSkillFactor`.
- There is no Scythes-specific projectile transpiler like Throwing.
- Damage scaling is expected to come from vanilla melee attack flow because the weapon's shared skill type is `Scythes`.

SecondaryAttacks implication:

- Warfare scythes are not `Skills.SkillType.Farming`.
- Any logic that detects scythes only by `m_skillType == Farming` will miss Warfare scythes.
- Current `ScytheToolCompatSystem.IsScytheLike` and `IsDefaultHarvestSweepWeapon` both require `Farming`, so Warfare scythes will not be treated as scythe/Farming tools or receive global `harvestSweep` by default unless another detection path is added.
- A safer compat detector would also accept:
  - `m_skillType == (Skills.SkillType)Math.Abs("Scythes".GetStableHashCode())`
  - or specific prefab/shared names `DualScytheBloodthirst_TW`, `ScytheVampiric_TW`
  - optionally animation or attack signatures if their prefabs use scythe-style animations.

## SkillManager Notes

The embedded `SkillManager.Skill` helper:

- Patches `Skills.GetSkillDef` so custom skill defs resolve.
- Patches cheat raise/reset and death skill loss handling.
- Adds config entries per custom skill:
  - `Skill gain factor`
  - `Skill effect factor`
  - `Skill loss`
- `SkillExtensions.GetSkillFactor(character, "Throwing")` and `GetSkillFactor(character, "Scythes")` multiply vanilla `GetSkillFactor(customSkillType)` by the skill's configurable `Skill effect factor`.

Observed config sections:

- `[skill_99738506]` = Throwing
- `[skill_1328437209]` = Scythes
