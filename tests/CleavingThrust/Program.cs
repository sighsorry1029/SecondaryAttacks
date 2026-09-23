using System;
using System.Linq;
using System.Reflection;
using SecondaryAttacks;
using UnityEngine;

internal static class Program
{
    private static int _checks;
    private static Attack _attack=null!;
    private static SecondaryAttackDefinition _definition=null!;
    private static void Check(bool value,string message) { _checks++; if(!value) throw new Exception(message); }
    private static void Near(float actual,float expected,string name)=>Check(MathF.Abs(actual-expected)<0.002f,$"{name}: {actual} != {expected}");
    private static void Reset()
    {
        Physics.Scene.Clear(); Physics.Queries.Clear(); Physics.Blocked.Clear(); Physics.FallbackQueries=0;
        _attack=new Attack(); _definition=new SecondaryAttackDefinition();
    }
    private static Character Mob(float x,float z,float y=1,float radius=0.3f)
    {
        var c=new Character(); c.transform.position=new Vector3(x,y,z);
        Physics.Scene.Add(new Collider { gameObject=c.gameObject,Center=c.transform.position,Radius=radius });
        return c;
    }
    private static Prop ObjectAt(float z)
    {
        var p=new Prop(); p.transform.position=new Vector3(0,1,z);
        Physics.Scene.Add(new Collider { gameObject=p.gameObject,Center=p.transform.position,Radius=0.1f,Layer=1 });
        return p;
    }
    private static void Fire()=>CleavingThrustSystem.Trigger(_attack,_definition);
    private static void CheckReleased()
    {
        var flags=BindingFlags.Static|BindingFlags.NonPublic;
        Check(((Collider[])typeof(CleavingThrustSystem).GetField("CharacterHits",flags)!.GetValue(null)!).All(c=>c==null),"Retained collider references cleared");
        var set=(System.Collections.Generic.HashSet<Character>)typeof(CleavingThrustSystem).GetField("CheckedCharacters",flags)!.GetValue(null)!;
        Check(set.Count==0,"Character identity cache cleared");
    }
    public static void Main()
    {
        Reset(); var edge=Mob(1,1.5f); Fire();
        Check(edge.Hits.Count==1,"Collider grazes near edge although its center is outside 0.5m sweep");
        Near(edge.Hits[0].m_damage.Total,300,"Single target damage");
        Near(edge.Hits[0].m_pushForce,60,"Single target push");
        Check(Physics.Queries.Count==8,"Native 30 degree fan has eight casts");
        Near((Physics.Queries[0].End-Physics.Queries[0].Start).magnitude,8.5f,"Extended sweep subtracts radius");
        Near(Physics.Queries[0].End.x,8.5f*MathF.Sin(MathF.PI/12),"Native first ray +15 degrees");
        Near(Physics.Queries[7].End.x,8.5f*MathF.Sin(-13*MathF.PI/180),"Native last ray -13 degrees");
        CheckReleased();

        Reset(); var close=Mob(0,1); var far=Mob(0,9); var beyond=Mob(0,9.4f); var outside=Mob(4,7); Fire();
        Check(close.Hits.Count==1&&far.Hits.Count==1&&beyond.Hits.Count==0&&outside.Hits.Count==0,"Range and side boundaries");
        Near(close.Hits[0].m_damage.Total,300,"First keeps full damage");
        Near(far.Hits[0].m_damage.Total,200,"Second retains N=2 penalty");
        Near(close.Hits[0].m_pushForce,40,"First push still divided");
        Near(far.Hits[0].m_pushForce,40,"Second push unchanged");

        Reset(); var prop=ObjectAt(0.5f); var first=Mob(0,2); var second=Mob(0,4); var third=Mob(0,6);
        Physics.Scene.Add(new Collider { gameObject=first.gameObject,Center=first.GetCenterPoint() }); Fire();
        Check(first.Hits.Count==1,"Multiple colliders and rays hit one character once");
        Near(prop.Hits.Single().m_damage.Total,100,"Closer prop keeps N=4 penalty");
        Near(first.Hits.Single().m_damage.Total,300,"First character after prop exempt");
        Near(second.Hits.Single().m_damage.Total,100,"Second keeps complete N including prop");
        Near(third.Hits.Single().m_damage.Total,100,"Third damage unchanged");
        Near(first.Hits[0].m_pushForce,20,"First character push not restored");
        Check(_attack.m_character.Effects.IncomingDamage.SequenceEqual(new[] {100f,300f,100f,100f}),"SEMan sees restored damage in original order");

        foreach(bool lowerDamage in new[]{false,true})
        {
            Reset(); _attack.m_lowerDamagePerHit=lowerDamage; _attack.m_multiHit=!lowerDamage;
            var a=Mob(0,1); var b=Mob(0,3); Fire();
            Near(a.Hits.Single().m_damage.Total,300,"Disabled penalty first");
            Near(b.Hits.Single().m_damage.Total,300,"Disabled penalty second");
        }
        Reset(); var propOnly=ObjectAt(1); var propOnly2=ObjectAt(2); Fire();
        Near(propOnly.Hits.Single().m_damage.Total,200,"No character means no exemption");
        Near(propOnly2.Hits.Single().m_damage.Total,200,"Second prop retains penalty");

        Reset(); var blocked=Mob(0,1); Physics.Blocked.Add(blocked); var visible=Mob(0,3); Fire();
        Check(blocked.Hits.Count==0,"Environment obstruction preserved");
        Near(visible.Hits.Single().m_damage.Total,300,"Blocked target excluded from count");
        Reset(); var friendly=Mob(0,1); friendly.Enemy=false; var dead=Mob(0,2); dead.Dead=true; var dodge=Mob(0,3); dodge.Dodging=true;
        var live=Mob(0,4); Fire();
        Check(friendly.Hits.Count==0&&dead.Hits.Count==0&&dodge.Hits.Count==0,"Friend/dead/dodge protections");
        Near(live.Hits.Single().m_damage.Total,300,"First eligible target only");

        Reset(); _attack.OriginJoint=new Transform { position=new Vector3(0,0,5) }; var atJoint=Mob(0,6.5f); var behindJoint=Mob(0,1); Fire();
        Check(atJoint.Hits.Count==1&&behindJoint.Hits.Count==0,"Private native direction accessor supplies origin joint");
        Reset(); _attack.Aim=new Vector3(0,1,1).normalized; var up=Mob(0,3,4); Fire();
        Check(up.Hits.Count==1,"Native aim pitch retained");
        Reset(); _attack.m_attackHeightChar1=-1; _attack.m_attackHeightChar2=1;
        var low=Mob(0,2,0); var high=Mob(0,2,2); Fire();
        Check(low.Hits.Count==1&&high.Hits.Count==1&&Physics.Queries.Count==24,"Two character-height bands plus base sweep");
        Reset(); _attack.m_attackHeightChar2=2; var noExtra=Mob(0,2,3); Fire();
        Check(noExtra.Hits.Count==0&&Physics.Queries.Count==8,"Height2 alone does not enable extra sweep");
        Reset(); _attack.m_attackRayWidthCharExtra=0.5f; var extraWidth=Mob(1.6f,2); Fire();
        Check(extraWidth.Hits.Count==1&&Physics.Queries.Count==16,"Extra character width admits a base-sweep miss once");
        Near(Physics.Queries[1].Radius,1,"Extra character radius");
        Near((Physics.Queries[1].End-Physics.Queries[1].Start).magnitude,8,"Extra radius shortens center segment");
        Reset(); var overlap=Mob(0,0.1f); Fire(); Check(overlap.Hits.Count==1,"Start overlap remains hittable");
        Reset(); _definition.CleavingThrust!.RangeFactor=0.5f; var shortHit=Mob(0,1); var shortMiss=Mob(0,3); Fire();
        Check(shortHit.Hits.Count==1&&shortMiss.Hits.Count==0,"Configured shortened range remains supported");

        // Independent point samples of sphere colliders across the original fan:
        // every native-length candidate remains admitted after extension.
        Reset(); for(int x=-5;x<=5;x++) for(int z=1;z<=10;z++) Mob(x*0.25f,z*0.25f);
        _definition.CleavingThrust!.RangeFactor=1; Fire();
        var original=Physics.Scene.Select(c=>c.gameObject.GetComponent<Character>()!).Where(c=>c.Hits.Count>0).ToArray();
        foreach(var collider in Physics.Scene) collider.gameObject.GetComponent<Character>()!.Hits.Clear();
        _definition.CleavingThrust.RangeFactor=3; Fire();
        Check(original.Length>0&&original.All(c=>c.Hits.Count==1),"Extending range loses no original-length contacts");

        Reset(); var crowded=Enumerable.Range(0,1100).Select(_=>Mob(0,2)).ToArray(); Fire();
        Check(crowded.All(c=>c.Hits.Count==1),"Overflow beyond retained buffer loses no targets or duplicates");
        Check(Physics.FallbackQueries>0,"Saturation uses complete fallback query"); CheckReleased();
        Reset(); var throwing=Mob(0,1); throwing.ThrowOnDamage=true;
        try { Fire(); throw new Exception("Expected target failure"); } catch(InvalidOperationException) { }
        CheckReleased();
        Reset(); var retry=Mob(0,1); Fire(); Check(retry.Hits.Count==1,"Attack after exception remains valid");
        Console.WriteLine($"PASS CleavingThrust: {_checks} assertions; linked production collector and HitData factory.");
    }
}
