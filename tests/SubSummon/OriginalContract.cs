using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using HarmonyLib;

internal static class OriginalContract
{
    internal static void Run(string modPath, string managedPath, string? combatMeterPath = null)
    {
        string core = @"C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\core";
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string directory in new[] { managedPath, core, Path.GetDirectoryName(Path.GetFullPath(modPath))! })
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return Assembly.Load(File.ReadAllBytes(candidate));
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
        List<CodeInstruction> Run(IEnumerable<CodeInstruction> input) =>
            ((IEnumerable<CodeInstruction>)patch.Invoke(null, new object[] { Copy(input), moveNext, generator })!).ToList();
        AssertResult(body, Run(body), expectObserver: false);
        void Reject(List<CodeInstruction> input, string reason)
        {
            try { Run(input); throw new Exception("Accepted " + reason); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { }
        }
        Reject(Copy(body.Where(c => !IsVanillaInstantiate(c))), "missing creation site");
        var duplicate = Copy(body);
        duplicate.Add(new CodeInstruction(body.Single(IsVanillaInstantiate)));
        Reject(duplicate, "ambiguous creation sites");
        Reject(Copy(body.Take(body.FindIndex(IsVanillaInstantiate) + 1)), "missing post-creation instructions");
        var noTarget = Copy(body);
        noTarget[ContinueBranch(noTarget)].operand = generator.DefineLabel();
        Reject(noTarget, "missing continuation target");
        var backward = Copy(body);
        backward[ContinueBranch(backward)].operand = backward[0].labels[0];
        Reject(backward, "backward continuation target");
        var duplicateTarget = Copy(body);
        duplicateTarget[0].labels.Add((Label)duplicateTarget[ContinueBranch(duplicateTarget)].operand);
        Reject(duplicateTarget, "ambiguous continuation target");
        var exceptionRegion = Copy(body);
        exceptionRegion[0].blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
        Reject(exceptionRegion, "unsupported exception region");

        var metadata = new HarmonyMethod(patch);
        if (metadata.after == null || !metadata.after.Contains("Likhtenvald.CombatMeter"))
            throw new Exception("Missing CombatMeter transpiler ordering contract");
        if (combatMeterPath != null)
        {
            using var sha = SHA256.Create();
            string hash = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(combatMeterPath))).Replace("-", "");
            if (hash != "24876FF87193EA8CDECD5E75FEBE8FEEEB8E4C2B0D37329A62BE76BB61783496")
                throw new Exception("Supply the reviewed original CombatMeter 0.12.0 DLL");
            Assembly combatMeter = Assembly.LoadFrom(Path.GetFullPath(combatMeterPath));
            MethodInfo combatPatch = combatMeter.GetType("DiagnosticDamageProbe.MagicSummonSpawnCoroutinePatch", true)!
                .GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic)!;
            // Invoke the actual third-party transpiler; do not imitate its insertion.
            var observed = ((IEnumerable<CodeInstruction>)combatPatch.Invoke(null, new object[] { Copy(body), moveNext })!).ToList();
            AssertResult(observed, Run(observed), expectObserver: true);
            MethodInfo sort = AccessTools.Method(typeof(PatchProcessor), "GetSortedPatchMethods");
            foreach (bool saRegisteredFirst in new[] { true, false })
            {
                var patches = new[]
                {
                    new Patch(metadata, saRegisteredFirst ? 0 : 1, "sighsorry.SecondaryAttacks"),
                    new Patch(new HarmonyMethod(combatPatch), saRegisteredFirst ? 1 : 0, "Likhtenvald.CombatMeter")
                };
                var sorted = (List<MethodInfo>)sort.Invoke(null, new object[] { moveNext, patches })!;
                if (sorted.Count != 2 || sorted[0] != combatPatch || sorted[1] != patch)
                    throw new Exception("Harmony must sort CombatMeter before SA for either registration order");
            }
            Console.WriteLine("PASS actual CombatMeter 0.12.0 -> merged SA transpilers, Harmony ordering for both registration orders, preserved observer, success/denial branch execution with boundary doubles.");
        }

        // Verify all new non-public field-injection and patch targets exist in the original.
        var character = definition.MainModule.Types.Single(t => t.Name == "Character");
        if (!character.Fields.Any(f => f.Name == "m_nview" && f.IsFamily) ||
            !definition.MainModule.Types.Single(t => t.Name == "SpawnAbility").Fields.Any(f => f.Name == "m_weapon" && f.IsPrivate) ||
            !definition.MainModule.Types.Single(t => t.Name == "ZDOMan").Methods.Any(m => m.Name == "HandleDestroyedZDO" && m.IsPrivate))
            throw new Exception("Original private contract missing");
        Console.WriteLine($"PASS merged-DLL transpiler against original {game.ManifestModule.ModuleVersionId}: single guarded creation, valid continue, seven malformed IL rejections, original private contracts. No game execution.");
    }

    private static List<CodeInstruction> Copy(IEnumerable<CodeInstruction> input) => input.Select(c => new CodeInstruction(c)).ToList();
    private static bool IsVanillaInstantiate(CodeInstruction c) =>
        c.operand is MethodInfo m && m.Name == "Instantiate" && m.DeclaringType?.FullName == "UnityEngine.Object";
    private static bool IsObserver(CodeInstruction c) =>
        c.operand is MethodInfo m && m.Name == "ObserveSpawnCreated" && m.DeclaringType?.FullName == "DiagnosticDamageProbe.MagicAttributionProbe";
    private static int ContinueBranch(List<CodeInstruction> codes)
    {
        int error = codes.FindIndex(c => c.opcode == OpCodes.Ldstr && c.operand is string s && s.Contains("has null prefab, skipping spawn"));
        return codes.FindIndex(error, c => c.opcode == OpCodes.Br || c.opcode == OpCodes.Br_S);
    }

    private static void AssertResult(List<CodeInstruction> input, List<CodeInstruction> result, bool expectObserver)
    {
        int at = result.FindIndex(c => c.operand is MethodInfo m && m.DeclaringType?.FullName == "SecondaryAttacks.SubSummonSystem" && m.Name == "Instantiate");
        if (at < 0 || result.Count != input.Count + 6 || result.Any(IsVanillaInstantiate))
            throw new Exception("Expected a single replacement and only six guard instructions");
        if (result[at + 1].opcode != OpCodes.Dup || result[at + 2].opcode != OpCodes.Brtrue ||
            result[at + 3].opcode != OpCodes.Pop || result[at + 4].opcode != OpCodes.Br)
            throw new Exception("Missing creation-result guard");
        Label success = (Label)result[at + 2].operand, denied = (Label)result[at + 4].operand;
        if (!result[at + 5].labels.Contains(success) || !denied.Equals(input[ContinueBranch(input)].operand))
            throw new Exception("Guard must preserve successful continuation and the exact vanilla loop-continue target");
        int target = result.FindIndex(c => c.labels.Contains(denied));
        if (target <= at + 5 || !result.Skip(target).Take(4).Any(c => c.opcode == OpCodes.Add))
            throw new Exception("Denial must branch to the original loop increment");
        if (result.Count(IsObserver) != (expectObserver ? 1 : 0) ||
            (expectObserver && result.FindIndex(IsObserver) <= at + 4))
            throw new Exception("CombatMeter must observe successful creation exactly once, after the guard");
        int originalAt = input.FindIndex(IsVanillaInstantiate);
        for (int i = originalAt + 1; i < input.Count; i++)
        {
            CodeInstruction preserved = result[i + 6];
            if (preserved.opcode != input[i].opcode || !Equals(preserved.operand, input[i].operand) ||
                !input[i].labels.All(preserved.labels.Contains) || !input[i].blocks.SequenceEqual(preserved.blocks))
                throw new Exception("Changed downstream observer/vanilla instructions or control-flow metadata");
        }
        if (!input[originalAt].labels.All(result[originalAt].labels.Contains))
            throw new Exception("Creation entry labels must move to the added owner load");
        ExecuteDecision(result.Skip(at + 1).Take(4).ToArray(), success, denied, expectObserver);
    }

    private static readonly List<object> Observed = new();
    private static readonly List<object> PostSpawn = new();
    private static void Observe(object value) => Observed.Add(value);
    private static void FinishSpawn(object value) => PostSpawn.Add(value);

    private static void ExecuteDecision(CodeInstruction[] guard, Label success, Label denied, bool hasObserver)
    {
        // Execute the production-emitted branch sequence, remapping its labels.
        // Object/observer/post-spawn boundaries are doubles, not Unity methods.
        var method = new DynamicMethod("SpawnDecision", typeof(void), new[] { typeof(object) }, typeof(OriginalContract).Module, true);
        ILGenerator il = method.GetILGenerator();
        Label created = il.DefineLabel(), skipped = il.DefineLabel();
        il.Emit(OpCodes.Ldarg_0);
        foreach (CodeInstruction instruction in guard)
        {
            if (instruction.operand is Label branch)
            {
                if (!branch.Equals(success) && !branch.Equals(denied)) throw new Exception("Unexpected guard branch");
                il.Emit(instruction.opcode, branch.Equals(success) ? created : skipped);
            }
            else if (instruction.operand == null) il.Emit(instruction.opcode);
            else throw new Exception("Unexpected guard operand");
        }
        il.MarkLabel(created);
        if (hasObserver)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Call, typeof(OriginalContract).GetMethod(nameof(Observe), BindingFlags.Static | BindingFlags.NonPublic)!);
        }
        il.Emit(OpCodes.Call, typeof(OriginalContract).GetMethod(nameof(FinishSpawn), BindingFlags.Static | BindingFlags.NonPublic)!);
        il.Emit(OpCodes.Ret);
        il.MarkLabel(skipped);
        il.Emit(OpCodes.Ret);
        var invoke = (Action<object?>)method.CreateDelegate(typeof(Action<object?>));
        foreach (object? value in new object?[] { new object(), null })
        {
            Observed.Clear(); PostSpawn.Clear();
            invoke(value);
            int allowed = value == null ? 0 : 1;
            if (PostSpawn.Count != allowed || Observed.Count != (hasObserver ? allowed : 0) ||
                PostSpawn.Any(item => !ReferenceEquals(item, value)) || Observed.Any(item => !ReferenceEquals(item, value)))
                throw new Exception("Creation-result guard lost object identity or executed denied-spawn callbacks");
        }
    }
}
