# SecondaryAttacks Architecture Rework Plan

## Goals

- Remove the "single static manager" bottleneck.
- Separate config parsing, config compilation, world mutation, runtime systems, and networking.
- Make reloads deterministic by swapping immutable compiled snapshots.
- Keep query/read paths pure.
- Restrict side effects to explicit apply/update systems.

## Current Structural Problems

- `SecondaryAttackManager` still acts as the central orchestrator for too many domains.
- Config loading and world mutation are still closely coupled.
- Runtime behavior depends on mutable global state such as `_activeConfig`, `_definitionRevision`, and live `DefinitionsByPrefabName`.
- Handshake logic patches connection flow directly and uses sequencing assumptions.
- Query methods can still sit too close to side-effecting code paths.

## Target Architecture

### 1. Config Layer

Purpose: read files and produce raw parsed config only.

Files:

- `Config/SecondaryAttackConfigLoader.cs`
- `Config/SecondaryAttackConfigFiles.cs`

Responsibilities:

- Read YAML from disk or synced values.
- Return raw config objects.
- No validation side effects.
- No `ObjectDB` access.

### 2. Compilation Layer

Purpose: convert raw config into a fully validated immutable runtime snapshot.

Files:

- `Compilation/SecondaryAttackConfigCompiler.cs`
- `Compilation/SecondaryAttackCompiledSnapshot.cs`
- `Compilation/SecondaryAttackCompileResult.cs`
- `Compilation/SecondaryAttackValidation.cs`

Responsibilities:

- Normalize raw config.
- Validate domain rules.
- Build immutable weapon definitions and effect definitions.
- Produce one `CompiledSnapshot` object.

Core rule:

- Runtime systems never read raw config directly.
- Runtime systems only consume `CompiledSnapshot`.

### 3. Snapshot Model

Purpose: represent a complete, immutable view of the mod state that can be applied to a world.

Files:

- `Model/SecondaryAttackCompiledSnapshot.cs`
- `Model/CompiledWeaponDefinition.cs`
- `Model/CompiledEffectDefinition.cs`
- `Model/CompiledBehavior/*.cs`

Contents:

- `SnapshotId`
- `WeaponsByPrefabName`
- `EffectsById`
- precomputed flags/indexes needed by runtime

Key property:

- Snapshot objects are immutable after compilation.

### 4. World Apply Layer

Purpose: mutate `ObjectDB` and runtime inventory state from one compiled snapshot.

Files:

- `WorldApply/SecondaryAttackWorldApplySystem.cs`
- `WorldApply/SecondaryAttackObjectDbStateStore.cs`
- `WorldApply/SecondaryAttackRuntimeWeaponRebind.cs`

Responsibilities:

- Capture and restore original `ObjectDB` state.
- Apply a compiled snapshot to `ObjectDB`.
- Rebind runtime weapon secondary attacks by `SnapshotId`.
- Be idempotent for the same `ObjectDB` + `SnapshotId`.

Core rule:

- Only this layer mutates `ObjectDB` or runtime weapon shared data.

### 5. Runtime Systems

Purpose: run gameplay behaviors using compiled definitions only.

Files:

- `Runtime/ProjectileRuntimeSystem.cs`
- `Runtime/ShieldRuntimeSystem.cs`
- `Runtime/StaffRuntimeSystem.cs`
- `Runtime/EffectRuntimeSystem.cs`
- `Runtime/AsyncActivityTracker.cs`

Responsibilities:

- Execute projectile, shield, staff, and effect logic.
- Maintain runtime-only state and controllers.
- Never parse config.
- Never mutate `ObjectDB`.

### 6. Status and HUD Layer

Purpose: display overhead state and maintain display sync.

Files:

- `Status/StatusStateStore.cs`
- `Status/StatusDisplaySystem.cs`
- `Status/OverheadStatusUiManager.cs`

Responsibilities:

- Store displayable status state.
- Update tracked targets only.
- Render HUD text.

Core rule:

- Read methods are pure.
- Cleanup and sync happen in explicit state update methods.
- Query methods must not trigger HUD refresh or write to ZDO.

### 7. Network Compatibility Layer

Purpose: keep SecondaryAttacks networking minimal and explicit.

Files:

- `Network/SecondaryAttackNetworkCompat.cs`
- `Network/SecondaryAttackVersionCheck.cs`

Responsibilities:

- Register any SecondaryAttacks-specific RPCs.
- Perform version/capability exchange.
- Disconnect only on confirmed incompatibility.

Core rule:

- No `RPC_PeerInfo` pre-emptive gating based on local mutable lists.
- No unregistered routed RPC calls.

### 8. Facade Layer

Purpose: provide a stable entry point for plugin init and Harmony hooks.

Files:

- `SecondaryAttackFacade.cs`

Responsibilities:

- Hold references to current/pending compiled snapshots.
- Coordinate config reload, snapshot swap, and apply timing.
- Expose high-level methods used by Harmony patches.

Core rule:

- The facade routes requests.
- It does not implement shield/projectile/staff logic directly.

## Design Rules

