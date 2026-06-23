using UnityEngine;

namespace SecondaryAttacks;

internal static class SecondaryAttackHitDataFactory
{
    internal static HitData CreateMeleeHit(
        Attack attack,
        Collider collider,
        Vector3 hitPoint,
        Vector3 hitDirection,
        float skillFactor,
        float damageFactor,
        float pushFactor,
        float skillRaiseAmount = 0f)
    {
        ItemDrop.ItemData weapon = attack.m_weapon;
        Character attacker = attack.m_character;
        Skills.SkillType skillType = weapon.m_shared.m_skillType;
        HitData hitData = new()
        {
            m_toolTier = (short)weapon.m_shared.m_toolTier,
            m_statusEffectHash = ResolveStatusEffectHash(weapon),
            m_skillLevel = attacker.GetSkillLevel(skillType),
            m_itemLevel = (short)weapon.m_quality,
            m_itemWorldLevel = (byte)weapon.m_worldLevel,
            m_pushForce = weapon.m_shared.m_attackForce * skillFactor * attack.m_forceMultiplier * pushFactor,
            m_backstabBonus = weapon.m_shared.m_backstabBonus,
            m_staggerMultiplier = attack.m_staggerMultiplier,
            m_dodgeable = weapon.m_shared.m_dodgeable,
            m_blockable = weapon.m_shared.m_blockable,
            m_skill = skillType,
            m_skillRaiseAmount = skillRaiseAmount,
            m_point = hitPoint,
            m_dir = hitDirection,
            m_hitCollider = collider,
            m_hitType = attacker is Player ? HitData.HitType.PlayerHit : HitData.HitType.EnemyHit,
            m_healthReturn = attack.m_attackHealthReturnHit
        };
        hitData.m_damage.Add(weapon.GetDamage());
        hitData.SetAttacker(attacker);
        ApplyDamageModifiers(attack, hitData, skillFactor, damageFactor);
        return hitData;
    }

    private static int ResolveStatusEffectHash(ItemDrop.ItemData weapon)
    {
        StatusEffect? statusEffect = weapon.m_shared.m_attackStatusEffect;
        if (statusEffect == null)
        {
            return 0;
        }

        return weapon.m_shared.m_attackStatusEffectChance >= 1f || Random.Range(0f, 1f) < weapon.m_shared.m_attackStatusEffectChance
            ? statusEffect.NameHash()
            : 0;
    }

    private static void ApplyDamageModifiers(Attack attack, HitData hitData, float skillFactor, float damageFactor)
    {
        if (!Mathf.Approximately(attack.m_damageMultiplier, 1f))
        {
            hitData.m_damage.Modify(attack.m_damageMultiplier);
        }

        if (!Mathf.Approximately(skillFactor, 1f))
        {
            hitData.m_damage.Modify(skillFactor);
        }

        if (!Mathf.Approximately(damageFactor, 1f))
        {
            hitData.m_damage.Modify(damageFactor);
        }

        hitData.m_damage.Modify(1f + Mathf.Max(0, attack.m_character.GetLevel() - 1) * 0.5f);
        if (attack.m_damageMultiplierPerMissingHP > 0f)
        {
            hitData.m_damage.Modify(1f + (attack.m_character.GetMaxHealth() - attack.m_character.GetHealth()) * attack.m_damageMultiplierPerMissingHP);
        }

        if (attack.m_damageMultiplierByTotalHealthMissing > 0f)
        {
            hitData.m_damage.Modify(1f + (1f - attack.m_character.GetHealthPercentage()) * attack.m_damageMultiplierByTotalHealthMissing);
        }
    }
}
