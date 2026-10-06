Run `dotnet run --project tests/EquippedThrowAppearance/EquippedThrowAppearance.csproj -c Debug` from the repository root.

This harness links the actual projectile visual and access sources. Game/Unity stubs simulate the original UpdateVisual boundary while assertions exercise production capture, packed metadata, owner-only writes, original item/effect identity, native visual gating, late remote data, one-time mesh/variant work, fallback on cosmetic creation failure, and transpiler match validation. It does not execute Unity or Harmony. Contexts and follow-up snapshots are supplied directly, so the production initial-burst and spear-rain connections are inspected statically rather than executed by this harness.

`Verify-GameContract.ps1` checks the original game DLL's exact lookup anchor, Attack.Start arguments, public equipment getters and private reflection contracts, plus the final merged mod. Pass `-GameDll`, `-ModDll` and `-CecilDll`; use original client and dedicated-server DLLs, never publicized copies.

The appearance is a launch-time snapshot. Hidden or unknown cosmetic prefabs use the normal visual; a missing prefab is not searched repeatedly in the same ObjectDB. Equipment changes after launch do not change an existing projectile. In-game owner/observer, variant, throw/return/drop, spear-rain and Armoire-enabled/disabled checks are still required.
