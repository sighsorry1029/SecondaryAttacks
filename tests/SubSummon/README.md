# Sub-summon regression checks

Build the mod with `dotnet build SecondaryAttacks.csproj -c Debug -p:DeployToGame=true`, then run from the repository root:

```powershell
dotnet run --project tests/SubSummon/SubSummon.csproj -c Debug -- bin/Debug/SecondaryAttacks.dll 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed'
```

The first suite links the production `SubSummonSystem.cs` with Unity, ZDO and RPC boundary doubles. It checks direct-cast eligibility, parent-specific limits, multiple creation attempts, death, missing data, confirmed destruction, reentry, failure recovery, save/load with changed ZDO IDs, RPC authorization, delayed replies, ownership transfer and faction restoration.

The second suite reads the **original** game IL with Cecil and invokes the transpiler from the merged mod DLL. It checks the guarded creation site, original continue target and private member contracts. It rejects seven unsupported inputs: missing/duplicate creation, missing post-creation instructions, absent/backward/duplicate continuation targets, and exception regions. Only operands used by the transpiler are resolved to reflection objects; untouched operands remain Cecil metadata. It also extracts and executes the four production-emitted guard instructions in a DynamicMethod, remapping labels and substituting object/observer/post-spawn boundaries. Success must deliver the same reference once; denial must leave an empty stack and skip both callbacks. This executes the small decision sequence, **not** the full game's MoveNext, Harmony detour installation, Unity or multiplayer. The Windows Framework runtime cannot execute the game's default-interface implementations.

To check the CombatMeter combination, add the reviewed **original CombatMeter 0.12.0 DLL** as the optional third argument:

```powershell
dotnet run --project tests/SubSummon/SubSummon.csproj -c Debug -- bin/Debug/SecondaryAttacks.dll 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed' '<path to CombatMeter.dll>'
```

The supplied DLL SHA-256 is `24876FF87193EA8CDECD5E75FEBE8FEEEB8E4C2B0D37329A62BE76BB61783496`. The check invokes its actual transpiler on original game IL, then the merged SA transpiler, and verifies preserved observer instructions/control-flow metadata and successful/denied branch behavior. Harmony's real patch sorter must place CombatMeter before SA for either registration order. This reproduces the formerly failing combination without copying third-party patch logic or making CombatMeter a required dependency. Original DLLs are not modified or publicized.

Remaining game checks: summon a tamed Charred Mage through a Blood Magic staff; verify two friendly children, no third creation, and resumption after one dies. Repeat with two parents, batch spawning, host/remote players, delayed destruction, area unload/reload, owner disconnection and world save/restart. Verify unrelated tame/wild casters and grandchildren retain their original behavior. Existing casters must be summoned again to acquire the new direct-cast marker. Child drops, levels, lifetime and parent-death cleanup remain the original creature's policy.

With CombatMeter installed, additionally check that both plugins finish initialization, supported direct summons retain their damage attribution, and denied child spawns produce no observer-side errors. The patch preserves CombatMeter's existing attribution scope; it does not add unsupported summon types or dedicated-server support to CombatMeter.
