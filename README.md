# SecondaryAttacks

![](https://i.ibb.co/PsySGSqH/sentinelcover.gif)

Adds secondary attacks for bows, staves, bombs, melee weapons, and Blood Magic, with auto presets, per-prefab overrides, summon tools, cooldown HUD, and localization. Preserves loaded weapons and improves Sneak and Blood Magic progression.

## Highlights

- Adds new secondary attacks for ranged weapons, melee weapons, bombs, elemental staves, and Blood Magic staves.
- Lets weapon groups use default presets from config while individual prefabs can be tuned through YAML.
- Syncs YAML configuration through ServerSync for dedicated servers and multiplayer clients.
- Supports cooldowns, durability cost, resource cost, ammo cost, skill gain, adrenaline gain, and HUD feedback.
- Includes compatibility support for MagicPlugin summon projectiles and the companion WarfareTweaks mod.

## Ranged Presets

`SecondaryAttacks.Ranged.yml` controls bows, crossbows, thrown bombs, and projectile staves. Weapon groups can be assigned a default preset in config, then individual prefabs can override that behavior in YAML.

### Sentinel

![](https://i.ibb.co/WWBPDnq6/sentinelgreen.gif)

Press the secondary attack key to spawn temporary sentinels that hover near you, search for targets, and fire on their own.

![](https://i.ibb.co/b5zS7vdB/extremesentinel.gif)

Sentinel count, lifetime, hover position, orbit speed, detection range, attack delay, and firing interval can all be tuned.

### Ranged Tuning

![](https://i.ibb.co/R42m619t/bowpresettest.gif)

Swap ranged presets by weapon group in config, or use `SecondaryAttacks.Ranged.yml` for per-prefab overrides.

![](https://i.ibb.co/F4ZJs2N3/fireballpresettest.gif)

Change spacing, interval, projectile speed, spread, damage, cost, and other preset values to build very different attacks from the same weapon.

### Spiral

![](https://i.ibb.co/B2q50Y9M/spiralburst.gif)

Spiral fires a configurable burst along a rotating pattern. Multi-projectile presets scale skill and adrenaline rewards so high projectile counts do not over-reward a single attack.

### Piercing

![](https://i.ibb.co/dwnYdtJK/piercingdundr.gif)

Piercing lets projectiles pass through enemies, with damage reduced after each character hit.

### Meteor

![](https://i.ibb.co/sd9wy7P6/greenmeteor.gif)

Meteor calls projectiles down from above. Count, interval, impact radius, spawn height, projectile scale, and area size are configurable.

### Volley

![](https://i.ibb.co/mrkq8chV/crossbowvolley.gif)

Volley drops aimed projectiles around a target point and can be especially strong against large enemies.

### Scatter

![](https://i.ibb.co/3yBLtsmT/bowscatter.gif)

Scatter splits after the first non-character hit, creating ricocheting projectiles that can catch nearby enemies.

![](https://i.ibb.co/kg1zqnQr/scatterfireball.gif)

Scattered projectiles lose speed and damage on each bounce through the `ricochetDecay` setting.

## Melee Presets

`SecondaryAttacks.Melee.yml` controls melee skills, copied throws, bombs, and utility-style secondaries. Several presets can use `copyFrom`, letting one weapon borrow another weapon's projectile or secondary attack pattern before applying its own preset behavior.

### Cleaving Thrust

![](https://i.ibb.co/NG62PSy/cleavingthrust.gif)

Greatswords can turn their secondary attack into a wider thrust with increased reach and push force.

### Rift Trail

![](https://i.ibb.co/0R82ZhFV/rifttrail.gif)

One-handed swords can leave a damaging rift trail that ticks against enemies inside it.

### Launch Slam

![](https://i.ibb.co/r2VmB8fF/launchslam.gif)

Maces can launch enemies into the air. When they land, nearby enemies take impact damage based on the falling target.

### Sneak Ambush

![](https://i.ibb.co/zh3ghDCN/sneakambush.gif)

Knives can charge an ambush while sneaking. Higher charge improves the aggro reset radius, non-aggro duration, and the next backstab window. Higher Sneak skill charges the attack faster.

### Aftershock

![](https://i.ibb.co/zW1gmBNX/aftershock1.gif)

Sledges can create forward-moving aftershocks with reduced damage and radius.

![](https://i.ibb.co/Kxq0MpNH/aftershockstay.gif)

Set the travel distance low and increase the delay to create a more stationary aftershock style.

### Knockback Chain

![](https://i.ibb.co/8yZwfGy/knockbackchain.gif)

Unarmed kicks can apply heavy push force, causing launched enemies to damage other nearby enemies on collision.

### Boomerang

![](https://i.ibb.co/B2vTtNvj/boomerang.gif)

One-handed axes can fly out in an arc, cut enemies or trees, then return to your hand.

### Impact Burst

![](https://i.ibb.co/p6Fp87XK/impactburst.gif)

Battleaxes can be thrown into an area burst that damages and pushes enemies near the impact point.

![](https://i.ibb.co/60nNWZjP/impactbursttree.gif)

Impact Burst can also be configured to affect destructibles such as trees.

### Spear Rain

![](https://i.ibb.co/h03YdLv/spearrain-1.gif)

Mark a target with your spear throw, then call additional spears down from above.

### Fracture Line

![](https://i.ibb.co/d0YPDxTc/fractureline.gif)

Pickaxe-style weapons can create a forward fracture that deals repeated damage to ore, rocks, and enemies along the line.

### Spinning Sweep

![](https://i.ibb.co/gbdbb7RJ/sweepingswip.gif)

Atgeirs can keep sweeping as long as you have enough stamina and durability to maintain the attack.

### Harvest Sweep

![](https://i.ibb.co/Pzg99DmX/harvestsweep.gif)

Scythes can use a steerable harvesting sweep for faster crop gathering.

### Sticky Detonator

![](https://i.ibb.co/1Y9kwYMr/stickydetonate2.gif)

Throw sticky bombs with the secondary attack key, then detonate active charges later with the block key.

![](https://i.ibb.co/r2PbW2Gd/stickydetonate.gif)

Blob bombs can be set up as delayed traps and detonated when enemies move into position.

### Overcharged Bomb

![](https://i.ibb.co/N2HWQ8RP/overchargedbomb.gif)

Overcharged Bomb greatly increases bomb damage and area size, at the cost of consuming multiple bombs at once.

## Blood Magic

`SecondaryAttacks.BloodMagic.yml` adds special support for Blood Magic staves.

### Shield Convert

![](https://i.ibb.co/8ns41wbW/shieldconvert.gif)

Shield Convert reads the active shield amount on you and nearby allies, then converts remaining shield into healing.

### Summon Empower

![](https://i.ibb.co/wFJXgXtn/empowertroll.gif)

Summon Empower buffs nearby summons with configurable duration, movement speed factor, and attack speed factor. The remaining empowered time is shown in the HUD.

![](https://i.ibb.co/1SKgq5S/empowersample2.gif)

Blood Magic summon quality can scale in two ways: `levelByQuality` increases summon level by staff quality, while `countByQuality` increases the number of active summons.

## Configuration

The mod creates these files in `BepInEx/config/SecondaryAttacks/`:

- `SecondaryAttacks.Ranged.yml`
- `SecondaryAttacks.Melee.yml`
- `SecondaryAttacks.BloodMagic.yml`
- `SecondaryAttacks_AnimationReferences.txt`

Use `SecondaryAttacks_AnimationReferences.txt` when choosing values for YAML `animation` fields.

Ranged automatic assignment is controlled by the `2 - Ranged` config options. Select `Off` for a weapon group to disable its automatic preset. Ranged `Global` blocks only define preset default values; prefab entries are used for exact per-prefab overrides.

Melee and Blood Magic presets use `Global` blocks for shared defaults and prefab entries for overrides. Use `preset: none` to opt out a specific prefab and keep its original secondary behavior. Disabled examples can remain in the YAML with `enabled: false`, so you can enable one sample at a time without rebuilding the whole entry.

## Misc

- Preserves loaded weapon state for crossbows and other reload-based weapons, so secondary handling does not unnecessarily lose a loaded shot.
- Replaces heavier MagicPlugin projectile compatibility work with lighter runtime hooks for ElementalMagic projectile tuning.
- Adds General config tweaks for Sneak scaling, including sneak movement speed, visibility reduction, and backstab skill gain tuning.
- Adds General config tweaks for Blood Magic health-cost behavior, including max-health based costs and Blood Magic skill gain from health-cost abilities.

## Github
https://github.com/sighsorry1029?tab=repositories