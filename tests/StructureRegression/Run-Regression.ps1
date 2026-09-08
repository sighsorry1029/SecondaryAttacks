param([string]$BaselineRef = 'HEAD')

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'StructureRegression.csproj'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$baselineDirectory = Join-Path $PSScriptRoot 'obj/baseline-source'
[IO.Directory]::CreateDirectory($baselineDirectory) | Out-Null
$baselineCommit = & git -C $repoRoot rev-parse --verify $BaselineRef
if ($LASTEXITCODE -ne 0) { throw "Cannot resolve baseline $BaselineRef" }

foreach ($sourceFile in @('SecondaryAttackDefinitionCompiler.cs', 'SecondaryAttackRuntimeWeaponRebind.cs')) {
    $sourceText = & git -C $repoRoot show "${baselineCommit}:$sourceFile"
    if ($LASTEXITCODE -ne 0) { throw "Cannot read baseline $sourceFile" }
    [IO.File]::WriteAllText((Join-Path $baselineDirectory $sourceFile), ($sourceText -join "`n") + "`n", [Text.UTF8Encoding]::new($false))
}

foreach ($variant in @('baseline', 'working-tree')) {
    $sourceRoot = if ($variant -eq 'baseline') { $baselineDirectory } else { $repoRoot }
    Write-Output "Running $variant (baseline commit: $baselineCommit)"
    & dotnet run --project $projectPath --configuration Release "-p:SourceRoot=$sourceRoot" "-p:BaseIntermediateOutputPath=obj/$variant/" "-p:OutputPath=bin/$variant/"
    if ($LASTEXITCODE -ne 0) { throw "$variant regression checks failed" }
}
