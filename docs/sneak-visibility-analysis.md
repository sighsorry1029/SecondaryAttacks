# Valheim Sneak Visibility Analysis

This note checks the Valheim Wiki description of Sneaking visibility against the local Valheim assembly.

Sources:

- https://valheim.fandom.com/wiki/Sneaking_(skill)
- https://valheim.fandom.com/wiki/Creature_senses
- Local Valheim `assembly_valheim_publicized.dll`

## Summary

The wiki's visibility meter explanation is accurate.

While crouching, the HUD stealth bar displays the player's `stealthFactor`. Enemy sight range is then reduced by that same value:

```text
effectiveViewRange = enemy.viewRange * player.stealthFactor
```

So if the visibility meter is 80% filled, an enemy effectively uses 80% of its normal view range when checking whether it can see the player.

## Stealth Factor Formula

`Player.UpdateStealth` calculates the target stealth factor from light level and Sneak skill:

```text
light = StealthSystem.GetLightFactor(player center point) // 0.0 to 1.0
sneak = Sneak level / 100.0

stealthFactor = lerp(
  0.5 + light * 0.5,  // Sneak 0
  0.2 + light * 0.4,  // Sneak 100
  sneak
)
```

The result is clamped to `0..1`, then status effects can modify it through `SEMan.ModifyStealth`.

This means:

```text
Sneak 0:
  darkest = 0.5
  brightest = 1.0

Sneak 100:
  darkest = 0.2
  brightest = 0.6
```

These values match the wiki's "50% to 100%" at Sneak 0 and "20% to 60%" at Sneak 100.

## HUD Meter

`Hud.UpdateStealth` calls:

```text
player.GetStealthFactor()
```

and passes that value directly to:

```text
m_stealthBar.SetValue(stealthFactor)
```

So the small HUD bar below the reticle is not just decorative. It is the same value used by enemy sight checks.

## Enemy Sight Check

`BaseAI.CanSeeTarget` does the important range check in this order:

```text
distance <= enemy.viewRange
stealthRange = enemy.viewRange * target.GetStealthFactor()
distance <= stealthRange
```

Then, if the enemy is not already alerted, it checks the enemy view angle. After that it checks line of sight with a raycast, and mist blocking if applicable.

Important implication:

- Sneak reduces sight distance, not line-of-sight or view angle by itself.
- If the player is inside the reduced view range, inside the view angle, and not blocked by terrain/objects, detection is deterministic.
- If the enemy is already alerted, the view-angle gate is skipped, so enemies can feel much harder to lose after they have noticed the player.

## Stamina Drain

`Player.OnSneaking` applies Sneak skill to stamina drain as:

```text
skillFactor = Sneak level / 100.0
skillCurve = sqrt(skillFactor)
staminaMultiplier = lerp(1.0, 0.25, skillCurve)
```

So Sneak 100 reduces crouch stamina usage to 25%, which is a 75% reduction. Because it uses `sqrt`, earlier levels have a larger visible effect than a linear curve.

Equipment and status effects can then modify this stamina cost.

## Sneak Skill Gain

`Player.OnSneaking` raises Sneak skill roughly once per second while sneaking.

It checks `BaseAI.InStealthRange(player)`:

```text
if any enemy is within its base viewRange, or within 10m,
and none of those enemies are alerted:
  raise Sneak by 1.0
else:
  raise Sneak by 0.1
```

This check does not use the current light level, the stealth meter value, enemy facing direction, or line of sight. It only checks enemy relation, distance, and alert state.

## Practical Interpretation

Sneaking in vanilla Valheim is not invisibility.

It reduces enemy visual detection range based on light and Sneak skill. It does not make the player safe when standing in front of an enemy inside the reduced range. Bright light, frontal approach, groups, and already-alerted enemies can still make sneaking feel weak even when the system is working as designed.

## SecondaryAttacks Tuning

SecondaryAttacks adds `Sneak Visibility Skill Effect Factor` in the general config. The value range is `1.0..2.0`, with `1.0` as vanilla.

The mod keeps the light baseline the same and scales only the visibility reduction gained from Sneak skill:

```text
baseVisibility = 0.5 + light * 0.5
vanillaMaxReduction = 0.3 + light * 0.1

visibility =
  baseVisibility
  - vanillaMaxReduction
  * sneak
  * SneakVisibilitySkillEffectFactor
```

The final visibility is clamped to a fixed minimum of `0.1`.

At factor `1.0`, vanilla examples remain:

```text
Sneak 0:
  darkest = 0.5
  brightest = 1.0

Sneak 100:
  darkest = 0.2
  brightest = 0.6
```

At factor `2.0`, Sneak 100 becomes:

```text
darkest = 0.1 // clamped from -0.1
brightest = 0.2
```
