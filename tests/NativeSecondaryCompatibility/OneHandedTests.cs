using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using MagicPlugin.Functions;
using SecondaryAttacks;
using UnityEngine;

namespace MagicPlugin.Functions
{
    public enum MagicSource { Both, Stamina, Eitr }
    public static class ConfigSetup
    {
        public static ConfigEntry<MagicSource>? _flameScepterSource = new(MagicSource.Eitr);
        public static ConfigEntry<float>? _flameScepterMagicSourceSecondary = new(80);
        public static ConfigEntry<MagicSource>? _iceScepterSource = new(MagicSource.Eitr);
        public static ConfigEntry<float>? _iceScepterMagicSourceSecondary = new(80);
        public static ConfigEntry<MagicSource>? _lightningScepterSource = new(MagicSource.Eitr);
        public static ConfigEntry<float>? _lightningScepterMagicSourceSecondary = new(80);
    }
}

internal static class OneHandedTests
{
    private static int _assertions;
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        _assertions++;
    }

    private static GameObject Weapon(string name, string secondary = "")
    {
        return new(name, new ItemDrop
        {
            name = name,
            m_itemData = new()
            {
                m_shared = new()
                {
                    m_skillType = Skills.SkillType.ElementalMagic,
                    m_itemType = ItemDrop.ItemData.ItemType.OneHandedWeapon,
                    m_attack = new()
                    {
                        m_attackAnimation = "swing_axe", m_attackType = Attack.AttackType.Projectile,
                        m_attackProjectile = new("payload", component: new Projectile())
                    },
                    m_secondaryAttack = new() { m_attackAnimation = secondary, m_attackEitr = 80 }
                }
            }
        });
    }

    private static ItemDrop.ItemData.SharedData Shared(GameObject item) => item.Item!.m_itemData.m_shared;
    private static RangedAutomaticWeaponFamily Family(GameObject item) => SecondaryAttackWeaponFamilyResolver.ResolveRangedFamily(item.Item!);
    private static bool Preserved(GameObject item) => NativeSecondaryAttackCompat.ShouldPreserveNativeSecondary(item.name, item.Item!);
    private static ObjectDB Db(params GameObject[] items) { var db = new ObjectDB(); db.m_items.AddRange(items); return db; }
    private static void Override(ObjectDB db, GameObject item)
    {
        NativeSecondaryAttackCompat.CaptureMagicWeaponOverride(item.name, item.Item!);
        SecondaryAttackObjectDbStateStore.CaptureSecondaryAttack(db, item.name, Shared(item).m_secondaryAttack);
        Shared(item).m_secondaryAttack = new Attack { m_attackAnimation = "SA_barrage", m_attackEitr = 16 };
    }

    private static void Reapply(ObjectDB db)
    {
        SecondaryAttackObjectDbStateStore.Restore(db);
        NativeSecondaryAttackCompat.PrepareForApply(db);
    }

    public static int Run(string mode)
    {
        bool installed = mode != "onehanded";
        if (installed) Chainloader.PluginInfos.Add(SecondaryAttacksPlugin.MagicPluginGuid, new PluginInfo(new MagicPluginMarker()));
        if (mode == "magicplugin-missing")
        {
            ConfigSetup._flameScepterSource = null;
            var staff = Weapon("BMP_FlameScepter", "sword_secondary");
            for (int i = 0; i < 3; i++) Reapply(Db(staff));
            Check(Preserved(staff), "missing cost API still preserves native attack");
            Check(Shared(staff).m_secondaryAttack.m_attackEitr == 80, "missing cost API leaves baseline intact");
            Check(SecondaryAttacksPlugin.ModLogger.Warnings.Count == 1, "missing cost API warns once");
        }
        else
        {
            CheckFamilies();
            CheckOriginals();
            CheckLiveCosts(installed);
            Check(SecondaryAttacksPlugin.ModLogger.Warnings.Count == 0, "no compatibility warnings");
        }

        Console.WriteLine($"PASS {mode}: {_assertions} assertions");
        return 0;
    }

    private static void CheckFamilies()
    {
        var wand = Weapon("AnotherModsWand");
        var shared = Shared(wand);
        Check(Family(wand) == RangedAutomaticWeaponFamily.OneHandedElemental, "swing_axe wand classification");
        Check(!Preserved(wand), "empty native attack is eligible");
        Check(SecondaryAttackWeaponFamilyResolver.ResolveAutomaticCooldownGroup(wand.name, wand.Item!) == "family:ranged:magic-onehanded", "shared one-handed cooldown group");
        shared.m_secondaryAttack.m_attackAnimation = "custom_native";
        Check(Family(wand) == RangedAutomaticWeaponFamily.OneHandedElemental && Preserved(wand), "family is independent of native preservation");
        shared.m_secondaryAttack.m_attackAnimation = "";
        foreach (string animation in new[] { "staff_fireball", "staff_rapidfire", "staff_lightningshot" })
        {
            shared.m_attack.m_attackAnimation = animation;
            Check(Family(wand) == RangedAutomaticWeaponFamily.OneHandedElemental, "one-handed precedence: " + animation);
        }
        foreach (object? payload in new object?[] { null, new Aoe(), new SpawnAbility() })
        {
            shared.m_attack.m_attackProjectile!.Component = payload;
            Check(Family(wand) == RangedAutomaticWeaponFamily.None, "non-Projectile payload rejected");
        }
        shared.m_attack.m_attackProjectile!.Component = new Projectile();
        shared.m_attack.m_attackType = Attack.AttackType.Horizontal;
        Check(Family(wand) == RangedAutomaticWeaponFamily.None, "melee magic weapon rejected");
        shared.m_attack.m_attackType = Attack.AttackType.Projectile;
        shared.m_skillType = Skills.SkillType.BloodMagic;
        Check(Family(wand) == RangedAutomaticWeaponFamily.None, "BloodMagic excluded");
        shared.m_skillType = Skills.SkillType.ElementalMagic;
        shared.m_itemType = ItemDrop.ItemData.ItemType.Ammo;
        Check(Family(wand) == RangedAutomaticWeaponFamily.None, "ammo excluded");
        shared.m_itemType = ItemDrop.ItemData.ItemType.TwoHandedWeapon;
        foreach (var (animation, family) in new[]
        {
            ("staff_fireball", RangedAutomaticWeaponFamily.FireballStaff),
            ("staff_rapidfire", RangedAutomaticWeaponFamily.RapidStaff),
            ("staff_lightningshot", RangedAutomaticWeaponFamily.ReloadStaff)
        })
        {
            shared.m_attack.m_attackAnimation = animation;
            Check(Family(wand) == family, "existing two-handed staff classification");
        }
    }

    private static void CheckOriginals()
    {
        var wand = Weapon("UnownedWand");
        var db = Db(wand);
        Reapply(db);
        Override(db, wand);
        Reapply(db);
        Check(Shared(wand).m_secondaryAttack.m_attackAnimation == "" && !Preserved(wand), "same-world Off/reapply restores empty original");
        Override(db, wand);
        var otherWorld = Db(wand);
        Reapply(otherWorld);
        Check(Shared(wand).m_secondaryAttack.m_attackAnimation == "" && !Preserved(wand), "shared prefab in new ObjectDB restores empty original");

        var staff = Weapon("OtherNativeWand", "other_secondary");
        var nativeDb = Db(staff);
        Reapply(nativeDb);
        Shared(staff).m_secondaryAttack.m_attackEitr = 42;
        Reapply(nativeDb);
        Check(Shared(staff).m_secondaryAttack.m_attackEitr == 42, "untouched native costs are not cached/restored");
        Override(nativeDb, staff);
        var differentPrefab = Weapon(staff.name, "new_prefab_attack");
        Reapply(Db(differentPrefab));
        Check(Shared(differentPrefab).m_secondaryAttack.m_attackAnimation == "new_prefab_attack", "same-name fresh prefab does not inherit stale snapshot");
        Reapply(Db(staff));
        Check(Shared(staff).m_secondaryAttack.m_attackAnimation == "other_secondary", "native restored across ObjectDB replacement");
        Shared(staff).m_secondaryAttack.m_attackEitr = 65;
        Reapply(Db(staff));
        Check(Shared(staff).m_secondaryAttack.m_attackEitr == 65, "restored baseline relinquishes ownership for future live updates");
    }

    private static void CheckLiveCosts(bool installed)
    {
        var settings = new[]
        {
            ("BMP_FlameScepter", ConfigSetup._flameScepterSource!, ConfigSetup._flameScepterMagicSourceSecondary!),
            ("BMP_IceScepter", ConfigSetup._iceScepterSource!, ConfigSetup._iceScepterMagicSourceSecondary!),
            ("BMP_LightningScepter", ConfigSetup._lightningScepterSource!, ConfigSetup._lightningScepterMagicSourceSecondary!)
        };
        foreach (var (name, source, drain) in settings)
        {
            var staff = Weapon(name, "native_secondary");
            var db = Db(staff);
            Reapply(db);
            Check(Preserved(staff), name + " native preserved independently of plugin presence");
            foreach (MagicSource mode in new[] { MagicSource.Stamina, MagicSource.Eitr, MagicSource.Both })
            {
                Override(db, staff);
                source.Value = mode;
                drain.Value = 123;
                Reapply(db);
                Attack attack = Shared(staff).m_secondaryAttack;
                float stamina = installed && mode != MagicSource.Eitr ? 123 : 0;
                float eitr = installed ? (mode != MagicSource.Stamina ? 123 : 0) : 80;
                Check(attack.m_attackAnimation == "native_secondary", name + " YAML removal/preset:none restores native structure");
                Check(attack.m_attackStamina == stamina && attack.m_attackEitr == eitr, name + " restores CURRENT " + mode + " cost");
            }
            Override(db, staff);
            source.Value = MagicSource.Eitr;
            drain.Value = 54;
            Reapply(Db(staff));
            Check(Shared(staff).m_secondaryAttack.m_attackEitr == (installed ? 54 : 80), name + " new world reads current cost");
            source.Value = (MagicSource)99;
            drain.Value = 299;
            Reapply(Db(staff));
            Check(Shared(staff).m_secondaryAttack.m_attackEitr == (installed ? 54 : 80), name + " unknown source leaves costs unchanged");
        }
    }

    private sealed class MagicPluginMarker { }
}
