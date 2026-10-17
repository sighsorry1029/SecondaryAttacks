using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using SecondaryAttacks;
using EpicLoot.MagicItemEffects;
using EpicLoot.Magic.MagicItemEffects;

// External boundaries only; payment, emission ordering, reflection and scopes
// are the linked production code. The PoV formulas are deterministic doubles.
namespace BepInEx.Configuration { public class ConfigEntry<T> { public T Value = default!; } }
namespace UnityEngine
{
    public class Component
    {
        public readonly Dictionary<Type, object> Components = new Dictionary<Type, object>();
        public T? GetComponent<T>() where T : class => Components.TryGetValue(typeof(T), out object value) ? (T)value : null;
        public Component? GetComponent(Type t) => Components.TryGetValue(t, out object value) ? (Component)value : null;
    }
}
public class Character : UnityEngine.Component { }
public class SEMan { [MethodImpl(MethodImplOptions.NoInlining)] public void ModifyAttack() { } }
public class Projectile : UnityEngine.Component { [MethodImpl(MethodImplOptions.NoInlining)] public void Setup() { } }
public class ZDO { public long Tamer; public long GetLong(string name, long fallback) => Tamer; }
public class ZNetView : UnityEngine.Component { public bool Valid = true; public ZDO Zdo = new ZDO(); public bool IsValid() => Valid; public ZDO GetZDO() => Zdo; }
namespace SecondaryAttacks
{
    internal static class SubSummonSystem
    {
        internal static bool Marked; internal static Player? Owner;
        internal static bool TryResolveSummoner(Character? c, out Player? p) { p = Owner; return Marked; }
    }
}
namespace PassiveTree
{
    public class ProjMods : UnityEngine.Component { public int Pierce, Chain, Bounce, Volley; }
    public class Plugin { public static BepInEx.Configuration.ConfigEntry<bool> DisableVanillaSkills = new BepInEx.Configuration.ConfigEntry<bool>(); }
    public static class SecondaryAttacks
    {
        public static Attack? producing;
        public static bool Secondary(Attack a) => a.IsSecondary;
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        public static class Humanoid_StartAttack { public static void Prefix() { } }
        [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
        public static class Attack_Start_Origin { public static void Prefix() { } }
    }
    public static class ChargedAttacks
    {
        public static Attack? producing;
        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
        public static class ReleaseAttack { public static void Prefix() { } }
    }
    public static class Blasts
    {
        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
        public static class Attack_OnAttackTrigger_Area { public static void Prefix() { } }
    }
    public static class WeaponAffixes { public static ItemDrop.ItemData? Override; }
    public static class TamingHunting
    {
        public static Player? Fallback;
        [MethodImpl(MethodImplOptions.NoInlining)] public static Player? Keeper(UnityEngine.Component c) => Fallback;
    }
    public static class Effects
    {
        [HarmonyPatch(typeof(Attack), nameof(Attack.GetAttackEitr), new[] { typeof(Character), typeof(ItemDrop.ItemData) })]
        public static class Attack_GetAttackEitr
        {
            public static void Postfix(Character character, ItemDrop.ItemData weapon, ref float __result) { if (character == Player.m_localPlayer) __result *= 0.5f; }
        }
    }
    public static class ProjectileMods
    {
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyAttack))]
        public static class ClassifyBeforeDamage { public static void Prefix() { } }
        [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
        public static class Attack_Start { public static void Postfix() { } }
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.Setup))]
        public static class Projectile_Setup { public static void Prefix() { } public static void Postfix() { } }
        public static Attack? creatingProjectile, lastAttack, burstFor;
        public static int addedShots, nativeShots, recoverable, shotIndex, volleyNow;
        public static bool usingAmmo, ammoSaved, inBurst, countingBurst;
        public static float ownSpread;
        public static Queue<bool> Saved = new Queue<bool>();
        public static int Recovered, Emissions;
        private static int serial;
        public static void OpenBurst(int native, float spread) { nativeShots = native; ownSpread = spread; inBurst = true; countingBurst = false; shotIndex = 0; volleyNow = ++serial; }
        public static void ObserveEmission(Attack a)
        {
            if (!PathOfValheimanCompat.Enabled) return;
            if (creatingProjectile != a || SecondaryAttacks.producing != a || ChargedAttacks.producing != a || WeaponAffixes.Override != a.m_weapon)
                throw new Exception("actual emission lost source attack/weapon");
            Emissions++;
            for (int i = 0; i < a.m_projectiles && recoverable > 0; i++) { recoverable--; Recovered++; }
        }
        [HarmonyPatch(typeof(Inventory), nameof(Inventory.RemoveItem))]
        public static class Inventory_RemoveItem
        {
            public static bool Prefix(ref bool __result)
            {
                if (!usingAmmo || Saved.Count == 0 || !Saved.Dequeue()) return true;
                __result = true; ammoSaved = true; return false;
            }
        }
    }
    public static class CrossbowRecoil
    {
        public static int Calls;
        [HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst))]
        public static class Burst { [MethodImpl(MethodImplOptions.NoInlining)] public static void Postfix(Attack __instance) { Calls++; } }
    }
    public static class AmmoVariety
    {
        public static int Calls;
        [HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst))]
        public static class Fired { [MethodImpl(MethodImplOptions.NoInlining)] public static void Postfix(Attack __instance) { Calls++; } }
    }
}
internal static class PoVTests
{
    private static int checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
    private static Attack New(int ammo = 30, int extra = 0, bool crossbow = false)
    {
        var a = new Attack { m_character = new Player() };
        Player.m_localPlayer = (Player)a.m_character;
        ((Player)a.m_character).m_weaponLoaded = a.m_weapon;
        a.m_character.Inventory.Items.Add(new ItemDrop.ItemData { m_stack = ammo });
        SecondaryAttackRuntimeContext.Active[a] = new ActiveSecondaryAttack();
        a.m_weapon.m_shared.m_attack = new Attack();
        SecondaryAttackRuntimeFacade.Definitions[a.m_weapon] = Definition(a);
        MultiShot.Procs.Clear(); AmmoConservation.Procs.Clear();
        PassiveTree.ProjectileMods.Saved.Clear(); PassiveTree.ProjectileMods.Recovered = 0; PassiveTree.ProjectileMods.Emissions = 0;
        PassiveTree.CrossbowRecoil.Calls = PassiveTree.AmmoVariety.Calls = 0;
        PassiveTree.ProjectileMods.lastAttack = a; PassiveTree.ProjectileMods.addedShots = extra;
        PassiveTree.ProjectileMods.nativeShots = 1; PassiveTree.ProjectileMods.burstFor = crossbow ? a : null;
        if (crossbow) a.m_projectileBursts += extra; else a.m_projectiles += extra;
        PathOfValheimanCompat.RegisterAttack(a, Definition(a));
        return a;
    }
    private static SecondaryAttackDefinition Definition(Attack a) => SecondaryAttackRuntimeContext.Active[a].Definition;
    private static bool Fire(Attack a) => ProjectileRuntimeSystem.TryFireBurstShot(a, Definition(a), out _, out _);
    internal static void Run()
    {
        Check(!PathOfValheimanCompat.Enabled, "absent PoV must remain inactive");
        var h = new Harmony(PathOfValheimanCompat.PluginGuid);
        foreach (Type t in new[] { typeof(PassiveTree.Effects.Attack_GetAttackEitr), typeof(PassiveTree.ProjectileMods.Inventory_RemoveItem), typeof(PassiveTree.CrossbowRecoil.Burst), typeof(PassiveTree.AmmoVariety.Fired) }) h.CreateClassProcessor(t).Patch();
        foreach (Type t in new[] { typeof(PassiveTree.ProjectileMods.ClassifyBeforeDamage), typeof(PassiveTree.ProjectileMods.Attack_Start), typeof(PassiveTree.ProjectileMods.Projectile_Setup), typeof(PassiveTree.SecondaryAttacks.Humanoid_StartAttack), typeof(PassiveTree.SecondaryAttacks.Attack_Start_Origin), typeof(PassiveTree.ChargedAttacks.ReleaseAttack), typeof(PassiveTree.Blasts.Attack_OnAttackTrigger_Area) }) h.CreateClassProcessor(t).Patch();
        new Harmony("test.eitr").Patch(AccessTools.Method(typeof(Attack), "GetAttackEitr", Type.EmptyTypes), prefix: new HarmonyMethod(typeof(AttackGetEitrConfiguredCostPatch), "Prefix"));
        BepInEx.Bootstrap.Chainloader.PluginInfos[PathOfValheimanCompat.PluginGuid] = new BepInEx.PluginInfo { Instance = new PassiveTree.Plugin(), Metadata = new BepInEx.Metadata { Version = new Version(99, 1, 0) } };
        PathOfValheimanCompat.Initialize();
        Check(PathOfValheimanCompat.Enabled, "compatible contract must not be version gated");
        var projectile = new Projectile();
        var modifiers = new PassiveTree.ProjMods { Pierce = 2, Chain = 3, Bounce = 1, Volley = 42 };
        projectile.Components[typeof(PassiveTree.ProjMods)] = modifiers;
        PathOfValheimanCompat.UsePresetCollision(projectile);
        Check(modifiers.Pierce == 0 && modifiers.Chain == 0 && modifiers.Bounce == 0 && modifiers.Volley == 42, "preset continuation has one owner while volley filtering survives");
        var a = New(extra: 2);
        Check(PathOfValheimanCompat.ExtraAmmo(a) == 2 && PassiveTree.ProjectileMods.lastAttack == null, "adopt bow extras and detach vanilla billing");
        Check(Fire(a) && a.m_character.Inventory.Total == 27 && PassiveTree.ProjectileMods.Recovered == 3, "bow extras paid/recoverable once");
        a = New(extra: 2, crossbow: true);
        Check(PathOfValheimanCompat.ExtraBursts(a) == 2 && a.m_projectileBursts == 1 && PassiveTree.ProjectileMods.burstFor == null, "adopt crossbow extra bursts");
        ProjectileRuntimeSystem.FireBurstFire(a, Definition(a));
        Check(ProjectileRuntimeSystem.Scheduled == 4 && a.m_character.Inventory.Total == 29, "schedule 3+2 actual shots without precharging repeats");
        a = New(extra: 1);
        MultiShot.Procs.Enqueue(true); AmmoConservation.Procs.Enqueue(true);
        // Six projectiles, four paid units, then one EpicLoot refund.
        foreach (bool saved in new[] { true, false, false, true, false, false }) PassiveTree.ProjectileMods.Saved.Enqueue(saved);
        Check(Fire(a) && a.m_character.Inventory.Total == 27 && PassiveTree.ProjectileMods.Recovered == 3, "combined refunds cannot create recoverable ammo");
        Check(ProjectileRuntimeSystem.Shots[ProjectileRuntimeSystem.Shots.Count - 1].Count == 6, "PoV extra shot and Epic multiplier both emitted");
        a = New(extra: 2);
        for (int i = 0; i < 3; i++) PassiveTree.ProjectileMods.Saved.Enqueue(true);
        AmmoConservation.Procs.Enqueue(true);
        int refunds = AmmoConservation.Calls;
        Check(Fire(a) && a.m_character.Inventory.Total == 30 && PassiveTree.ProjectileMods.Recovered == 0 && AmmoConservation.Calls == refunds, "fully saved shot cannot refund or recover unpaid ammo");
        a = New(ammo: 2, extra: 2);
        int shots = ProjectileRuntimeSystem.Shots.Count;
        Check(!Fire(a) && a.m_character.Inventory.Total == 2 && ProjectileRuntimeSystem.Shots.Count == shots && a.ResourcePayments == 0, "shortage has no partial payment/emission");
        a = New(extra: 2);
        a.m_character.Inventory.Items[0].ValidPayload = false;
        Check(!Fire(a) && a.m_character.Inventory.Total == 30 && a.ResourcePayments == 0, "invalid payload costs nothing");
        a = New(extra: 2);
        var oldAttack = new Attack();
        PassiveTree.SecondaryAttacks.producing = oldAttack;
        PassiveTree.ChargedAttacks.producing = oldAttack;
        PassiveTree.ProjectileMods.creatingProjectile = oldAttack;
        PassiveTree.ProjectileMods.recoverable = 17;
        PassiveTree.WeaponAffixes.Override = oldAttack.m_weapon;
        ProjectileRuntimeSystem.ThrowSpawn = true;
        try { Fire(a); } catch (InvalidOperationException) { }
        finally { ProjectileRuntimeSystem.ThrowSpawn = false; }
        Check(PassiveTree.SecondaryAttacks.producing == oldAttack && PassiveTree.ChargedAttacks.producing == oldAttack && PassiveTree.ProjectileMods.creatingProjectile == oldAttack && PassiveTree.ProjectileMods.recoverable == 17 && PassiveTree.WeaponAffixes.Override == oldAttack.m_weapon, "exception restores enclosing PoV contexts");
        using (PathOfValheimanCompat.BeginAttack(a))
        {
            using (PathOfValheimanCompat.BeginAttack(a, true)) { }
            Check(PassiveTree.SecondaryAttacks.producing == a && PassiveTree.ProjectileMods.creatingProjectile == oldAttack, "nested scope restores enclosing state");
        }
        Check(PassiveTree.SecondaryAttacks.producing == oldAttack, "outer scope restored");
        PassiveTree.CrossbowRecoil.Burst.Postfix(a); PassiveTree.AmmoVariety.Fired.Postfix(a);
        Check(PassiveTree.CrossbowRecoil.Calls == 0 && PassiveTree.AmmoVariety.Calls == 0, "empty callbacks suppressed");
        PathOfValheimanCompat.AfterBurstShot(a);
        Check(PassiveTree.CrossbowRecoil.Calls == 1 && PassiveTree.AmmoVariety.Calls == 1, "actual shot callbacks called once");
        float cost = 8;
        PathOfValheimanCompat.ModifyConfiguredEitr(a, a.m_character, a.m_weapon, ref cost);
        Check(cost == 4, "configured Eitr follows original scalar callback");
        a = New();
        a.m_weapon.m_shared.m_ammoType = "";
        ((ProjectileSecondaryBehavior)Definition(a).Behavior).Preset = SecondaryAttackPreset.Other;
        SecondaryAttackRuntimeContext.Active.Remove(a); // Attack.Start has not registered it yet.
        Check(a.GetAttackEitr() == 5, "equal primary/secondary Eitr does not charge unsupported extra projectiles");
        a.m_attackEitr = 8;
        Check(a.GetAttackEitr() == 4, "different configured Eitr uses the same scalar path");
        a.m_attackEitr = 10; a.IsSecondary = false;
        Check(a.GetAttackEitr() == 20, "primary keeps PoV's public overload and extra-shot cost");
        a = New();
        ((ProjectileSecondaryBehavior)Definition(a).Behavior).Preset = SecondaryAttackPreset.Other;
        PassiveTree.ProjectileMods.recoverable = 5;
        PassiveTree.ProjectileMods.inBurst = PassiveTree.ProjectileMods.countingBurst = true;
        int volley;
        using (PathOfValheimanCompat.BeginProjectile(a))
        {
            volley = PassiveTree.ProjectileMods.volleyNow;
            Check(PassiveTree.ProjectileMods.recoverable == 0 && !PassiveTree.ProjectileMods.inBurst && !PassiveTree.ProjectileMods.countingBurst, "fixed delayed projectile cannot inherit ammo recovery or spread");
        }
        using (PathOfValheimanCompat.BeginProjectile(a)) Check(PassiveTree.ProjectileMods.volleyNow == volley, "scheduled fixed projectiles retain their volley identity");
        Check(PassiveTree.ProjectileMods.recoverable == 5 && PassiveTree.ProjectileMods.inBurst, "fixed emission restores unrelated projectile state");
        EpicLootCompat.Dispose();
        a = New();
        ProjectileRuntimeSystem.FireBurstFire(a, Definition(a));
        Check(a.m_character.Resets == 0, "PoV-only first Burst shot defers reload");
        ProjectileRuntimeSystem.Finish(a);
        Check(a.m_character.Resets == 1, "PoV-only Burst resets reload at completion");
        var component = new Character(); var view = new ZNetView();
        component.Components[typeof(ZNetView)] = view; component.Components[typeof(Character)] = component;
        var fallback = new Player(); PassiveTree.TamingHunting.Fallback = fallback;
        SubSummonSystem.Marked = true; SubSummonSystem.Owner = null;
        Check(PassiveTree.TamingHunting.Keeper(component) == null, "offline summoner prevents stranger fallback");
        SubSummonSystem.Owner = Player.m_localPlayer;
        Check(PassiveTree.TamingHunting.Keeper(component) == Player.m_localPlayer, "known summoner selected");
        view.Zdo.Tamer = 456;
        Check(PassiveTree.TamingHunting.Keeper(component) == fallback, "explicit PoV owner takes precedence");
        view.Zdo.Tamer = 0; view.Valid = false;
        Check(PassiveTree.TamingHunting.Keeper(component) == fallback, "invalid view preserves original handling");
        PathOfValheimanCompat.Dispose();
        Check(!PathOfValheimanCompat.Enabled && PassiveTree.TamingHunting.Keeper(component) == fallback, "dispose unpatches optional bridge");
        h.Unpatch(AccessTools.Method(typeof(Inventory), nameof(Inventory.RemoveItem)), AccessTools.Method(typeof(PassiveTree.ProjectileMods.Inventory_RemoveItem), "Prefix"));
        PathOfValheimanCompat.Initialize();
        Check(!PathOfValheimanCompat.Enabled, "missing callback registration rejects bridge");
        h.UnpatchSelf();
        Console.WriteLine($"PASS: {checks} PoV production bridge assertions; deterministic boundaries, no game execution.");
    }
}
