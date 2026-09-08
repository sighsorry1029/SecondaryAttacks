param(
    [string]$AssemblyPath,
    [string]$ValheimGamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
)

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $AssemblyPath = Join-Path $repoRoot 'bin/Debug/SecondaryAttacks.dll'
}

$AssemblyPath = (Resolve-Path -LiteralPath $AssemblyPath).Path
$ValheimGamePath = (Resolve-Path -LiteralPath $ValheimGamePath).Path
$unityAssembly = Join-Path $ValheimGamePath 'valheim_Data/Managed/UnityEngine.CoreModule.dll'
if (-not (Test-Path -LiteralPath $unityAssembly -PathType Leaf)) {
    throw "UnityEngine.CoreModule.dll was not found at $unityAssembly"
}

& dotnet run --project (Join-Path $PSScriptRoot 'SkillTooltipLayout.csproj') --configuration Release "-p:ValheimGamePath=$ValheimGamePath" -- $AssemblyPath $ValheimGamePath
if ($LASTEXITCODE -ne 0) {
    throw 'Skill tooltip geometry checks failed.'
}
