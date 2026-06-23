# SecondaryAttacks

SecondaryAttacks turns Valheim's secondary attack slot into a configurable weapon system. It adds new ranged, melee, bomb, staff, and Blood Magic behaviors while keeping everything driven by synced YAML so servers can decide exactly which weapons receive which tools.

## Highlights

- Adds configurable secondary attacks for bows, crossbows, elemental staves, bombs, melee weapons, and Blood Magic staves.
- Automatically assigns presets by weapon group, with per-prefab overrides for precise tuning.
- Syncs YAML configuration through ServerSync for dedicated servers and multiplayer clients.
- Uses root `Global` preset blocks for defaults, plus `enabled: false` examples that can be switched on without rewriting schema.
- Supports `copyFrom` for melee presets, so one weapon can borrow another weapon's throw or secondary attack pattern.
- Includes a dedicated cooldown HUD with draggable position and icon-only cooldown slots.
- Includes localization support for preset names, cooldown labels, and common UI text.
- Keeps Warfare-native effect tuning separated into the companion `WarfareTweaks` mod.

## Ranged Presets

`SecondaryAttacks.Ranged.yml` controls projectile weapons and staves.

- `barrage`: fires a configurable spread of projectiles.
- `volley`: drops an aimed projectile volley around a target point.
- `piercing`: reuses the full primary projectile pattern with piercing behavior.
- `scatter`: splits into ricocheting projectiles after impact.
- `spiral`: sends projectiles along a spiral pattern.
- `sentinel`: creates temporary orbiting sentinels that search and fire.
- `meteor`: calls down large falling projectiles.
- `burst`: repeats the weapon's normal projectile fire pattern.
- `stickyDetonator`: places active bomb charges and detonates them manually.
- `overchargedBomb`: strengthens bomb impact and area damage.

## Melee Presets

`SecondaryAttacks.Melee.yml` controls melee and copied-throw behaviors.

- `sneakAmbush`: rewards charged stealth attacks and backstab setups.
- `riftTrail`: leaves a damaging sword trail.
- `cleavingThrust`: expands greatsword secondaries into a wider thrust fan.
- `launchSlam`: launches enemies and damages them again on landing.
- `aftershock`: repeats weakened shockwaves after heavy area attacks.
- `knockbackChain`: turns strong knockback into chained collision damage.
- `boomerang`: throws a copied projectile visual that returns.
- `impactBurst`: throws a copied projectile that bursts on impact.
- `spearRain`: calls down repeated spear projectiles.
- `spinningSweep`: adds polearm-style sweeping movement.
- `fractureLine`: creates a forward fracture attack for pickaxe-style weapons.
- `harvestSweep`: gives scythes a steerable harvesting sweep.

## Blood Magic

`SecondaryAttacks.BloodMagic.yml` adds Blood Magic staff support.

- `summonEmpower` buffs nearby summons with configurable radius, duration, movement speed, and attack speed factors.
- `shieldConvert` converts active StaffShield protection into healing.
- Summon overrides can replace spawned creatures, assign weighted spawn choices, and optionally customize display names or health.
- The global Magic Summon Quality preset can scale summon count or summon level by item quality.

## Configuration

The mod creates these files in `BepInEx/config/SecondaryAttacks/`:

- `SecondaryAttacks.Ranged.yml`
- `SecondaryAttacks.Melee.yml`
- `SecondaryAttacks.BloodMagic.yml`
- `SecondaryAttacks_AnimationReferences.txt`

Use `preset: none` to opt out a prefab and keep its original secondary. Use `enabled: false` on a prefab example to keep it as a ready-to-edit sample without applying it.

## Compatibility

- Works on dedicated servers through synced config.
- Includes compatibility hooks for MagicPlugin summon projectiles.
- Can cooperate with WarfareTweaks through its reflection bridge so generated projectile or follow-up damage is not counted as ordinary direct weapon hits.

## Installation

Install BepInExPack Valheim, then place `SecondaryAttacks.dll` in a BepInEx plugin folder. On first launch, the mod writes default config files that can be edited and reloaded through normal config sync.
