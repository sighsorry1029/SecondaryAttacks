// Boundary doubles: Physics intersects query capsules with spherical scene
// colliders. Production code owns fan generation, admission, ordering and damage.
using System;
using System.Collections.Generic;
using System.Linq;
// Installed Harmony targets the game's Mono runtime, not the .NET 9 harness.
// Only its binding boundary is doubled here; the final DLL is checked separately.
namespace HarmonyLib
{
    public static class AccessTools
    {
        public static System.Reflection.MethodInfo DeclaredMethod(Type type,string name,Type[] parameters)=>
            type.GetMethod(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance,null,parameters,null)!;
        public static T MethodDelegate<T>(System.Reflection.MethodInfo method) where T:Delegate => method.CreateDelegate<T>();
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero => new(0,0,0);
        public static Vector3 up => new(0,1,0);
        public float sqrMagnitude => x*x+y*y+z*z;
        public float magnitude => MathF.Sqrt(sqrMagnitude);
        public Vector3 normalized => magnitude > 0 ? this / magnitude : zero;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a, float b) => new(a.x*b,a.y*b,a.z*b);
        public static Vector3 operator /(Vector3 a, float b) => a*(1/b);
        public static float Dot(Vector3 a, Vector3 b) => a.x*b.x+a.y*b.y+a.z*b.z;
        public static Vector3 ProjectOnPlane(Vector3 a, Vector3 normal) => a-normal*Dot(a,normal);
    }
    public struct Quaternion
    {
        private System.Numerics.Quaternion Value;
        public static Quaternion identity => new() { Value=System.Numerics.Quaternion.Identity };
        public static Quaternion AngleAxis(float degrees, Vector3 axis) => new() { Value=System.Numerics.Quaternion.CreateFromAxisAngle(new(axis.x,axis.y,axis.z),degrees*MathF.PI/180) };
        public static Quaternion Euler(float x,float y,float z) => AngleAxis(y,Vector3.up);
        public static Vector3 operator *(Quaternion q, Vector3 p)
        { var v=System.Numerics.Vector3.Transform(new System.Numerics.Vector3(p.x,p.y,p.z),q.Value); return new(v.X,v.Y,v.Z); }
    }
    public static class Mathf
    {
        public static float Max(float a,float b)=>MathF.Max(a,b);
        public static int Max(int a,int b)=>Math.Max(a,b);
        public static float Clamp(float a,float b,float c)=>Math.Clamp(a,b,c);
        public static float Sqrt(float x)=>MathF.Sqrt(x);
        public static int CeilToInt(float x)=>(int)MathF.Ceiling(x);
        public static float Lerp(float a,float b,float t)=>a+(b-a)*t;
        public static bool Approximately(float a,float b)=>MathF.Abs(a-b)<0.00001f;
    }
    public class Object
    { public static T Instantiate<T>(T obj, Vector3 pos, Quaternion rot) where T:Object => obj; }
    public class Transform
    {
        public Vector3 position;
        public Quaternion rotation=Quaternion.identity;
        public Vector3 forward=>rotation*new Vector3(0,0,1);
        public Vector3 right=>rotation*new Vector3(1,0,0);
        public Vector3 TransformDirection(Vector3 v)=>rotation*v;
        public Vector3 InverseTransformDirection(Vector3 v)=>new(Vector3.Dot(v,right),v.y,Vector3.Dot(v,forward));
    }
    public class GameObject:Object
    {
        public readonly Dictionary<Type,object> Components=new();
        public T? GetComponent<T>() where T:class => Components.Values.OfType<T>().FirstOrDefault();
        public T? GetComponentInChildren<T>() where T:class=>GetComponent<T>();
    }
    public class MonoBehaviour:Object
    {
        public readonly Transform transform=new();
        public readonly GameObject gameObject=new();
        public T? GetComponentInChildren<T>() where T:class=>gameObject.GetComponent<T>();
    }
    public struct Bounds { public Vector3 center; }
    public class Collider:Object
    {
        public GameObject gameObject=null!;
        public Vector3 Center;
        public float Radius=0.3f;
        public int Layer=2;
        public bool IsTrigger;
        public Bounds bounds=>new() { center=Center };
        public Vector3 ClosestPoint(Vector3 p) { var d=p-Center; return d.magnitude<=Radius?p:Center+d.normalized*Radius; }
    }
    public struct RaycastHit { public Collider collider; public float distance; }
    public enum QueryTriggerInteraction { Ignore }
    public static class LayerMask
    { public static int GetMask(params string[] names)=>names.Any(n=>n=="character") ? (names.Contains("Default")?3:2) : 1; }
    public static class Random { public static float Range(float a,float b)=>a; }
    public static class Physics
    {
        public static readonly List<Collider> Scene=new();
        public static readonly List<(Vector3 Start,Vector3 End,float Radius)> Queries=new();
        public static readonly HashSet<Character> Blocked=new();
        public static int FallbackQueries;
        private static Collider[] Query(Vector3 a,Vector3 b,float radius,int mask)
        {
            Queries.Add((a,b,radius));
            var segment=b-a;
            return Scene.Where(c=>!c.IsTrigger && (c.Layer&mask)!=0 &&
                (c.Center-(a+segment*(segment.sqrMagnitude==0?0:Math.Clamp(Vector3.Dot(c.Center-a,segment)/segment.sqrMagnitude,0,1)))).magnitude<=radius+c.Radius).ToArray();
        }
        public static int OverlapCapsuleNonAlloc(Vector3 a,Vector3 b,float r,Collider[] output,int mask,QueryTriggerInteraction q)
        { var all=Query(a,b,r,mask); int n=Math.Min(all.Length,output.Length); Array.Copy(all,output,n); return n; }
        public static Collider[] OverlapCapsule(Vector3 a,Vector3 b,float r,int mask,QueryTriggerInteraction q)
        { FallbackQueries++; return Query(a,b,r,mask); }
        public static Collider[] OverlapSphere(Vector3 p,float r,int mask,QueryTriggerInteraction q)=>Scene.Where(c=>(c.Layer&mask)!=0&&(c.Center-p).magnitude<=r+c.Radius).ToArray();
        public static RaycastHit[] RaycastAll(Vector3 p,Vector3 d,float length,int mask,QueryTriggerInteraction q)
        {
            if(Blocked.Any(c=>(c.GetCenterPoint()-(p+d*length)).magnitude<0.01f))
                return new[] { new RaycastHit { collider=new Collider { gameObject=new GameObject() },distance=length/2 } };
            return Array.Empty<RaycastHit>();
        }
    }
}
public enum DestructibleType { None,Character,Default }
public interface IDestructible { DestructibleType GetDestructibleType(); void Damage(HitData data); }
public interface IProjectile { void Setup(Character a,UnityEngine.Vector3 d,float n,object? h,ItemDrop.ItemData w,ItemDrop.ItemData? ammo); }
public class EffectList { public void Create(UnityEngine.Vector3 p,UnityEngine.Quaternion q) { } }
public class StatusEffect { public int NameHash()=>1; }
public class Skills { public enum SkillType { Swords } }
public class Character:UnityEngine.MonoBehaviour,IDestructible
{
    public bool Dead,Enemy=true,Dodging,Tamed,Pvp;
    public int DodgeReports;
    public readonly List<HitData> Hits=new();
    public readonly SEMan Effects=new();
    public Character() { gameObject.Components[typeof(Character)]=this; }
    public bool IsDead()=>Dead;
    public bool IsTamed()=>Tamed;
    public bool IsPlayer()=>this is Player;
    public bool IsPVPEnabled()=>Pvp;
    public bool IsDodgeInvincible()=>Dodging;
    public UnityEngine.Vector3 GetCenterPoint()=>transform.position;
    public float GetRandomSkillFactor(Skills.SkillType s)=>1;
    public float GetSkillLevel(Skills.SkillType s)=>50;
    public int GetLevel()=>1;
    public float GetMaxHealth()=>100;
    public float GetHealth()=>100;
    public float GetHealthPercentage()=>1;
    public SEMan GetSEMan()=>Effects;
    public void Heal(float n) { }
    public DestructibleType GetDestructibleType()=>DestructibleType.Character;
    public bool ThrowOnDamage;
    public void Damage(HitData d) { if(ThrowOnDamage) throw new InvalidOperationException("test failure"); Hits.Add(d); }
}
public class Player:Character { public void HitWhileDodging()=>DodgeReports++; }
public class Prop:UnityEngine.MonoBehaviour,IDestructible
{
    public readonly List<HitData> Hits=new();
    public Prop() { gameObject.Components[typeof(Prop)]=this; }
    public DestructibleType GetDestructibleType()=>DestructibleType.Default;
    public void Damage(HitData d)=>Hits.Add(d);
}
public class SEMan
{
    public readonly List<float> IncomingDamage=new();
    public void ModifyAttack(Skills.SkillType s,ref HitData h)=>IncomingDamage.Add(h.m_damage.Total);
}
public class ItemDrop
{
    public class ItemData
    {
        public SharedData m_shared=new();
        public int m_quality=1,m_worldLevel;
        public HitData.DamageTypes GetDamage()=>new() { Total=100 };
        public class SharedData
        {
            public Skills.SkillType m_skillType;
            public int m_toolTier;
            public bool m_tamedOnly,m_dodgeable=true,m_blockable=true;
            public float m_attackForce=10,m_backstabBonus=1,m_attackStatusEffectChance;
            public StatusEffect? m_attackStatusEffect;
            public EffectList m_hitEffect=new();
        }
    }
}
public class HitData
{
    public enum HitType { PlayerHit,EnemyHit }
    public struct DamageTypes { public float Total; public void Add(DamageTypes d)=>Total+=d.Total; public void Modify(float f)=>Total*=f; }
    public DamageTypes m_damage;
    public short m_toolTier,m_itemLevel;
    public byte m_itemWorldLevel;
    public int m_statusEffectHash;
    public float m_skillLevel,m_pushForce,m_backstabBonus,m_staggerMultiplier,m_skillRaiseAmount,m_healthReturn;
    public bool m_dodgeable,m_blockable;
    public Skills.SkillType m_skill;
    public UnityEngine.Vector3 m_point,m_dir;
    public UnityEngine.Collider m_hitCollider=null!;
    public HitType m_hitType;
    public void SetAttacker(Character c) { }
}
public class Attack
{
    public enum AttackType { Horizontal }
    public Character m_character=new Player();
    public ItemDrop.ItemData m_weapon=new();
    public ItemDrop.ItemData? m_lastUsedAmmo;
    public AttackType m_attackType;
    public UnityEngine.GameObject? m_attackProjectile,m_spawnOnHit;
    public string m_attackAnimation="greatsword_secondary";
    public float m_attackRange=3,m_attackRayWidth=0.5f,m_attackAngle=30,m_attackHeight=1,m_attackRayWidthCharExtra,m_attackHeightChar1,m_attackHeightChar2,m_attackOffset;
    public float m_raiseSkillAmount=1,m_attackHealthReturnHit,m_spawnOnHitChance,m_damageMultiplier=3,m_forceMultiplier=1,m_staggerMultiplier=1,m_damageMultiplierPerMissingHP,m_damageMultiplierByTotalHealthMissing;
    public bool m_multiHit=true,m_lowerDamagePerHit=true,m_hitFriendly;
    public static int m_attackMask=3;
    public EffectList m_hitEffect=new();
    public UnityEngine.Transform? OriginJoint;
    public UnityEngine.Vector3? Aim;
    private void GetMeleeAttackDir(out UnityEngine.Transform joint,out UnityEngine.Vector3 direction)
    { joint=OriginJoint??m_character.transform; direction=Aim??m_character.transform.forward; }
}
public class Projectile { public static UnityEngine.GameObject FindHitObject(UnityEngine.Collider c)=>c.gameObject; }
namespace SecondaryAttacks
{
    internal class CleavingThrustDefinition { public float RangeFactor=3,DamageFactor=1,PushFactor=6,DurabilityFactor=2; }
    internal class SecondaryAttackDefinition { public CleavingThrustDefinition? CleavingThrust=new(); public float DurabilityFactor=2; }
    internal static class SecondaryAttackManager
    {
        public static void PlayTriggeredAttackEffects(Attack a,float d) { }
        public static bool IsEnemyOrAggravatableTarget(Character a,Character b)=>b.Enemy;
        public static UnityEngine.Vector3 ResolveSafeClosestPoint(UnityEngine.Collider c,UnityEngine.Vector3 origin)=>c.ClosestPoint(origin);
    }
    internal static class SecondaryAttackAdrenalineSystem { public static void TryGrantOnce(Attack a,Character c,float f,string n) { } }
}
