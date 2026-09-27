using BepInEx;
using BepInEx.Bootstrap;
using SecondaryAttacks;
using UnityEngine;

internal sealed class HunterPlugin { }

internal static class HunterTests
{
    private static int assertions;
    private static void Check(bool condition, string description)
    {
        if (!condition) throw new Exception(description);
        assertions++;
    }

    private static GameObject Item(string name, ItemDrop.ItemData.ItemType type = ItemDrop.ItemData.ItemType.Bow)
    {
        var prefab = new GameObject(name, new ItemDrop());
        prefab.Item!.m_itemData.m_dropPrefab = prefab;
        prefab.Item.m_itemData.m_shared.m_itemType = type;
        prefab.Item.m_itemData.m_shared.m_secondaryAttack = new() { m_attackAnimation = "hunter", m_attackEitr = 12 };
        return prefab;
    }

    private static bool Skip(GameObject item, bool explicitOverride = false) =>
        HunterLegacyCompat.ShouldSkipWeapon(item.name, item.Item!, explicitOverride);

    internal static int Run(string mode)
    {
        bool loaded = mode != "hunter-absent";
        bool supported = mode is not ("hunter-version" or "hunter-contract");
        bool brokenRegistry = mode is "hunter-registry" or "hunter-contract";
        if (loaded)
        {
            var plugin = new PluginInfo(mode == "hunter-contract" ? new object() : new HunterPlugin());
            if (mode == "hunter-version") plugin.Metadata.Version = new Version(1, 2, 0);
            Chainloader.PluginInfos.Add(HunterLegacyCompat.PluginGuid, plugin);
        }
        HunterLegacyCompat.Initialize();
        int initialWarnings = SecondaryAttacksPlugin.ModLogger.Warnings.Count;
        Check(initialWarnings == (loaded && !supported ? 1 : 0), "unreviewed clone contract warns once");

        var bow = Item("BowGoldenStrider_DO");
        var sling = Item("SlingshotMeadows_DO");
        var crossbow = Item("CrossbowDemonHunter_DO", ItemDrop.ItemData.ItemType.TwoHandedWeapon);
        var offhand = Item("OffhandCrossbowBlackforest_DO", ItemDrop.ItemData.ItemType.Shield);
        offhand.Item!.m_itemData.m_shared.m_skillType = Skills.SkillType.Crossbows;
        offhand.Item.m_itemData.m_shared.m_secondaryAttack.m_attackAnimation = "0";
        if (brokenRegistry) ItemManager.PrefabManager.FailDiscovery();
        HunterLegacyCompat.PrepareForApply();
        foreach (var item in new[] { bow, sling, crossbow, offhand })
        {
            Check(Skip(item) == loaded, item.name + " automatic protection including offhand placeholder");
            Check(Skip(item, true) == loaded, item.name + " explicit override protection");
            Skip(item, true);
        }
        Check(SecondaryAttacksPlugin.ModLogger.Warnings.Count == initialWarnings + (loaded ? 4 + (brokenRegistry ? 1 : 0) : 0),
            "one warning per explicit override, no repeat warnings");
        int warnings = SecondaryAttacksPlugin.ModLogger.Warnings.Count;
        Check(!Skip(Item("BowHuntsman")), "vanilla unaffected");
        Check(!Skip(Item("OtherModBow_DO"), true), "suffix does not imply ownership");
        var futureBow = Item("HunterFutureBow");
        var futureSling = Item("HunterFutureSling");
        var futureShield = Item("HunterFutureShield", ItemDrop.ItemData.ItemType.Shield);
        var futureAmmo = Item("HunterFutureAmmo", ItemDrop.ItemData.ItemType.Ammo);
        futureAmmo.Item!.m_itemData.m_shared.m_skillType = Skills.SkillType.Bows;
        Check(!Skip(futureBow), "unregistered unknown name unaffected");
        if (!brokenRegistry)
        {
            foreach (var item in new[] { futureBow, futureSling, futureShield, futureAmmo }) ItemManager.PrefabManager.Register(item);
        }
        HunterLegacyCompat.PrepareForApply();
        Check(Skip(futureBow) == (loaded && !brokenRegistry), "retry empty registry and discover bow");
        Check(Skip(futureSling) == (loaded && !brokenRegistry), "Bow type covers custom slingshot skill");
        Check(!Skip(futureShield) && !Skip(futureAmmo), "discovery does not protect armor or ammo as weapons");

        // No native snapshot: live Hunter costs and attack identities survive repeated apply/world changes.
        var native = bow.Item!.m_itemData.m_shared.m_secondaryAttack;
        var db = new ObjectDB();
        db.m_items.AddRange(new[] { bow, sling, crossbow, offhand });
        for (int i = 0; i < 3; i++)
        {
            native.m_attackEitr++;
            HunterLegacyCompat.PrepareForApply();
            NativeSecondaryAttackCompat.PrepareForApply(db);
            Check(ReferenceEquals(native, bow.Item.m_itemData.m_shared.m_secondaryAttack) && native.m_attackEitr == 13 + i,
                "Hunter retains live attack ownership");
        }
        Check(SecondaryAttacksPlugin.ModLogger.Warnings.Count == warnings, "apply has no repeated diagnostics");
        if (loaded && !brokenRegistry)
        {
            ItemManager.PrefabManager.FailDiscovery();
            HunterLegacyCompat.PrepareForApply();
            Check(Skip(futureBow), "successful discovery cached without further registry reads");
        }

        ShieldTests(offhand, loaded);
        CloneTests(loaded && supported);
        HunterLegacyCompat.Dispose();
        Check(!Skip(bow) && !HunterLegacyCompat.IsOffhandCrossbow(offhand.Item.m_itemData), "dispose releases optional adapter");
        var source = new Attack();
        var afterDispose = Hunter_Legacy.AmmoUsageOverride.TrackClone(source);
        HunterLegacyCompat.ForgetPreparedClone(source, afterDispose);
        Check(Hunter_Legacy.AmmoUsageOverride.Has(afterDispose), "dispose does not clear Hunter tracking");
        ItemManager.PrefabManager.Reset();
        HunterLegacyCompat.Initialize();
        Check(Skip(bow) == loaded, "reinitialize retains known weapon protection");
        Console.WriteLine($"PASS {mode}: {assertions} assertions");
        return 0;
    }

