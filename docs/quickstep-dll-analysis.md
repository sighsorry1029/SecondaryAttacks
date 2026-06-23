# Quickstep DLL Analysis

Analyzed target:

```text
C:\Users\blizz\AppData\Roaming\com.kesomannen.gale\valheim\profiles\zxcv\BepInEx\plugins\shudnal-Quickstep\Quickstep.dll
```

## Summary

Quickstep 1.0.12 does not appear to contain or load a custom quickstep animation asset.

It also does not appear to play a named vanilla dodge/roll clip and cut a specific normalized-time segment from it. The implementation is closer to a code-driven dash:

- intercept `Player.UpdateDodge` with a Harmony prefix
- when quickstep is allowed, skip the original dodge update
- temporarily manipulate existing vanilla animator booleans such as `equipping`, `Player.s_crouching`, and `Humanoid.s_blocking`
- briefly scale animator speed
- move the player with a Rigidbody impulse for the configured dash time
- restore dodge/crouch/invincibility/animator-speed state afterward

So the visible quickstep motion is most likely Valheim's existing crouch/equip/block animation state blended with forced movement, not a bundled custom animation clip.

## File Metadata

```text
Assembly: Quickstep, Version=1.0.12.0
MVID: 4762b400-3eb2-4913-aa5c-4d3e4f226ed0
Size: 87040 bytes
Last write time: 2026-05-28 17:46:53
SHA256: BD784D63DBDA33C5E352315BBFE0AE0BF7464C1DABCD5292B20F17C73F44108B
```

The bundled `manifest.json` identifies the package as:

```json
{
  "name": "Quickstep",
  "version_number": "1.0.12",
  "website_url": "https://github.com/shudnal/Quickstep",
  "description": "Replace dodging roll animation with quickstep. Customizable by weapon types and custom prefabs.",
  "dependencies": ["denikson-BepInExPack_Valheim-5.4.2333"]
}
```

## Resource And Asset Findings

The assembly resources contain only:

```text
Embedded ILRepack.List
```

No embedded asset bundle, animation clip, controller, or other custom asset resource was found in the DLL.

Searches through IL method operands found no calls or references matching:

```text
AssetBundle
AnimationClip
RuntimeAnimatorController
AnimatorOverrideController
GetManifestResourceStream
LoadFromFile
LoadFromMemory
LoadAsset
Resources.Load
Addressables
CrossFade
Animator.Play
GetCurrentAnimatorStateInfo
normalizedTime
```

This is strong evidence that the mod does not ship a custom animation asset and does not explicitly select a clip time range from an existing animation.

## Main Types

The relevant plugin type is:

```text
Quickstep.Quickstep
```

Important nested patch/coroutine types:

```text
Quickstep.Quickstep/Player_UpdateDodge_Quickstep
Quickstep.Quickstep/<Dash>d__95
Quickstep.Quickstep/<>c__DisplayClass95_0
```

The main behavior is split across:

```text
AllowQuickstep(Player, out dashForceWeapon, out dashTimeWeapon)
CheckQuickstep(Player, dt)
UpdateQuickstep(Player, dt, dashForceWeapon, dashTimeWeapon)
PerformQuickstep(Player, staminaUse, dashForceWeapon, dashTimeWeapon)
Dash(Player, dodgeDir, reducedIFrames, dashForceWeapon, dashTimeWeapon, currentVel)
```

## Harmony Entry Point

`Player_UpdateDodge_Quickstep.Prefix(Player, dt)` patches `Player.UpdateDodge`.

Observed logic:

```text
if mod disabled:
  return true  // allow vanilla Player.UpdateDodge

return CheckQuickstep(player, dt)
```

`CheckQuickstep` returns `false` after `UpdateQuickstep` handles the dash, which skips the original vanilla dodge update. If regular dodge should happen, for example the double-click dodge path, it returns `true`.

## Quickstep Trigger Flow

`UpdateQuickstep` decreases `Player.m_queuedDodgeTimer`, then checks common dodge conditions:

```text
queued dodge timer > 0
on ground
not dead
not in attack
not encumbered
not already in dodge
not staggering
not already quickstep-dashing
enough stamina
```

If these pass, it calls `PerformQuickstep`.

`PerformQuickstep` does the immediate setup:

