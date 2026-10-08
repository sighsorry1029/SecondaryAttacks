param(
    [Parameter(Mandatory = $true)][string]$EpicLootDll,
    [Parameter(Mandatory = $true)][string]$ModDll,
    [Parameter(Mandatory = $true)][string]$GameManaged,
    [Parameter(Mandatory = $true)][string]$BepInExCore
)
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $BepInExCore 'Mono.Cecil.dll')))
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($GameManaged)
$resolver.AddSearchDirectory($BepInExCore)
$resolver.AddSearchDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($EpicLootDll)))
$options = [Mono.Cecil.ReaderParameters]::new()
$options.AssemblyResolver = $resolver
$epic = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($EpicLootDll, $options)
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($ModDll, $options)
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameManaged 'assembly_valheim.dll'), $options)
$script:checks = 0
function Check($condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:checks++
}
function Method($type, [string]$name) { return $type.Methods | Where-Object Name -eq $name | Select-Object -First 1 }
function Calls($method, [string]$name) {
    return @($method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and $_.Operand.Name -eq $name
    })
}
try {
    Check ((Get-FileHash -LiteralPath $EpicLootDll -Algorithm SHA256).Hash -eq '802790E0CBD8A6F951627619A488B84E6A406E8770E2687B2FFF8B63D2ADF9EA') 'Use the reviewed unmodified EpicLoot 0.14.13 DLL.'
    $identity = @($epic.MainModule.Types.CustomAttributes | Where-Object {
        $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' -and $_.ConstructorArguments[0].Value -eq 'randyknapp.mods.epicloot'
    })
    Check ($identity.Count -eq 1 -and $identity[0].ConstructorArguments[2].Value -eq '0.14.13') 'EpicLoot plugin metadata changed (assembly version is not plugin version).'
    $multi = $epic.MainModule.GetType('EpicLoot.MagicItemEffects.MultiShot')
    foreach ($pair in @(@('Attack_OnAttackTrigger_Prefix','Attack'),
        @('Attack_FireProjectileBurst_Prefix','Attack,System.Nullable`1<HitData/DamageTypes>&'),
        @('Attack_FireProjectileBurst_Finalizer','Attack,System.Nullable`1<HitData/DamageTypes>'))) {
        $method = Method $multi $pair[0]
        Check ($method.IsPublic -and $method.IsStatic -and $method.ReturnType.FullName -eq 'System.Void' -and
            ($method.Parameters.ParameterType.FullName -join ',') -eq $pair[1]) ('EpicLoot callback changed: ' + $pair[0])
    }
    foreach ($name in @('IsTripleShotActive', 'ShotProjectiles', '_pendingShot')) {
        $field = $multi.Fields | Where-Object Name -eq $name
        Check ($field.IsStatic -and !$field.IsInitOnly -and $field.IsPrivate -eq ($name -eq '_pendingShot')) ('EpicLoot state field changed: ' + $name)
    }
    $conserve = $epic.MainModule.GetType('EpicLoot.Magic.MagicItemEffects.AmmoConservation')
    $reload = $conserve.Fields | Where-Object Name -eq 'skipReload'
    Check ($reload.IsPrivate -and $reload.IsStatic -and $reload.FieldType.FullName -eq 'System.Boolean') 'Conservation reload flag changed.'
    $refund = Method ($conserve.NestedTypes | Where-Object Name -eq 'AmmoConservation_Attack_UseAmmo_Patch') 'Postfix'
    Check ($refund.IsPublic -and $refund.IsStatic -and ($refund.Parameters.ParameterType.FullName -join ',') -eq 'Attack,System.Boolean&,ItemDrop/ItemData') 'Conservation callback signature changed.'
    Check (@(Calls $refund 'AddItem').Count -eq 1) 'Conservation refund path changed.'

    $attack = $game.MainModule.GetType('Attack')
    $character = $attack.Fields | Where-Object Name -eq 'm_character'
    Check ($character.IsPrivate -and !$character.IsStatic -and $character.FieldType.FullName -eq 'Humanoid') 'Original Attack.m_character access contract changed.'
    Check ((Method $attack 'GetWeapon').IsPublic) 'Attack.GetWeapon must be a public original-game API.'
    foreach ($name in @('m_projectiles', 'm_projectileAccuracy')) {
        Check (($attack.Fields | Where-Object Name -eq $name).IsPublic) ('Original attack field is not public: ' + $name)
    }
    $shared = $game.MainModule.GetType('ItemDrop').NestedTypes | Where-Object Name -eq 'ItemData' |
        ForEach-Object { $_.NestedTypes } | Where-Object Name -eq 'SharedData'
    Check (($shared.Fields | Where-Object Name -eq 'm_damages').IsPublic) 'Original shared damage field is not public.'
    $plugin = $mod.MainModule.GetType('SecondaryAttacks.SecondaryAttacksPlugin')
    $dependency = @($plugin.CustomAttributes | Where-Object {
        $_.AttributeType.FullName -eq 'BepInEx.BepInDependency' -and $_.ConstructorArguments[0].Value -eq 'randyknapp.mods.epicloot'
    })
    Check ($dependency.Count -eq 1 -and $dependency[0].ConstructorArguments[1].Value -eq 2) 'EpicLoot must remain a soft dependency.'
    Check (@($mod.MainModule.AssemblyReferences | Where-Object Name -eq 'EpicLoot').Count -eq 0) 'EpicLoot must not be a required assembly reference.'
    $projectiles = $mod.MainModule.GetType('SecondaryAttacks.ProjectileRuntimeSystem')
    $shot = Method $projectiles 'TryFireBurstShot'
    $offset = -1
    foreach ($name in @('BeginShot','TryCommitEpicLootBurstAmmo','PrepareProjectiles','CreateLaunchData','SpawnPrimaryProjectileCluster','Complete')) {
        $call = @(Calls $shot $name)
        Check ($call.Count -eq 1 -and $call[0].Offset -gt $offset) ('Actual shot sequence changed: ' + $name)
        $offset = $call[0].Offset
    }
    Check (@($shot.Body.ExceptionHandlers | Where-Object HandlerType -eq 'Finally').Count -gt 0) 'Shot needs exception-safe restoration.'
    $payment = Method ($mod.MainModule.GetType('SecondaryAttacks.SecondaryAttackRuntimeFacade')) 'TryCommitEpicLootBurstAmmo'
    $offset = -1
    foreach ($name in @('TrySelectConfiguredAmmo','TryValidateBurstPresetPayload','ConsumePerBurstResourcesIfNeeded','CommitConfiguredAmmo')) {
        $call = @(Calls $payment $name)
        Check ($call.Count -eq 1 -and $call[0].Offset -gt $offset) ('Payment sequence changed: ' + $name)
        $offset = $call[0].Offset
    }
    Write-Output "PASS: $script:checks EpicLoot original/merged contracts. Static IL/metadata only; no game execution."
} finally {
    $epic.Dispose(); $mod.Dispose(); $game.Dispose(); $resolver.Dispose()
}
