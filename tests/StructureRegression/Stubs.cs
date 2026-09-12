// Dependency doubles only. The compiler and rebind logic are linked from production.
namespace UnityEngine
{
    internal sealed class GameObject
    {
        public string name = "Weapon";
        public object? Component;
        public T? GetComponent<T>() where T : class => Component as T;
    }
}

internal sealed class Attack
{
    public enum AttackType { Melee, Projectile }
    public AttackType m_attackType = AttackType.Projectile;
    public string m_attackAnimation = "attack";
    public string Label = "primary";
}

internal sealed class ItemDrop
{
    public ItemData m_itemData = new();
    internal sealed class ItemData
    {
        public UnityEngine.GameObject? m_dropPrefab;
        public SharedData m_shared = new();
        internal sealed class SharedData
        {
            public Attack m_attack = new();
            public Attack m_secondaryAttack = new() { Label = "original" };
        }
    }
}

internal sealed class ObjectDB
{
    public static ObjectDB? instance;
}

internal sealed class Inventory
{
    public List<ItemDrop.ItemData> Items { get; } = new();
    public List<ItemDrop.ItemData> GetAllItems() => Items;
}

internal sealed class Player
{
    public static Player? m_localPlayer;
    public Inventory? Inventory;
    public Inventory? GetInventory() => Inventory;
}

namespace SecondaryAttacks
{
    internal sealed class NormalizedWeaponConfig
    {
        public bool Enabled { get; set; } = true;
        public bool HasEnabledMeleeFeatureConfig { get; set; }
        public NormalizedSecondaryModeConfig? Secondary { get; set; }
    }

    internal sealed class NormalizedSecondaryModeConfig
    {
        public string Type { get; set; } = "";
        public string CopyFrom { get; set; } = "";
        public NormalizedProjectileSecondaryConfig? Projectile { get; set; }
    }

    internal sealed class NormalizedProjectileSecondaryConfig
    {
        public string Preset { get; set; } = "burst";
    }

    internal sealed class SecondaryAttackDefinition
    {
        public string Kind = "configured";
        public bool AppliesSecondaryOverride = true;
        public bool DynamicOriginalSecondary;
        public Attack? CooldownFallbackSecondaryAttack;
        public Attack? ConfiguredSecondaryAttack;
    }

    internal sealed class SecondaryAttackAppliedWorldSnapshot
    {
        public int ApplyRevision;
        public Dictionary<string, SecondaryAttackDefinition> DefinitionsByPrefabName { get; } = new();
    }

    internal static class ProjectilePresetCooldownPolicy
    {
        public static bool UsesDynamicOriginalSecondary(SecondaryAttackDefinition definition) => definition.DynamicOriginalSecondary;
    }

    internal sealed class TestLogger
    {
        public List<string> Warnings { get; } = new();
        public void LogWarning(string warning) => Warnings.Add(warning);
    }

    internal static class SecondaryAttacksPlugin
    {
        public static TestLogger ModLogger { get; } = new();
    }

    internal static class SecondaryAttackManager
    {
        // A storage double for the old Manager API, required only when running HEAD.
        // Revised production rebind owns its real ConditionalWeakTable itself.
        private static readonly Dictionary<ItemDrop.ItemData, int> BaselineRevisions = new();
        public static List<string> Calls { get; } = new();
        public static bool ResolveCopy = true;
        public static bool BuilderResult = true;
        public static bool ThrowDuringBuild;
        public static string? LastCopySource;

        public static void Reset()
        {
            Calls.Clear();
            SecondaryAttacksPlugin.ModLogger.Warnings.Clear();
            ResolveCopy = true;
            BuilderResult = true;
            ThrowDuringBuild = false;
            LastCopySource = null;
            ObjectDB.instance = new ObjectDB();
            Player.m_localPlayer = null;
        }

        private static bool Build(string kind, out SecondaryAttackDefinition? definition)
        {
            Calls.Add(kind);
            definition = BuilderResult ? new SecondaryAttackDefinition { Kind = kind } : null;
            return BuilderResult;
        }

        public static SecondaryAttackDefinition CreateEffectOnlyDefinition(string name, NormalizedWeaponConfig config)
        {
            Calls.Add("effect");
            return new SecondaryAttackDefinition { Kind = "effect", AppliesSecondaryOverride = false };
        }

        public static bool TryCreateSummonEmpowerDefinition(string n, ItemDrop.ItemData.SharedData s, Attack a, NormalizedWeaponConfig c, out SecondaryAttackDefinition? d) => Build("summonEmpower", out d);
        public static bool TryCreateShieldConvertDefinition(string n, ItemDrop.ItemData.SharedData s, Attack a, NormalizedWeaponConfig c, out SecondaryAttackDefinition? d) => Build("shieldConvert", out d);
        public static bool TryCreateCustomPayloadDefinition(string n, ItemDrop.ItemData.SharedData s, Attack a, NormalizedWeaponConfig c, out SecondaryAttackDefinition? d) => Build("projectile", out d);
        public static bool TryCreateAftershockDefinition(SecondaryAttackDefinitionBuildContext b, string n, ItemDrop.ItemData.SharedData s, Attack a, NormalizedWeaponConfig c, out SecondaryAttackDefinition? d) => Build("aftershock", out d);
        public static bool TryCreateFractureLineDefinition(SecondaryAttackDefinitionBuildContext b, string n, Attack a, NormalizedWeaponConfig c, out SecondaryAttackDefinition? d) => Build("fractureLine", out d);
        public static bool TryCreateSecondaryOverrideDefinition(string n, string source, Attack primary, Attack secondary, NormalizedWeaponConfig c, out SecondaryAttackDefinition? d) => Build("copy", out d);

        public static bool TryResolveSecondarySourceAttack(ObjectDB db, string source, out Attack? attack, out string reason)
        {
            Calls.Add("resolve-copy");
            LastCopySource = source;
            attack = ResolveCopy ? new Attack() : null;
            reason = "copy source missing.";
            return ResolveCopy;
        }

        public static int GetRuntimeWeaponAppliedWorldRevision(ItemDrop.ItemData weapon) => BaselineRevisions.GetValueOrDefault(weapon, -1);
        public static void SetRuntimeWeaponAppliedWorldRevision(ItemDrop.ItemData weapon, int revision) => BaselineRevisions[weapon] = revision;
        public static Attack ResolveSourceAttack(ObjectDB db, ItemDrop drop, SecondaryAttackDefinition definition)
        {
            Calls.Add("resolve-rebind");
            return drop.m_itemData.m_shared.m_attack;
        }

        public static Attack BuildSecondaryAttack(Attack source, SecondaryAttackDefinition definition)
        {
            Calls.Add("build-rebind");
            if (ThrowDuringBuild) throw new InvalidOperationException("injected build failure");
            return new Attack { Label = "configured" };
        }

        public static void NormalizeCopiedProjectileAim(Attack attack, SecondaryAttackDefinition definition) => Calls.Add("normalize-rebind");
        public static Attack CloneAttack(Attack? source)
        {
            Calls.Add("clone:" + (source?.Label ?? "null"));
            return new Attack { Label = source?.Label ?? "null" };
        }
    }
}
