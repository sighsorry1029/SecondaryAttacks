using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using SecondaryAttacks;
using EpicLoot.MagicItemEffects;
using EpicLoot.Magic.MagicItemEffects;

internal static class Program
{
    private static int _checks;
    internal static bool DelayFirst, EmptyCallback;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        _checks++;
    }

    private static Attack NewAttack(int ammo = 20, int count = 3, int cost = 1)
    {
        var player = new Player();
        Player.m_localPlayer = player;
        var attack = new Attack { m_character = player };
        player.m_weaponLoaded = attack.m_weapon;
        player.Inventory.Items.Add(new ItemDrop.ItemData { m_stack = ammo });
        var active = new ActiveSecondaryAttack();
        var behavior = (ProjectileSecondaryBehavior)active.Definition.Behavior;
        behavior.ProjectileCount = count;
        behavior.AmmoConsumption = cost;
        SecondaryAttackRuntimeContext.Active[attack] = active;
        MultiShot.Procs.Clear();
        MultiShot.EffectCount = 3;
        AmmoConservation.Procs.Clear();
        AmmoConservation.Calls = 0;
        AmmoConservation.PendingReload = true; // another attack's pending state
        MultiShot.SeedGlobals();
        ProjectileRuntimeSystem.Shots.Clear();
        ProjectileRuntimeSystem.ThrowSpawn = false;
        DelayFirst = EmptyCallback = false;
        return attack;
    }

    private static SecondaryAttackDefinition Definition(Attack attack) => SecondaryAttackRuntimeContext.Active[attack].Definition;

    private static bool Repeat(Attack attack)
    {
        EmptyCallback = true;
        try { attack.FireProjectileBurst(); }
        finally { EmptyCallback = false; }
        return ProjectileRuntimeSystem.TryFireBurstShot(attack, Definition(attack), out _, out _);
    }

    private static void CheckRestored(Attack attack)
    {
        Check(attack.m_projectiles == 1 && attack.m_projectileAccuracy == 10 && attack.m_weapon.m_shared.m_damages.m_damage == 100,
            "shot fields/shared damage leaked");
        Check(MultiShot.Pending == 30 && MultiShot.TripleFlag && MultiShot.ProjectileFlag == 7,
            "EpicLoot pending globals leaked");
        Check(AmmoConservation.PendingReload, "unrelated pending reload lost");
    }

    public static int Main(string[] args)
    {
        try
        {
            // Register EpicLoot first, just as the plugin soft dependency requires.
            var epicHarmony = new Harmony("test.epicloot");
            epicHarmony.CreateClassProcessor(typeof(MultiShot)).Patch();
            epicHarmony.CreateClassProcessor(typeof(AmmoConservation.AmmoConservation_Attack_UseAmmo_Patch)).Patch();
            var saHarmony = new Harmony("test.secondaryattacks");
            saHarmony.Patch(AccessTools.Method(typeof(Attack), nameof(Attack.OnAttackTrigger)),
                prefix: new HarmonyMethod(typeof(AttackBridge), "Trigger") { priority = Priority.First });
            saHarmony.Patch(AccessTools.Method(typeof(Attack), nameof(Attack.FireProjectileBurst)),
                prefix: new HarmonyMethod(typeof(AttackBridge), "Fire")
                { priority = args.Length > 0 && args[0] == "epic-first" ? Priority.Last : Priority.First });

            var absent = NewAttack();
            EpicLootCompat.Initialize();
            Check(!EpicLootCompat.HandlesAttack(absent), "absent EpicLoot activated");
            BepInEx.Bootstrap.Chainloader.PluginInfos[EpicLootCompat.PluginGuid] = new BepInEx.PluginInfo();
            EpicLootCompat.Initialize();

            for (int procCount = 0; procCount <= 3; procCount++)
            {
                var a = NewAttack();
                for (int i = 0; i < 3; i++) MultiShot.Procs.Enqueue(i < procCount);
                a.OnAttackTrigger();
                Check(Repeat(a) && Repeat(a), "repeat failed with enough ammo");
                ProjectileRuntimeSystem.Finish(a);
                Check(a.m_character.Inventory.Total == 20 - (3 + 2 * procCount), $"expected 3/5/7/9 consumption: procs={procCount}, ammo={a.m_character.Inventory.Total}, rolls={a.Rolls}, prepares={a.Prepares}, shots={ProjectileRuntimeSystem.Shots.Count}");
                Check(a.Rolls == 3 && a.Prepares == 3 && a.ResourcePayments == 3 && a.ExtraPayments == procCount,
                    "duplicate or missing roll/prepare/resource payment through Harmony");
                Check(ProjectileRuntimeSystem.Shots.Count == 3 && a.m_character.SideEffects == 3, "wrong shot/side-effect count");
                Check(ProjectileRuntimeSystem.Shots.Take(procCount).All(s => s.Count == 3 && s.Damage == 40 && s.Accuracy == 12.5f),
                    "proc damage/accuracy not applied to actual projectile creation");
                Check(ProjectileRuntimeSystem.Shots.Skip(procCount).All(s => s.Count == 1 && s.Damage == 100 && s.Accuracy == 10),
                    "proc leaked to following shot");
                Check(a.m_character.Resets == 1, "burst must reset reload once at completion");
                CheckRestored(a);
            }

            var shortage = NewAttack(ammo: 2);
            MultiShot.Procs.Enqueue(true);
            shortage.OnAttackTrigger();
            Check(shortage.m_character.Inventory.Total == 2 && ProjectileRuntimeSystem.Shots.Count == 0 && shortage.Stops > 0,
                "shortage must not partially pay/fire");
            Check(shortage.ResourcePayments == 0 && shortage.ExtraPayments == 0 && shortage.m_character.SideEffects == 0 && shortage.m_character.Resets == 0,
                "shortage paid shot resources or applied deferred side effects");
            CheckRestored(shortage);

            var split = NewAttack(ammo: 1, count: 1);
            split.m_character.Inventory.Items.Add(new ItemDrop.ItemData { m_stack = 2 });
            MultiShot.Procs.Enqueue(true);
            split.OnAttackTrigger();
            Check(split.m_character.Inventory.Total == 0 && ProjectileRuntimeSystem.Shots.Single().Count == 3 && split.m_character.Resets == 1,
                "same-prefab split stacks/single-shot reload failed");

            var mixed = NewAttack(ammo: 1);
            mixed.m_character.Inventory.Items.Add(new ItemDrop.ItemData { m_stack = 20, m_dropPrefab = new UnityEngine.GameObject("OtherBolt") });
            MultiShot.Procs.Enqueue(true);
            mixed.OnAttackTrigger();
            Check(mixed.m_character.Inventory.Total == 21 && ProjectileRuntimeSystem.Shots.Count == 0, "mixed prefabs combined for payment");

            var free = NewAttack(cost: 0, count: 1);
            MultiShot.Procs.Enqueue(true);
            AmmoConservation.Procs.Enqueue(true);
            free.OnAttackTrigger();
            Check(free.m_character.Inventory.Total == 20 && AmmoConservation.Calls == 0 && free.m_character.Resets == 1,
                "zero ammo policy minted ammo or preserved reload");

            var custom = NewAttack(cost: 2, count: 1);
            MultiShot.EffectCount = 5;
            MultiShot.Procs.Enqueue(true);
            custom.OnAttackTrigger();
            Check(custom.m_character.Inventory.Total == 10 && ProjectileRuntimeSystem.Shots.Single().Count == 5,
                "custom cost/proc projectile configuration ignored");

            var cluster = NewAttack(count: 1);
            cluster.m_projectiles = 4;
            MultiShot.Procs.Enqueue(false);
            cluster.OnAttackTrigger();
            Check(cluster.m_character.Inventory.Total == 19 && ProjectileRuntimeSystem.Shots.Single().Count == 4,
                "native projectile cluster incorrectly multiplied ammo");

            var conserved = NewAttack();
            for (int i = 0; i < 3; i++) { MultiShot.Procs.Enqueue(true); AmmoConservation.Procs.Enqueue(true); }
            conserved.OnAttackTrigger(); Repeat(conserved); Repeat(conserved); ProjectileRuntimeSystem.Finish(conserved);
            Check(conserved.m_character.Inventory.Total == 14 && conserved.m_character.Resets == 0, "conservation must refund exactly one per triple");
            CheckRestored(conserved);

            var lastNormal = NewAttack();
            AmmoConservation.Procs.Enqueue(true); AmmoConservation.Procs.Enqueue(false);
            lastNormal.OnAttackTrigger(); Repeat(lastNormal); ProjectileRuntimeSystem.Finish(lastNormal);
            Check(lastNormal.m_character.Resets == 1, "earlier proc incorrectly preserved final reload");

            var interrupted = NewAttack(ammo: 3);
            MultiShot.Procs.Enqueue(true); MultiShot.Procs.Enqueue(true);
            AmmoConservation.Procs.Enqueue(true);
            interrupted.OnAttackTrigger();
            Check(!Repeat(interrupted), "unaffordable later shot fired");
            ProjectileRuntimeSystem.Finish(interrupted);
            Check(interrupted.m_character.Inventory.Total == 1 && interrupted.m_character.Resets == 0,
                "failed repeat lost last successful conservation result");

            var switched = NewAttack();
            AmmoConservation.Procs.Enqueue(true);
            switched.OnAttackTrigger();
            var otherWeapon = new ItemDrop.ItemData();
            ((Player)switched.m_character).m_weaponLoaded = otherWeapon;
            ProjectileRuntimeSystem.Finish(switched);
            Check(ReferenceEquals(((Player)switched.m_character).m_weaponLoaded, otherWeapon), "switch reset another weapon");

            var delayed = NewAttack(count: 1);
            DelayFirst = true;
            delayed.OnAttackTrigger();
            Check(delayed.m_character.Inventory.Total == 20 && delayed.Rolls == 0 && delayed.m_character.Resets == 0,
                "delayed trigger paid/rolled before actual shot");
            MultiShot.Procs.Enqueue(true);
            delayed.FireProjectileBurst();
            Check(delayed.m_character.Inventory.Total == 17 && delayed.Rolls == 1 && delayed.m_character.Resets == 1,
                "delayed actual shot missing payment/reset");

            var changed = NewAttack(count: 1);
            DelayFirst = true;
            changed.OnAttackTrigger();
            changed.m_character.Inventory.Items.Clear();
            changed.m_character.Inventory.Items.Add(new ItemDrop.ItemData { m_stack = 20, ValidPayload = false });
            MultiShot.Procs.Enqueue(true);
            changed.FireProjectileBurst();
            Check(changed.m_character.Inventory.Total == 20 && changed.ResourcePayments == 0 && ProjectileRuntimeSystem.Shots.Count == 0,
                "reselected invalid payload paid before validation");

            var noResources = NewAttack();
            noResources.ResourcesAvailable = false;
            MultiShot.Procs.Enqueue(true);
            noResources.OnAttackTrigger();
            Check(noResources.m_character.Inventory.Total == 20 && ProjectileRuntimeSystem.Shots.Count == 0, "resource failure paid ammo");

            foreach (bool prepareFailure in new[] { true, false })
            {
                var exceptional = NewAttack();
                exceptional.ThrowPrepare = prepareFailure;
                ProjectileRuntimeSystem.ThrowSpawn = !prepareFailure;
                MultiShot.Procs.Enqueue(true);
                try { ProjectileRuntimeSystem.TryFireBurstShot(exceptional, Definition(exceptional), out _, out _); throw new Exception("expected failure"); }
                catch (InvalidOperationException) { }
                CheckRestored(exceptional);
            }

            var primary = NewAttack();
            SecondaryAttackRuntimeContext.Active.Remove(primary);
            MultiShot.Procs.Enqueue(true);
            primary.OnAttackTrigger(); primary.FireProjectileBurst();
            Check(primary.Rolls == 1 && primary.Prepares == 1 && primary.m_weapon.m_shared.m_damages.m_damage == 100,
                "ordinary EpicLoot hooks/finalizer were suppressed");
            var other = NewAttack();
            ((ProjectileSecondaryBehavior)Definition(other).Behavior).Preset = SecondaryAttackPreset.Other;
            Check(!EpicLootCompat.HandlesAttack(other), "other preset captured");
            var remote = NewAttack(); Player.m_localPlayer = new Player();
            Check(!EpicLootCompat.HandlesAttack(remote), "remote player captured");
            var staff = NewAttack(); staff.m_weapon.m_shared.m_ammoType = "";
            Check(!EpicLootCompat.HandlesAttack(staff), "non-ammo staff captured");

            EpicLootCompat.Dispose();
            var disposed = NewAttack(); disposed.OnAttackTrigger();
            Check(disposed.Rolls == 1 && !EpicLootCompat.HandlesAttack(disposed), "dispose left suppression patches");
            var plugin = BepInEx.Bootstrap.Chainloader.PluginInfos[EpicLootCompat.PluginGuid];
            foreach (Version version in new[] { new Version(0, 14, 14), new Version(0, 13, 0), new Version(1, 0, 0) })
            {
                plugin.Metadata.Version = version;
                EpicLootCompat.Initialize();
                var compatible = NewAttack(count: 1);
                MultiShot.Procs.Enqueue(true);
                compatible.OnAttackTrigger();
                Check(EpicLootCompat.HandlesAttack(compatible) && compatible.Rolls == 1 &&
                    compatible.m_character.Inventory.Total == 17 && ProjectileRuntimeSystem.Shots.Single().Count == 3,
                    "compatible callbacks rejected by version number");
                CheckRestored(compatible);
            }

            // Keeping the methods is not enough if their actual Harmony roles move.
            foreach (var hook in new[] {
                (Target: "OnAttackTrigger", Callback: AccessTools.Method(typeof(MultiShot), "Attack_OnAttackTrigger_Prefix", new[] { typeof(Attack) }), Kind: HarmonyPatchType.Prefix),
                (Target: "FireProjectileBurst", Callback: AccessTools.Method(typeof(MultiShot), "Attack_FireProjectileBurst_Prefix"), Kind: HarmonyPatchType.Prefix),
                (Target: "FireProjectileBurst", Callback: AccessTools.Method(typeof(MultiShot), "Attack_FireProjectileBurst_Finalizer"), Kind: HarmonyPatchType.Finalizer),
                (Target: "UseAmmo", Callback: AccessTools.Method(typeof(AmmoConservation.AmmoConservation_Attack_UseAmmo_Patch), "Postfix"), Kind: HarmonyPatchType.Postfix) })
            {
                EpicLootCompat.Dispose();
                MethodInfo target = AccessTools.Method(typeof(Attack), hook.Target);
                epicHarmony.Unpatch(target, hook.Callback);
                EpicLootCompat.Initialize();
                var missing = NewAttack();
                Check(!EpicLootCompat.HandlesAttack(missing), "missing native hook activated bridge");
                MultiShot.Attack_OnAttackTrigger_Prefix(missing);
                Check(missing.Rolls == 1, "rejected contract left suppression patch installed");
                var method = new HarmonyMethod(hook.Callback);
                epicHarmony.Patch(target,
                    prefix: hook.Kind == HarmonyPatchType.Prefix ? method : null,
                    postfix: hook.Kind == HarmonyPatchType.Postfix ? method : null,
                    finalizer: hook.Kind == HarmonyPatchType.Finalizer ? method : null);
            }

            // Same plugin version, incompatible reflection contract: reject before patching.
            plugin.Metadata.Version = new Version(0, 14, 13);
            foreach (string fault in new[] { "count-type", "readonly-count", "instance-count", "literal-count",
                "pending-type", "roll-argument", "prepare-return", "refund-argument", "unregistered" })
            {
                plugin.Instance = BuildContractProbe(fault);
                EpicLootCompat.Initialize();
                var rejected = NewAttack();
                rejected.OnAttackTrigger();
                Check(!EpicLootCompat.HandlesAttack(rejected) && rejected.Rolls == 1,
                    "incompatible contract activated or left suppression: " + fault);
            }
            plugin.Instance = new EpicLoot.TestPlugin();
            EpicLootCompat.Initialize();
            Check(EpicLootCompat.HandlesAttack(NewAttack()), "valid contract failed after rejected initialization");
            Console.WriteLine($"PASS: {_checks} Burst assertions ({string.Join(",", args)}) with real HarmonyX and deterministic game/EpicLoot doubles. No Unity/game execution.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        finally { EpicLootCompat.Dispose(); }
    }

    private static object BuildContractProbe(string fault)
    {
        var assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("EpicLootProbe_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Probe");
        var pending = module.DefineEnum("Probe.Pending", TypeAttributes.Public, typeof(byte));
        pending.DefineLiteral("Uninterpreted", (byte)17);
        Type pendingType = pending.CreateType();
        var multi = module.DefineType("EpicLoot.MagicItemEffects.MultiShot", TypeAttributes.Public);
        multi.DefineField("_pendingShot", fault == "pending-type" ? typeof(int) : pendingType, FieldAttributes.Public | FieldAttributes.Static);
        multi.DefineField("IsTripleShotActive", typeof(bool), FieldAttributes.Private | FieldAttributes.Static);
        FieldAttributes countAttributes = FieldAttributes.Public | FieldAttributes.Static;
        if (fault == "readonly-count") countAttributes |= FieldAttributes.InitOnly;
        if (fault == "instance-count") countAttributes &= ~FieldAttributes.Static;
        if (fault == "literal-count") countAttributes |= FieldAttributes.Literal | FieldAttributes.HasDefault;
        var count = multi.DefineField("ShotProjectiles", fault == "count-type" ? typeof(long) : typeof(int), countAttributes);
        if (fault == "literal-count") count.SetConstant(3);
        AddMethod(multi, "Attack_OnAttackTrigger_Prefix", typeof(void), fault == "roll-argument" ? typeof(object) : typeof(Attack));
        AddMethod(multi, "Attack_FireProjectileBurst_Prefix", fault == "prepare-return" ? typeof(bool) : typeof(void),
            typeof(Attack), typeof(HitData.DamageTypes?).MakeByRefType());
        AddMethod(multi, "Attack_FireProjectileBurst_Finalizer", typeof(void), typeof(Attack), typeof(HitData.DamageTypes?));
        multi.CreateType();
        var conserve = module.DefineType("EpicLoot.Magic.MagicItemEffects.AmmoConservation", TypeAttributes.Public);
        conserve.DefineField("skipReload", typeof(bool), FieldAttributes.Public | FieldAttributes.Static);
        var refund = conserve.DefineNestedType("AmmoConservation_Attack_UseAmmo_Patch", TypeAttributes.NestedPublic);
        AddMethod(refund, "Postfix", typeof(void), typeof(Attack),
            fault == "refund-argument" ? typeof(bool) : typeof(bool).MakeByRefType(), typeof(ItemDrop.ItemData));
        refund.CreateType();
        conserve.CreateType();
        var plugin = module.DefineType("Probe.Plugin", TypeAttributes.Public);
        plugin.DefineDefaultConstructor(MethodAttributes.Public);
        return Activator.CreateInstance(plugin.CreateType())!;
    }

    private static void AddMethod(TypeBuilder type, string name, Type result, params Type[] arguments)
    {
        var method = type.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, result, arguments);
        ILGenerator il = method.GetILGenerator();
        if (result == typeof(bool)) il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);
    }

    [HarmonyPatch]
    private static class AttackBridge
    {
        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Trigger(Attack __instance)
        {
            if (!EpicLootCompat.HandlesAttack(__instance)) return true;
            SecondaryAttackRuntimeFacade.OuterTrigger(__instance);
            if (!DelayFirst) __instance.FireProjectileBurst();
            return false;
        }
        [HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst)), HarmonyPrefix, HarmonyPriority(Priority.First)]
        private static bool Fire(Attack __instance)
        {
            if (!EpicLootCompat.HandlesAttack(__instance)) return true;
            if (!EmptyCallback) ProjectileRuntimeSystem.FireBurstFire(__instance, Definition(__instance));
            return false;
        }
    }
}
