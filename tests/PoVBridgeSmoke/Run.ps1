param(
    [Parameter(Mandatory)][string] $MonoHost,
    [Parameter(Mandatory)][string] $PathOfValheimanDll,
    [Parameter(Mandatory)][string] $OptionalDependencyDirectory,
    [string] $GamePath = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim',
    [string] $ModDll
)
$ErrorActionPreference = 'Stop'
if (!$ModDll) { $ModDll = Join-Path $PSScriptRoot '../../bin/Debug/SecondaryAttacks.dll' }
$ModDll = (Resolve-Path -LiteralPath $ModDll).Path
$PathOfValheimanDll = (Resolve-Path -LiteralPath $PathOfValheimanDll).Path
dotnet build (Join-Path $PSScriptRoot 'PoVBridgeSmoke.csproj') -c Debug "-p:GameRoot=$GamePath" --nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$managed = Join-Path $GamePath 'valheim_Data/Managed'
$core = Join-Path $GamePath 'BepInEx/core'
$runtime = Join-Path $GamePath 'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll'
$output = Join-Path $PSScriptRoot 'bin/Debug'
Get-FileHash -LiteralPath $ModDll,$PathOfValheimanDll,"$managed/assembly_valheim.dll",$runtime |
    Format-List | Out-File (Join-Path $output 'inputs.txt')
& $MonoHost $runtime $managed (Join-Path $GamePath 'MonoBleedingEdge/etc') (Join-Path $output 'net48/PoVBridgeSmoke.exe') $ModDll $PathOfValheimanDll $managed $core $OptionalDependencyDirectory 2>&1 |
    Tee-Object -FilePath (Join-Path $output 'mono.log')
exit $LASTEXITCODE
