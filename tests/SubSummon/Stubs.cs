// Boundary doubles only. The linked production source owns admission, origin
// marking, slots, RPC validation, faction restoration and load reconciliation.
using System.Reflection;
namespace System.Runtime.CompilerServices { internal static class IsExternalInit { } }

namespace UnityEngine
{
    public struct Vector3 { }
    public struct Quaternion { }
    public class Object
    {
        public static Func<GameObject, GameObject> Create = null!;
        public static int Created;
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object
        {
            Created++;
            return (T)(Object)Create((GameObject)(Object)original);
        }
        public static void Destroy(Object target) { }
    }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public T? GetComponent<T>() where T : class => gameObject.GetComponent<T>();
    }
    public class GameObject : Object
    {
        public readonly List<Component> Components = new();
        public T Add<T>(T component) where T : Component { component.gameObject = this; Components.Add(component); return component; }
        public T? GetComponent<T>() where T : class => Components.OfType<T>().FirstOrDefault();
    }
}

public static class Hashes
{
    public static int GetStableHashCode(this string value)
    {
        unchecked { int hash = 17; foreach (char c in value) hash = hash * 31 + c; return hash; }
    }
}
public readonly record struct ZDOID(long Id) { public static readonly ZDOID None = new(0); }
public class ZDO
{
    public ZDOID m_uid;
    public long Owner;
    public readonly Dictionary<int, object> Data = new();
    public bool IsOwner() => Owner == ZNet.Local;
    public long GetOwner() => Owner;
    public string GetString(int key, string fallback) => Data.TryGetValue(key, out object? v) ? (string)v : fallback;
    public long GetLong(int key, long fallback) => Data.TryGetValue(key, out object? v) ? (long)v : fallback;
    public float GetFloat(int key, float fallback) => Data.TryGetValue(key, out object? v) ? (float)v : fallback;
    public void Set(int key, string value) => Data[key] = value;
    public void Set(int key, long value) => Data[key] = value;
    public static KeyValuePair<int, int> GetHashZDOID(string name) => new((name + "_u").GetStableHashCode(), (name + "_i").GetStableHashCode());
    public void Set(KeyValuePair<int, int> key, ZDOID value) => Data[key.Key] = value;
    public ZDOID GetZDOID(KeyValuePair<int, int> key) => Data.TryGetValue(key.Key, out object value) ? (ZDOID)value : ZDOID.None;
}
public static class ZDOVars { public static readonly int s_health = "health".GetStableHashCode(); }
public class ZDOMan
{
    public static ZDOMan instance = null!;
    public readonly Dictionary<ZDOID, ZDO> Objects = new();
    public ZDO? GetZDO(ZDOID id) => Objects.TryGetValue(id, out ZDO value) ? value : null;
}
public static class ZDOExtraData
{
    public enum Type { String }
    public static List<ZDOID> GetAllZDOIDsWithHash(Type type, int key) => ZDOMan.instance.Objects.Values.Where(z => z.Data.ContainsKey(key)).Select(z => z.m_uid).ToList();
}
public class ZNetView : UnityEngine.Component
{
    public ZDO? Zdo;
    public ZDO? GetZDO() => Zdo;
}
public class ZNetPeer { public long m_uid; }
public class ZNet
{
    public static ZNet instance = new();
    public static long Local = 1;
    public bool Server = true;
    public bool IsServer() => Server;
    public ZNetPeer? GetServerPeer() => Server ? null : new() { m_uid = 1 };
}
public class ZNetScene
{
    public static ZNetScene instance = new();
    public void Destroy(UnityEngine.GameObject child)
    {
        ZDO? zdo = child.GetComponent<ZNetView>()?.Zdo;
        if (zdo != null) ZDOMan.instance.Objects.Remove(zdo.m_uid);
    }
}
public class Character : UnityEngine.Component
{
    public enum Faction { Players, PlayerSpawned, Undead }
    public Faction m_faction = Faction.Undead;
    public bool Tamed;
    public bool Dead;
    public bool IsTamed() => Tamed;
    public bool IsDead() => Dead;
    public void SetTamed(bool value) => Tamed = value;
    public Faction GetFaction() => m_faction;
}
public class Player : Character { public long GetPlayerID() => 123; }
public static class Skills { public enum SkillType { BloodMagic, Other } }
public class ItemDrop
{
    public class ItemData
    {
        public SharedData m_shared = new();
        public class SharedData { public Skills.SkillType m_skillType; }
    }
}
public class SpawnAbility
{
    // Deliberately private: the production accessors must resolve these fields.
    private Character m_owner;
    private ItemDrop.ItemData m_weapon;
    public SpawnAbility(Character owner, bool bloodMagic = true)
    {
        m_owner = owner;
        m_weapon = new() { m_shared = new() { m_skillType = bloodMagic ? Skills.SkillType.BloodMagic : Skills.SkillType.Other } };
    }
}
public class ZRoutedRpc
{
    public static ZRoutedRpc instance = new();
    public readonly Dictionary<string, Delegate> Handlers = new();
    public readonly List<(long Target, string Name, object[] Args)> Sent = new();
    public void Register<T, U>(string name, Action<long, T, U> handler) => Handlers.Add(name, handler);
    public void InvokeRoutedRPC(string name, params object[] args) => Sent.Add((1, name, args));
    public void InvokeRoutedRPC(long target, string name, params object[] args) => Sent.Add((target, name, args));
    public void Deliver(long sender, string name, params object[] args) => Handlers[name].DynamicInvoke(new object[] { sender }.Concat(args).ToArray());
}
