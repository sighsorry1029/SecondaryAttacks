# Changelog

## 1.1.0

- Added a localized in-game Compendium guide with icons and detailed descriptions for every ranged, melee, Blood Magic, and summon-quality preset, including skill-scaled cooldowns and Sneak Ambush scaling.
- Consolidated cooldown and charge feedback into a dedicated HUD that supports dynamic rows and a stable top anchor, fixed its scale at 2, set its default horizontal position to 0.615, and removed the scale configuration.
- Expanded Summon Empower to scale walking, running, flying, swimming, acceleration, turning, attack animations, weapon attack intervals, and the AI minimum attack interval.
- Reworked live `.cfg` and YAML reloads with stable-file reads, bounded retries, watcher recovery, strict validation, atomic world application, and stale cooldown/effect cleanup.
- Fixed Sticky Detonator charges following moving targets, stale Magic Summon Quality state after rules stop matching, and summon health restoration when overrides are removed.

## 1.0.11

- Restored near-immediate live application for the six ranged presets and Magic Summon Quality without waiting for long-lived projectiles, summons, or other asynchronous secondary-attack work to finish.
- Replaced leading-edge config and YAML reload throttling with a 250 ms trailing debounce, so burst save events reliably reload `.cfg` and Ranged/Melee/BloodMagic YAML changes while the last valid YAML snapshot remains active if compilation fails.

## 1.0.10

- Rebuilt dedicated-server Cleaving Thrust extended-trail synchronization as a single owner-authoritative visual session with sequence and stale-payload validation.
- Observers now scale the trail only at its sampling point and reliably restore local state, so other players can see the full extended two-handed sword secondary attack without layered hit or animation workarounds.

## 1.0.9

- Fixed a 1.0.8 projectile-impact cleanup regression in the overcharged-bomb Harmony patch that could throw `NullReferenceException` when postfix/finalizer cleanup ran twice, including immediately before leaving dedicated servers.

## 1.0.8

- Hardened secondary attack state cleanup so projectile, reload, skill-scaling, crouch, and copied-throw overrides are restored even when patched methods fail.
- Consolidated configuration compilation, world application, projectile cascades, boomerang behavior, and cooldown fallbacks while removing redundant runtime and debug layers.

## 1.0.7

- Fixed dedicated-server cleavingThrust observer trail length by applying its range scale at the exact MeleeWeaponTrail sampling point, preventing remote animation or equipment refreshes from restoring the normal-length arc.

## 1.0.6

- Improved dedicated-server cleavingThrust observer trail scaling by refreshing the visual scale at the trigger timing and reapplying it when remote weapon visuals are created late.
- Added MagicPlugin summon cleanup compatibility to remove stale destroyed summon references before MagicPlugin update spam can repeat.

## 1.0.5

- Improved dedicated-server observer visuals for riftTrail by sending sampled weapon trail ribbons instead of only a fallback fan.
- Added observer trail scaling for cleavingThrust so other players can see the enlarged attack arc.

## 1.0.4

- Added spinningSweep damageFactor and pushFactor options, defaulting to 0.75 damage and 0.5 push per loop attack.

## 1.0.3

- Kept vanilla Blood Magic skill gain active while adding optional health-cost based bonus skill gain.
- Added owner-safe Sneak Ambush smoke/awareness handling for multiplayer.
- Added visual-only observer RPCs for spinning sweep, harvest sweep, and riftTrail on dedicated servers.

## 1.0.2

- Improved preset cooldown timing precision so the HUD counts down smoothly on dedicated servers instead of stepping in large intervals.

## 1.0.1

- Added a client-side admin option that lets host or server-admin players use presets without preset cooldowns.
- Cooldown HUD/status cleanup now clears stale cooldown displays while admin cooldown bypass is active.

## 1.0.0

- Initial Release.
