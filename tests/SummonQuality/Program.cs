using System.Collections;
using System.Reflection;
using System.Runtime.Loader;
using Mono.Cecil;

if (args.Length != 2)
    throw new ArgumentException("Expected final SecondaryAttacks.dll and Valheim game directory.");

string assemblyPath = Path.GetFullPath(args[0]);
string gamePath = Path.GetFullPath(args[1]);
string[] roots = { Path.GetDirectoryName(assemblyPath)!, Path.Combine(gamePath, "valheim_Data", "Managed"), Path.Combine(gamePath, "BepInEx", "core") };
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    foreach (string root in roots)
    {
        string path = Path.Combine(root, name.Name + ".dll");
        if (File.Exists(path)) return context.LoadFromAssemblyPath(path);
    }
    return null;
};

Assembly mod = AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
Type ModType(string name) => mod.GetType("SecondaryAttacks." + name, true)!;
const BindingFlags methods = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
// The loader needs BepInEx's config-root string even though this harness only
// parses in-memory YAML. Set it in this process without loading/writing user files.
Type paths = Assembly.LoadFrom(Path.Combine(gamePath, "BepInEx", "core", "BepInEx.dll")).GetType("BepInEx.Paths", true)!;
paths.GetProperty("ConfigPath", methods)!.SetValue(null, Path.Combine(Path.GetTempPath(), "SecondaryAttacks-SummonQuality-tests"));
int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    checks++;
}

Type rawType = ModType("BloodMagicWeaponConfig");
MethodInfo parse = ModType("SecondaryAttackConfigLoader").GetMethod("TryParseDictionary", methods)!.MakeGenericMethod(rawType);
object domain = Enum.Parse(ModType("SecondaryAttackYamlDomainId"), "BloodMagic");
MethodInfo normalize = ModType("SecondaryAttackMagicSummonNormalizer").GetMethod("Normalize", methods)!;
Type system = ModType("MagicSummonQualityPresetSystem");
MethodInfo buildRules = system.GetMethod("BuildQualityRules", methods)!;

// Parse complete root entries through the production loader. Old keys must not
// discard siblings such as lifetime/spawn choices, or influence summon growth.
foreach (string preset in new[] { "countByQuality", "levelByQuality" })
foreach (int? formerLimit in new int?[] { null, 4, 6, 0, 99 })
{
    string oldKey = formerLimit.HasValue ? $"    maxQuality: {formerLimit}\n" : "";
    string yaml = $"StaffSkeleton:\n  enabled: true\n  summon:\n    qualityPreset: {preset}\n{oldKey}    lifetimeSeconds: 900\n    spawnChoices:\n      - sourcePrefab: Charred_Melee\n        clonePrefab: TestSkeleton\n";
    object?[] parseArgs = { domain, yaml, null };
    Check((bool)parse.Invoke(null, parseArgs)!, "YAML parsing failed");
    IDictionary raw = (IDictionary)parseArgs[2]!;
    Check(raw.Count == 1 && raw.Contains("StaffSkeleton"), "Old maxQuality discarded the staff entry");
    IDictionary normalized = (IDictionary)normalize.Invoke(null, new[] { raw })!;
    object config = normalized["StaffSkeleton"]!;
    Check((int?)config.GetType().GetProperty("LifetimeSeconds")!.GetValue(config) == 900, "Lifetime override lost");
    IList summons = (IList)config.GetType().GetProperty("Summons")!.GetValue(config)!;
    Check(summons.Count == 1, "Spawn choice lost");
    Check((string)summons[0]!.GetType().GetProperty("ClonePrefab")!.GetValue(summons[0])! == "TestSkeleton", "Clone identity changed");
    IList rules = (IList)buildRules.Invoke(null, new[] { normalized })!;
    Check(rules.Count == 1, "Expected one quality rule");
    object rule = rules[0]!;
    foreach ((int quality, int expected) in new[] { (-1, 1), (1, 1), (4, 4), (5, 5), (6, 6), (10, 10), (11, 10), (int.MaxValue, 10) })
    {
        int level = (int)rule.GetType().GetMethod("GetSummonLevel", methods)!.Invoke(rule, new object[] { quality })!;
        int count = (int)rule.GetType().GetMethod("GetMaxInstances", methods)!.Invoke(rule, new object[] { quality })!;
        Check(level == (preset == "levelByQuality" ? expected : 1), $"{preset}, old limit {formerLimit}, quality {quality}: level {level}");
        Check(count == (preset == "countByQuality" ? expected : 1), $"{preset}, old limit {formerLimit}, quality {quality}: count {count}");
    }
    Console.WriteLine($"PASS {preset}: legacy maxQuality {formerLimit?.ToString() ?? "omitted"}, YAML siblings and quality boundaries");
}

// Inspect the actual merged DLL, not source strings. No remaining runtime read
// of the retired option or item crafting-limit write may bypass this policy.
using AssemblyDefinition definition = AssemblyDefinition.ReadAssembly(assemblyPath);
List<string> craftingWrites = new();
List<string> retiredOptionReads = new();
IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] { t }.Concat(AllTypes(t.NestedTypes)));
foreach (TypeDefinition type in AllTypes(definition.MainModule.Types).Where(t => t.Namespace == "SecondaryAttacks" || t.DeclaringType?.Namespace == "SecondaryAttacks"))
foreach (MethodDefinition method in type.Methods.Where(m => m.HasBody))
foreach (var instruction in method.Body.Instructions)
{
    if (instruction.OpCode == Mono.Cecil.Cil.OpCodes.Stfld && instruction.Operand is FieldReference field && field.Name == "m_maxQuality")
        craftingWrites.Add(method.FullName);
    if (instruction.Operand is MethodReference call && call.DeclaringType.Name == "MagicSummonOverrideConfig" && call.Name == "get_MaxQuality")
        retiredOptionReads.Add(method.FullName);
}
Check(craftingWrites.Count == 0, "Mod writes crafting quality limits: " + string.Join(", ", craftingWrites));
Check(retiredOptionReads.Count == 0, "Runtime reads retired maxQuality: " + string.Join(", ", retiredOptionReads));
RemovalRegression.Run(mod, Check);
Console.WriteLine($"PASS {checks} checks against the merged mod and original game references; no Unity/game execution.");
