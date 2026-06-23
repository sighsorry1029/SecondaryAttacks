using UnityEngine;

namespace SecondaryAttacks;

internal static class BloodMagicSkillGainSystem
{
    private static bool _allowConfiguredBloodMagicRaise;

    private static float GetHealthCostSkillRaiseFactor()
    {
        return Mathf.Max(0f, SecondaryAttacksPlugin.BloodMagicHealthCostSkillRaiseFactor?.Value ?? 0f);
    }

    internal static bool IsHealthCostSkillGainEnabled()
    {
        return GetHealthCostSkillRaiseFactor() > 0f;
    }

    internal static bool ShouldUseMaxHealthForPercentageCost()
    {
        return SecondaryAttacksPlugin.BloodMagicHealthCostUsesMaxHealth?.Value == SecondaryAttacksPlugin.Toggle.On;
    }

    internal static void ApplyMaxHealthPercentageCost(Attack attack, ref float healthCost)
    {
        if (!ShouldUseMaxHealthForPercentageCost() ||
            attack == null ||
            attack.m_character == null ||
            attack.m_weapon?.m_shared.m_skillType != Skills.SkillType.BloodMagic ||
            attack.m_attackHealthPercentage <= 0f)
        {
            return;
        }

        float baseCost = Mathf.Max(0f, attack.m_attackHealth) +
                         Mathf.Max(0f, attack.m_character.GetMaxHealth()) * Mathf.Max(0f, attack.m_attackHealthPercentage) / 100f;
        if (baseCost <= 0f)
        {
            healthCost = 0f;
            return;
        }

        float skillFactor = Mathf.Clamp01(attack.m_character.GetSkillFactor(Skills.SkillType.BloodMagic));
        healthCost = baseCost - baseCost * 0.33f * skillFactor;
    }

    internal static bool ShouldBlockBloodMagicRaise(Skills.SkillType skillType)
    {
        return skillType == Skills.SkillType.BloodMagic &&
               IsHealthCostSkillGainEnabled() &&
               !_allowConfiguredBloodMagicRaise;
    }

    internal static void TryGrantForHealthUse(Character character, float previousHealth)
    {
        if (!IsHealthCostSkillGainEnabled() || character is not Player player || character is not Humanoid humanoid)
        {
            return;
        }

        float consumedHealth = previousHealth - character.GetHealth();
        if (consumedHealth <= 0.001f)
        {
            return;
        }

        Attack? attack = humanoid.m_currentAttack;
        if (attack == null || attack.m_character != humanoid || attack.m_weapon?.m_shared.m_skillType != Skills.SkillType.BloodMagic)
        {
            return;
        }

        float factor = GetHealthCostSkillRaiseFactor();
        float raiseAmount = consumedHealth * factor;
        if (raiseAmount <= 0f)
        {
            return;
        }

        _allowConfiguredBloodMagicRaise = true;
        try
        {
            player.RaiseSkill(Skills.SkillType.BloodMagic, raiseAmount);
        }
        finally
        {
            _allowConfiguredBloodMagicRaise = false;
        }
    }
}
