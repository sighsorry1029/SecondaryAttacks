using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class AftershockSystem
{
    private static readonly Collider[] Hits = new Collider[128];
    private static readonly HashSet<GameObject> HitObjects = new();
    private static int _attackMask;
    private static int _attackMaskTerrain;
    private static int _attackMaskCharacters;

    internal static bool CanHandle(Attack attack, SecondaryAttackDefinition definition)
    {
        return attack != null &&
               definition?.Aftershock != null &&
               attack.m_character != null &&
               attack.m_weapon != null &&
               attack.m_attackType == Attack.AttackType.Area &&
               attack.m_attackRayWidth > 0f;
    }

    internal static void Trigger(Attack attack, SecondaryAttackDefinition definition)
    {
        if (!CanHandle(attack, definition) || !SecondaryAttackManager.HasCharacterAuthority(attack.m_character))
        {
            return;
        }

        AftershockDefinition aftershock = definition.Aftershock!;
        ApplyAttackTriggerSideEffects(attack);
        AftershockController controller = attack.m_character.gameObject.AddComponent<AftershockController>();
        controller.Initialize(attack, aftershock);
    }

    private static void ApplyAttackTriggerSideEffects(Attack attack)
    {
        if (attack.m_toggleFlying)
        {
            if (attack.m_character.IsFlying())
            {
                attack.m_character.Land();
            }
            else
            {
                attack.m_character.TakeOff();
            }
        }

        if (attack.m_recoilPushback != 0f)
        {
            attack.m_character.ApplyPushback(-attack.m_character.transform.forward, attack.m_recoilPushback);
        }

        if (attack.m_selfDamage > 0f)
        {
            HitData selfHit = new();
            selfHit.m_damage.m_damage = attack.m_selfDamage;
            attack.m_character.Damage(selfHit);
        }

        if (attack.m_consumeItem)
        {
            attack.ConsumeItem();
        }

        if (attack.m_requiresReload)
        {
            attack.m_character.ResetLoadedWeapon();
        }
    }

    private static void ApplyWave(AftershockController controller, int waveIndex)
    {
        Attack attack = controller.Attack;
        AftershockDefinition aftershock = controller.Aftershock;
        Character attacker = attack.m_character;
        ItemDrop.ItemData weapon = attack.m_weapon;
        if (attacker == null || weapon?.m_shared == null)
        {
            return;
        }

        float waveScale = waveIndex <= 0
            ? 1f
            : Mathf.Pow(1f - aftershock.WaveDecay, waveIndex);
        float damageScale = waveScale;
        float radiusScale = waveScale;
        float pushScale = waveScale;
        float vfxScale = waveScale;
        float sfxVolume = waveScale;

        float hitRadius = Mathf.Max(0.01f, attack.m_attackRayWidth * radiusScale);
        float characterHitRadius = Mathf.Max(0.01f, (attack.m_attackRayWidth + attack.m_attackRayWidthCharExtra) * radiusScale);
        Vector3 origin = controller.BaseOrigin + controller.Forward * aftershock.ForwardStep * waveIndex;
        Transform attackOrigin = ResolveAttackOrigin(attack);
        CreateScaledEffects(weapon.m_shared.m_triggerEffect, origin, controller.Rotation, attackOrigin, vfxScale, sfxVolume);
        CreateScaledEffects(attack.m_triggerEffect, origin, controller.Rotation, attackOrigin, vfxScale, sfxVolume);

        int hitCount = 0;
        Vector3 averageHitPoint = Vector3.zero;
        float skillDamageFactor = attacker.GetRandomSkillFactor(weapon.m_shared.m_skillType);
        float maxAdrenalineMultiplier = 0f;
        HitObjects.Clear();
        int layerMask = attack.m_hitTerrain ? GetAttackMaskTerrain() : GetAttackMask();
        CheckHits(
            controller,
            origin,
            Physics.OverlapSphereNonAlloc(origin, hitRadius, Hits, layerMask, QueryTriggerInteraction.UseGlobal),
            damageScale,
            pushScale,
            skillDamageFactor,
            ref hitCount,
            ref averageHitPoint,
            ref maxAdrenalineMultiplier);

        if (attack.m_attackRayWidthCharExtra > 0f || attack.m_attackHeightChar1 != 0f)
        {
            CheckHits(
                controller,
                origin,
                Physics.OverlapSphereNonAlloc(origin + Vector3.up * attack.m_attackHeightChar1, characterHitRadius, Hits, GetAttackMaskCharacters(), QueryTriggerInteraction.UseGlobal),
                damageScale,
                pushScale,
                skillDamageFactor,
                ref hitCount,
                ref averageHitPoint,
                ref maxAdrenalineMultiplier);

            if (!Mathf.Approximately(attack.m_attackHeightChar2, attack.m_attackHeightChar1))
            {
                CheckHits(
                    controller,
                    origin,
                    Physics.OverlapSphereNonAlloc(origin + Vector3.up * attack.m_attackHeightChar2, characterHitRadius, Hits, GetAttackMaskCharacters(), QueryTriggerInteraction.UseGlobal),
                    damageScale,
                    pushScale,
                    skillDamageFactor,
                    ref hitCount,
                    ref averageHitPoint,
                    ref maxAdrenalineMultiplier);
            }
        }

        if (hitCount > 0)
        {
            averageHitPoint /= hitCount;
            CreateScaledEffects(weapon.m_shared.m_hitEffect, averageHitPoint, Quaternion.identity, null, vfxScale, sfxVolume);
            CreateScaledEffects(attack.m_hitEffect, averageHitPoint, Quaternion.identity, null, vfxScale, sfxVolume);
            controller.DrainDurabilityOnce();
            attacker.AddNoise(attack.m_attackHitNoise);
            if (maxAdrenalineMultiplier > 0f)
            {
                SecondaryAttackAdrenalineSystem.TryGrantOnce(attack, maxAdrenalineMultiplier, 1f, "aftershock");
            }
        }

        if (attack.m_spawnOnTrigger != null)
        {
            Object.Instantiate(attack.m_spawnOnTrigger, origin, Quaternion.identity)
                .GetComponent<IProjectile>()?
                .Setup(attacker, attacker.transform.forward, -1f, null, null, attack.m_lastUsedAmmo);
        }
    }

    private static void CheckHits(
        AftershockController controller,
        Vector3 origin,
        int count,
        float damageScale,
        float pushScale,
        float skillDamageFactor,
        ref int hitCount,
        ref Vector3 averageHitPoint,
        ref float maxAdrenalineMultiplier)
    {
        Attack attack = controller.Attack;
        Character attacker = attack.m_character;
        ItemDrop.ItemData weapon = attack.m_weapon;
        Transform attackerTransform = attacker.transform;

        for (int i = 0; i < count; i++)
        {
            Collider collider = Hits[i];
            if (collider == null || collider.gameObject == attacker.gameObject)
            {
                continue;
            }

            GameObject hitObject = Projectile.FindHitObject(collider);
            if (hitObject == null ||
                hitObject == attacker.gameObject ||
                !HitObjects.Add(hitObject))
            {
                continue;
            }

            Vector3 hitPoint = SecondaryAttackManager.ResolveSafeClosestPoint(collider, origin);
            if ((hitPoint - origin).sqrMagnitude < 0.0001f)
            {
                hitPoint = collider.bounds.center;
            }

            IDestructible? destructible = hitObject.GetComponent<IDestructible>();
            if (destructible == null)
            {
                continue;
            }

            Vector3 hitDirection = hitPoint - origin;
            hitDirection.y = 0f;
            Vector3 fallbackDirection = hitPoint - attackerTransform.position;
            if (Vector3.Dot(fallbackDirection, hitDirection) < 0f)
            {
                hitDirection = fallbackDirection;
            }

            if (hitDirection.sqrMagnitude < 0.0001f)
            {
                hitDirection = attackerTransform.forward;
            }

            hitDirection.Normalize();
            HitData hitData = CreateHitData(attack, collider, hitPoint, hitDirection, skillDamageFactor, damageScale, pushScale);
            Character? character = destructible as Character;
            bool isEnemy = false;
            if (character != null)
            {
                isEnemy = BaseAI.IsEnemy(attacker, character) ||
                          (character.GetBaseAI() != null && character.GetBaseAI().IsAggravatable() && attacker.IsPlayer());
                if (((!attack.m_hitFriendly || attacker.IsTamed()) && !attacker.IsPlayer() && !isEnemy) ||
                    (!weapon.m_shared.m_tamedOnly && attacker.IsPlayer() && !attacker.IsPVPEnabled() && !isEnemy) ||
                    (weapon.m_shared.m_tamedOnly && !character.IsTamed()))
                {
                    continue;
                }

                if (isEnemy && character.m_enemyAdrenalineMultiplier > maxAdrenalineMultiplier)
                {
                    maxAdrenalineMultiplier = character.m_enemyAdrenalineMultiplier;
                }

                if (hitData.m_dodgeable && character.IsDodgeInvincible())
                {
                    if (character.IsPlayer())
                    {
                        (character as Player)?.HitWhileDodging();
                    }

                    continue;
                }
            }
            else if (weapon.m_shared.m_tamedOnly)
            {
                continue;
            }

            TrySpawnOnHit(attack, hitObject);
            attacker.GetSEMan().ModifyAttack(weapon.m_shared.m_skillType, ref hitData);
            if (attack.m_attackHealthReturnHit > 0f && isEnemy)
            {
                attacker.Heal(attack.m_attackHealthReturnHit);
            }

            destructible.Damage(hitData);
            hitCount++;
            averageHitPoint += hitPoint;
        }
    }

    private static HitData CreateHitData(
        Attack attack,
        Collider collider,
        Vector3 hitPoint,
        Vector3 hitDirection,
        float skillDamageFactor,
        float damageScale,
        float pushScale)
    {
        return SecondaryAttackHitDataFactory.CreateMeleeHit(
            attack,
            collider,
            hitPoint,
            hitDirection,
            skillDamageFactor,
            damageScale,
            pushScale);
    }

    private static void TrySpawnOnHit(Attack attack, GameObject hitObject)
    {
        if (attack.m_spawnOnHitChance <= 0f ||
            attack.m_spawnOnHit == null ||
            Random.Range(0f, 1f) >= attack.m_spawnOnHitChance)
        {
            return;
        }

        GameObject spawned = Object.Instantiate(attack.m_spawnOnHit, hitObject.transform.position, hitObject.transform.rotation);
        spawned.GetComponentInChildren<IProjectile>()?.Setup(
            attack.m_character,
            attack.m_character.transform.forward,
            -1f,
            null,
            attack.m_weapon,
            attack.m_lastUsedAmmo);
    }

    private static Transform ResolveAttackOrigin(Attack attack)
    {
        if (!string.IsNullOrWhiteSpace(attack.m_attackOriginJoint))
        {
            GameObject visual = attack.m_character.GetVisual();
            if (visual != null)
            {
                Transform child = Utils.FindChild(visual.transform, attack.m_attackOriginJoint);
                if (child != null)
                {
                    return child;
                }
            }
        }

        return attack.m_character.transform;
    }

    private static void CreateScaledEffects(
        EffectList effects,
        Vector3 position,
        Quaternion rotation,
        Transform? parent,
        float visualScale,
        float volumeScale)
    {
        if (effects == null || !effects.HasEffects())
        {
            return;
        }

        float clampedVisualScale = Mathf.Max(0f, visualScale);
        GameObject[] spawned = effects.Create(position, rotation, parent, clampedVisualScale);
        foreach (GameObject instance in spawned)
        {
            if (instance == null)
            {
                continue;
            }

            ScaleParticleSystems(instance, clampedVisualScale);

            foreach (ZSFX sfx in instance.GetComponentsInChildren<ZSFX>(true))
            {
                sfx.SetVolumeModifier(sfx.GetVolumeModifier() * volumeScale);
            }

            foreach (AudioSource audioSource in instance.GetComponentsInChildren<AudioSource>(true))
            {
                if (audioSource.GetComponent<ZSFX>() == null)
                {
                    audioSource.volume *= volumeScale;
                }
            }
        }
    }

    private static void ScaleParticleSystems(GameObject instance, float visualScale)
    {
        if (Mathf.Approximately(visualScale, 1f))
        {
            return;
        }

        foreach (ParticleSystem particleSystem in instance.GetComponentsInChildren<ParticleSystem>(true))
        {
            ParticleSystem.MainModule main = particleSystem.main;
            if (main.startSize3D)
            {
                main.startSizeX = ScaleCurve(main.startSizeX, visualScale);
                main.startSizeY = ScaleCurve(main.startSizeY, visualScale);
                main.startSizeZ = ScaleCurve(main.startSizeZ, visualScale);
            }
            else
            {
                main.startSize = ScaleCurve(main.startSize, visualScale);
            }

            main.startSpeed = ScaleCurve(main.startSpeed, visualScale);

            ParticleSystem.ShapeModule shape = particleSystem.shape;
            if (shape.enabled)
            {
                shape.radius *= visualScale;
                shape.scale *= visualScale;
                shape.position *= visualScale;
            }

            ParticleSystem.TrailModule trails = particleSystem.trails;
            if (trails.enabled)
            {
                trails.widthOverTrail = ScaleCurve(trails.widthOverTrail, visualScale);
            }

            ParticleSystemRenderer renderer = particleSystem.GetComponent<ParticleSystemRenderer>();
            if (renderer != null)
            {
                renderer.lengthScale *= visualScale;
                renderer.velocityScale *= visualScale;
            }
        }
    }

    private static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float scale)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                curve.constant *= scale;
                break;
            case ParticleSystemCurveMode.TwoConstants:
                curve.constantMin *= scale;
                curve.constantMax *= scale;
                break;
            case ParticleSystemCurveMode.Curve:
            case ParticleSystemCurveMode.TwoCurves:
                curve.curveMultiplier *= scale;
                break;
        }

        return curve;
    }

    private static int GetAttackMask()
    {
        if (_attackMask == 0)
        {
            _attackMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
        }

        return _attackMask;
    }

    private static int GetAttackMaskTerrain()
    {
        if (_attackMaskTerrain == 0)
        {
            _attackMaskTerrain = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
        }

        return _attackMaskTerrain;
    }

    private static int GetAttackMaskCharacters()
    {
        if (_attackMaskCharacters == 0)
        {
            _attackMaskCharacters = LayerMask.GetMask("character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
        }

        return _attackMaskCharacters;
    }

    internal sealed class AftershockController : MonoBehaviour
    {
        private int _nextWave;
        private float _nextWaveTime;
        private bool _registered;
        private bool _durabilityDrained;
        private bool _finished;

        internal Attack Attack { get; private set; } = null!;

        internal AftershockDefinition Aftershock { get; private set; } = null!;

        internal Vector3 BaseOrigin { get; private set; }

        internal Vector3 Forward { get; private set; }

        internal Quaternion Rotation { get; private set; }

        internal void Initialize(Attack attack, AftershockDefinition aftershock)
        {
            Attack = attack;
            Aftershock = aftershock;
            Transform attackerTransform = attack.m_character.transform;
            Transform attackOrigin = ResolveAttackOrigin(attack);
            Forward = attackerTransform.forward;
            Rotation = attackerTransform.rotation;
            BaseOrigin = attackOrigin.position +
                         Vector3.up * attack.m_attackHeight +
                         Forward * attack.m_attackRange +
                         attackerTransform.right * attack.m_attackOffset;
            _nextWave = 0;
            _nextWaveTime = Time.time;
            _registered = true;
            SecondaryAttackManager.RegisterAsyncSecondaryWork(attack.m_character);
            enabled = true;
        }

        private void Update()
        {
            if (Attack?.m_character == null ||
                Attack.m_weapon == null ||
                Attack.m_character.IsDead() ||
                !SecondaryAttackManager.HasCharacterAuthority(Attack.m_character))
            {
                Finish();
                return;
            }

            while (_nextWave <= Aftershock.Waves && Time.time >= _nextWaveTime)
            {
                ApplyWave(this, _nextWave);
                _nextWave++;
                float interval = Mathf.Max(0f, Aftershock.Interval);
                _nextWaveTime += interval;
                if (interval > 0f)
                {
                    break;
                }
            }

            if (_nextWave > Aftershock.Waves)
            {
                Finish();
            }
        }

        internal void DrainDurabilityOnce()
        {
            if (_durabilityDrained)
            {
                return;
            }

            SecondaryAttackManager.DrainAttackDurability(Attack, Aftershock.DurabilityFactor);
            _durabilityDrained = true;
        }

        private void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            Destroy(this);
        }

        private void OnDestroy()
        {
            if (_registered)
            {
                SecondaryAttackManager.UnregisterAsyncSecondaryWork(Attack?.m_character);
                _registered = false;
            }
        }
    }
}
