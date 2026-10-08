param([string]$BepInExCore = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
# Compile the actual payment/shot methods, not a second implementation. Unity
# creation and EpicLoot randomness are deterministic doubles in this harness.
function Extract-Member([string]$source, [string]$name) {
    $pattern = '(?m)^    (?:private|internal) (?:static |readonly )*(?:[\w?.<>]+ )?' + [regex]::Escape($name) + '\b'
    $match = [regex]::Match($source, $pattern)
    if (!$match.Success) { throw "Production member missing: $name" }
    $open = $source.IndexOf('{', $match.Index)
    $depth = 1
    $end = $open + 1
    while ($depth -gt 0 -and $end -lt $source.Length) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        $end++
    }
    if ($depth -ne 0) { throw "Unbalanced production member: $name" }
    return $source.Substring($match.Index, $end - $match.Index)
}
$facade = [IO.File]::ReadAllText((Join-Path $root 'SecondaryAttackRuntimeFacade.cs'))
$controllers = [IO.File]::ReadAllText((Join-Path $root 'ProjectileRuntimeControllers.cs'))
$text = "using System; using System.Linq; using UnityEngine; namespace SecondaryAttacks {`ninternal static partial class SecondaryAttackRuntimeFacade {`n"
foreach ($name in @('TrySelectConfiguredAmmo', 'FindConfiguredAmmo', 'CommitConfiguredAmmo', 'TryCommitEpicLootBurstAmmo',
    'CountAmmo', 'RemoveAmmo', 'IsSameAmmoPrefab', 'IsAmmoItemForType', 'ConsumePerBurstResourcesIfNeeded',
    'ConfiguredAmmoContext', 'ApplyAttackTriggerSideEffects')) {
    $text += (Extract-Member $facade $name) + "`n"
}
$text += "}`ninternal static partial class ProjectileRuntimeSystem {`n"
foreach ($name in @('TryFireBurstShot', 'FireBurstFire', 'ConsumeBurstFireReload',
    'ShouldDeferBurstFireReloadReset', 'ClearDeferredBurstFireReloadReset')) {
    $text += (Extract-Member $controllers $name) + "`n"
}
$text += "}}`n"
[void][IO.Directory]::CreateDirectory((Join-Path $PSScriptRoot 'obj'))
[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'obj/ProductionSlices.cs'), $text)
dotnet build (Join-Path $PSScriptRoot 'EpicLootBurst.csproj') -c Debug "-p:BepInExCore=$BepInExCore" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Harness build failed.' }
foreach ($order in @('epic-first', 'sa-first')) {
    & (Join-Path $PSScriptRoot 'bin/Debug/net48/EpicLootBurst.exe') $order
    if ($LASTEXITCODE -ne 0) { throw "Burst regression failed: $order" }
}
