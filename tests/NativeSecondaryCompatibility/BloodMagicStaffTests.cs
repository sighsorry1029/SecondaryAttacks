using SecondaryAttacks;
using UnityEngine;

internal static class BloodMagicStaffTests
{
    private static int _assertions;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        _assertions++;
    }

    private static GameObject Staff(string name = "RD_inv_staff_plains", string animation = "staff_fireball", string secondary = "") =>
        new(name, new ItemDrop
        {
            m_itemData = new()
            {
                m_shared = new()
                {
                    m_skillType = Skills.SkillType.BloodMagic,
                    m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeapon,
                    m_attack = new()
                    {
                        m_attackType = Attack.AttackType.Projectile, m_attackAnimation = animation,
                        m_attackProjectile = new("payload", component: new Projectile()),
                        m_attackEitr = 30, m_attackHealth = 15
                    },
                    m_secondaryAttack = new() { m_attackAnimation = secondary, m_attackEitr = 7, m_attackHealth = 3 }
                }
            }
        });

    private static ItemDrop.ItemData.SharedData Shared(GameObject item) => item.Item!.m_itemData.m_shared;
    private static RangedAutomaticWeaponFamily Family(GameObject item) => SecondaryAttackWeaponFamilyResolver.ResolveRangedFamily(item.Item!);
    private static bool Preserved(GameObject item) => NativeSecondaryAttackCompat.ShouldPreserveNativeSecondary(item.name, item.Item!);
    private static ObjectDB Db(params GameObject[] items) { var db = new ObjectDB(); db.m_items.AddRange(items); return db; }
    private static void Reapply(ObjectDB db)
    {
        SecondaryAttackObjectDbStateStore.Restore(db);
        NativeSecondaryAttackCompat.PrepareForApply(db);
    }
    private static void Override(ObjectDB db, GameObject item)
    {
        NativeSecondaryAttackCompat.CaptureMagicWeaponOverride(item.name, item.Item!);
        SecondaryAttackObjectDbStateStore.CaptureSecondaryAttack(db, item.name, Shared(item).m_secondaryAttack);
        Shared(item).m_secondaryAttack = new() { m_attackAnimation = "SA_sentinel", m_attackEitr = 60, m_attackHealth = 30 };
    }

    public static int Run()
    {
        // Input values reviewed in the original Vikings Summoner 1.5.3 bundle.
        // This checks our classification, not execution of its assets or scripts.
        foreach (var (region, animation, eitr, health, perBurst) in new[]
        {
            ("meadows", "staff_fireball", 8, 2, false),
            ("bforest", "staff_fireball", 10, 2, false),
            ("swamp", "staff_fireball", 20, 5, false),
            ("mountain", "staff_rapidfire", 8, 1, true),
            ("plains", "staff_fireball", 30, 15, false),
            ("mistlands", "staff_fireball", 30, 12, false),
            ("ashlands", "staff_fireball", 30, 15, false)
        })
        {
            var item = Staff("RD_inv_staff_" + region, animation);
            var primary = Shared(item).m_attack;
            primary.m_attackEitr = eitr;
            primary.m_attackHealth = health;
            primary.m_perBurstResourceUsage = perBurst;
            var expected = perBurst ? RangedAutomaticWeaponFamily.RapidStaff : RangedAutomaticWeaponFamily.FireballStaff;
            Check(Family(item) == expected, region + " correct staff family");
            Check(!Preserved(item), region + " empty secondary remains eligible");
            Check(SecondaryAttackWeaponFamilyResolver.ResolveBloodMagicFamily(item.name, item.Item!) == BloodMagicAutomaticWeaponFamily.None, region + " is not a summon/shield");
            string group = perBurst ? "family:ranged:staff-rapid" : "family:ranged:staff-fireball";
            Check(SecondaryAttackWeaponFamilyResolver.ResolveAutomaticCooldownGroup(item.name, item.Item!) == group, region + " shares corresponding staff cooldown");
            Check(primary.m_attackEitr == eitr && primary.m_attackHealth == health && primary.m_perBurstResourceUsage == perBurst && Shared(item).m_skillType == Skills.SkillType.BloodMagic, region + " classification preserves primary costs/skill");
        }

        CheckExclusions();
        CheckRestoration();
        Console.WriteLine($"PASS bloodmagic: {_assertions} assertions; classification/restoration doubles, no game execution.");
        return 0;
    }

    private static void CheckExclusions()
    {
        Check(!SecondaryAttackWeaponFamilyResolver.IsOffensiveBloodMagicStaff(null), "null shared data");
        var item = Staff("UnrelatedModStaff", "STAFF_LIGHTNINGSHOT");
        var shared = Shared(item);
        Check(Family(item) == RangedAutomaticWeaponFamily.ReloadStaff, "generic support, case-insensitive animation");
        shared.m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
        Check(Family(item) == RangedAutomaticWeaponFamily.ReloadStaff, "other two-handed item type");
        foreach (var type in new[] { ItemDrop.ItemData.ItemType.OneHandedWeapon, ItemDrop.ItemData.ItemType.Shield, ItemDrop.ItemData.ItemType.Ammo, ItemDrop.ItemData.ItemType.AmmoNonEquipable })
        {
            shared.m_itemType = type;
            Check(Family(item) == RangedAutomaticWeaponFamily.None, "no expansion to " + type);
        }
        shared.m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeapon;
        foreach (string animation in new[] { "staff_summon", "staff_shield", "custom_buff", "", " " })
        {
            shared.m_attack.m_attackAnimation = animation;
            Check(Family(item) == RangedAutomaticWeaponFamily.None, "unknown/support animation excluded: " + animation);
        }
        shared.m_attack.m_attackAnimation = "staff_fireball";
        shared.m_attack.m_attackType = Attack.AttackType.Area;
        Check(Family(item) == RangedAutomaticWeaponFamily.None, "area attack excluded");
        shared.m_attack.m_attackType = Attack.AttackType.Projectile;
        foreach (object? component in new object?[] { null, new Aoe(), new SpawnAbility() })
        {
            shared.m_attack.m_attackProjectile = new("non_projectile", component: component);
            Check(Family(item) == RangedAutomaticWeaponFamily.None, "non-Projectile payload excluded");
        }
        shared.m_attack.m_attackProjectile = null;
        Check(Family(item) == RangedAutomaticWeaponFamily.None, "missing payload excluded");
        var projectile = new Projectile { m_spawnOnHit = new("separate_impact_aoe", component: new Aoe()) };
        var payload = new GameObject("payload", component: projectile);
        shared.m_attack.m_attackProjectile = payload;
        Check(Family(item) == RangedAutomaticWeaponFamily.FireballStaff, "separate Cursed Moon impact AOE remains supported");
        foreach (object support in new object[] { new Aoe(), new SpawnAbility() })
        {
            payload.ExtraComponents.Add(support);
            Check(Family(item) == RangedAutomaticWeaponFamily.None, "hybrid root payload excluded");
            payload.ExtraComponents.Clear();
            payload.Children.Add(new("support_child", component: support));
            Check(Family(item) == RangedAutomaticWeaponFamily.None, "hybrid child payload excluded");
            payload.Children.Clear();
        }
        shared.m_attack.m_attackProjectile = new("summon", component: new SpawnAbility { m_spawnPrefab = new[] { new GameObject("creature") } });
        Check(SecondaryAttackWeaponFamilyResolver.ResolveBloodMagicFamily(item.name, item.Item!) == BloodMagicAutomaticWeaponFamily.Summon, "existing summon family preserved");
        Check(SecondaryAttackWeaponFamilyResolver.ResolveBloodMagicFamily("StaffShield", item.Item!) == BloodMagicAutomaticWeaponFamily.Shield, "existing shield family preserved");
    }

    private static void CheckRestoration()
    {
        var item = Staff();
        var db = Db(item);
        var primary = Shared(item).m_attack;
        Reapply(db);
        Override(db, item);
        Reapply(db);
        Check(Shared(item).m_secondaryAttack.m_attackAnimation == "" && !Preserved(item), "Off/removal restores empty baseline");
        Override(db, item);
        Reapply(Db(item));
        Check(!Preserved(item), "reused prefab in new ObjectDB does not treat SA override as native");
        Check(ReferenceEquals(primary, Shared(item).m_attack) && primary.m_attackHealth == 15 && primary.m_attackEitr == 30, "primary remains untouched");

        var native = Staff("OtherBloodStaff", secondary: "native_secondary");
        var nativeDb = Db(native);
        Reapply(nativeDb);
        Check(Preserved(native), "native secondary preserved automatically");
        Shared(native).m_secondaryAttack.m_attackEitr = 42;
        Reapply(nativeDb);
        Check(Shared(native).m_secondaryAttack.m_attackEitr == 42, "untouched native live changes retained");
        Override(nativeDb, native); // Explicit YAML may replace an existing native.
        var fresh = Staff(native.name, secondary: "fresh_native");
        Reapply(Db(fresh));
        Check(Shared(fresh).m_secondaryAttack.m_attackAnimation == "fresh_native", "same-name new prefab has no stale identity snapshot");
        Reapply(Db(native));
        Check(Preserved(native) && Shared(native).m_secondaryAttack.m_attackEitr == 42 && Shared(native).m_secondaryAttack.m_attackHealth == 3, "explicit override removal restores native attack and costs across worlds");
        Shared(native).m_secondaryAttack.m_attackHealth = 5;
        Reapply(Db(native));
        Check(Shared(native).m_secondaryAttack.m_attackHealth == 5, "restoration relinquishes ownership");

        Override(db, item);
        Shared(item).m_attack.m_attackAnimation = "custom_buff";
        Reapply(Db(item));
        Check(Shared(item).m_secondaryAttack.m_attackAnimation == "" && Family(item) == RangedAutomaticWeaponFamily.None, "restoration still runs after becoming ineligible");
    }
}
