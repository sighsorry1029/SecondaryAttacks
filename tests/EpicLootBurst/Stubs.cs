using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace BepInEx
{
    public class PluginInfo
    {
        public object Instance = new EpicLoot.TestPlugin();
        public Metadata Metadata = new Metadata();
    }
    public class Metadata { public Version Version = new Version(0, 14, 13); }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static Dictionary<string, BepInEx.PluginInfo> PluginInfos = new Dictionary<string, BepInEx.PluginInfo>(); }
}
namespace UnityEngine
{
    public struct Vector3 { public static Vector3 zero; public static Vector3 operator -(Vector3 v) => v; }
    public struct Quaternion { public static Quaternion LookRotation(Vector3 v) => default; }
    public class Transform { public Vector3 forward; }
    public class GameObject
    {
        public string name;
        public GameObject(string name) { this.name = name; }
        public T AddComponent<T>() where T : new() => new T();
    }
    public static class Mathf
    {
        public static int Min(int a, int b) => Math.Min(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Clamp(int v, int min, int max) => Math.Min(max, Math.Max(min, v));
    }
}
public class HitData
{
    public DamageTypes m_damage;
    public struct DamageTypes { public float m_damage; }
}
public class MessageHud { public enum MessageType { Center } }
public class ItemDrop
{
    public class ItemData
    {
        public SharedData m_shared = new SharedData();
        public UnityEngine.GameObject m_dropPrefab = new UnityEngine.GameObject("Bolt");
        public int m_stack;
        public bool ValidPayload = true;
        public enum ItemType { Ammo, AmmoNonEquipable, Weapon }
        public class SharedData
        {
            public string m_ammoType = "bolt";
            public ItemType m_itemType = ItemType.Ammo;
            public HitData.DamageTypes m_damages = new HitData.DamageTypes { m_damage = 100 };
        }
    }
}
public class Inventory
{
    public readonly List<ItemDrop.ItemData> Items = new List<ItemDrop.ItemData>();
    public List<ItemDrop.ItemData> GetAllItems() => Items;
    public bool RemoveItem(ItemDrop.ItemData item, int amount)
    {
        if (item.m_stack < amount) throw new Exception("partial/unavailable payment");
        item.m_stack -= amount;
        if (item.m_stack == 0) Items.Remove(item);
        return true;
    }
    public int Total => Items.Sum(i => i.m_stack);
}
public class Humanoid
{
    public Inventory Inventory = new Inventory();
    public UnityEngine.Transform transform = new UnityEngine.Transform();
    public int Messages, Resets, SideEffects;
    public Inventory GetInventory() => Inventory;
    public void Message(MessageHud.MessageType type, string text) { Messages++; }
    public bool IsFlying() => false;
    public void Land() { }
    public void TakeOff() { }
    public void ApplyPushback(UnityEngine.Vector3 v, float f) { SideEffects++; }
    public void Damage(HitData hit) { }
    public void ResetLoadedWeapon() { Resets++; ((Player)this).m_weaponLoaded = null; }
}
public class Player : Humanoid
{
    public static Player? m_localPlayer;
    public ItemDrop.ItemData? m_weaponLoaded;
}
public class Attack
{
    public Humanoid m_character = null!;
    public ItemDrop.ItemData m_weapon = new ItemDrop.ItemData();
    public ItemDrop.ItemData? m_ammoItem, m_lastUsedAmmo;
    public int m_projectiles = 1;
    public float m_projectileAccuracy = 10;
    public bool m_perBurstResourceUsage = true, m_requiresReload = true;
    public bool m_toggleFlying, m_consumeItem;
    public float m_recoilPushback = 1, m_selfDamage;
    public int Stops, ResourcePayments, ExtraPayments, Rolls, Prepares;
    public bool ResourcesAvailable = true, ThrowPrepare;
    public Effects m_burstEffect = new Effects();
    public ItemDrop.ItemData GetWeapon() => m_weapon;
    public void Stop() { Stops++; }
    public void ConsumeItem() { }
    public void GetProjectileSpawnPoint(out UnityEngine.Vector3 p, out UnityEngine.Vector3 d) { p = d = default; }
    public static ItemDrop.ItemData? FindAmmo(Humanoid c, ItemDrop.ItemData w) => c.Inventory.Items.FirstOrDefault();
    [MethodImpl(MethodImplOptions.NoInlining)] public void OnAttackTrigger() { }
    [MethodImpl(MethodImplOptions.NoInlining)] public void FireProjectileBurst() { }
}
public class Effects
{
    public bool HasEffects() => false;
    public void Create(UnityEngine.Vector3 p, UnityEngine.Quaternion q) { }
}
namespace SecondaryAttacks
{
    internal static class SecondaryAttacksPlugin
    {
        internal static readonly Logger ModLogger = new Logger();
    }
    internal class Logger
    {
        internal void LogInfo(string text) => Console.WriteLine(text);
        internal void LogWarning(string text) => Console.WriteLine(text);
    }
    internal enum SecondaryAttackPreset { Burst, Other }
    internal class ProjectileSecondaryBehavior { internal SecondaryAttackPreset Preset = SecondaryAttackPreset.Burst; internal int ProjectileCount = 3, AmmoConsumption = 1; }
    internal class SecondaryAttackDefinition { internal object Behavior = new ProjectileSecondaryBehavior(); }
    internal class ActiveSecondaryAttack { internal SecondaryAttackDefinition Definition = new SecondaryAttackDefinition(); internal bool BurstAmmoConserved; }
    internal static class SecondaryAttackRuntimeContext
    {
        internal static readonly Dictionary<Attack, ActiveSecondaryAttack> Active = new Dictionary<Attack, ActiveSecondaryAttack>();
        internal static bool TryGetActiveAttack(Attack a, out ActiveSecondaryAttack? active) => Active.TryGetValue(a, out active);
    }
    internal static class SecondaryAttackManager
    {
        internal struct ReloadStateConsumptionScope { }
        internal static int PersistedConsumed;
        internal static ReloadStateConsumptionScope BeginReloadStateConsumption(Attack a) => default;
        internal static void EndReloadStateConsumption(ref ReloadStateConsumptionScope scope) { }
        internal static void ConsumePersistedReloadedWeaponState(Player p, ItemDrop.ItemData w) { PersistedConsumed++; }
    }
    internal static partial class SecondaryAttackRuntimeFacade
    {
        private static bool TryConsumeAttackResources(Attack attack, bool stopAttackOnFailure, bool flashHudOnFailure)
        {
            if (!attack.ResourcesAvailable) return false;
            attack.ResourcePayments++;
            return true;
        }
        internal static void OuterTrigger(Attack a)
        {
            TrySelectConfiguredAmmo(a.m_character, a.m_weapon, a.m_character.Inventory, "bolt", 1, out ConfiguredAmmoContext c);
            CommitConfiguredAmmo(a, c);
            ConsumePerBurstResourcesIfNeeded(a);
            ApplyAttackTriggerSideEffects(a);
        }
    }
    internal static partial class ProjectileRuntimeSystem
    {
        private const int MaxBurstShotCount = 16;
        private static readonly HashSet<Attack> DeferredBurstFireReloadResets = new HashSet<Attack>();
        internal static readonly List<(int Count, float Damage, float Accuracy)> Shots = new List<(int, float, float)>();
        internal static bool ThrowSpawn;
        internal struct ProjectileLaunchData { internal float Damage, Accuracy; }
        internal static bool TryValidateBurstPresetPayload(Attack a, SecondaryAttackDefinition d, SecondaryAttackPreset p, ItemDrop.ItemData? ammo) => ammo != null && ammo.ValidPayload;
        private static ProjectileLaunchData CreateLaunchData(Attack a, SecondaryAttackDefinition d) => new ProjectileLaunchData { Damage = a.m_weapon.m_shared.m_damages.m_damage, Accuracy = a.m_projectileAccuracy };
        private static bool TryValidateProjectilePayload(Attack a, SecondaryAttackDefinition d, ProjectileLaunchData l) => true;
        private static void PrepareCustomProjectileBurst(Attack a) { }
        private static void OrientPlayerBodyToCurrentAim(Attack a) { }
        private static void OrientNonPlayerCharacterBodyToProjectileAim(Attack a, UnityEngine.Vector3 v) { }
        private static UnityEngine.Vector3 ApplyLaunchAngle(Attack a, UnityEngine.Vector3 v) => v;
        private static void SpawnPrimaryProjectileCluster(Attack a, ProjectileLaunchData l, UnityEngine.Vector3 p, UnityEngine.Vector3 d)
        {
            if (ThrowSpawn) throw new InvalidOperationException("spawn failure");
            Shots.Add((a.m_projectiles, l.Damage, l.Accuracy));
        }
        private static string GetPresetName(SecondaryAttackPreset p) => p.ToString();
        private class BurstFireController { public void Initialize(Attack a, SecondaryAttackDefinition d, int count) { } }
        internal static void Finish(Attack a) => ConsumeBurstFireReload(a);
    }
}
namespace EpicLoot { public class TestPlugin { } }
namespace EpicLoot.MagicItemEffects
{
    // Deterministic stand-ins for the reviewed 0.14.13 callbacks. The harness
    // uses real HarmonyX detours, including patches on these patch methods.
    [HarmonyPatch]
    public static class MultiShot
    {
        private enum PendingShotType { None, TripleBow, DoubleMagic }
        private static PendingShotType _pendingShot;
        public static bool IsTripleShotActive;
        public static int ShotProjectiles;
        public static Queue<bool> Procs = new Queue<bool>();
        public static int EffectCount = 3;
        public static int Pending => (int)_pendingShot;
        public static void SeedGlobals() { _pendingShot = PendingShotType.DoubleMagic; IsTripleShotActive = true; ShotProjectiles = 7; }
        [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger)), HarmonyPrefix, MethodImpl(MethodImplOptions.NoInlining)]
        public static void Attack_OnAttackTrigger_Prefix(Attack __instance)
        {
            __instance.Rolls++;
            bool proc = Procs.Count > 0 && Procs.Dequeue();
            _pendingShot = proc ? PendingShotType.TripleBow : PendingShotType.None;
            IsTripleShotActive = proc;
            ShotProjectiles = proc ? EffectCount : 0;
        }
        [HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst)), HarmonyPrefix, MethodImpl(MethodImplOptions.NoInlining)]
        public static void Attack_FireProjectileBurst_Prefix(Attack __instance, ref HitData.DamageTypes? __state)
        {
            __instance.Prepares++;
            __state = null;
            if (_pendingShot == PendingShotType.None) return;
            __state = __instance.GetWeapon().m_shared.m_damages;
            __instance.GetWeapon().m_shared.m_damages.m_damage *= 0.4f;
            __instance.m_projectiles *= EffectCount;
            __instance.m_projectileAccuracy *= 1.25f;
            __instance.ExtraPayments++;
            if (__instance.ThrowPrepare) throw new InvalidOperationException("prepare failure");
        }
        [HarmonyPatch(typeof(Attack), nameof(Attack.FireProjectileBurst)), HarmonyFinalizer, MethodImpl(MethodImplOptions.NoInlining)]
        public static void Attack_FireProjectileBurst_Finalizer(Attack __instance, HitData.DamageTypes? __state)
        {
            if (__state.HasValue) __instance.GetWeapon().m_shared.m_damages = __state.Value;
        }
    }
}
namespace EpicLoot.Magic.MagicItemEffects
{
    public class AmmoConservation
    {
        private static bool skipReload;
        public static Queue<bool> Procs = new Queue<bool>();
        public static int Calls;
        public static bool PendingReload { get => skipReload; set => skipReload = value; }
        public static class AmmoConservation_Attack_UseAmmo_Patch
        {
            public static void Postfix(Attack __instance, ref bool __result, ItemDrop.ItemData ammoItem)
            {
                Calls++;
                if (Procs.Count == 0 || !Procs.Dequeue()) return;
                __instance.m_character.Inventory.Items.Add(new ItemDrop.ItemData { m_stack = 1, m_dropPrefab = ammoItem.m_dropPrefab });
                if (__instance.m_requiresReload) skipReload = true;
            }
        }
    }
}
