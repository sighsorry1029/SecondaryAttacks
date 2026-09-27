using BepInEx;
using BepInEx.Bootstrap;
using SecondaryAttacks;
using UnityEngine;

internal sealed class WizardryPlugin { }
internal sealed class MagicSupremacyPlugin
{
    public static AssetBundle? MainAssetBundle;
}

internal static class Program
{
    private static int _assertions;

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        _assertions++;
    }

    private static GameObject Item(string name, string animation, float eitr = 0f, GameObject? payload = null)
    {
        return new GameObject(name, new ItemDrop
        {
            m_itemData = new()
            {
                m_shared = new()
                {
                    m_secondaryAttack = new Attack
                    {
                        m_attackAnimation = animation, m_attackEitr = eitr, m_attackProjectile = payload
                    }
                }
            }
        });
    }

    private static Attack Secondary(GameObject item) => item.Item!.m_itemData.m_shared.m_secondaryAttack;
    private static bool Preserved(GameObject item) =>
        NativeSecondaryAttackCompat.ShouldPreserveNativeSecondary(item.name, item.Item!);

    private static int Main(string[] args)
    {
        string mode = args.Single();
        if (mode.StartsWith("hunter")) return HunterTests.Run(mode);
        if (mode is "onehanded" or "magicplugin" or "magicplugin-missing")
        {
            return OneHandedTests.Run(mode);
        }
        bool wizardry = mode is "wizardry" or "both" or "fallback";
        bool magic = mode is "magic" or "both";
        bool discoveryFailure = mode == "fallback";
        if (wizardry) Chainloader.PluginInfos.Add(NativeSecondaryAttackCompat.WizardryGuid, new PluginInfo(new WizardryPlugin()));
        if (magic) Chainloader.PluginInfos.Add(NativeSecondaryAttackCompat.MagicSupremacyGuid, new PluginInfo(new MagicSupremacyPlugin()));

        var db = new ObjectDB();
        var staves = new[]
        {
            Item("StaffBlackforest_TW", "staff_shield", 26, new("StaffRoot_spawn_TW")),
            Item("StaffSwamp_TW", "StaffSwampSecondary_TW", 38, new("StaffSwamp_SpawnPoisoncloud_TW")),
            Item("StaffMountain_TW", "StaffMountainSecondary_TW", 34, new("StaffMountainHeal_aoe_TW")),
            Item("StaffPlains_TW", "StaffPlains_Secondary_TW", 46, new("StaffPlains_SpawnMeteor_TW")),
            Item("StaffMistlands_TW", "StaffMistlandsSecondary_TW", 50, new("StaffMistlands_NovaAOE_TW"))
        };
        db.m_items.AddRange(staves);
        var summons = new[] { Item("StaffSurtling_TW", ""), Item("StaffGolem_TW", "") };
        db.m_items.AddRange(summons);
        var unrelated = Item("AnotherModsStaff_TW", "foreign_attack");
        var magicStaff = Item("StaffFirecallerBlaster_DO", "magic_secondary", 17, new("MagicPayload"));
        var discoveredMagic = Item("FutureMagicStaff_DO", "future_magic");
        var emptyMagic = Item("FutureEmptyMagicStaff_DO", "");
        var discoveredWizardry = Item("FutureWizardryStaff_TW", "future_wizardry");
        db.m_items.AddRange(new[] { unrelated, magicStaff, discoveredMagic, emptyMagic, discoveredWizardry });
        db.m_items.Add(new GameObject("non-item"));
        db.m_items.Add(null!);

        // Registry and bundle are initially unavailable/empty. Known names still
        // work, and discovery must retry once the owner finishes registration.
        if (discoveryFailure) ItemManager.PrefabManager.FailDiscovery();
        NativeSecondaryAttackCompat.PrepareForApply(db);
        foreach (GameObject staff in staves) Check(Preserved(staff) == wizardry, staff.name + " plugin guard");
        Check(Preserved(magicStaff) == magic, "Magic Supremacy plugin guard");
        Check(!Preserved(unrelated), "_TW suffix does not imply Wizardry ownership");
        foreach (GameObject summon in summons) Check(!Preserved(summon), summon.name + " keeps automatic eligibility");
        Check(!Preserved(discoveredWizardry), "undiscovered item is not owned");
        if (!discoveryFailure) ItemManager.PrefabManager.Register(discoveredWizardry);
        MagicSupremacyPlugin.MainAssetBundle = new AssetBundle(
            "assets/test/futuremagicstaff_do.prefab", "assets\\test\\FutureEmptyMagicStaff_DO.PREFAB", "assets/test/texture.png");
        NativeSecondaryAttackCompat.PrepareForApply(db);
        Check(Preserved(discoveredWizardry) == (wizardry && !discoveryFailure), "Wizardry registry discovery/retry");
        Check(Preserved(discoveredMagic) == magic, "Magic Supremacy bundle discovery/retry and case handling");
        Check(!Preserved(emptyMagic), "empty discovered secondary stays eligible");

        var protectedItems = new List<GameObject>();
        if (wizardry) protectedItems.AddRange(staves);
        if (magic) protectedItems.Add(magicStaff);
        foreach (GameObject staff in protectedItems)
        {
            Attack native = Secondary(staff);
            string animation = native.m_attackAnimation;
            float eitr = native.m_attackEitr;
            GameObject? payload = native.m_attackProjectile;
            // Exercise the production ObjectDB store's ordering around an explicit
            // override, then remove that override as preset:none/reload would do.
            SecondaryAttackObjectDbStateStore.CaptureSecondaryAttack(db, staff.name, native);
            staff.Item!.m_itemData.m_shared.m_secondaryAttack = new Attack { m_attackAnimation = "SA_override" };
            SecondaryAttackObjectDbStateStore.Restore(db);
            NativeSecondaryAttackCompat.PrepareForApply(db);
            Check(Secondary(staff).m_attackAnimation == animation, staff.name + " restores native animation");
            Check(Secondary(staff).m_attackEitr == eitr, staff.name + " restores native cost");
            Check(ReferenceEquals(Secondary(staff).m_attackProjectile, payload), staff.name + " retains Aoe/SpawnAbility/other payload");
            Check(!ReferenceEquals(Secondary(staff), native), staff.name + " snapshot does not alias the mutable Attack");
            Secondary(staff).m_attackAnimation = "SA_override_again";

            // A new ObjectDB can reuse already-mutated asset prefabs. Its own
            // store has no snapshot, so compatibility must retain the original.
            var nextDb = new ObjectDB();
            nextDb.m_items.Add(staff);
            SecondaryAttackObjectDbStateStore.Restore(nextDb);
            NativeSecondaryAttackCompat.PrepareForApply(nextDb);
            Check(Secondary(staff).m_attackAnimation == animation, staff.name + " survives ObjectDB replacement");
        }

        var emptyItems = new List<GameObject>();
        if (wizardry) emptyItems.AddRange(summons);
        if (magic) emptyItems.Add(emptyMagic);
        foreach (GameObject item in emptyItems)
        {
            Secondary(item).m_attackAnimation = "SA_auto_fallback";
            Check(!Preserved(item), item.name + " does not mistake automatic attack for native");
            var nextDb = new ObjectDB();
            nextDb.m_items.Add(item);
            NativeSecondaryAttackCompat.PrepareForApply(nextDb);
            Check(Secondary(item).m_attackAnimation == "", item.name + " restores empty baseline in another ObjectDB");
            Check(!Preserved(item), item.name + " remains eligible after restore");
        }

        Check(Secondary(unrelated).m_attackAnimation == "foreign_attack", "unrelated mod remains untouched");
        for (int i = 0; i < 3; i++) NativeSecondaryAttackCompat.PrepareForApply(db);
        Check(MagicSupremacyPlugin.MainAssetBundle.ReadCount == (magic ? 1 : 0), "bundle discovery is cached");
        Check(SecondaryAttacksPlugin.ModLogger.Warnings.Count == (discoveryFailure ? 1 : 0), "discovery failure warns only once");
        Check(SecondaryAttacksPlugin.ModLogger.Info.Count == (wizardry && !discoveryFailure ? 1 : 0) + (magic ? 1 : 0), "successful discovery logs only once");
        Console.WriteLine($"PASS {mode}: {_assertions} assertions");
        return 0;
    }
}
