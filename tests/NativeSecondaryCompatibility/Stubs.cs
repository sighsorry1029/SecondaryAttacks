namespace UnityEngine
{
    public sealed class GameObject
    {
        public string name;
        public ItemDrop? Item;
        public object? Component;
        public GameObject(string name, ItemDrop? item = null, object? component = null) { this.name = name; Item = item; Component = component; }
        public T? GetComponent<T>() where T : class => Item as T ?? Component as T;
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
    public bool m_requiresReload;
    public Attack Clone() => (Attack)MemberwiseClone();
}

public interface IProjectile { }
public sealed class Projectile : IProjectile { }
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
        public enum ItemType { None, OneHandedWeapon, TwoHandedWeapon, Bow, Ammo, AmmoNonEquipable }
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
    public sealed class PluginInfo(object instance) { public object Instance = instance; }
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
        public static readonly TestLogger ModLogger = new();
    }

    internal sealed class TestLogger
    {
        public readonly List<string> Info = new();
        public readonly List<string> Warnings = new();
        public void LogInfo(string message) => Info.Add(message);
        public void LogWarning(string message) => Warnings.Add(message);
    }
}
