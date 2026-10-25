using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using HarmonyLib;
using ItemData = ItemDrop.ItemData;

internal static class Program
{
    private const BindingFlags S = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags I = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private static int checks;
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeInt();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeHash(IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate float NativeTime();
    private static readonly NativeInt Offset = () => 0;
    private static readonly NativeHash Hash = _ => 0;
    private static readonly NativeTime Time = () => 10f;
    [DllImport("mono-2.0-bdwgc.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void mono_add_internal_call(string name, IntPtr method);
    private static bool Equality(UnityEngine.Object lhs, UnityEngine.Object rhs, ref bool __result) { __result = ReferenceEquals(lhs, rhs); return false; }
    private static bool Mine(ref bool __result) { __result = true; return false; }
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); checks++; }
    private static object New(Type t) => FormatterServices.GetUninitializedObject(t);
    private static Attack AttackFixture() { var a = (Attack)New(typeof(Attack)); a.m_requiresReload = true; return a; }
    private static int Main(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("SA.dll PoV.dll Managed BepInEx/core OptionalDependencyDirectory");
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            foreach (string dir in new[] { args[2], args[3], Path.GetDirectoryName(args[0])!, args[4] })
            { string file = Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll"); if (File.Exists(file)) return Assembly.LoadFrom(file); }
            return null;
        };
        try { return Run(args); } catch (Exception e) { System.Console.Error.WriteLine(e); return 1; }
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        Assembly bep = Assembly.LoadFrom(Path.Combine(args[3], "BepInEx.dll"));
        bep.GetType("BepInEx.Paths", true)!.GetMethod("SetExecutablePath", S)!.Invoke(null, new object[] {
            Path.Combine(Path.GetTempPath(), "sa-pov-smoke", "valheim.exe"), null!, args[2], new[] { args[2], args[3] } });
        Type threading = bep.GetType("BepInEx.ThreadingHelper", true)!;
        object queue = New(threading);
        threading.GetField("_invokeLock", I)!.SetValue(queue, new object());
        threading.GetField("<Instance>k__BackingField", S)!.SetValue(null, queue);
        mono_add_internal_call("UnityEngine.Object::GetOffsetOfInstanceIDInCPlusPlusObject", Marshal.GetFunctionPointerForDelegate(Offset));
        mono_add_internal_call("UnityEngine.Animator::StringToHash_Injected", Marshal.GetFunctionPointerForDelegate(Hash));
        mono_add_internal_call("UnityEngine.Time::get_time", Marshal.GetFunctionPointerForDelegate(Time));
        var fixture = new Harmony("sa.pov.smoke.fixture");
        var native = new Harmony("treextr.pathofvalheiman");
        Assembly mod = Assembly.LoadFrom(args[0]), pov = Assembly.LoadFrom(args[1]);
        Type bridge = mod.GetType("SecondaryAttacks.PathOfValheimanCompat", true)!;
        try
        {
            fixture.Patch(AccessTools.Method(typeof(UnityEngine.Object), "CompareBaseObjects"), prefix: new HarmonyMethod(typeof(Program), nameof(Equality)));
            Type magazine = pov.GetType("PassiveTree.CrossbowMagazine", true)!;
            fixture.Patch(magazine.GetMethod("Mine", S), prefix: new HarmonyMethod(typeof(Program), nameof(Mine)));
            foreach (string name in new[] { "Effects+Attack_GetAttackEitr", "ProjectileMods+Inventory_RemoveItem", "CrossbowRecoil+Burst", "AmmoVariety+Fired", "ProjectileMods+ClassifyBeforeDamage", "ProjectileMods+Attack_Start", "ProjectileMods+Projectile_Setup", "SecondaryAttacks+Humanoid_StartAttack", "SecondaryAttacks+Attack_Start_Origin", "ChargedAttacks+ReleaseAttack", "Blasts+Attack_OnAttackTrigger_Area", "ProjectileMods+VolleyResources", "CrossbowMagazine+Shot", "CrossbowMagazine+KeepLoaded", "CrossbowMagazine+Position", "CrossbowMagazine+Fill", "CrossbowMagazine+Restore", "CrossbowMagazine+Gate", "CrossbowMagazine+NextShot" })
                native.CreateClassProcessor(pov.GetType("PassiveTree." + name, true)).Patch();
            var config = new ConfigFile(Path.Combine(Path.GetTempPath(), "sa-pov-smoke-unused.cfg"), false) { SaveOnConfigSet = false };
            pov.GetType("PassiveTree.Plugin", true)!.GetField("DisableVanillaSkills", S)!.SetValue(null, config.Bind("test", "freeze", false));
            Type apiType = bridge.GetNestedType("Api", BindingFlags.NonPublic)!;
            object api = Activator.CreateInstance(apiType, I, null, new object[] { pov }, null)!;
            Check(api != null, "merged bridge binds all registered original PoV 5.3.2 contracts on original Mono");
            Check(apiType.GetField("BurstFor", I)!.GetValue(api) == null, "new contract has no old burst state dependency");
            Delegate Bound(string name) => (Delegate)apiType.GetField(name, I)!.GetValue(api)!;
            var weapon = new ItemData { m_shared = new ItemData.SharedData { m_skillType = Skills.SkillType.Crossbows, m_attack = AttackFixture() }, m_customData = new System.Collections.Generic.Dictionary<string, string>() };
            Check((bool)Bound("MagazineIs").DynamicInvoke(weapon)!, "original crossbow classifier");
            // Seed through PoV's writer; the smoke does not reimplement its saved keys or anti-clone revisions.
            magazine.GetMethod("Write", S)!.Invoke(null, new object[] { weapon, 2, 3, "BoltIron", 1 });
            Check((int)Bound("MagazineRemaining").DynamicInvoke(weapon)! == 2, "original magazine writer/read contract");
            var player = (Player)New(typeof(Player));
            AccessTools.Field(typeof(Player), "m_localPlayer").SetValue(null, player);
            var attack = AttackFixture();
            AccessTools.Field(typeof(Attack), "m_character").SetValue(attack, player);
            AccessTools.Field(typeof(Attack), "m_weapon").SetValue(attack, weapon);
            attack.m_requiresReload = true;
            var beginPosition = (Func<Attack, IDisposable>)Bound("BeginPosition");
            Type hit = pov.GetType("PassiveTree.HitContext", true)!;
            object Tag() => hit.GetProperty("Current", S)!.GetValue(null)!;
            object oldTag = Tag();
            using (beginPosition(attack)) Check(Convert.ToInt32(Tag()) == 0, "middle loaded shot has no first/last flag");
            Check(Tag().Equals(oldTag), "original Pop restores enclosing tags");
            object?[] shot = { attack, null };
            Check((bool)Bound("MagazineShot").DynamicInvoke(shot)!, "original private Shot prefix callable");
            Type projectiles = pov.GetType("PassiveTree.ProjectileMods", true)!;
            projectiles.GetField("ammoSaved", S)!.SetValue(null, false);
            Check(!(bool)Bound("MagazineKeepLoaded").DynamicInvoke(player)!, "original KeepLoaded retains partial magazine");
            Check((int)Bound("MagazineRemaining").DynamicInvoke(weapon)! == 1, "original KeepLoaded consumes one position");
            magazine.GetField("firing", S)!.SetValue(null, shot[1]);
            object?[] repeated = { attack, null };
            Check(!(bool)Bound("MagazineShot").DynamicInvoke(repeated)!, "original shotDone rejects duplicate trigger");
            magazine.GetField("firing", S)!.SetValue(null, repeated[1]);
            using (beginPosition(attack)) Check(Convert.ToInt32(Tag()) == 0, "delayed shot keeps trigger position after magazine update");
            using (beginPosition(AttackFixture())) { } // classifier must tolerate no weapon.
            System.Console.WriteLine($"PASS: {checks} original PoV/Mono bridge checks; native identity, owner and clock are fixtures. No live game.");
            return 0;
        }
        finally { native.UnpatchSelf(); fixture.UnpatchSelf(); }
    }
}
