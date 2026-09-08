# Structure regression checks

Run from the repository root with PowerShell and the .NET 9 SDK installed:

```powershell
& ./tests/StructureRegression/Run-Regression.ps1
```

The runner reads the two production source files at `HEAD` using `git show`, then runs the same assertions against that baseline and the working tree. Use `-BaselineRef <commit>` to select another baseline. Exported baseline sources and build output stay under this test project's ignored `obj`/`bin` directories. No game installation, external NuGet packages, production build, packaging, or deployment is used. This standalone project is intentionally absent from the game solution.

To run only the working tree:

```powershell
dotnet run --project ./tests/StructureRegression/StructureRegression.csproj --configuration Release
```

`SecondaryAttackDefinitionCompiler.cs` and `SecondaryAttackRuntimeWeaponRebind.cs` are compiled directly through linked Compile entries. Assertions execute their real control flow; they do not inspect source text or duplicate those algorithms. Small dependency doubles represent normalized inputs, builder results/calls, logging, inventory objects, and fallback cloning. Only baseline rebind uses a test storage double for the old Manager revision API; the revised rebind runs its own production ConditionalWeakTable.

Compiler coverage includes disabled/opt-out inputs, legacy case and whitespace distinctions, missing shared data/secondary/primary/preset, each supported builder dispatch, effect-only fallback, copy source resolution and warning suppression, and propagation of builder rejection. Rebind coverage includes first/same/changed revisions, failed application retry, per-item identity, fallback selection, and local inventory refresh.

The linked rebind source can report CS8600 under the test project's .NET 9 nullable Dictionary annotations. The same warning appears for the baseline and working tree; it is separate from the production .NET Framework 4.8 build.

These tests do not execute the real normalized model calculations, builders, Unity null/destroy semantics, Harmony patches, weak-reference collection, network ownership, RPCs, item persistence, UI, or gameplay. They cannot establish full mod compatibility or item-loss/duplication safety. The summon coroutine restoration change also requires separate Unity/integration validation.

The main project can be compiled separately with an available MSBuild and its existing game dependency configuration:

```powershell
MSBuild.exe ./SecondaryAttacks.csproj /t:Build /p:Configuration=Release /p:VerificationBuild=true
```

`VerificationBuild=true` skips the production installation/package targets. Actual client, host, and dedicated-server validation remains a separate step after build and these unit-level checks.
