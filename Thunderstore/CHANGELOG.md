# Changelog

## 1.1.10

- Added owner-authoritative lifetimes for player-cast Blood Magic summons, using a 1200-second server-synchronized base, linear Blood Magic skill scaling, a configurable skill-100 multiplier, and optional per-staff `summon.lifetimeSeconds` overrides; existing summons keep their assigned deadlines.
- Added a synchronized summon countdown beside the health bar on the active star row, including CreatureManager, Creature Level & Loot Control, StarLevelSystem, and SecondaryAttacks star HUD layouts.
- Replicated ranged Burst follow-up animations and local-only VFX/SFX to other clients, including dedicated-server observers, with bounded presentation traffic, network-effect filtering, and a 32-shot Burst limit.
- Added the server-synchronized `Keep Crouching During Elemental Damage Over Time` option so nonlethal Fire, Spirit, and Poison ticks can preserve crouching without changing direct-hit, stagger, knockback, or lethal-damage behavior.
- Reorganized generated config groups into `1 - General`, `2 - Blood Magic`, `3 - Ranged`, and `4 - UI`, moved the admin cooldown convenience into General, and intentionally omitted legacy migration; existing custom values under old groups must be entered again.

## 1.1.9

- Fixed the final shot of the ranged Burst preset losing its weapon and attack trigger VFX/SFX, while preserving per-shot ammunition, durability, and post-trigger side effects.
- Prevented delayed Burst animation events, zero-cooldown attacks, rapid-fire hold handling, and weapons with multiple native projectile bursts from starting duplicate Burst sequences.
- Made `ammoConsumption: -1` for Burst consume one item per repeated shot instead of scaling to the configured shot count for every shot, and safely consumed persisted reload state when a Burst is interrupted by switching weapons.

## 1.1.8

- Changed secondary cooldown sharing to use automatically detected weapon families first, with independent Blood Magic summon and shield groups and per-preset fallback for unrecognized modded weapons.
- Added localized preset descriptions to applied weapon tooltips with a client-side on/off option.
- Hardened projectile secondary startup so cooldown, payload, resources, and the selected ammunition prefab are validated before ammo, durability, item, or reload side effects are committed.
- Consolidated preset, weapon-family, cooldown, summon, key-hint, and weapon-trail runtime paths while preserving dynamic ObjectDB rebinding and server-synchronized admin access.

## 1.1.7

- Added automatic summon-star HUD compatibility for CreatureManager, Creature Level & Loot Control, and StarLevelSystem, yielding to an active external star display while keeping SecondaryAttacks as the fallback when none is active.
- Isolated SecondaryAttacks' extended-star objects from CLLC's `level_N` groups so provider changes and level updates no longer reuse or alter externally owned HUD objects.

## 1.1.6

- Prevented transient TextMeshPro `LiberationSans SDF` warnings by keeping overhead Empower and Shield status text inactive until the Valheim HUD font has been assigned, without removing the status display.

## 1.1.5

- Fixed Cleaving Thrust treating a placed training dummy's own `piece` collider as an environmental obstruction, allowing the special attack to damage `piece_TrainingDummy` while preserving real wall blocking.

## 1.1.4

- Replaced the individual Quickstep tuning entries with one server-synchronized `Quickstep Enabled` option in General, using fixed 200 acceleration, 0.25-second duration, 0.15-second shield invincibility, 0.5-second cooldown, and 60% dodge stamina cost.
- Made a second dodge input during Quickstep hand off to a regular roll for the same 60% cost, and added a concise localized Compendium guide for fist and knife weapons.
- Kept quickstep state isolated from Sneak Ambush and custom crouch effects, restored vanilla player state on interruptions, and disabled the integration when the standalone Quickstep plugin is loaded.
- Added owner-authoritative fixed-target homing to launched Sentinel projectiles, using a limited turn rate without mid-flight target searches, retargeting, or additional network messages.

## 1.1.3

- Made Ranged, Melee, and Blood Magic YAML synchronization atomic through one strictly framed payload, rejecting malformed data and stale staged updates without replacing the last applied configuration.
- Added compensating world rollback and narrowed original-state snapshots to actual mutations, preventing failed reloads from leaving mixed secondary-attack, summon-quality, or summon-prefab state.
- Hardened `StartAttack` exception cleanup and direct-hit Harmony ordering so temporary cooldown/adrenaline state is restored and nested damage does not re-trigger direct-hit effects.
- Removed the legacy generic `effects` pipeline, the unsupported `cooldownFallback` YAML field, ignored preset fields, and redundant compiler/runtime layers while preserving the active preset feature sets.

## 1.1.2

- Moved the embedded MagicPlugin projectile-burst, teleport, and Character-registry compatibility fixes into the standalone InteropFixes mod, removing duplicate patch ownership from SecondaryAttacks.
- Kept MagicPlugin as a soft integration and Harmony ordering target, so SecondaryAttacks' summon features still cooperate with it without making MagicPlugin or InteropFixes a hard dependency.

## 1.1.1

- Fixed Harvest Sweep missing valid crops and foraging targets on the `Default` and `Default_small` layers, and expanded its collider search capacity for unusually dense planting areas.
- Updated Groundwork compatibility to use Groundwork's native scythe-harvest eligibility and farming hooks, so cultivated pickables and range-pickup suppression behave consistently.

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
