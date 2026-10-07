namespace UnityEngine
{
    public sealed class GameObject
    {
        public string name;
        public ItemDrop? Item;
        public object? Component;
        public readonly List<object> ExtraComponents = new();
        public readonly List<GameObject> Children = new();
        public GameObject(string name, ItemDrop? item = null, object? component = null) { this.name = name; Item = item; Component = component; }
        public T? GetComponent<T>() where T : class => Item as T ?? Component as T ?? ExtraComponents.OfType<T>().FirstOrDefault();
        public T? GetComponentInChildren<T>(bool includeInactive = false) where T : class =>
            GetComponent<T>() ?? Children.Select(child => child.GetComponentInChildren<T>(includeInactive)).FirstOrDefault(component => component != null);
    }

    public sealed class AssetBundle(params string[] names)
    {
        public int ReadCount;
        public string[] GetAllAssetNames() { ReadCount++; return names; }
    }
}

public sealed class Attack
{
    public enum AttackType { Horizontal, Vertical, Projectile, None, Area }
    public AttackType m_attackType;
    public string m_attackAnimation = "";
    public UnityEngine.GameObject? m_attackProjectile;
    public float m_attackEitr;
    public float m_attackStamina;
    public float m_attackHealth;
    public float m_attackHealthPercentage;
    public bool m_perBurstResourceUsage;
    public bool m_requiresReload;
    public Attack Clone() => (Attack)MemberwiseClone();
}

public interface IProjectile { }
public sealed class Projectile : IProjectile { public UnityEngine.GameObject? m_spawnOnHit; }
public sealed class Aoe : IProjectile { }
public sealed class SpawnAbility : IProjectile { public UnityEngine.GameObject[] m_spawnPrefab = Array.Empty<UnityEngine.GameObject>(); }
public static class Skills
{
    public enum SkillType { None, Swords, Knives, Clubs, Polearms, Spears, Blocking, Axes, Bows, ElementalMagic, BloodMagic, Unarmed, Pickaxes, Crossbows, Farming }
}

public sealed class ItemDrop
{
    public string name = "";
    public ItemData m_itemData = new();
    public sealed class ItemData
    {
        public enum ItemType { None, OneHandedWeapon, TwoHandedWeapon, Bow, Ammo, AmmoNonEquipable, Shield, TwoHandedWeaponLeft }
        public UnityEngine.GameObject? m_dropPrefab;
        public Dictionary<string, string> m_customData = new();
        public SharedData m_shared = new();
        public sealed class SharedData
        {
            public Attack m_secondaryAttack = new();
            public Attack m_attack = new();
            public Skills.SkillType m_skillType;
            public ItemType m_itemType;
            public string m_ammoType = "";
        }
    }
}

public sealed class ObjectDB
{
    public readonly List<UnityEngine.GameObject> m_items = new();
}

namespace BepInEx
{
    public sealed class PluginInfo(object instance)
    {
        public object Instance = instance;
        public PluginMetadata Metadata = new();
    }
    public sealed class PluginMetadata { public Version Version = new(1, 1, 4); }
}

namespace BepInEx.Bootstrap
{
    public static class Chainloader
    {
        public static readonly Dictionary<string, BepInEx.PluginInfo> PluginInfos = new();
    }
}

namespace BepInEx.Configuration
{
    public abstract class ConfigEntryBase { public abstract object BoxedValue { get; set; } }
    public sealed class ConfigEntry<T>(T value) : ConfigEntryBase where T : notnull
    {
        public T Value = value;
        public override object BoxedValue { get => Value; set => Value = (T)value; }
    }
}

namespace ItemManager
{
    // Match the private field accessed in Wizardry 1.2.0; null exercises discovery failure.
    public static class PrefabManager
    {
        private static List<UnityEngine.GameObject>? prefabs = new();
        public static void Register(UnityEngine.GameObject item) => prefabs!.Add(item);
        public static void FailDiscovery() => prefabs = null;
        public static void Reset() => prefabs = new();
    }
}

namespace SecondaryAttacks
{
    internal static class SecondaryAttackManager
    {
        public static Attack CloneAttack(Attack? source) => source?.Clone() ?? new Attack();
    }

    internal static class SecondaryAttacksPlugin
    {
        internal const string MagicPluginGuid = "blacks7ar.MagicPlugin";
        internal const string ShieldMeBruhPluginGuid = "vapok.mods.shieldmebruh";
        internal enum Toggle { On, Off }
        internal static readonly BepInEx.Configuration.ConfigEntry<Toggle> AutoEquipLastShield = new(Toggle.On);
        public static readonly TestLogger ModLogger = new();
    }

    internal sealed class TestLogger
    {
        public readonly List<string> Info = new();
        public readonly List<string> Warnings = new();
        public void LogInfo(string message) => Info.Add(message);
        public void LogWarning(string message) => Warnings.Add(message);
        public void LogError(string message) => throw new Exception(message);
    }
}

public class Humanoid
{
    public ItemDrop.ItemData? RightItem, LeftItem;
    public readonly List<ItemDrop.ItemData> Equipped = new();
    public bool EquipItem(ItemDrop.ItemData item, bool effects) { Equipped.Add(item); LeftItem = item; return true; }
}

public sealed class Player : Humanoid
{
    public static Player? m_localPlayer;
    public Dictionary<string, string> m_customData = new();
    private readonly Inventory inventory = new();
    public Inventory GetInventory() => inventory;
}

public sealed class Inventory
{
    private readonly List<ItemDrop.ItemData> items = new();
    public List<ItemDrop.ItemData> GetAllItems() => items;
}

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method, params Type[] args) { }
    }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfix : Attribute { }
}
