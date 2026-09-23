# Cleaving Thrust regression checks

```powershell
dotnet run --project tests/CleavingThrust/CleavingThrust.csproj -c Debug
dotnet build SecondaryAttacks.csproj -c Debug -p:DeployToGame=true
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests/GameCompatibility/Verify-Compatibility.ps1 -ModDll bin/Debug/SecondaryAttacks.dll -GameManaged 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed' -BepInExCore 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core'
```

The harness links the production collector and HitData factory. Physics doubles
intersect query capsules with spherical scene colliders; they do not implement
the production fan, target selection, damage, or knockback calculations. Checks
cover grazing bodies, native fan directions and radius-subtracted range, the
original-length volume remaining included after extension, origin/aim and extra
height bands, walls/friendly/dead/dodge exclusions, duplicate colliders, crowd
buffer overflow, and cleanup after target exceptions. Damage checks include
closer props, first-character exemption, unchanged N for other targets, unchanged
push, disabled penalty flags, and restoration before status-effect processing.

The installed Harmony build does not initialize in the .NET 9 harness, so only
its method-binding boundary is doubled there. The compatibility verifier checks
the private Attack.GetMeleeAttackDir signature and out parameters against original
game metadata without publicizing it. A standalone .NET Framework binding probe
cannot load the game's default interface methods. Actual installed Harmony
delegate execution therefore remains part of the game's Mono validation.

Geometry source: original Valheim 1.0.15 client build 25390630, c4210710 bundle
SHA-256 A41629783AB3FBABB7DA4DDD0EE91C0EE4936B6CDD0B378DCF33642E1482A967.
THSwordKrom, THSwordSlayer (including variants), and THSwordWood use secondary
range 3, angle 30, ray radius 0.5, height 1, zero character height/width extras,
offset 0, and maxYAngle 0. Native fan directions are +15,+11,+7,+3,-1,-5,-9,-13
degrees. A rangeFactor of 3 produces 8.5m center segments plus 0.5m end caps.
The filled footprint does not include a target's own collider radius.

Real game checks still required: near-side grazes, sloped ground and tall/short
creatures, terrain/pieces blocking the center line, starting overlaps, first
target damage and knockback, cooldown fallback to vanilla, and remote observers.
OverlapCapsule deliberately includes starting overlaps (as the old custom point
test did); vanilla SphereCast and its forward-hit-point filter differ at the
origin. The plot and numerical harness are not a Unity collision recording.
