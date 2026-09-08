# Skill tooltip layout and heading checks

These checks read layout constants and invoke `SecondaryAttackSkillTooltipLayout` and the tooltip system's `AppendSection` from an already built `SecondaryAttacks.dll` through reflection. They use the installed game's real `UnityEngine.CoreModule.dll` for `Rect`, `Vector2`, and the production helper's math. No production source is copied or linked, and no Unity model doubles or external NuGet packages are used. The project targets .NET 9 and is intentionally separate from the game solution.

Build the current production project first using its verification build option, then run from the repository root:

```powershell
& ./tests/SkillTooltipLayout/Run-Tests.ps1 -AssemblyPath ./bin/Debug/SecondaryAttacks.dll
```

The script defaults to `bin/Debug/SecondaryAttacks.dll`; specify `-AssemblyPath` when using another output directory. It prints the loaded DLL's full path, version, and SHA256 to identify the artifact under test. An old DLL without the layout helper fails with an explicit message. The runner builds only this test console; it does not build, install, package, or start the mod.

The default game directory is the project's standard Steam Valheim location. Both test compilation and runtime dependency resolution can use a different installation:

```powershell
& ./tests/SkillTooltipLayout/Run-Tests.ps1 `
    -AssemblyPath ./bin/Release/SecondaryAttacks.dll `
    -ValheimGamePath 'D:\SteamLibrary\steamapps\common\Valheim'
```

Direct invocation is also possible:

```powershell
dotnet run --project ./tests/SkillTooltipLayout/SkillTooltipLayout.csproj --configuration Release `
    '-p:ValheimGamePath=D:\SteamLibrary\steamapps\common\Valheim' -- `
    'C:\path\to\SecondaryAttacks.dll' 'D:\SteamLibrary\steamapps\common\Valheim'
```

Coverage includes the vanilla body layout width of 250 UI units, separate 16-unit side padding, outer width of 282, panel-relative left placement with an 8-unit gap, consistent alignment across skill rows, row-top tracking, all four screen boundaries with a 12-unit inset, negative canvas coordinates, and uniform downscaling of oversized content. Small-viewport checks retain the unscaled text layout width while scaling the whole tooltip. The geometry matrix combines viewport sizes (including one narrower than the tooltip), panel positions, row positions, and content heights. Viewports smaller than the two combined margins are outside these fixtures.

Text-format checks verify that only the added section heading receives centered rich-text alignment, while original descriptions and multiline body tokens remain unchanged. Fixtures cover Sneak and Blood Magic with empty and nonempty original descriptions. They call the production formatting method without initializing gameplay configuration or constructing game/UI objects.

These tests do not create Unity objects, simulate a canvas, render text, or capture game screenshots. They cannot establish actual TextMeshPro height measurement or rich-text rendering, centered title and left-aligned body appearance, correct `SkillsFrame` selection, dark backdrop visibility, localization wrapping, mouse hover behavior, UI scaling, compatibility with other tooltip patches, or client/host/dedicated-server behavior. Those remain separate game checks at the intended resolutions and UI scales, including both Sneak and Blood Magic tooltips and long translated descriptions.
