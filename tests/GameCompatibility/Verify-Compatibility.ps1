param(
    [Parameter(Mandatory = $true)][string] $ModDll,
    [Parameter(Mandatory = $true)][string] $GameManaged,
    [Parameter(Mandatory = $true)][string] $BepInExCore,
    [string] $CecilDll = "$BepInExCore/Mono.Cecil.dll",
    [string] $ReportPath
)
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($CecilDll)) | Out-Null
$resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
$resolver.AddSearchDirectory($GameManaged)
$resolver.AddSearchDirectory($BepInExCore)
$resolver.AddSearchDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($ModDll)))
$options = [Mono.Cecil.ReaderParameters]::new()
$options.AssemblyResolver = $resolver
$mod = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($ModDll, $options)
$failures = [Collections.Generic.List[string]]::new()
$references = [Collections.Generic.HashSet[string]]::new()
$targets = [Collections.Generic.HashSet[string]]::new()
$privateAccesses = [Collections.Generic.HashSet[string]]::new()
$indirectChecks = [Collections.Generic.List[string]]::new()
function Is-GameScope($reference) {
    return $reference.Scope.Name -match '^(assembly_|UnityEngine|gui_framework|SoftReferenceableAssets|Splatform)'
}
function Find-Field($type, $name) {
    while ($type) {
        $field = $type.Fields | Where-Object Name -eq $name | Select-Object -First 1
        if ($field) { return $field }
        $type = if ($type.BaseType) { $type.BaseType.Resolve() } else { $null }
    }
}
try {
    $game = $resolver.Resolve([Mono.Cecil.AssemblyNameReference]::new('assembly_valheim', [Version]'0.0.0.0'))
    if ([IO.Path]::GetFullPath($game.MainModule.FileName) -ne [IO.Path]::GetFullPath("$GameManaged/assembly_valheim.dll")) {
        throw 'Compatibility checks must resolve the selected original game assembly.'
    }
    foreach ($reference in $mod.MainModule.GetTypeReferences()) {
        if (!(Is-GameScope $reference)) { continue }
        try { if (!$reference.Resolve()) { $failures.Add("Missing type: $reference") } }
        catch { $failures.Add("Unresolved type: $reference : $_") }
    }
    foreach ($type in $mod.MainModule.GetTypes()) {
        foreach ($method in $type.Methods) {
            if (!$method.HasBody) { continue }
            foreach ($instruction in $method.Body.Instructions) {
                $member = $instruction.Operand -as [Mono.Cecil.MemberReference]
                if (!$member -or !$member.DeclaringType -or !(Is-GameScope $member.DeclaringType)) { continue }
                if ($member -isnot [Mono.Cecil.MethodReference] -and $member -isnot [Mono.Cecil.FieldReference]) { continue }
                [void]$references.Add($member.FullName)
                try {
                    $resolved = $member.Resolve()
                    if (!$resolved) { $failures.Add("Missing member: $member used by $method"); continue }
                    if ($resolved -is [Mono.Cecil.FieldDefinition] -and $resolved.IsLiteral) {
                        $failures.Add("Literal field instruction: $instruction in $method")
                    }
                    if ($resolved.IsPrivate -or $resolved.IsAssembly) {
                        [void]$privateAccesses.Add($member.FullName)
                    }
                }
                catch { $failures.Add("Unresolved member: $member : $_") }
            }
        }

        # Class-level patches and method-level patches (ServerSync uses both).
        $classPatches = @($type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' -and $_.ConstructorArguments.Count -ge 2 })
        $patchSites = @()
        foreach ($attribute in $classPatches) {
            $patchSites += @{ Attribute = $attribute; Methods = @($type.Methods | Where-Object { $_.Name -in @('Prefix','Postfix','Finalizer','Transpiler') }) }
        }
        foreach ($method in $type.Methods) {
            foreach ($attribute in @($method.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'HarmonyLib.HarmonyPatch' -and $_.ConstructorArguments.Count -ge 2 })) {
                $patchSites += @{ Attribute = $attribute; Methods = @($method) }
            }
        }
        foreach ($site in $patchSites) {
            $arguments = $site.Attribute.ConstructorArguments
            $targetType = $arguments[0].Value.Resolve()
            $name = [string]$arguments[1].Value
            $candidates = @($targetType.Methods | Where-Object Name -eq $name)
            if ($arguments.Count -gt 2) {
                $signature = (@($arguments[2].Value | ForEach-Object { $_.Value.FullName }) -join ',')
                $candidates = @($candidates | Where-Object { ($_.Parameters.ParameterType.FullName -join ',') -eq $signature })
            }
            if ($candidates.Count -ne 1) { $failures.Add("Patch target $($targetType.FullName)::$name has $($candidates.Count) candidates in $($type.FullName)"); continue }
            $target = $candidates[0]
            [void]$targets.Add($target.FullName)
            foreach ($patch in $site.Methods) {
                foreach ($parameter in $patch.Parameters) {
                    $parameterName = $parameter.Name
                    if ($parameterName.StartsWith('___')) {
                        if (!(Find-Field $targetType $parameterName.Substring(3))) { $failures.Add("Missing injected field $parameterName for $patch") }
                    }
                    elseif (!$parameterName.StartsWith('__') -and $patch.Name -ne 'Transpiler') {
                        if ($parameterName -notin $target.Parameters.Name) { $failures.Add("Missing patch argument $parameterName for $patch -> $target") }
                    }
                }
            }
        }
    }
    # Explicit indirect game contracts used by the production reflection helpers.
    # Optional external mod APIs are outside this original-game check.
    $reflectionFields = @{
        Character = @('m_backstabTime')
        Projectile = @('m_weapon','m_owner','m_originalHitData','m_vel','m_didHit')
        SEMan = @('m_character')
        SE_Shield = @('m_totalAbsorbDamage','m_damage')
        Attack = @('m_visEquipment')
        VisEquipment = @('m_rightItemInstance')
        MeleeWeaponTrail = @('_base','_tip','_material','_colors','_sizes','_lifeTime','subdivisions',
            'm_trailMesh','m_lastPosition','_emitTime','m_points','m_smoothedPoints','m_smoothBaseList',
            'm_smoothTipList','m_newVertices','m_newUV','m_newColors','m_newTriangles')
    }
    foreach ($typeName in $reflectionFields.Keys) {
        $type = $game.MainModule.GetType($typeName)
        foreach ($name in $reflectionFields[$typeName]) {
            $field = Find-Field $type $name
            if (!$field) { $failures.Add("Missing reflection field: ${typeName}::$name") }
            else { $indirectChecks.Add($field.FullName + ' [' + $field.Attributes + ']') }
        }
    }
    foreach ($contract in @(@('Tameable','UnSummon'), @('Localization','SetupLanguage'),
        @('FejdStartup','SetupGui'), @('FejdStartup','Start'))) {
        $assembly = if ($contract[0] -eq 'Localization') {
            $resolver.Resolve([Mono.Cecil.AssemblyNameReference]::new('assembly_guiutils', [Version]'0.0.0.0'))
        } else { $game }
        $type = $assembly.MainModule.GetType($contract[0])
        $matches = @($type.Methods | Where-Object Name -eq $contract[1])
        if ($matches.Count -ne 1) { $failures.Add("Indirect method not unique: $contract") }
        else { $indirectChecks.Add($matches[0].FullName) }
    }
    # These are the exact field-load predicates used by the movement transpiler.
    # Check original IL; successful matching is not a Harmony/Unity execution test.
    $movementFields = @{
        UpdateWalking = @('m_walkSpeed','m_speed','m_runSpeed','m_acceleration','m_turnSpeed','m_runTurnSpeed')
        UpdateFlying = @('m_flySlowSpeed','m_flyFastSpeed','m_acceleration','m_flyTurnSpeed')
        UpdateSwimming = @('m_swimSpeed','m_swimAcceleration','m_swimTurnSpeed')
    }
    $character = $game.MainModule.GetType('Character')
    foreach ($name in $movementFields.Keys) {
        $methods = @($character.Methods | Where-Object { $_.Name -eq $name -and ($_.Parameters.ParameterType.FullName -join ',') -eq 'System.Single' })
        if ($methods.Count -ne 1) { $failures.Add("Missing dynamic Character::$name(float)"); continue }
        $loads = @($methods[0].Body.Instructions | Where-Object {
            $_.OpCode.Code -eq [Mono.Cecil.Cil.Code]::Ldfld -and
            $_.Operand.DeclaringType.FullName -eq 'Character' -and $_.Operand.FieldType.FullName -eq 'System.Single'
        } | ForEach-Object { $_.Operand.Name })
        foreach ($field in $movementFields[$name]) {
            if ($field -notin $loads) { $failures.Add("Missing movement transpiler load: ${name}::$field") }
        }
        $indirectChecks.Add($methods[0].FullName + ': expected movement field loads')
    }
    $accessAssemblies = @($mod.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute' } | ForEach-Object { $_.ConstructorArguments[0].Value })
    foreach ($assemblyName in @('assembly_valheim','assembly_utils','assembly_guiutils')) {
        if ($assemblyName -notin $accessAssemblies) { $failures.Add("Missing final access attribute: $assemblyName") }
    }
    if ('System.Security.UnverifiableCodeAttribute' -notin $mod.MainModule.CustomAttributes.AttributeType.FullName) {
        $failures.Add('Missing final UnverifiableCode module attribute.')
    }
    Write-Host "Checked $($references.Count) direct game/Unity member references, $($targets.Count) attribute Harmony targets, and $($indirectChecks.Count) indirect contracts against $GameManaged."
    if ($ReportPath) {
        [ordered]@{
            modDll = [IO.Path]::GetFullPath($ModDll)
            modSha256 = (Get-FileHash -LiteralPath $ModDll -Algorithm SHA256).Hash
            gameManaged = [IO.Path]::GetFullPath($GameManaged)
            directReferences = @($references | Sort-Object)
            attributeHarmonyTargets = @($targets | Sort-Object)
            indirectContracts = @($indirectChecks | Sort-Object)
            nonPublicDirectReferences = @($privateAccesses | Sort-Object)
            accessAttributes = $accessAssemblies
            failures = @($failures)
            limitation = 'Static original-DLL check only. Does not execute Harmony, Unity, optional mods, or multiplayer; non-public runtime access requires separate validation.'
        } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
    }
    if ($failures.Count) {
        $failures | Sort-Object -Unique | ForEach-Object { Write-Host $_ }
        throw "$($failures.Count) compatibility failures."
    }
    Write-Host ('Existing direct non-public member references: ' + $privateAccesses.Count)
    Write-Host 'Static compatibility checks passed. This does not execute Unity or multiplayer.'
}
finally {
    $mod.Dispose()
    $resolver.Dispose()
}
