using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

internal static class OriginalContract
{
    internal static void Run(string modPath, string managedPath)
    {
        string core = @"C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\core";
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string directory in new[] { managedPath, core, Path.GetDirectoryName(Path.GetFullPath(modPath))! })
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
            }
            return null;
        };
        Assembly game = Assembly.LoadFrom(Path.Combine(managedPath, "assembly_valheim.dll"));
        Assembly mod = Assembly.LoadFrom(Path.GetFullPath(modPath));
        Type spawn = game.GetType("SpawnAbility", true)!;
        MethodInfo source = spawn.GetMethod("Spawn", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if (source == null || !source.IsPrivate || !spawn.GetField("m_owner", BindingFlags.Instance | BindingFlags.NonPublic)!.IsPrivate)
            throw new Exception("Expected original, non-publicized SpawnAbility contracts");
        MethodInfo moveNext = AccessTools.EnumeratorMoveNext(source);
        // Framework cannot GetMethodBody on Unity's default-interface types.
        // Read untouched game IL with Cecil and resolve operands to reflection
        // handles, without executing or publicizing any game method.
        using var definition = Mono.Cecil.AssemblyDefinition.ReadAssembly(Path.Combine(managedPath, "assembly_valheim.dll"));
        var method = definition.MainModule.Types.Single(t => t.Name == "SpawnAbility").NestedTypes
            .Single(t => t.Name.StartsWith("<Spawn>")).Methods.Single(m => m.Name == "MoveNext");
        if (method.Body.ExceptionHandlers.Count != 0) throw new Exception("New exception regions need explicit verification");
        var generator = new DynamicMethod("LabelsOnly", typeof(void), Type.EmptyTypes).GetILGenerator();
        var labels = method.Body.Instructions.ToDictionary(i => i, _ => generator.DefineLabel());
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(OpCode))
            .Select(f => (OpCode)f.GetValue(null)!).ToDictionary(o => o.Value);
        object? Operand(object? operand) => operand switch
        {
            Mono.Cecil.Cil.Instruction i => labels[i],
            Mono.Cecil.Cil.Instruction[] list => list.Select(i => labels[i]).ToArray(),
            Mono.Cecil.MethodReference m when m.Name == "Instantiate" => game.ManifestModule.ResolveMethod(m.MetadataToken.ToInt32()),
            Mono.Cecil.Cil.VariableDefinition v => v.Index,
            Mono.Cecil.ParameterDefinition p => p.Index + 1,
            _ => operand
        };
        var body = method.Body.Instructions.Select(i =>
        {
            var c = new CodeInstruction(opcodes[i.OpCode.Value], Operand(i.Operand));
            c.labels.Add(labels[i]);
            return c;
        }).ToList();
        MethodInfo patch = mod.GetType("SecondaryAttacks.SpawnAbilitySubSummonPatch", true)!.GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic)!;
        List<CodeInstruction> Run(IEnumerable<CodeInstruction> input) => ((IEnumerable<CodeInstruction>)patch.Invoke(null, new object[] { input, moveNext })!).ToList();
        var result = Run(body.Select(c => new CodeInstruction(c)).ToList());
        int at = result.FindIndex(c => c.operand is MethodInfo m && m.DeclaringType?.FullName == "SecondaryAttacks.SubSummonSystem" && m.Name == "Instantiate");
        if (at < 0 || !result[at + 1].IsStloc() || result[at + 3].opcode != OpCodes.Brfalse)
            throw new Exception("Missing pre-creation guard result/continue branch");
        Label label = (Label)result[at + 3].operand;
        int target = result.FindIndex(c => c.labels.Contains(label));
        if (target <= at || !result.Skip(target).Take(4).Any(c => c.opcode == OpCodes.Add))
            throw new Exception("Denial must branch to the original loop increment");
        if (result.Count != body.Count + 4)
            throw new Exception("Unexpected original instruction removal");
        if (result.Any(c => c.operand is MethodInfo m && m.DeclaringType?.FullName == "UnityEngine.Object" && m.Name == "Instantiate"))
            throw new Exception("Original creation escaped the guard");
        var missing = body.Where(c => !(c.operand is MethodInfo m && m.Name == "Instantiate")).Select(c => new CodeInstruction(c)).ToList();
        try { Run(missing); throw new Exception("Missing creation site accepted"); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        var duplicate = body.Select(c => new CodeInstruction(c)).ToList();
        duplicate.Add(new CodeInstruction(body.Single(c => c.operand is MethodInfo m && m.Name == "Instantiate")));
        try { Run(duplicate); throw new Exception("Ambiguous creation sites accepted"); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }

        // Verify all new non-public field-injection and patch targets exist in the original.
        var character = definition.MainModule.Types.Single(t => t.Name == "Character");
        if (!character.Fields.Any(f => f.Name == "m_nview" && f.IsFamily) ||
            !definition.MainModule.Types.Single(t => t.Name == "SpawnAbility").Fields.Any(f => f.Name == "m_weapon" && f.IsPrivate) ||
            !definition.MainModule.Types.Single(t => t.Name == "ZDOMan").Methods.Any(m => m.Name == "HandleDestroyedZDO" && m.IsPrivate))
            throw new Exception("Original private contract missing");
        Console.WriteLine($"PASS merged-DLL transpiler against original {game.ManifestModule.ModuleVersionId}: single guarded creation, valid continue, rejected missing/ambiguous IL, original private contracts. No game execution.");
    }
}
