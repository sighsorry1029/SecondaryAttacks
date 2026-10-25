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
        public static Attack? creatingProjectile, lastAttack;
#if POV_MAGAZINE
        public static ItemDrop.ItemData? ammoWeapon, ObservedAmmoWeapon;
        public static bool ThrowPayment;
        public static int ResourceCalls;
        [HarmonyPatch(typeof(Attack), nameof(Attack.GetProjectileSpawnPoint))]
        public static class VolleyResources
        {
            [MethodImpl(MethodImplOptions.NoInlining)] public static void Postfix(Attack __instance)
            {
                if (creatingProjectile == __instance && __instance.m_character == Player.m_localPlayer)
                { ResourceCalls++; __instance.ResourcePayments++; }
            }
        }
#else
        public static Attack? burstFor;
#endif
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
#if POV_MAGAZINE
                if (usingAmmo) ObservedAmmoWeapon = ammoWeapon;
                if (ThrowPayment) throw new InvalidOperationException("payment failure");
#endif
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
#if POV_MAGAZINE
    [Flags] public enum HitTag { None = 0, First = 2048, Last = 4096, Other = 8192 }
    public static class HitContext
    {
        public static HitTag Current;
        public static HitTag Push(HitTag t) { var previous = Current; Current |= t; return previous; }
        public static void Pop(HitTag t) { Current = t; }
    }
    // Only external state/side effects are doubled. The bridge and the caller's
    // payment/reload code are compiled verbatim from production sources.
    public static class CrossbowMagazine
    {
        public static Attack? firing;
        public static readonly HashSet<Attack> Done = new HashSet<Attack>();
        public static int Calls, Now, MeasuredAt, Fills;
        public static bool ThrowKeep;
        private static bool restoring;
        public static bool Is(ItemDrop.ItemData w) => w.Crossbow;
        public static int Remaining(ItemDrop.ItemData w) => w.Magazine;
        public static HitTag ShotPosition(ItemDrop.ItemData w) => (w.Used == 0 ? HitTag.First : 0) | (w.Magazine <= 1 ? HitTag.Last : 0);
        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
        public static class Shot
        {
            public static bool Prefix(Attack __instance, out Attack? __state)
            {
                __state = firing;
                firing = __instance.m_requiresReload && __instance.m_character == Player.m_localPlayer && Is(__instance.m_weapon) ? __instance : null;
                if (firing != null && Done.Contains(firing)) { firing = null; return false; }
                return true;
            }
            public static void Finalizer(Attack? __state) { firing = __state; }
        }
        [HarmonyPatch(typeof(Player), nameof(Player.ResetLoadedWeapon))]
        public static class KeepLoaded
        {
            public static bool Prefix(Player __instance)
            {
                if (firing == null || firing.m_character != __instance) return true;
                if (ThrowKeep) throw new InvalidOperationException("magazine failure");
                Calls++; MeasuredAt = Now; Done.Add(firing);
                if (!ProjectileMods.ammoSaved) firing.m_weapon.Magazine = Math.Max(0, firing.m_weapon.Magazine - 1);
                firing.m_weapon.Used++;
                return firing.m_weapon.Magazine == 0;
            }
        }
        [HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst))]
        public static class Position
        {
            public static void Prefix(Attack __instance, out HitTag __state) { __state = HitContext.Push(Is(__instance.m_weapon) ? ShotPosition(__instance.m_weapon) : HitTag.None); }
            public static void Finalizer(HitTag __state) { HitContext.Pop(__state); }
        }
        [HarmonyPatch(typeof(Player), nameof(Player.SetWeaponLoaded))]
        public static class Fill
        {
            public static void Prefix(Player __instance, ItemDrop.ItemData weapon, out bool __state) { __state = !restoring && Is(weapon) && __instance.m_weaponLoaded != weapon; }
            public static void Postfix(ItemDrop.ItemData weapon, bool __state) { if (__state) { Fills++; weapon.Magazine = 3; weapon.Used = 0; } }
        }
        [HarmonyPatch(typeof(Player), nameof(Player.UpdateWeaponLoading))]
        public static class Restore
        {
            public static void Prefix(Player __instance, ItemDrop.ItemData weapon)
            {
                if (!Is(weapon) || weapon.Magazine <= 0 || __instance.m_weaponLoaded == weapon) return;
                restoring = true;
                try { __instance.SetWeaponLoaded(weapon); } finally { restoring = false; }
            }
        }
        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
        public static class Gate { public static void Prefix() { } public static void Postfix() { } }
        [HarmonyPatch(typeof(Player), nameof(Player.HaveQueuedChain))]
        public static class NextShot
        {
            [MethodImpl(MethodImplOptions.NoInlining)] public static void Postfix(Player __instance, ref bool __result)
            { if (__instance.m_weaponLoaded?.Magazine > 0) __result = true; }
        }
    }