```text
player.m_queuedDodgeTimer = 0
if left item is shield:
  reducedIFrames = true

if not crouching:
  player.m_zanim.SetBool("equipping", true)

player.AddNoise(3)
player.UseStamina(staminaUse)
player.UpdateBodyFriction()
player.m_dodgeEffects.Create(...)
player.StartCoroutine(Dash(...))
```

## Animation-Related Calls

The only direct Animator/ZSyncAnimation-related calls found in the quickstep path are:

```text
ZSyncAnimation.SetBool(string, bool)
ZSyncAnimation.SetBool(int, bool)
ZSyncAnimation.SetSpeed(float)
Animator.get_speed()
```

The observed parameters/states are:

```text
SetBool("equipping", true/false)
SetBool(Player.s_crouching, true/false)
SetBool(Humanoid.s_blocking, false)
SetSpeed(originalSpeed * 3.0)
SetSpeed(originalSpeed * 1.5)
SetSpeed(originalSpeed)
```

No `Animator.Play`, `Animator.CrossFade`, clip load, clip override, or normalized clip-time manipulation was found.

## Dash Coroutine Behavior

`Dash` stores the original crouch/block/stamina/animator-speed state, then:

```text
isDashed = true
player.ClearActionQueue()
player.m_inDodge = true
player.m_dodgeInvincible = true
player.m_dodgeInvincibleCached = true
ZDO[ZDOVars.s_dodgeinv] = true
```

If the player was not already crouching:

```text
SetSpeed(originalSpeed * 3.0)
if blocking:
  m_internalBlockingState = false
  ZDO[ZDOVars.s_isBlockingHash] = false
  SetBool(Humanoid.s_blocking, false)

SetCrouch(true)
SetBool(Player.s_crouching, true)
wait fixed update
SetBool("equipping", false)
wait fixed update
SetSpeed(originalSpeed * 1.5)
```

Then it calculates dash duration and force:

```text
dashTimeCurrent = weaponDashTime != 0 ? weaponDashTime : globalDashTime
dashVector = dodgeDir * (weaponDashForce != 0 ? weaponDashForce : globalDashForce)
dashVector.y = 0
```

It applies horizontal force with:

```text
player.m_body.AddForce(dashVector, ForceMode.Impulse)
```

During the dash, it waits in `0.01s` slices and updates invincibility if shield-reduced i-frames apply.

At the end:

```text
player.m_body.linearVelocity = Lerp(currentVelocity, originalVelocity, 0.5) * 0.3
player.m_dodgeInvincible = false
player.m_dodgeInvincibleCached = false
ZDO[ZDOVars.s_dodgeinv] = false
player.m_inDodge = false
player.m_beenHitWhileDodging = false
SetCrouch(originalCrouchState)
SetBool(Player.s_crouching, originalCrouchState)
SetSpeed(originalSpeed)
wait dash cooldown
isDashed = false
skipToDodge = false
restore player.m_dodgeStaminaUsage
```

## Custom Asset Vs Existing Animation Segment

Conclusion:

```text
Custom quickstep animation asset: no evidence found.
Existing roll/dodge clip sliced by time: no evidence found.
Existing vanilla animator state reuse: yes, strongly indicated.
Physics/code-driven movement: yes, strongly indicated.
```

More precisely, Quickstep appears to fake the quickstep by forcing the character into existing vanilla crouch/equip-related animator states for a short time, increasing animator speed during the transition, and applying a Rigidbody impulse. This replaces the feel of the vanilla roll without introducing a new animation clip.

## Practical Notes For SecondaryAttacks

If implementing a similar quickstep-style secondary attack, the closest pattern from this DLL is not "import an animation." It is:

```text
1. intercept/replace the dodge or attack transition
2. temporarily force a vanilla pose/state such as crouching/equipping/blocking
3. scale animator speed for the short transition
4. apply horizontal Rigidbody impulse
5. manage invincibility/cooldown/stamina explicitly
6. restore every touched state at the end
```

The fragile parts are state restoration and interaction with blocking/crouching/double-click regular dodge. The DLL spends a notable amount of code restoring `m_inDodge`, `m_dodgeInvincible`, crouch state, animator speed, and original dodge stamina usage, which is the part worth copying conceptually if this behavior is reproduced.
