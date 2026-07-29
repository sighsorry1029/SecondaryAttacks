using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class WeaponTrailAccess
{
    private static readonly FieldInfo? AttackVisEquipmentField =
        AccessTools.Field(typeof(Attack), "m_visEquipment");
    private static readonly FieldInfo? VisRightItemInstanceField =
        AccessTools.Field(typeof(VisEquipment), "m_rightItemInstance");
    private static readonly FieldInfo? TrailBaseField =
        AccessTools.Field(typeof(MeleeWeaponTrail), "_base");
    private static readonly FieldInfo? TrailTipField =
        AccessTools.Field(typeof(MeleeWeaponTrail), "_tip");
    private static readonly FieldInfo? TrailMaterialField =
        AccessTools.Field(typeof(MeleeWeaponTrail), "_material");
    private static readonly FieldInfo? TrailColorsField =
        AccessTools.Field(typeof(MeleeWeaponTrail), "_colors");
    private static readonly FieldInfo? TrailSizesField =
        AccessTools.Field(typeof(MeleeWeaponTrail), "_sizes");
    private static readonly FieldInfo? TrailLifeTimeField =
        AccessTools.Field(typeof(MeleeWeaponTrail), "_lifeTime");
    private static readonly FieldInfo? TrailSubdivisionsField =
        AccessTools.Field(typeof(MeleeWeaponTrail), "subdivisions");
    private static readonly MeleeWeaponTrail[] NoTrails = Array.Empty<MeleeWeaponTrail>();

    internal static GameObject? GetRightItemInstance(Attack? attack)
    {
        if (attack == null)
        {
            return null;
        }

        VisEquipment? visEquipment = GetValue(AttackVisEquipmentField, attack) as VisEquipment;
        GameObject? rightItemInstance = GetRightItemInstance(visEquipment);
        return rightItemInstance != null
            ? rightItemInstance
            : GetRightItemInstance(attack.m_character);
    }

    internal static GameObject? GetRightItemInstance(Character? character)
    {
        VisEquipment? visEquipment = character != null
            ? character.GetComponent<VisEquipment>()
            : null;
        return GetRightItemInstance(visEquipment);
    }

    internal static MeleeWeaponTrail[] GetTrails(Attack? attack)
    {
        return GetTrails(GetRightItemInstance(attack));
    }

    internal static MeleeWeaponTrail[] GetTrails(Character? character)
    {
        return GetTrails(GetRightItemInstance(character));
    }

    internal static MeleeWeaponTrail[] GetTrails(GameObject? rightItemInstance)
    {
        if (rightItemInstance == null)
        {
            return NoTrails;
        }

        try
        {
            return rightItemInstance.GetComponentsInChildren<MeleeWeaponTrail>(includeInactive: true) ?? NoTrails;
        }
        catch (Exception)
        {
            return NoTrails;
        }
    }

    internal static bool TryGetEndpoints(
        MeleeWeaponTrail? trail,
        out Transform baseTransform,
        out Transform tipTransform)
    {
        baseTransform = GetBaseTransform(trail)!;
        tipTransform = GetTipTransform(trail)!;
        return baseTransform != null && tipTransform != null;
    }

    internal static Transform? GetBaseTransform(MeleeWeaponTrail? trail)
    {
        return GetValue(TrailBaseField, trail) as Transform;
    }

    internal static Transform? GetTipTransform(MeleeWeaponTrail? trail)
    {
        return GetValue(TrailTipField, trail) as Transform;
    }

    internal static Material? GetMaterial(MeleeWeaponTrail? trail)
    {
        return GetValue(TrailMaterialField, trail) as Material;
    }

    internal static Color[]? GetColors(MeleeWeaponTrail? trail)
    {
        return GetValue(TrailColorsField, trail) as Color[];
    }

    internal static float[]? GetSizes(MeleeWeaponTrail? trail)
    {
        return GetValue(TrailSizesField, trail) as float[];
    }

    internal static float GetLifeTime(MeleeWeaponTrail? trail, float fallback)
    {
        return GetValue(TrailLifeTimeField, trail) is float lifeTime ? lifeTime : fallback;
    }

    internal static int GetSubdivisions(MeleeWeaponTrail? trail, int fallback)
    {
        return GetValue(TrailSubdivisionsField, trail) is int subdivisions ? subdivisions : fallback;
    }

    internal static object? GetValue(FieldInfo? field, object? instance)
    {
        if (field == null || instance == null)
        {
            return null;
        }

        try
        {
            return field.GetValue(instance);
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal static bool SetValue(FieldInfo? field, object? instance, object? value)
    {
        if (field == null || instance == null)
        {
            return false;
        }

        try
        {
            field.SetValue(instance, value);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static GameObject? GetRightItemInstance(VisEquipment? visEquipment)
    {
        return GetValue(VisRightItemInstanceField, visEquipment) as GameObject;
    }
}
