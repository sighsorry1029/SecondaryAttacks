# Valheim 1.0.7 compatibility checks

Build the production DLL first:

```powershell
dotnet build SecondaryAttacks.csproj -c Debug -p:DeployToGame=true
powershell -NoProfile -ExecutionPolicy Bypass -File tests/GameCompatibility/Verify-Compatibility.ps1 -ModDll bin/Debug/SecondaryAttacks.dll -GameManaged 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed' -BepInExCore 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core'
```

Repeat with `-GameManaged 'D:/SteamLibrary/steamapps/common/Valheim dedicated server/valheim_server_Data/Managed'` for the server. An optional `-ReportPath` writes the inspected contracts and failures as JSON. Supply original game DLLs, never the generated `obj/.../publicized` compile references.

The check resolves game/Unity member references in the merged DLL, rejects instructions accessing literal fields, checks explicitly attributed Harmony targets and named arguments/injected fields, and checks the listed reflection contracts and movement transpiler field loads. It also checks that ILRepack preserved the existing compile-access support attributes. Existing private/internal references are reported separately; their existence and the attributes alone do not establish runtime access safety.

This is a static check, not a full Harmony patch application, a complete reflection inventory, or a Unity/multiplayer test. The listed indirect contracts must be maintained when those helpers change. External mod APIs, scene/resource hierarchies, RPC authorization and gameplay still require integration checks. See [the compatibility record](../../docs/Valheim-1.0.7-compatibility.md).
