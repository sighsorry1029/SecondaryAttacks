# SecondaryAttacks DLL Split Review

## Proposed Split

The requested split is possible, but fully copying every shared surface into each DLL is not recommended.

Proposed modules:

- `SecondaryAttacks`
  - Bow/crossbow ranged presets
  - Melee presets
  - Warfare compatibility
  - Sneak skill/config
  - Main weapon secondary framework
- `SecondaryAttacks_Magic`
  - Elemental magic presets
  - Blood Magic presets
  - Summoner YAML
  - Blood Magic skill/cost logic
  - MagicPlugin fixes
- `SecondaryAttacks_Shield`
  - Shield YAML
  - Shield throw/charge/primary/blockCharge

## Main Concern

The split is technically feasible, but the shared surface is wide:

- YAML sync/load/compile
- Runtime definition registry
- Active secondary attack context
- Start attack patches
- Animation override/copyFrom logic
- Projectile spawn/helper code
- Cooldown status effects
- Secondary cooldown HUD
- Key hint helpers
- ObjectDB/ZNetScene apply paths

Small helpers can be copied safely, such as:

- `KeyHintCell`
- simple config wrappers
- simple status effect classes
- icon resolver helpers
- small local runtime utility functions

Large runtime systems should not be copied wholesale:

- projectile runtime
- active attack attribution
- start attack dispatch
- ObjectDB apply/compiler pipeline
- shared secondary definition/facade
- cooldown HUD internals

Copying those into multiple DLLs would make bug fixes and behavior changes diverge quickly.

## Magic Module

`SecondaryAttacks_Magic` is the easiest module to split partially.

Good standalone candidates:

- Blood Magic health cost and skill gain logic
- Blood Magic staff special presets
- `MagicSummons.yml`
- Magic summon quality/count presets
- MagicPlugin compatibility fixes
- Summon teleport/follow fixes

Riskier candidates:

- Elemental projectile presets

Elemental projectile presets share a lot with the ranged projectile runtime. They should either remain in the main `SecondaryAttacks` DLL for now, or be moved later after a small shared core/API exists.

## Shield Module

`SecondaryAttacks_Shield` can be standalone if it owns its own simpler runtime.

Recommended standalone behavior:

- Use its own shield YAML.
- Use fixed/default animations when the main `SecondaryAttacks` DLL is absent.
- Use status effect cooldowns when the main cooldown HUD is absent.
- Optionally use the main HUD through a soft bridge if `SecondaryAttacks` is installed.

This lowers the shared surface because the shield DLL does not need to depend on the main secondary animation/copyFrom framework for basic operation.

Animation override and `copyFrom` support can be treated as enhanced features:

- With `SecondaryAttacks` installed: allow shared animation/copyFrom/HUD integration.
- Without `SecondaryAttacks`: use default shield animations and status effect cooldowns.

This is a reasonable compromise.

## HUD Split

Letting `SecondaryAttacks_Magic` and `SecondaryAttacks_Shield` use the main HUD only when `SecondaryAttacks` is installed does reduce coupling.

However, HUD is not the largest coupling point. The larger coupling points are:

- animation override
- copied attack resolution
- projectile runtime
- active secondary context
- ObjectDB apply pipeline

So HUD fallback helps, but it does not solve the hard part by itself.

## Better Long-Term Architecture

The cleanest long-term split would be four DLLs:

```text
SecondaryAttacks.Core
SecondaryAttacks
SecondaryAttacks_Magic
SecondaryAttacks_Shield
```

`SecondaryAttacks.Core` would contain:

- config/server sync helpers
- runtime definition registry
- start attack hook
- animation/copyFrom helper
- cooldown HUD API
- shared projectile helper pieces
- status effect/icon helper code

Then feature DLLs can depend on Core instead of copying large runtime systems.

The downside is that this does not satisfy a strict "each DLL can run completely alone" goal.

## Recommended Direction

Best practical path:

1. Split Magic first, but only the highly independent pieces:
   - Blood Magic skill/cost
   - Blood Magic staff specials
   - Magic summon YAML
   - MagicPlugin fixes
2. Keep elemental projectile presets in main `SecondaryAttacks` for now.
3. Split Shield as a standalone module with:
   - default animations
   - own status effect cooldowns
   - optional soft bridge to the main cooldown HUD
4. Avoid copying the full projectile runtime or active attack framework into multiple DLLs.
5. Consider `SecondaryAttacks.Core` later if more modules need shared advanced behavior.

Overall recommendation:

- `SecondaryAttacks_Magic` partial split is low to medium risk.
- `SecondaryAttacks_Shield` standalone split is medium risk if simplified.
- Full three-way split with duplicated framework is high maintenance risk.
