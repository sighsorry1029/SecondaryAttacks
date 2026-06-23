using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace SecondaryAttacks;

internal static class GroundworkCompat
{
    private const string GroundworkAssemblyName = "Groundwork";
    private const string FarmingSkillOverrideTypeName = "SecondaryAttacks.FarmingSkillOverrideSystem";
    private static readonly IDisposable NoopScope = new NoopDisposable();
    private static bool _resolved;
    private static MethodInfo? _suppressRangePickupMethod;
    private static MethodInfo? _isForagingTargetMethod;

    internal static IDisposable SuppressForagingRangePickup()
    {
        Resolve();
        if (_suppressRangePickupMethod == null)
        {
            return NoopScope;
        }

        try
        {
            return _suppressRangePickupMethod.Invoke(null, null) as IDisposable ?? NoopScope;
        }
        catch
        {
            return NoopScope;
        }
    }

    internal static bool IsForagingTarget(Pickable? pickable)
    {
        if (pickable == null)
        {
            return false;
        }

        Resolve();
        if (_isForagingTargetMethod != null)
        {
            try
            {
                if (_isForagingTargetMethod.Invoke(null, new object[] { pickable }) is bool result)
                {
                    return result;
                }
            }
            catch
            {
                // Fall through to the local structural check.
            }
        }

        return IsForagingTargetFallback(pickable);
    }

    private static void Resolve()
    {
        if (_resolved)
        {
            return;
        }

        _resolved = true;
        Assembly? groundworkAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, GroundworkAssemblyName, StringComparison.OrdinalIgnoreCase));
        Type? farmingType = groundworkAssembly?.GetType(FarmingSkillOverrideTypeName);
        _suppressRangePickupMethod = farmingType?.GetMethod("SuppressRangePickup", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        _isForagingTargetMethod = farmingType?.GetMethod("IsForagingTarget", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static bool IsForagingTargetFallback(Pickable pickable)
    {
        return pickable.m_respawnTimeMinutes > 0f && DropsEdibleItem(pickable);
    }

    private static bool DropsEdibleItem(Pickable pickable)
    {
        if (IsEdibleItemPrefab(pickable.m_itemPrefab))
        {
            return true;
        }

        if (pickable.m_extraDrops?.m_drops == null)
        {
            return false;
        }

        foreach (DropTable.DropData drop in pickable.m_extraDrops.m_drops)
        {
            if (IsEdibleItemPrefab(drop.m_item))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsEdibleItemPrefab(GameObject? itemPrefab)
    {
        ItemDrop? itemDrop = itemPrefab != null ? itemPrefab.GetComponent<ItemDrop>() : null;
        if (itemDrop?.m_itemData?.m_shared == null)
        {
            return false;
        }

        ItemDrop.ItemData.SharedData shared = itemDrop.m_itemData.m_shared;
        return shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable &&
               (shared.m_food > 0f || shared.m_foodStamina > 0f || shared.m_foodEitr > 0f);
    }

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
