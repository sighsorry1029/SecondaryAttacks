param(
    [Parameter(Mandatory = $true)][string]$PathOfValheimanDll,
    [Parameter(Mandatory = $true)][string]$ModDll,
    [string]$GameManaged = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/valheim_Data/Managed',
    [string]$BepInExCore = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/core'
)
$ErrorActionPreference = 'Stop'
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $BepInExCore 'Mono.Cecil.dll')))
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($GameManaged)
$resolver.AddSearchDirectory($BepInExCore)
$options = [Mono.Cecil.ReaderParameters]::new()
$options.AssemblyResolver = $resolver
$pov = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($PathOfValheimanDll, $options)
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($ModDll, $options)
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Join-Path $GameManaged 'assembly_valheim.dll'), $options)
$script:checks = 0
function Check($value, $message) { if (!$value) { throw $message }; $script:checks++ }
function FindType($assembly, $name) { return $assembly.MainModule.GetType($name) }
function Method($type, $name) { return $type.Methods | Where-Object Name -eq $name | Select-Object -First 1 }
try {
    # Fixture identity only. Runtime acceptance checks signatures/registrations,
    # not this hash or version. No external assembly is loaded or publicized.
    Check ((Get-FileHash -LiteralPath $PathOfValheimanDll).Hash -eq '36E45B65B44667AD596446CAC2AE989041669948C11710B05185E4FE16697DB4') 'Unexpected PoV review fixture.'
    $identity = (FindType $pov 'PassiveTree.Plugin').CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' }
    Check ($identity.ConstructorArguments[0].Value -eq 'treextr.pathofvalheiman' -and $identity.ConstructorArguments[2].Value -eq '5.0.4') 'Unexpected plugin identity.'
    $fields = @{
        'PassiveTree.SecondaryAttacks' = @{ producing = 'Attack' }
        'PassiveTree.ChargedAttacks' = @{ producing = 'Attack' }
        'PassiveTree.WeaponAffixes' = @{ Override = 'ItemDrop/ItemData' }
        'PassiveTree.Plugin' = @{ DisableVanillaSkills = 'BepInEx.Configuration.ConfigEntry`1<System.Boolean>' }
        'PassiveTree.ProjectileMods' = @{
            creatingProjectile = 'Attack'; lastAttack = 'Attack'; burstFor = 'Attack'
            addedShots = 'System.Int32'; nativeShots = 'System.Int32'; recoverable = 'System.Int32'
            shotIndex = 'System.Int32'; volleyNow = 'System.Int32'; ownSpread = 'System.Single'
            usingAmmo = 'System.Boolean'; ammoSaved = 'System.Boolean'; inBurst = 'System.Boolean'; countingBurst = 'System.Boolean'
        }
    }
    foreach ($typeName in $fields.Keys) {
        foreach ($name in $fields[$typeName].Keys) {
            $field = (FindType $pov $typeName).Fields | Where-Object Name -eq $name
            Check ($field -and $field.IsStatic -and !$field.IsInitOnly -and !$field.IsLiteral -and $field.FieldType.FullName -eq $fields[$typeName][$name]) "State contract changed: $typeName.$name"
        }
    }
    foreach ($contract in @(
        @('PassiveTree.TamingHunting', 'Keeper', 'Player', 'UnityEngine.Component', ''),
        @('PassiveTree.ProjectileMods', 'OpenBurst', 'System.Void', 'System.Int32,System.Single', ''),
        @('PassiveTree.SecondaryAttacks', 'Secondary', 'System.Boolean', 'Attack', ''),
        @('PassiveTree.Effects/Attack_GetAttackEitr', 'Postfix', 'System.Void', 'Character,ItemDrop/ItemData,System.Single&', 'GetAttackEitr'),
        @('PassiveTree.ProjectileMods/Inventory_RemoveItem', 'Prefix', 'System.Boolean', 'System.Boolean&', 'RemoveItem'),
        @('PassiveTree.CrossbowRecoil/Burst', 'Postfix', 'System.Void', 'Attack', 'FireProjectileBurst'),
        @('PassiveTree.AmmoVariety/Fired', 'Postfix', 'System.Void', 'Attack', 'FireProjectileBurst')
    )) {
        $type = FindType $pov $contract[0]
        $method = Method $type $contract[1]
        Check ($method -and $method.IsStatic -and $method.ReturnType.FullName -eq $contract[2] -and ($method.Parameters.ParameterType.FullName -join ',') -eq $contract[3]) "Method contract changed: $($contract[0]).$($contract[1])"
        if ($contract[4]) {
            $patch = $type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' }
            Check ($patch.ConstructorArguments[1].Value -eq $contract[4]) "Patch target changed: $($contract[0])"
        }
    }
    foreach ($contract in @(
        @('PassiveTree.ProjectileMods/ClassifyBeforeDamage', 'Prefix', 'SEMan', 'ModifyAttack'),
        @('PassiveTree.ProjectileMods/Attack_Start', 'Postfix', 'Attack', 'Start'),
        @('PassiveTree.ProjectileMods/Projectile_Setup', 'Prefix', 'Projectile', 'Setup'),
        @('PassiveTree.ProjectileMods/Projectile_Setup', 'Postfix', 'Projectile', 'Setup'),
        @('PassiveTree.SecondaryAttacks/Humanoid_StartAttack', 'Prefix', 'Humanoid', 'StartAttack'),
        @('PassiveTree.SecondaryAttacks/Attack_Start_Origin', 'Prefix', 'Attack', 'Start'),
        @('PassiveTree.ChargedAttacks/ReleaseAttack', 'Prefix', 'Attack', 'OnAttackTrigger'),
        @('PassiveTree.Blasts/Attack_OnAttackTrigger_Area', 'Prefix', 'Attack', 'OnAttackTrigger')
    )) {
        $type = FindType $pov $contract[0]
        $method = Method $type $contract[1]
        $patch = $type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' }
        Check ($method.IsStatic -and $patch.ConstructorArguments[0].Value.FullName -eq $contract[2] -and $patch.ConstructorArguments[1].Value -eq $contract[3]) "Required callback contract changed: $($contract[0]).$($contract[1])"
    }
    $attack = FindType $game 'Attack'
    foreach ($name in @('Pierce','Chain','Bounce')) {
        $field = (FindType $pov 'PassiveTree.ProjMods').Fields | Where-Object Name -eq $name
        Check ($field -and !$field.IsStatic -and !$field.IsInitOnly -and $field.FieldType.FullName -eq 'System.Int32') "Continuation field changed: $name"
    }
    foreach ($name in @('m_character', 'm_projectileBurstsFired')) {
        Check (($attack.Fields | Where-Object Name -eq $name).IsPrivate) "Original private field changed: $name"
    }
    foreach ($name in @('m_projectiles', 'm_projectileBursts')) {
        Check (($attack.Fields | Where-Object Name -eq $name).IsPublic) "Original public field changed: $name"
    }
    Check ((Method $attack 'GetWeapon').IsPublic) 'GetWeapon must be public.'
    Check ((Method (FindType $game 'ZNetView') 'IsValid').IsPublic) 'IsValid must be public.'
    $bridge = FindType $mod 'SecondaryAttacks.PathOfValheimanCompat'
    foreach ($type in @($bridge) + @($bridge.NestedTypes)) {
        foreach ($method in $type.Methods) {
            foreach ($instruction in $method.Body.Instructions) {
                $field = $instruction.Operand -as [Mono.Cecil.FieldReference]
                if ($field -and $field.DeclaringType.FullName -eq 'Attack') {
                    Check ($field.Name -notin @('m_character','m_projectileBurstsFired')) "Direct private game access introduced: $field"
                }
            }
        }
    }
    $dependency = (FindType $mod 'SecondaryAttacks.SecondaryAttacksPlugin').CustomAttributes | Where-Object {
        $_.AttributeType.FullName -eq 'BepInEx.BepInDependency' -and $_.ConstructorArguments[0].Value -eq 'treextr.pathofvalheiman'
    }
    Check ($dependency.Count -eq 1 -and $dependency.ConstructorArguments[1].Value -eq 2) 'PoV must remain optional.'
    Check (@($mod.MainModule.AssemblyReferences | Where-Object Name -eq 'PathOfValheiman').Count -eq 0) 'No hard PoV assembly reference allowed.'
    $prefix = Method (FindType $mod 'SecondaryAttacks.AttackOnAttackTriggerPatch') 'Prefix'
    Check (@($prefix.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyAfter' -and $_.ConstructorArguments[0].Value[0].Value -eq 'treextr.pathofvalheiman' }).Count -eq 1) 'Custom melee must run after PoV preparation.'
    foreach ($name in @('SecondaryAttacks.ProjectileOnHitPatch', 'SecondaryAttacks.CharacterDamageDirectWeaponHitEffectsPatch')) {
        Check (@((Method (FindType $mod $name) 'Prefix').Parameters | Where-Object Name -eq '__runOriginal').Count -eq 1) "Earlier rejection must be observed: $name"
    }
    Write-Output "PASS: $script:checks original PoV/game and merged bridge contracts. Metadata/IL only; no game execution."
} finally { $pov.Dispose(); $mod.Dispose(); $game.Dispose(); $resolver.Dispose() }