- Query methods must be pure.
- Mutation must be explicit and domain-local.
- Config parse must not mutate world state.
- Snapshot compile must not mutate world state.
- World apply must use one compiled snapshot input.
- Runtime systems must not read raw config or normalized config.
- Handshake must fail only on proven incompatibility.

## Proposed File Structure

```text
SecondaryAttacks/
  Config/
    SecondaryAttackConfigLoader.cs
    SecondaryAttackConfigFiles.cs
  Compilation/
    SecondaryAttackConfigCompiler.cs
    SecondaryAttackCompileResult.cs
    SecondaryAttackValidation.cs
  Model/
    SecondaryAttackCompiledSnapshot.cs
    CompiledWeaponDefinition.cs
    CompiledEffectDefinition.cs
    CompiledBehavior/
      CompiledProjectileBehavior.cs
      CompiledShieldBehavior.cs
      CompiledSummonEmpowerBehavior.cs
      CompiledShieldConvertBehavior.cs
      CompiledCopiedBehavior.cs
  WorldApply/
    SecondaryAttackWorldApplySystem.cs
    SecondaryAttackObjectDbStateStore.cs
    SecondaryAttackRuntimeWeaponRebind.cs
  Runtime/
    ProjectileRuntimeSystem.cs
    ShieldRuntimeSystem.cs
    StaffRuntimeSystem.cs
    EffectRuntimeSystem.cs
    AsyncActivityTracker.cs
  Status/
    StatusStateStore.cs
    StatusDisplaySystem.cs
    OverheadStatusUiManager.cs
  Network/
    SecondaryAttackNetworkCompat.cs
    SecondaryAttackVersionCheck.cs
  SecondaryAttackFacade.cs
  Plugin.cs
```

## Mapping From Current Files

- `SecondaryAttackManager.ConfigReload.cs`
  - split into `ConfigLoader`, `Compiler`, `Facade`, `WorldApplySystem`
- `SecondaryAttackManager.RuntimeDefinitions.cs`
  - split into `WorldApplySystem` and `RuntimeWeaponRebind`
- `SecondaryAttackManager.ProjectileRuntime.cs`
  - move to `ProjectileRuntimeSystem`
- `SecondaryAttackManager.ShieldRuntime.cs`
  - move to `ShieldRuntimeSystem`
- `SecondaryAttackManager.StaffRuntime.cs`
  - move to `StaffRuntimeSystem` and `StatusStateStore`
- `WeaponEffectManager.cs`
  - move to `EffectRuntimeSystem`
- `VersionHandshake.cs`
  - replace with `SecondaryAttackNetworkCompat`

## Migration Order

### Phase 1: Introduce Snapshot Model

- Add immutable `CompiledSnapshot`.
- Add compiler that produces compiled snapshot from current normalized config.
- Keep current runtime paths, but feed them from compiled snapshot.

Success criteria:

- `_activeConfig` no longer drives runtime directly.
- Definitions come from compiled snapshot.

### Phase 2: Introduce World Apply System

- Move `ApplyToObjectDb` logic into `SecondaryAttackWorldApplySystem`.
- Make apply idempotent by `ObjectDB` + `SnapshotId`.
- Move runtime item rebind into `SecondaryAttackRuntimeWeaponRebind`.

Success criteria:

- `SecondaryAttackManager` no longer mutates `ObjectDB` directly.

### Phase 3: Snapshot Swap Reload Flow

- Replace `StageConfig` / direct active config mutation with:
  - `PendingSnapshot`
  - `CurrentSnapshot`
- Apply world mutations only through `ApplySnapshot(CurrentSnapshot)`.

Success criteria:

- Reload and apply are fully separated.

### Phase 4: Extract Runtime Systems

- Move projectile/shield/staff/effect logic into separate runtime systems.
- Keep Harmony patches thin and route through facade.

Success criteria:

- No gameplay domain logic remains in facade.

### Phase 5: Replace Handshake

- Remove `ValidatedPeers` pre-gating.
- Use a minimal version/capability RPC only.
- Disconnect only on confirmed mismatch.

Success criteria:

- No SecondaryAttacks logic in `RPC_PeerInfo` gating path.

### Phase 6: Final Cleanup

- Remove remaining convenience state from transitional classes.
- Reduce `SecondaryAttackManager` or replace it fully with `SecondaryAttackFacade`.

## First Implementation Scope

The first implementation pass should be intentionally limited.

### Included

- Add immutable compiled snapshot types.
- Add compiler from normalized config to compiled snapshot.
- Add facade fields:
  - `CurrentSnapshot`
  - `PendingSnapshot`
- Redirect current definition lookup to snapshot-backed storage.

### Not Yet Included

- No handshake rewrite yet.
- No runtime controller rewrite yet.
- No file move explosion in the first pass.

Reason:

- This creates the critical new boundary without changing too many runtime call sites at once.

## What "Done" Looks Like

- Config changes produce immutable snapshots.
- World apply is a single explicit step.
- Runtime systems operate only on compiled definitions.
- Query methods are pure.
- HUD and status sync are event-driven.
- Handshake is small and does not depend on sequencing assumptions.
