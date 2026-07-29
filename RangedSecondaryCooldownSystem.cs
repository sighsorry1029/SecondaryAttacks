using UnityEngine;

namespace SecondaryAttacks;

internal static class RangedSecondaryCooldownSystem
{
    private const string FallbackIconPrefabName = "Bow";

    internal static bool CanStart(Humanoid humanoid, ItemDrop.ItemData? weapon)
    {
        if (humanoid == null ||
            weapon == null ||
            !SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition) ||
            definition.Behavior is not ProjectileSecondaryBehavior projectileBehavior)
        {
            return true;
        }

        return CanUse(humanoid, projectileBehavior);
    }

    internal static bool CanUse(Attack attack, ProjectileSecondaryBehavior behavior)
    {
        if (attack?.m_character == null)
        {
            return true;
        }

        return CanUse(attack.m_character, behavior);
    }

    internal static bool StartCooldown(Attack attack, ProjectileSecondaryBehavior behavior)
    {
        if (attack?.m_character == null)
        {
            return true;
        }

        return StartCooldown(attack.m_character, attack.m_weapon, behavior);
    }

    private static bool CanUse(
        Character attacker,
        ProjectileSecondaryBehavior behavior)
    {
        if (attacker == null)
        {
            return true;
        }

        if (behavior == null)
        {
            return true;
        }

        string cooldownGroup =
            SecondaryAttackCooldownGroupResolver.ResolveRangedGroup(behavior);
        return SecondaryCooldownGroupSystem.IsReady(attacker, cooldownGroup);
    }

    private static bool StartCooldown(
        Character attacker,
        ItemDrop.ItemData? weapon,
        ProjectileSecondaryBehavior behavior)
    {
        if (attacker == null)
        {
            return true;
        }

        if (behavior == null)
        {
            return true;
        }

        string cooldownGroup =
            SecondaryAttackCooldownGroupResolver.ResolveRangedGroup(behavior);
        float baseCooldown = Mathf.Max(0f, behavior.Cooldown);
        float skillLevel = ResolveCooldownSkillLevel(attacker, weapon);
        float reduction =
            Mathf.Clamp01(skillLevel / 100f) *
            Mathf.Clamp01(behavior.CooldownReductionFactor);
        float finalCooldown = Mathf.Max(0f, baseCooldown * (1f - reduction));
        Sprite? icon = ResolveWeaponIcon(weapon) ?? ResolveRegisteredIcon();
        return SecondaryCooldownGroupSystem.TryConsume(
            attacker,
            cooldownGroup,
            finalCooldown,
            icon);
    }

    private static float ResolveCooldownSkillLevel(
        Character attacker,
        ItemDrop.ItemData? weapon)
    {
        if (weapon?.m_shared == null ||
            weapon.m_shared.m_skillType == Skills.SkillType.None)
        {
            return 0f;
        }

        return Mathf.Clamp(
            attacker.GetSkillLevel(weapon.m_shared.m_skillType),
            0f,
            100f);
    }

    private static Sprite? ResolveWeaponIcon(ItemDrop.ItemData? weapon)
    {
        return weapon?.m_shared?.m_icons is { Length: > 0 } icons ? icons[0] : null;
    }

    private static Sprite? ResolveIcon(ObjectDB objectDb, string itemPrefabName)
    {
        ItemDrop? itemDrop =
            objectDb.GetItemPrefab(itemPrefabName)?.GetComponent<ItemDrop>();
        return itemDrop?.m_itemData?.m_shared?.m_icons is { Length: > 0 } icons
            ? icons[0]
            : null;
    }

    private static Sprite? ResolveRegisteredIcon()
    {
        return ObjectDB.instance != null
            ? ResolveIcon(ObjectDB.instance, FallbackIconPrefabName)
            : null;
    }
}
