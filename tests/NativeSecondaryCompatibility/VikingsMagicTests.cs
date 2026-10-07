using BepInEx;
using BepInEx.Bootstrap;
using SecondaryAttacks;
using UnityEngine;

internal static class VikingsMagicTests
{
    private static int _assertions;

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        _assertions++;
    }

    private static GameObject Staff(string name, string secondaryAnimation = "", float cost = 0, GameObject? payload = null) =>
        new(name, new ItemDrop
        {
            // Use the explicitly supplied prefab name, not ItemDrop.name.
            name = "component_name",
            m_itemData = new()
            {
                m_shared = new()
                {
                    m_skillType = Skills.SkillType.ElementalMagic,
                    m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeapon,
                    m_attack = new()
                    {
                        m_attackType = Attack.AttackType.Projectile, m_attackAnimation = "staff_fireball",
                        m_attackEitr = 24, m_attackProjectile = new("primary", component: new Projectile())
                    },
                    m_secondaryAttack = new()
                    {
                        m_attackType = Attack.AttackType.Projectile, m_attackAnimation = secondaryAnimation,
                        m_attackEitr = cost, m_attackProjectile = payload
                    }
                }
            }
        });

    private static ItemDrop.ItemData.SharedData Shared(GameObject item) => item.Item!.m_itemData.m_shared;
    private static bool Preserved(GameObject item) => NativeSecondaryAttackCompat.ShouldPreserveNativeSecondary(item.name, item.Item!);
    private static RangedAutomaticWeaponFamily Family(GameObject item) => SecondaryAttackWeaponFamilyResolver.ResolveRangedFamily(item.Item!);
    private static ObjectDB Db(params GameObject[] items) { var db = new ObjectDB(); db.m_items.AddRange(items); return db; }

    private static void Reapply(ObjectDB db)
    {
        SecondaryAttackObjectDbStateStore.Restore(db);
        NativeSecondaryAttackCompat.PrepareForApply(db);
    }

    private static void Override(ObjectDB db, GameObject item)
    {
        // The production WorldApply permits explicit YAML before native protection.
        NativeSecondaryAttackCompat.CaptureMagicWeaponOverride(item.name, item.Item!);
        SecondaryAttackObjectDbStateStore.CaptureSecondaryAttack(db, item.name, Shared(item).m_secondaryAttack);
        Shared(item).m_secondaryAttack = new() { m_attackAnimation = "SA_override", m_attackEitr = 99 };
    }

    public static int Run(string mode)
    {
        bool loaded = mode == "vikingsmagic";
        if (loaded) Chainloader.PluginInfos.Add(NativeSecondaryAttackCompat.VikingsMagicGuid, new PluginInfo(new object()));

        // Serialized defaults from the original Vikings Magic 1.1.8 bundle.
        // Dependency doubles check our decisions, not execution of nova/meteor effects.
        foreach (var (region, cost) in new[]
        {
            ("meadows", 15), ("bforest", 21), ("swamp", 27), ("mountain", 30),
            ("plains", 36), ("mistlands", 42), ("ashlands", 47)
        })
        {
            var payload = region == "plains"
                ? new GameObject("projectile_meteor_plains", component: new SpawnAbility())
                : new GameObject("projectile_nova_aoe_" + region, component: new Aoe());
            var item = Staff("RDM_staff_" + region, "staff_shield", cost, payload);
            var db = Db(item);
            var native = Shared(item).m_secondaryAttack;
            var primary = Shared(item).m_attack;
            Reapply(db);
            Check(Preserved(item) == loaded, region + " native protection requires plugin presence");
            Check(Family(item) == RangedAutomaticWeaponFamily.FireballStaff, region + " underlying family unchanged");
            Check(ReferenceEquals(native, Shared(item).m_secondaryAttack), region + " untouched native remains owned by Vikings Magic");
            if (!loaded) continue;

            Override(db, item);
            Reapply(db); // Same-ObjectDB reload / preset:none / override removal.
            CheckNative(item, cost, payload, region + " reload");
            Check(!ReferenceEquals(native, Shared(item).m_secondaryAttack), region + " restores a snapshot, not a mutable Attack alias");

            Override(db, item);
            Reapply(Db(item)); // A new world can reuse the already-overridden prefab.
            CheckNative(item, cost, payload, region + " world re-entry");
            Check(ReferenceEquals(primary, Shared(item).m_attack) && primary.m_attackEitr == 24,
                region + " primary untouched");

            Shared(item).m_secondaryAttack.m_attackEitr = cost + 3;
            Reapply(Db(item));
            Check(Shared(item).m_secondaryAttack.m_attackEitr == cost + 3, region + " restoration releases native ownership");
        }

        var begin = Staff("RDM_staff_begin");
        var beginDb = Db(begin);
        Check(!Preserved(begin) && Family(begin) == RangedAutomaticWeaponFamily.FireballStaff,
            "First Oath's empty secondary keeps Fireball automatic eligibility");
        if (loaded)
        {
            Override(beginDb, begin);
            Reapply(beginDb);
            Check(!Preserved(begin) && Shared(begin).m_secondaryAttack.m_attackAnimation == "", "First Oath Off/removal restores empty secondary");
            Override(beginDb, begin);
            Reapply(Db(begin));
            Check(!Preserved(begin) && Family(begin) == RangedAutomaticWeaponFamily.FireballStaff,
                "First Oath automatic override is not mistaken for native in another world");

            var native = Staff("RDM_staff_plains", "staff_shield", 36, new("meteor", component: new SpawnAbility()));
            Override(Db(native), native);
            var fresh = Staff(native.name, "new_native", 55);
            Reapply(Db(fresh));
            Check(Shared(fresh).m_secondaryAttack.m_attackAnimation == "new_native" && Shared(fresh).m_secondaryAttack.m_attackEitr == 55,
                "same-name fresh prefab does not inherit a stale snapshot");
            Shared(native).m_skillType = Skills.SkillType.None;
            native.name = "renamed_staff";
            Reapply(Db(native));
            Check(Shared(native).m_secondaryAttack.m_attackAnimation == "staff_shield", "restore is independent of current eligibility/name");
        }

        foreach (string region in new[] { "bforest", "swamp", "mountain", "plains" })
        {
            var wand = Staff("RDM_wand_" + region);
            Shared(wand).m_itemType = ItemDrop.ItemData.ItemType.OneHandedWeapon;
            Shared(wand).m_attack.m_attackAnimation = "swing_longsword";
            Check(!Preserved(wand) && Family(wand) == RangedAutomaticWeaponFamily.OneHandedElemental, region + " wand keeps automatic eligibility");
            Shared(wand).m_secondaryAttack.m_attackAnimation = "external_native";
            Check(Preserved(wand), region + " wand still benefits from generic one-handed protection");
        }
        foreach (int tier in new[] { 1, 2, 3 })
        {
            var book = Staff("RDM_book_heal_0" + tier);
            Shared(book).m_itemType = (ItemDrop.ItemData.ItemType)22;
            Shared(book).m_attack.m_attackAnimation = "staff_summon";
            Shared(book).m_attack.m_attackProjectile = new("heal_aoe", component: new Aoe());
            Check(!Preserved(book) && Family(book) == RangedAutomaticWeaponFamily.None, "healing book " + tier + " remains outside automatic groups");
        }
        foreach (string name in new[] { "RDM_staff_plains_clone", "RDM_staff_future", "OtherModStaff", "StaffFireball" })
            Check(!Preserved(Staff(name, "external_native")), "no broad ownership inferred from " + name);
        var broken = Staff("RDM_staff_plains");
        broken.Item!.m_itemData.m_shared = null!;
        Check(!Preserved(broken), "missing shared data is not protected");
        NativeSecondaryAttackCompat.CaptureMagicWeaponOverride(broken.name, broken.Item);

        Console.WriteLine($"PASS {mode}: {_assertions} assertions; classification/restoration doubles, no game execution.");
        return 0;
    }

    private static void CheckNative(GameObject item, float cost, GameObject payload, string label)
    {
        Attack attack = Shared(item).m_secondaryAttack;
        Check(Preserved(item) && attack.m_attackAnimation == "staff_shield", label + " native animation preserved");
        Check(attack.m_attackEitr == cost && attack.m_attackType == Attack.AttackType.Projectile, label + " cost/type preserved");
        Check(ReferenceEquals(attack.m_attackProjectile, payload), label + " original Aoe/SpawnAbility reference preserved");
    }
}
