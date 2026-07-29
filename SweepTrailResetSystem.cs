using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SweepTrailResetSystem
{
    private static readonly FieldInfo? TrailMeshField = AccessTools.Field(typeof(MeleeWeaponTrail), "m_trailMesh");
    private static readonly FieldInfo? TrailLastPositionField = AccessTools.Field(typeof(MeleeWeaponTrail), "m_lastPosition");
    private static readonly FieldInfo? TrailEmitTimeField = AccessTools.Field(typeof(MeleeWeaponTrail), "_emitTime");
    private static readonly FieldInfo?[] TrailListFields =
    [
        AccessTools.Field(typeof(MeleeWeaponTrail), "m_points"),
        AccessTools.Field(typeof(MeleeWeaponTrail), "m_smoothedPoints"),
        AccessTools.Field(typeof(MeleeWeaponTrail), "m_smoothBaseList"),
        AccessTools.Field(typeof(MeleeWeaponTrail), "m_smoothTipList"),
        AccessTools.Field(typeof(MeleeWeaponTrail), "m_newVertices"),
        AccessTools.Field(typeof(MeleeWeaponTrail), "m_newUV"),
        AccessTools.Field(typeof(MeleeWeaponTrail), "m_newColors"),
        AccessTools.Field(typeof(MeleeWeaponTrail), "m_newTriangles")
    ];

    internal static void ClearWeaponTrails(Attack? attack)
    {
        foreach (MeleeWeaponTrail trail in WeaponTrailAccess.GetTrails(attack))
        {
            ClearTrail(trail);
        }
    }

    internal static void ClearWeaponTrails(Character? character)
    {
        foreach (MeleeWeaponTrail trail in WeaponTrailAccess.GetTrails(character))
        {
            ClearTrail(trail);
        }
    }

    private static void ClearTrail(MeleeWeaponTrail trail)
    {
        if (trail == null)
        {
            return;
        }

        foreach (FieldInfo? field in TrailListFields)
        {
            if (WeaponTrailAccess.GetValue(field, trail) is IList list)
            {
                TryClearList(list);
            }
        }

        if (WeaponTrailAccess.GetValue(TrailMeshField, trail) is Mesh mesh)
        {
            TryClearMesh(mesh);
        }

        WeaponTrailAccess.SetValue(TrailEmitTimeField, trail, 0f);
        WeaponTrailAccess.SetValue(TrailLastPositionField, trail, ResolveCurrentTrailPosition(trail));
    }

    private static Vector3 ResolveCurrentTrailPosition(MeleeWeaponTrail trail)
    {
        Transform? tip = WeaponTrailAccess.GetTipTransform(trail);
        if (tip != null)
        {
            return tip.position;
        }

        Transform? baseTransform = WeaponTrailAccess.GetBaseTransform(trail);
        return baseTransform != null ? baseTransform.position : trail.transform.position;
    }

    private static void TryClearList(IList list)
    {
        try
        {
            list.Clear();
        }
        catch (System.Exception)
        {
        }
    }

    private static void TryClearMesh(Mesh mesh)
    {
        try
        {
            mesh.Clear();
        }
        catch (System.Exception)
        {
        }
    }
}