    private static void ShieldTests(GameObject offhand, bool loaded)
    {
        const string selection = "SecondaryAttacks.LastShieldSelection";
        const string identity = "SecondaryAttacks.LastShieldId";
        var player = new Player();
        Player.m_localPlayer = player;
        LastEquippedShieldSystem.Initialize();
        var shield = Item("ShieldWood", ItemDrop.ItemData.ItemType.Shield).Item!.m_itemData;
        var weapon = Item("SwordIron", ItemDrop.ItemData.ItemType.OneHandedWeapon).Item!.m_itemData;
        var wrist = offhand.Item!.m_itemData;
        player.GetInventory().GetAllItems().AddRange(new[] { shield, wrist, weapon });
        LastEquippedShieldSystem.HandleEquip(player, shield, true, true);
        string remembered = player.m_customData[selection];
        LastEquippedShieldSystem.HandleEquip(player, wrist, true, true);
        Check((player.m_customData[selection] == remembered) == loaded, "offhand does not replace remembered shield when Hunter loaded");
        player.RightItem = weapon;
        LastEquippedShieldSystem.HandleEquip(player, weapon, true, true);
        Check(ReferenceEquals(player.LeftItem, loaded ? shield : wrist), "ordinary remembered shield still equips");

        // Saved by an older SA version; do not migrate or delete the stored identity.
        wrist.m_customData[identity] = Guid.NewGuid().ToString("N");
        player.m_customData[selection] = "v1:" + wrist.m_customData[identity];
        player.LeftItem = null;
        player.Equipped.Clear();
        LastEquippedShieldSystem.HandleEquip(player, weapon, true, true);
        Check(player.Equipped.Count == (loaded ? 0 : 1), "old offhand selection excluded during lookup");
        Check(player.m_customData[selection] == "v1:" + wrist.m_customData[identity], "stored data retained");

        player.LeftItem = null;
        LastEquippedShieldSystem.HandleEquip(player, shield, true, true);
        var duplicate = Item("ShieldOther", ItemDrop.ItemData.ItemType.Shield).Item!.m_itemData;
        duplicate.m_customData[identity] = shield.m_customData[identity];
        player.GetInventory().GetAllItems().Add(duplicate);
        player.Equipped.Clear();
        LastEquippedShieldSystem.HandleEquip(player, weapon, true, true);
        Check(player.Equipped.Count == 0, "duplicate shield identity guard remains");
        LastEquippedShieldSystem.Dispose();
    }

    private static void CloneTests(bool cleanup)
    {
        var source = new Attack();
        var unrelated = Hunter_Legacy.AmmoUsageOverride.TrackClone(new Attack());
        for (int i = 0; i < 100; i++)
        {
            var template = Hunter_Legacy.AmmoUsageOverride.TrackClone(source);
            HunterLegacyCompat.ForgetPreparedClone(source, template);
            Check(Hunter_Legacy.AmmoUsageOverride.Has(template) != cleanup, "prepared clone tracking removed only with verified plugin");
            var runtime = Hunter_Legacy.AmmoUsageOverride.TrackClone(template);
            Check(Hunter_Legacy.AmmoUsageOverride.StartsSecondary(runtime, template), "actual runtime clone still identifies secondary template");
            Check(!Hunter_Legacy.AmmoUsageOverride.Has(runtime), "runtime tracking consumed on start");
        }
        Check(Hunter_Legacy.AmmoUsageOverride.Has(unrelated), "unrelated clone preserved");
        var changedMapping = Hunter_Legacy.AmmoUsageOverride.TrackClone(new Attack());
        HunterLegacyCompat.ForgetPreparedClone(source, changedMapping);
        Check(Hunter_Legacy.AmmoUsageOverride.Has(changedMapping), "another postfix mapping is not removed");
        Hunter_Legacy.AmmoUsageOverride.TrackSelf(source);
        HunterLegacyCompat.ForgetPreparedClone(source, source);
        Check(Hunter_Legacy.AmmoUsageOverride.Has(source), "identity-returning Clone cannot erase source tracking");
        HunterLegacyCompat.ForgetPreparedClone(null!, source);
        HunterLegacyCompat.ForgetPreparedClone(source, null!);
    }
}

namespace Hunter_Legacy
{
    // Contract double; original DLL metadata and the production CloneAttack call
    // are checked separately by Verify-HunterContract.ps1.
    public static class AmmoUsageOverride
    {
        private static readonly Dictionary<Attack, Attack> CloneSources = new();
        public static Attack TrackClone(Attack source)
        {
            var clone = source.Clone();
            CloneSources[clone] = source;
            return clone;
        }
        public static void TrackSelf(Attack attack) => CloneSources[attack] = attack;
        public static bool Has(Attack clone) => CloneSources.ContainsKey(clone);
        public static bool StartsSecondary(Attack runtime, Attack secondary)
        {
            bool matched = CloneSources.TryGetValue(runtime, out var source) && ReferenceEquals(source, secondary);
            CloneSources.Remove(runtime);
            return matched;
        }
    }
}
