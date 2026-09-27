param(
    [Parameter(Mandatory = $true)][string]$HunterDll,
    [Parameter(Mandatory = $true)][string]$ModDll,
    [Parameter(Mandatory = $true)][string]$GameManaged,
    [Parameter(Mandatory = $true)][string]$BepInExCore
)
$ErrorActionPreference = 'Stop'
Add-Type -Path (Join-Path $BepInExCore 'Mono.Cecil.dll')
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($GameManaged)
$resolver.AddSearchDirectory($BepInExCore)
$resolver.AddSearchDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($HunterDll)))
$options = [Mono.Cecil.ReaderParameters]::new()
$options.AssemblyResolver = $resolver
$hunter = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($HunterDll, $options)
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($ModDll, $options)
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameManaged 'assembly_valheim.dll'), $options)
$script:checks = 0
function Check($condition, [string]$message) {
    if (!$condition) { throw $message }
    $script:checks++
}
function Method($type, [string]$name) {
    return $type.Methods | Where-Object Name -eq $name | Select-Object -First 1
}
function Calls($method, [string]$typeName, [string]$methodName) {
    return @($method.Body.Instructions | Where-Object {
        $_.Operand -is [Mono.Cecil.MethodReference] -and
        $_.Operand.DeclaringType.FullName -eq $typeName -and $_.Operand.Name -eq $methodName
    })
}
try {
    Check ((Get-FileHash -LiteralPath $HunterDll -Algorithm SHA256).Hash -eq 'E50EB6908B8AA46BF652D39C9F942CD200C2317CADB3303C9F0E30BC66F52A1E') 'Use the reviewed, unmodified Hunter Legacy 1.1.4 DLL.'
    $plugin = $hunter.MainModule.GetType('Hunter_Legacy.Hunter_LegacyPlugin')
    $identity = $plugin.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' }
    Check ($identity.ConstructorArguments[0].Value -eq 'Dreanegade.Hunter_Legacy' -and $identity.ConstructorArguments[2].Value -eq '1.1.4') 'Hunter plugin identity changed.'
    $registry = $hunter.MainModule.GetType('ItemManager.PrefabManager').Fields | Where-Object Name -eq 'prefabs'
    Check ($registry.IsPrivate -and $registry.IsStatic -and $registry.FieldType.FullName -eq 'System.Collections.Generic.List`1<UnityEngine.GameObject>') 'Hunter prefab registry contract changed.'
    $ammo = $hunter.MainModule.GetType('Hunter_Legacy.AmmoUsageOverride')
    $clones = $ammo.Fields | Where-Object Name -eq 'CloneSources'
    Check ($clones.IsPrivate -and $clones.IsStatic -and $clones.IsInitOnly -and $clones.FieldType.FullName -eq 'System.Collections.Generic.Dictionary`2<Attack,Attack>') 'Hunter clone tracking contract changed.'
    $postfix = Method ($ammo.NestedTypes | Where-Object Name -eq 'Attack_Clone_Patch') 'Postfix'
    Check (@($postfix.Body.Instructions | Where-Object { $_.Operand -is [Mono.Cecil.FieldReference] -and $_.Operand.Name -eq 'CloneSources' }).Count -eq 1) 'Clone postfix no longer uses CloneSources.'
    Check (@(Calls $postfix 'System.Collections.Generic.Dictionary`2<Attack,Attack>' 'set_Item').Count -eq 1) 'Clone postfix no longer records clone source.'
    foreach ($pair in @(@('Attack_Start_Patch', 'Prefix'), @('Attack_Stop_Patch', 'Postfix'))) {
        $method = Method ($ammo.NestedTypes | Where-Object Name -eq $pair[0]) $pair[1]
        Check (@(Calls $method 'System.Collections.Generic.Dictionary`2<Attack,Attack>' 'Remove').Count -gt 0) ($pair[0] + ' no longer consumes runtime clone tracking.')
    }
    $gameStart = Method ($game.MainModule.GetType('Humanoid')) 'StartAttack'
    $gameClone = @(Calls $gameStart 'Attack' 'Clone')
    $gameAttackStart = @(Calls $gameStart 'Attack' 'Start')
    Check ($gameClone.Count -eq 2 -and $gameAttackStart.Count -eq 1 -and
        $gameClone[0].Offset -lt $gameAttackStart[0].Offset -and $gameClone[1].Offset -lt $gameAttackStart[0].Offset) 'Original game no longer clones its primary/secondary template branches before Start.'
    Check ((Method ($game.MainModule.GetType('Attack')) 'Clone').IsPublic) 'Attack.Clone is not public in original game metadata.'

    $saPlugin = $mod.MainModule.GetType('SecondaryAttacks.SecondaryAttacksPlugin')
    $dependency = @($saPlugin.CustomAttributes | Where-Object {
        $_.AttributeType.FullName -eq 'BepInEx.BepInDependency' -and $_.ConstructorArguments[0].Value -eq 'Dreanegade.Hunter_Legacy'
    })
    Check ($dependency.Count -eq 1 -and $dependency[0].ConstructorArguments[1].Value -eq 2) 'Hunter must remain a soft dependency.'
    Check (@($mod.MainModule.AssemblyReferences | Where-Object Name -eq 'Hunter_Legacy').Count -eq 0) 'Hunter must not become a required assembly reference.'
    $cloneHelper = Method ($mod.MainModule.GetType('SecondaryAttacks.SecondaryAttackManager')) 'CloneAttack'
    $publicClone = @(Calls $cloneHelper 'Attack' 'Clone')
    $cleanup = @(Calls $cloneHelper 'SecondaryAttacks.HunterLegacyCompat' 'ForgetPreparedClone')
    Check ($publicClone.Count -eq 1 -and $cleanup.Count -eq 1 -and $publicClone[0].Offset -lt $cleanup[0].Offset) 'Preparation must use public Clone then scoped tracking cleanup.'
    $apply = Method ($mod.MainModule.GetType('SecondaryAttacks.SecondaryAttackWorldApplySystem')) 'Apply'
    $guard = @(Calls $apply 'SecondaryAttacks.HunterLegacyCompat' 'ShouldSkipWeapon')
    $compile = @(Calls $apply 'SecondaryAttacks.SecondaryAttackDefinitionCompiler' 'TryCreateDefinition')
    $restore = @(Calls $apply 'SecondaryAttacks.SecondaryAttackObjectDbStateStore' 'Restore')
    Check ($guard.Count -eq 1 -and $restore[0].Offset -lt $guard[0].Offset -and $guard[0].Offset -lt $compile[0].Offset) 'Restore must precede Hunter exclusion, which must precede definition compilation.'
    Check (@(Calls $apply 'SecondaryAttacks.SecondaryAttackDefinitionCompiler' 'IsPresetOptOut').Count -eq 1) 'Explicit opt-out should not produce an override warning.'
    $shield = $mod.MainModule.GetType('SecondaryAttacks.LastEquippedShieldSystem')
    foreach ($name in @('HandleEquip', 'FindRememberedShield')) {
        Check (@(Calls (Method $shield $name) 'SecondaryAttacks.HunterLegacyCompat' 'IsOffhandCrossbow').Count -eq 1) ('Offhand guard missing in ' + $name)
    }
    Write-Output "PASS Hunter original/merged contracts: $script:checks checks. Static IL/metadata inspection only; no Unity or Harmony execution."
} finally {
    $hunter.Dispose()
    $mod.Dispose()
    $game.Dispose()
    $resolver.Dispose()
}
