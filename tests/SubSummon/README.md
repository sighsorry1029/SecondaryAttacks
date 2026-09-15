# Sub-summon regression checks

Build the mod with `dotnet build SecondaryAttacks.csproj -c Debug -p:DeployToGame=true`, then run from the repository root:

```powershell
dotnet run --project tests/SubSummon/SubSummon.csproj -c Debug -- bin/Debug/SecondaryAttacks.dll 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed'
```

The first suite links the production `SubSummonSystem.cs` with Unity, ZDO and RPC boundary doubles. It checks direct-cast eligibility, parent-specific limits, multiple creation attempts, death, missing data, confirmed destruction, reentry, failure recovery, save/load with changed ZDO IDs, RPC authorization, delayed replies, ownership transfer and faction restoration.

The second suite reads the **original** game IL with Cecil and invokes the transpiler from the merged mod DLL. It checks the guarded creation site, original continue target, rejection of missing/ambiguous creation calls and original private member contracts. Only operands used by the transpiler are resolved to reflection objects; untouched operands remain Cecil metadata. This is a structural IL check, not emitted-code/JIT or Unity execution. The Windows Framework runtime cannot execute the game's default-interface implementations.

Remaining game checks: summon a tamed Charred Mage through a Blood Magic staff; verify two friendly children, no third creation, and resumption after one dies. Repeat with two parents, batch spawning, host/remote players, delayed destruction, area unload/reload, owner disconnection and world save/restart. Verify unrelated tame/wild casters and grandchildren retain their original behavior. Existing casters must be summoned again to acquire the new direct-cast marker. Child drops, levels, lifetime and parent-death cleanup remain the original creature's policy.
