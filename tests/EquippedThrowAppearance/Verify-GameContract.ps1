param(
    [Parameter(Mandatory=$true)][string]$GameDll,
    [Parameter(Mandatory=$true)][string]$ModDll,
    [Parameter(Mandatory=$true)][string]$CecilDll
)
$ErrorActionPreference='Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($CecilDll)) | Out-Null
$game=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($GameDll)
$mod=[Mono.Cecil.AssemblyDefinition]::ReadAssembly($ModDll)
$script:passed=0
function Check($value,[string]$description) {
    if(!$value) { throw $description }
    $script:passed++
}
try {
    $projectile=$game.MainModule.GetType('Projectile')
    $update=@($projectile.Methods | Where-Object Name -eq 'UpdateVisual')
    Check ($update.Count -eq 1 -and $update[0].Parameters.Count -eq 0 -and $update[0].IsPrivate) 'Original UpdateVisual signature'
    $lookups=@($update[0].Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and
        $_.Operand.DeclaringType.FullName -eq 'ObjectDB' -and $_.Operand.Name -eq 'GetItemPrefab' -and
        ($_.Operand.Parameters.ParameterType.FullName -join ',') -eq 'System.String'
    })
    Check ($lookups.Count -eq 1) 'Exactly one original ObjectDB string lookup anchor'
    $changed=@($projectile.Fields | Where-Object Name -eq 'm_changedVisual')
    Check ($changed.Count -eq 1 -and $changed[0].FieldType.FullName -eq 'System.Boolean' -and $changed[0].IsPrivate) 'Cached changedVisual reflection contract'
    $equipment=$game.MainModule.GetType('IEquipmentVisual')
    Check ($equipment -and $equipment.IsInterface -and !$equipment.IsPublic) 'Original equipment visual interface remains non-public'
    Check (@($equipment.Methods | Where-Object { $_.Name -eq 'Setup' -and ($_.Parameters.ParameterType.FullName -join ',') -eq 'System.Int32' }).Count -eq 1) 'Cached equipment Setup(int) contract'
    $humanoid=$game.MainModule.GetType('Humanoid')
    foreach($name in @('get_LeftItem','get_RightItem')) {
        Check (@($humanoid.Methods | Where-Object { $_.Name -eq $name -and $_.IsPublic -and $_.ReturnType.FullName -eq 'ItemDrop/ItemData' }).Count -eq 1) "Public $name getter"
    }
    $start=@($game.MainModule.GetType('Attack').Methods | Where-Object Name -eq 'Start')
    Check ($start.Count -eq 1 -and $start[0].ReturnType.FullName -eq 'System.Boolean') 'Attack.Start unambiguous target'
    Check (@($start[0].Parameters | Where-Object { $_.Name -eq 'character' -and $_.ParameterType.FullName -eq 'Humanoid' }).Count -eq 1) 'Attack.Start character injection'
    Check (@($start[0].Parameters | Where-Object { $_.Name -eq 'weapon' -and $_.ParameterType.FullName -eq 'ItemDrop/ItemData' }).Count -eq 1) 'Attack.Start weapon injection before instance initialization'
    $access=$mod.MainModule.GetType('SecondaryAttacks.ProjectileAccess')
    $variant=@($access.Methods | Where-Object Name -eq 'SetEquipmentVisualVariant')[0]
    Check (@($variant.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.DeclaringType.FullName -eq 'IEquipmentVisual' }).Count -eq 0) 'New variant helper does not directly call internal game interface'
    $strings=@($access.Methods | Where-Object Name -eq '.cctor' | ForEach-Object { $_.Body.Instructions } | Where-Object { $_.OpCode.Name -eq 'ldstr' } | ForEach-Object Operand)
    Check ('IEquipmentVisual' -in $strings -and 'm_changedVisual' -in $strings) 'Final mod caches the explicit reflection contracts'
    $visual=$mod.MainModule.GetType('SecondaryAttacks.CopiedThrowProjectileVisualSystem')
    Check (@($visual.Fields | Where-Object { $_.Name -eq 'EquippedAppearanceKey' -and $_.Constant -eq 'SecondaryAttacks_EquippedThrowAppearance' }).Count -eq 1) 'Final mod uses the distinct cosmetic network key'
    Write-Host "PASS $script:passed original-DLL and final-mod contract checks: $GameDll"
    Write-Host 'Static metadata/IL checks only; does not execute Harmony, Unity, or multiplayer.'
}
finally { $game.Dispose();$mod.Dispose() }