#endif
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
        PassiveTree.ProjectileMods.nativeShots = 1;
#if POV_MAGAZINE
        if (crossbow)
        {
            a.m_weapon.Crossbow = true;
            a.m_weapon.Magazine = 1 + extra;
            PassiveTree.ProjectileMods.lastAttack = null;
            PassiveTree.ProjectileMods.addedShots = 0;
        }
        else a.m_projectiles += extra;
        a.m_character.m_currentAttack = a;
#else
        PassiveTree.ProjectileMods.burstFor = crossbow ? a : null;
        if (crossbow) a.m_projectileBursts += extra; else a.m_projectiles += extra;
#endif
        PathOfValheimanCompat.RegisterAttack(a, Definition(a));
        return a;
    }
    private static SecondaryAttackDefinition Definition(Attack a) => SecondaryAttackRuntimeContext.Active[a].Definition;
    private static bool Fire(Attack a) => ProjectileRuntimeSystem.TryFireBurstShot(a, Definition(a), out _, out _);
#if POV_MAGAZINE
    private static void MagazineTests()
    {
        var a = New(extra: 2, crossbow: true);
        var p = (Player)a.m_character;
        var previous = new Attack();
        PassiveTree.CrossbowMagazine.firing = previous;
        PassiveTree.ProjectileMods.ammoSaved = true;
        PassiveTree.CrossbowMagazine.Now = 10;
        int before = PassiveTree.CrossbowMagazine.Calls;
        ProjectileRuntimeSystem.FireBurstFire(a, Definition(a));
        Check(a.m_weapon.Magazine == 2 && p.Resets == 0 && PassiveTree.CrossbowMagazine.Calls == before + 1, "first successful shot owns one trigger magazine use");
        Check(PassiveTree.CrossbowMagazine.firing == previous && PassiveTree.ProjectileMods.ammoSaved, "first-shot magazine scope restores outer context");
        Check(!p.HaveQueuedChain(), "magazine auto-chain cannot interrupt a pending configured Burst");
        PassiveTree.CrossbowMagazine.Now = 30;
        PassiveTree.ProjectileMods.Saved.Enqueue(true);
        Check(Fire(a), "saved delayed shot emitted");
        ProjectileRuntimeSystem.Finish(a); ProjectileRuntimeSystem.Finish(a);
        Check(a.m_weapon.Magazine == 2 && PassiveTree.CrossbowMagazine.Calls == before + 1 && PassiveTree.CrossbowMagazine.MeasuredAt == 10, "last-shot saving and duplicate completion do not change first-trigger accounting or timing");
        Check(p.HaveQueuedChain() && p.Resets == 0, "remaining magazine resumes native chaining after completion");
        PassiveTree.HitContext.Current = PassiveTree.HitTag.Other;
        using (PathOfValheimanCompat.BeginEmission(a))
            Check(PassiveTree.HitContext.Current == (PassiveTree.HitTag.Other | PassiveTree.HitTag.First), "delayed emission retains trigger's first-shot position");
        Check(PassiveTree.HitContext.Current == PassiveTree.HitTag.Other, "hit tags restore enclosing context");

        a = New(crossbow: true); p = (Player)a.m_character;
        Check(Fire(a) && a.m_weapon.Magazine == 0 && p.Resets == 0, "last magazine position consumed before physical unload");
        using (PathOfValheimanCompat.BeginEmission(a))
            Check((PassiveTree.HitContext.Current & PassiveTree.HitTag.Last) != 0, "last-shot position survives deferred emission");
        ProjectileRuntimeSystem.Finish(a); ProjectileRuntimeSystem.Finish(a);
        Check(p.Resets == 1, "exhausted magazine unloads once");

        a = New(crossbow: true); p = (Player)a.m_character;
        PassiveTree.ProjectileMods.Saved.Enqueue(true);
        Check(Fire(a) && Fire(a), "saved trigger and paid repeat emitted");
        ProjectileRuntimeSystem.Finish(a);
        Check(a.m_weapon.Magazine == 1 && p.Resets == 0, "trigger conservation remains owned by first shot; repeat pays inventory but not another trigger");

        a = New(crossbow: true); p = (Player)a.m_character;
        Fire(a);
        var other = new ItemDrop.ItemData(); p.m_weaponLoaded = other;
        int consumed = SecondaryAttackManager.PersistedConsumed;
        ProjectileRuntimeSystem.Finish(a);
        Check(p.m_weaponLoaded == other && SecondaryAttackManager.PersistedConsumed == consumed + 1, "cancel after weapon switch only clears exhausted attack weapon state");
        a = New(crossbow: true); p = (Player)a.m_character;
        Fire(a); a.m_weapon.Magazine = 3; // PoV reload/clear while an old controller is being destroyed.
        ProjectileRuntimeSystem.Finish(a);
        Check(p.Resets == 0 && a.m_weapon.Magazine == 3, "old completion cannot unload a newly filled magazine");

        a = New(extra: 2, crossbow: true); p = (Player)a.m_character;
        Fire(a); a.m_weapon.m_customData["SecondaryAttacks.ReloadLoaded"] = "1";
        int fills = PassiveTree.CrossbowMagazine.Fills;
        foreach (bool saFirst in new[] { true, false })
        {
            p.m_weaponLoaded = null;
            if (saFirst) SecondaryAttackManager.TryRestorePersistedReloadedWeaponState(p, a.m_weapon);
            p.UpdateWeaponLoading(a.m_weapon);
            if (!saFirst) SecondaryAttackManager.TryRestorePersistedReloadedWeaponState(p, a.m_weapon);
            Check(a.m_weapon.Magazine == 2 && p.m_weaponLoaded == a.m_weapon && PassiveTree.CrossbowMagazine.Fills == fills, "partial magazine restores without refill in either callback order");
        }
        p.m_weaponLoaded = null; a.m_weapon.Magazine = 0;
        SecondaryAttackManager.TryRestorePersistedReloadedWeaponState(p, a.m_weapon); p.UpdateWeaponLoading(a.m_weapon);
        Check(p.m_weaponLoaded == null, "stale SA loaded key cannot resurrect cleared PoV magazine");

        a = New(extra: 1);
        PassiveTree.ProjectileMods.ammoWeapon = other;
        PathOfValheimanCompat.RollAmmoCost(a, 1);
        Check(PassiveTree.ProjectileMods.ObservedAmmoWeapon == a.m_weapon && PassiveTree.ProjectileMods.ammoWeapon == other, "saving rolls use attack weapon and restore outer weapon");
        PassiveTree.ProjectileMods.ThrowPayment = true;
        try { PathOfValheimanCompat.RollAmmoCost(a, 1); } catch (InvalidOperationException) { }
        finally { PassiveTree.ProjectileMods.ThrowPayment = false; }
        Check(PassiveTree.ProjectileMods.ammoWeapon == other, "failed saving roll restores weapon");
        PassiveTree.ProjectileMods.creatingProjectile = a;
        int resources = a.ResourcePayments;
        a.GetProjectileSpawnPoint(out _, out _); a.GetProjectileSpawnPoint(out _, out _);
        Check(a.ResourcePayments == resources, "aim queries do not charge SA Burst resources");
        ((ProjectileSecondaryBehavior)Definition(a).Behavior).Preset = SecondaryAttackPreset.Other;
        using (PathOfValheimanCompat.BeginEmission(a)) a.GetProjectileSpawnPoint(out _, out _);
        Check(a.ResourcePayments == resources, "fixed formations do not double charge resources");
        SecondaryAttackRuntimeContext.Active.Remove(a);
        a.GetProjectileSpawnPoint(out _, out _);
        Check(a.ResourcePayments == resources + 1, "unmanaged attacks retain PoV resource processing");

        a = New(crossbow: true);
        PassiveTree.CrossbowMagazine.Done.Add(a);
        before = PassiveTree.CrossbowMagazine.Calls;
        Fire(a); ProjectileRuntimeSystem.Finish(a);
        Check(PassiveTree.CrossbowMagazine.Calls == before && a.m_character.Resets == 0, "Shot false cannot consume or unload an already completed trigger");
        a = New(crossbow: true);
        PassiveTree.CrossbowMagazine.firing = previous;
        PassiveTree.ProjectileMods.ammoSaved = true;
        PassiveTree.CrossbowMagazine.ThrowKeep = true;
        try { Fire(a); } catch (InvalidOperationException) { }
        finally { PassiveTree.CrossbowMagazine.ThrowKeep = false; }
        Check(PassiveTree.CrossbowMagazine.firing == previous && PassiveTree.ProjectileMods.ammoSaved, "throwing magazine callback restores outer state");
    }
