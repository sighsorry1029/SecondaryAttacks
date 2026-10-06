using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace HarmonyLib
{
    public static class AccessTools
    {
        public static FieldInfo? Field(Type type, string name) => type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        public static MethodInfo Method(Type type,string name,Type[]? parameters=null) => parameters==null ? type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance)! : type.GetMethod(name,parameters)!;
    }
    public class CodeInstruction
    {
        public OpCode opcode;
        public object? operand;
        public List<Label> labels=new();
        public List<object> blocks=new();
        public CodeInstruction(OpCode code,object? target=null) { opcode=code;operand=target; }
        public bool Calls(MethodInfo method)=>Equals(operand,method) && (opcode==OpCodes.Call || opcode==OpCodes.Callvirt);
    }
}
namespace UnityEngine
{
    public class Object
    {
        public string name = "";
        public static int Instances;
        public static GameObject? ThrowOnceOnPrefab;
        public static void Destroy(Object value) { }
        public static GameObject Instantiate(GameObject prefab, Transform parent, bool worldPositionStays = true)
        {
            if(ReferenceEquals(prefab,ThrowOnceOnPrefab)) { ThrowOnceOnPrefab=null;throw new InvalidOperationException("cosmetic instantiate failed"); }
            Instances++;
            GameObject clone = new(prefab.name);
            clone.transform.SetParent(parent, worldPositionStays);
            if (prefab.GetComponent<EquipmentVisual>() != null) clone.AddComponent<EquipmentVisual>();
            return clone;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject = null!;
        public Transform transform => gameObject.transform;
        public T? GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T? GetComponentInChildren<T>() where T : class => gameObject.GetComponentInChildren<T>();
        public void GetComponentsInChildren<T>(bool includeInactive, List<T> results) where T : class { }
    }
    public class MonoBehaviour : Component { public bool enabled; }
    public class GameObject : Object
    {
        private readonly List<Component> _components = new();
        public Transform transform;
        public int layer;
        public bool active = true;
        public GameObject(string value = "") { name = value; transform = new Transform { gameObject = this }; }
        public T AddComponent<T>() where T : Component
        {
            T component = (T)Activator.CreateInstance(typeof(T), nonPublic: true)!;
            component.gameObject = this; _components.Add(component); return component;
        }
        public T? GetComponent<T>() where T : class => _components.OfType<T>().FirstOrDefault();
        public T? GetComponentInChildren<T>() where T : class => GetComponent<T>() ?? transform.Children.Select(t => t.gameObject.GetComponentInChildren<T>()).FirstOrDefault(c => c != null);
        public Component? GetComponentInChildren(Type type)=>_components.FirstOrDefault(c=>type.IsInstanceOfType(c)) ?? transform.Children.Select(t=>t.gameObject.GetComponentInChildren(type)).FirstOrDefault(c=>c!=null);
        public void SetActive(bool value) => active = value;
    }
    public class Transform : Component
    {
        public List<Transform> Children = new();
        public Vector3 position, localPosition, localScale;
        public Quaternion localRotation;
        public Vector3 forward => new(0, 0, 1);
        public int childCount => Children.Count;
        public Transform? Find(string name) => Children.FirstOrDefault(t => t.gameObject.name == name);
        public Transform GetChild(int index) => Children[index];
        public void SetParent(Transform parent, bool worldPositionStays) => parent.Children.Add(this);
    }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c) { x=a;y=b;z=c; }
        public static Vector3 zero => default;
        public static Vector3 one => new(1,1,1);
        public static Vector3 up => new(0,1,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public Vector3 normalized => this;
        public static Vector3 operator -(Vector3 a,Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator +(Vector3 a,Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator *(Vector3 a,float b) => new(a.x*b,a.y*b,a.z*b);
        public static float Dot(Vector3 a,Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
        public static Vector3 ProjectOnPlane(Vector3 a,Vector3 b) => a;
    }
    public struct Quaternion { public static Quaternion identity => default; }
    public static class Mathf
    {
        public static float Max(float a,float b) => Math.Max(a,b);
        public static bool Approximately(float a,float b) => Math.Abs(a-b)<0.0001f;
    }
    public class Renderer : Component { public bool enabled; }
    public class TrailRenderer : Renderer { }
    public class ParticleSystemRenderer : Renderer { }
}
public class ZDO
{
    public Dictionary<string, object> Values = new();
    public void Set(string key, object value) => Values[key] = value;
    public int GetInt(string key) => Values.TryGetValue(key,out object? value) ? (int)value : 0;
    public long GetLong(string key) => Values.TryGetValue(key,out object? value) ? (long)value : 0L;
    public bool GetBool(string key) => Values.TryGetValue(key,out object? value) && (bool)value;
    public bool GetString(string key,out string value) { value=Values.TryGetValue(key,out object? raw)?(string)raw:"";return Values.ContainsKey(key); }
}
public static class ZDOVars
{
    public const string s_leftItem="leftItem",s_rightItem="rightItem",s_leftItemVariant="leftItemVariant",s_visual="visual";
}
public class ZNetView : Component
{
    public bool Valid=true,Owner=true;
    public ZDO Data=new();
    public bool IsValid()=>Valid;
    public bool IsOwner()=>Owner;
    public ZDO GetZDO()=>Data;
}
public class Character : MonoBehaviour
{
    public Vector3 GetCenterPoint()=>default;
    public float GetRadius()=>1;
}
public class Humanoid : Character
{
    public ItemDrop.ItemData? LeftItem,RightItem;
    public Attack? m_currentAttack;
    public bool m_currentAttackIsSecondary=true;
}
public class Player : Humanoid { public long GetPlayerID()=>1; }
public static class Skills { public enum SkillType { Swords,ElementalMagic,BloodMagic } }
public class ItemDrop : Component
{
    public ItemData m_itemData=new();
    public class ItemData
    {
        public enum ItemType { OneHandedWeapon,TwoHandedWeapon,TwoHandedWeaponLeft,Bow,Shield }
        public GameObject m_dropPrefab=null!;
        public int m_variant;
        public SharedData m_shared=new();
        public class SharedData
        {
            public ItemType m_itemType=ItemType.OneHandedWeapon;
            public Skills.SkillType m_skillType;
            public Attack m_secondaryAttack=new();
            public EffectList m_hitEffect=new();
        }
    }
}
public class Attack
{
    public enum AttackType { Projectile,Melee }
    public AttackType m_attackType=AttackType.Projectile;
    public GameObject m_attackProjectile=null!;
    public ItemDrop.ItemData m_weapon=null!;
    public Character m_character=null!;
}
public class HitData { }
public class EffectList
{
    public class EffectData { }
    public EffectData[] m_effectPrefabs=Array.Empty<EffectData>();
    public bool HasEffects()=>m_effectPrefabs.Length>0;
}
public interface IEquipmentVisual { void Setup(int variant); }
public class EquipmentVisual : Component,IEquipmentVisual
{
    public int Variant=-1,Calls;
    public void Setup(int variant) { Variant=variant;Calls++; }
}
public class ObjectDB : UnityEngine.Object
{
    public static ObjectDB? instance;
    public Dictionary<int,GameObject> Prefabs=new();
    public int CosmeticLookups;
    public GameObject GetItemPrefab(int hash) { CosmeticLookups++;return Prefabs.TryGetValue(hash,out GameObject? prefab)?prefab:null!; }
    public GameObject GetItemPrefab(string name)=>Prefabs.TryGetValue(name.GetStableHashCode(),out GameObject? prefab)?prefab:null!;
}
public static class HashExtensions
{
    public static int GetStableHashCode(this string value) { int hash=17;foreach(char c in value)hash=unchecked(hash*31+c);return hash; }
}
public class Projectile : MonoBehaviour
{
    public ItemDrop.ItemData? m_weapon,m_spawnItem;
    public Character? m_owner;
    public HitData? m_originalHitData;
    public Vector3 m_vel;
    public bool m_didHit,m_changedVisual,m_canChangeVisuals,m_respawnItemOnHit,m_spawnOnTtl;
    public float m_rayRadius,m_rotateVisual,m_rotateVisualY,m_rotateVisualZ;
    public GameObject m_visual=null!;
    public EffectList m_hitEffects=new();
    // Mirrors the original UpdateVisual call boundary; selection/caching/variant logic is linked production code.
    public void UpdateVisual()
    {
        try { UpdateVisualCore(); }
        catch(Exception exception)
        {
            Exception? remaining=SecondaryAttacks.CopiedThrowProjectileVisualSystem.RestoreAfterAppearanceFailure(this,exception);
            if(remaining!=null) throw remaining;
        }
    }
    private void UpdateVisualCore()
    {
        SecondaryAttacks.CopiedThrowProjectileVisualSystem.PrepareProjectileIfNeeded(this);
        ZNetView? view=GetComponent<ZNetView>();
        if(m_canChangeVisuals && view!=null && view.IsValid() && !m_changedVisual && view.GetZDO().GetString(ZDOVars.s_visual,out string name))
        {
            GameObject prefab=SecondaryAttacks.CopiedThrowProjectileVisualSystem.ResolveEquippedVisualPrefab(ObjectDB.instance!,name,this);
            GameObject? attach=prefab.transform.Find("attach")?.gameObject;
            if(attach!=null) { m_visual.SetActive(false);m_visual=UnityEngine.Object.Instantiate(attach,transform);m_changedVisual=true; }
        }
        SecondaryAttacks.CopiedThrowProjectileVisualSystem.EnsureProjectileVisualSpinIfNeeded(this);
    }
}
namespace SecondaryAttacks
{
    public class SecondaryAttackDefinition
    {
        public object Behavior=new CopiedSecondaryBehavior();
        public string PrefabName="Weapon";
        public SpinConfig? Boomerang,OnProjectileHit;
    }
    public class CopiedSecondaryBehavior { }
    public class SpinConfig { public string ProjectileSpinAxis="none";public Vector3 ProjectileVisualRotationOffset; }
    public class ActiveSecondaryAttack { public SecondaryAttackDefinition Definition=new(); }
    public static class SecondaryAttackRuntimeFacade
    {
        public static SecondaryAttackDefinition Definition=new();
        public static bool TryGetDefinition(ItemDrop.ItemData? item,out SecondaryAttackDefinition definition) { definition=Definition;return item!=null; }
        public static bool TryGetDefinition(string item,out SecondaryAttackDefinition definition) { definition=Definition;return true; }
        public static void SetProjectileAttackAttribution(Projectile projectile,string name,bool secondaryAttack,SecondaryAttackDefinition definition,bool disableCurrentAttackFallback) { }
    }
    public static class SecondaryAttackRuntimeContext
    {
        public static bool TryGetActiveAttack(Attack attack,out ActiveSecondaryAttack? value) { value=new();return true; }
    }
    public static class SecondaryAttackStartAttackDispatch { public static bool IsProjectilePresetOriginalCooldownFallback(Attack attack)=>false; }
    public static class SecondaryAttacksPlugin { public static Logger ModLogger=new(); }
    public class Logger { public void LogWarning(string value)=>Console.WriteLine(value); }
    public static class SecondaryAttackProjectileToolTierSystem { public static void ApplyToHitData(HitData? hit,Projectile projectile,ItemDrop.ItemData weapon) { } }
    public static class MeleeBoomerangProjectileSystem { public static void TryApplyToProjectileSetup(Projectile projectile,Attack attack,ItemDrop.ItemData weapon) { } }
    public static class MeleeProjectileHitCascadeSystem { public static void RegisterOnProjectileHitSource(Projectile projectile,Attack attack,ItemDrop.ItemData weapon) { } }
    public class ThrowProjectileVisualSpin
    {
        public enum AxisMode { None,HorizontalSide,WorldUp }
        public static bool IsConfigured(GameObject visual,AxisMode axis,Vector3 forward)=>true;
        public static void Ensure(GameObject visual,AxisMode axis,Vector3 forward) { }
    }
    public static class ThrowProjectileVisualRotationOffset { public static void Ensure(GameObject visual,Vector3 offset) { } }
    public static class ProjectileSpinAxis
    {
        public const string None="none",Horizontal="horizontal",Vertical="vertical";
        public static bool TryResolveAxisMode(string raw,out ThrowProjectileVisualSpin.AxisMode mode) { mode=ThrowProjectileVisualSpin.AxisMode.None;return true; }
    }
}
