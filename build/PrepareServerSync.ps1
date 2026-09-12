param(
    [Parameter(Mandatory = $true)][string] $InputDll,
    [Parameter(Mandatory = $true)][string] $OutputDll,
    [Parameter(Mandatory = $true)][string] $GameDll,
    [Parameter(Mandatory = $true)][string] $CecilDll
)
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($CecilDll)) | Out-Null
$inputPath = [IO.Path]::GetFullPath($InputDll)
$outputPath = [IO.Path]::GetFullPath($OutputDll)
if ($inputPath -eq $outputPath -or [IO.Path]::GetFullPath($GameDll) -eq $outputPath) {
    throw 'ServerSync preparation requires a separate build output; originals must remain unchanged.'
}
# This is the bundled upstream binary, not a replacement library from another mod.
$sha256 = [Security.Cryptography.SHA256]::Create()
try { $inputHash = [BitConverter]::ToString($sha256.ComputeHash([IO.File]::ReadAllBytes($inputPath))).Replace('-', '') }
finally { $sha256.Dispose() }
if ($inputHash -ne '166956302A294E224474B26F4C7D58409084AD3F48BD0AF1FEB7551F229C8F60') {
    throw 'ServerSync input changed. Review its game API usage before updating the compatibility patch.'
}
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($GameDll)
$sync = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($inputPath)
try {
    $everybody = $game.MainModule.GetType('ZRoutedRpc').Fields | Where-Object Name -eq 'Everybody'
    if (!$everybody.IsLiteral -or $everybody.FieldType.FullName -ne 'System.Int64' -or [long]$everybody.Constant -ne 0) {
        throw 'Expected Valheim 1.0.7 ZRoutedRpc.Everybody = const Int64 0.'
    }
    $count = 0
    foreach ($type in $sync.MainModule.GetTypes()) {
        foreach ($method in $type.Methods) {
            if (!$method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                $field = $instruction.Operand -as [Mono.Cecil.FieldReference]
                if (!$field -or $field.DeclaringType.FullName -ne 'ZRoutedRpc' -or $field.Name -ne 'Everybody') { continue }
                if ($instruction.OpCode.Code -ne [Mono.Cecil.Cil.Code]::Ldsfld -or $field.FieldType.FullName -ne 'System.Int64') {
                    throw "Unexpected Everybody access in $($method.FullName)."
                }
                # Match a recompile against the new public const, preserving the broadcast recipient.
                $instruction.OpCode = [Mono.Cecil.Cil.OpCodes]::Ldc_I8
                $instruction.Operand = [long]0
                $count++
            }
        }
    }
    if ($count -ne 3) { throw "Expected 3 upstream Everybody reads, found $count." }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
    $sync.Write($outputPath)
    Write-Host "ServerSync: prepared $count constant reads for Valheim 1.0.7; upstream binary preserved."
}
finally {
    $sync.Dispose()
    $game.Dispose()
}