#endif
    internal static void Run()
    {
        Check(!PathOfValheimanCompat.Enabled, "absent PoV must remain inactive");
        var h = new Harmony(PathOfValheimanCompat.PluginGuid);
        foreach (Type t in new[] { typeof(PassiveTree.Effects.Attack_GetAttackEitr), typeof(PassiveTree.ProjectileMods.Inventory_RemoveItem), typeof(PassiveTree.CrossbowRecoil.Burst), typeof(PassiveTree.AmmoVariety.Fired) }) h.CreateClassProcessor(t).Patch();
        foreach (Type t in new[] { typeof(PassiveTree.ProjectileMods.ClassifyBeforeDamage), typeof(PassiveTree.ProjectileMods.Attack_Start), typeof(PassiveTree.ProjectileMods.Projectile_Setup), typeof(PassiveTree.SecondaryAttacks.Humanoid_StartAttack), typeof(PassiveTree.SecondaryAttacks.Attack_Start_Origin), typeof(PassiveTree.ChargedAttacks.ReleaseAttack), typeof(PassiveTree.Blasts.Attack_OnAttackTrigger_Area) }) h.CreateClassProcessor(t).Patch();
#if POV_MAGAZINE
        foreach (Type t in new[] { typeof(PassiveTree.ProjectileMods.VolleyResources), typeof(PassiveTree.CrossbowMagazine.Shot), typeof(PassiveTree.CrossbowMagazine.KeepLoaded), typeof(PassiveTree.CrossbowMagazine.Position), typeof(PassiveTree.CrossbowMagazine.Fill), typeof(PassiveTree.CrossbowMagazine.Restore), typeof(PassiveTree.CrossbowMagazine.Gate), typeof(PassiveTree.CrossbowMagazine.NextShot) }) h.CreateClassProcessor(t).Patch();
#endif
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
#if POV_MAGAZINE
        Check(PathOfValheimanCompat.ExtraBursts(a) == 0 && a.m_projectileBursts == 1, "magazine capacity is not extra Burst shots");
        ProjectileRuntimeSystem.FireBurstFire(a, Definition(a));
        Check(ProjectileRuntimeSystem.Scheduled == 2 && a.m_weapon.Magazine == 2 && a.m_character.Inventory.Total == 29, "configured 3 shots consume one magazine position at trigger");
#else
        Check(PathOfValheimanCompat.ExtraBursts(a) == 2 && a.m_projectileBursts == 1 && PassiveTree.ProjectileMods.burstFor == null, "adopt crossbow extra bursts");
        ProjectileRuntimeSystem.FireBurstFire(a, Definition(a));
        Check(ProjectileRuntimeSystem.Scheduled == 4 && a.m_character.Inventory.Total == 29, "schedule 3+2 actual shots without precharging repeats");
#endif
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
        PathOfValheimanCompat.AfterBurstShot(a, 1);
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
#if POV_MAGAZINE
        a = New(crossbow: true);
        AmmoConservation.Procs.Enqueue(true);
        Check(Fire(a), "EpicLoot-refunded magazine trigger emitted");
        ProjectileRuntimeSystem.Finish(a);
        Check(a.m_weapon.Magazine == 1 && a.m_character.Resets == 0, "actual paid ammo after EpicLoot refund controls trigger conservation");
#endif
        EpicLootCompat.Dispose();
        a = New();
        ProjectileRuntimeSystem.FireBurstFire(a, Definition(a));
        Check(a.m_character.Resets == 0, "PoV-only first Burst shot defers reload");
        ProjectileRuntimeSystem.Finish(a);
        Check(a.m_character.Resets == 1, "PoV-only Burst resets reload at completion");
#if POV_MAGAZINE
        MagazineTests();
#endif
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
#if POV_MAGAZINE
        foreach (var missing in new[] {
            (typeof(Attack), nameof(Attack.GetProjectileSpawnPoint), typeof(PassiveTree.ProjectileMods.VolleyResources), "Postfix"),
            (typeof(Player), nameof(Player.ResetLoadedWeapon), typeof(PassiveTree.CrossbowMagazine.KeepLoaded), "Prefix"),
            (typeof(Attack), nameof(Attack.OnAttackTrigger), typeof(PassiveTree.CrossbowMagazine.Shot), "Finalizer"),
            (typeof(Attack), nameof(Attack.FireProjectileBurst), typeof(PassiveTree.CrossbowMagazine.Position), "Finalizer"),
            (typeof(Player), nameof(Player.UpdateWeaponLoading), typeof(PassiveTree.CrossbowMagazine.Restore), "Prefix") })
        {
            var target = AccessTools.Method(missing.Item1, missing.Item2);
            var callback = AccessTools.Method(missing.Item3, missing.Item4);
            h.Unpatch(target, callback);
            PathOfValheimanCompat.Initialize();
            Check(!PathOfValheimanCompat.Enabled && !Harmony.HasAnyPatches("sighsorry.SecondaryAttacks.PathOfValheiman"), "partial new PoV registration rejects bridge without leftover suppression");
            var patch = new HarmonyMethod(callback);
            h.Patch(target, prefix: missing.Item4 == "Prefix" ? patch : null,
                postfix: missing.Item4 == "Postfix" ? patch : null, finalizer: missing.Item4 == "Finalizer" ? patch : null);
        }
#endif
        h.Unpatch(AccessTools.Method(typeof(Inventory), nameof(Inventory.RemoveItem)), AccessTools.Method(typeof(PassiveTree.ProjectileMods.Inventory_RemoveItem), "Prefix"));
        PathOfValheimanCompat.Initialize();
        Check(!PathOfValheimanCompat.Enabled, "missing callback registration rejects bridge");
        h.UnpatchSelf();
        Console.WriteLine($"PASS: {checks} PoV production bridge assertions; deterministic boundaries, no game execution.");
    }
}
