using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class SweepTrailResetSystem
{
    private static readonly FieldInfo AttackVisEquipmentField = AccessTools.Field(typeof(Attack), "m_visEquipment")!;
    private static readonly FieldInfo VisRightItemInstanceField = AccessTools.Field(typeof(VisEquipment), "m_rightItemInstance")!;
    private static readonly FieldInfo? TrailBaseField = AccessTools.Field(typeof(MeleeWeaponTrail), "_base");
    private static readonly FieldInfo? TrailTipField = AccessTools.Field(typeof(MeleeWeaponTrail), "_tip");
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
        GameObject? rightItemInstance = GetRightItemInstance(attack);
        if (rightItemInstance == null)
        {
            return;
        }

        foreach (MeleeWeaponTrail trail in rightItemInstance.GetComponentsInChildren<MeleeWeaponTrail>(includeInactive: true))
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
            if (field?.GetValue(trail) is IList list)
            {
                list.Clear();
            }
        }

        if (TrailMeshField?.GetValue(trail) is Mesh mesh)
        {
            mesh.Clear();
        }

        TrailEmitTimeField?.SetValue(trail, 0f);
        if (TrailLastPositionField != null)
        {
            TrailLastPositionField.SetValue(trail, ResolveCurrentTrailPosition(trail));
        }
    }

    private static Vector3 ResolveCurrentTrailPosition(MeleeWeaponTrail trail)
    {
        Transform? tip = TrailTipField?.GetValue(trail) as Transform;
        if (tip != null)
        {
            return tip.position;
        }

        Transform? baseTransform = TrailBaseField?.GetValue(trail) as Transform;
        return baseTransform != null ? baseTransform.position : trail.transform.position;
    }

    private static GameObject? GetRightItemInstance(Attack? attack)
    {
        if (attack?.m_character == null)
        {
            return null;
        }

        VisEquipment? visEquipment = AttackVisEquipmentField.GetValue(attack) as VisEquipment;
        GameObject? rightItemInstance = visEquipment != null
            ? VisRightItemInstanceField.GetValue(visEquipment) as GameObject
            : null;
        if (rightItemInstance != null)
        {
            return rightItemInstance;
        }

        visEquipment = attack.m_character.GetComponent<VisEquipment>();
        return visEquipment != null ? VisRightItemInstanceField.GetValue(visEquipment) as GameObject : null;
    }
}
