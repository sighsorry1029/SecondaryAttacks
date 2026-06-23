using System;
using System.Collections.Generic;
using UnityEngine;

namespace SecondaryAttacks;

internal static class MeleeBoomerangProjectileSystem
{
    internal const int ReturnAutoEquipDelayFrames = 1;
    private static readonly List<PendingReturnAutoEquip> PendingReturnAutoEquips = new();

    internal static void TryApplyToProjectileSetup(Projectile projectile, Attack attack, ItemDrop.ItemData weapon)
    {
        if (projectile == null ||
            attack == null ||
            weapon?.m_dropPrefab == null ||
            !SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition) ||
            definition.Behavior is not CopiedSecondaryBehavior ||
            definition.Boomerang == null)
        {
            return;
        }

        System.Diagnostics.Stopwatch? perf = SecondaryAttackPerformanceLog.Start();
        try
        {
            BoomerangProjectileController controller =
                projectile.GetComponent<BoomerangProjectileController>() ??
                projectile.gameObject.AddComponent<BoomerangProjectileController>();
            controller.Configure(projectile, attack, definition.Boomerang);
        }
        finally
        {
            SecondaryAttackPerformanceLog.Stop(
                perf,
                "boomerang.setup",
                $"weapon={weapon.m_dropPrefab.name} projectile={projectile.name} maxDistance={definition.Boomerang.MaxDistance:0.###} curve={definition.Boomerang.CurveFactor:0.###}");
        }
    }

    internal static void UpdateDeferredReturnAutoEquips(Player player)
    {
        if (player == null || PendingReturnAutoEquips.Count == 0)
        {
            return;
        }

        for (int i = PendingReturnAutoEquips.Count - 1; i >= 0; i--)
        {
            PendingReturnAutoEquip pending = PendingReturnAutoEquips[i];
            if (pending.Owner == null || pending.Item == null)
            {
                PendingReturnAutoEquips.RemoveAt(i);
                continue;
            }

            if (pending.Owner != (Humanoid)player)
            {
                continue;
            }

            if (Time.frameCount < pending.DueFrame)
            {
                continue;
            }

            PendingReturnAutoEquips.RemoveAt(i);
            if (pending.Item.m_equipped)
            {
                continue;
            }

            System.Diagnostics.Stopwatch? perf = SecondaryAttackPerformanceLog.Start();
            pending.Owner.EquipItem(pending.Item);
            SecondaryAttackPerformanceLog.Stop(
                perf,
                "boomerang.return.equipDeferred",
                () => $"owner={pending.Owner.name} item={pending.ItemName} equipped={pending.Item.m_equipped}");
        }
    }

    internal static void QueueReturnAutoEquip(Humanoid owner, ItemDrop.ItemData item, string itemName)
    {
        int dueFrame = Time.frameCount + ReturnAutoEquipDelayFrames;
        for (int i = PendingReturnAutoEquips.Count - 1; i >= 0; i--)
        {
            PendingReturnAutoEquip pending = PendingReturnAutoEquips[i];
            if (pending.Owner == owner && pending.Item == item)
            {
                PendingReturnAutoEquips.RemoveAt(i);
            }
        }

        PendingReturnAutoEquips.Add(new PendingReturnAutoEquip(owner, item, itemName, dueFrame));
    }

    private readonly struct PendingReturnAutoEquip
    {
        public PendingReturnAutoEquip(Humanoid owner, ItemDrop.ItemData item, string itemName, int dueFrame)
        {
            Owner = owner;
            Item = item;
            ItemName = itemName;
            DueFrame = dueFrame;
        }

        public Humanoid Owner { get; }
        public ItemDrop.ItemData Item { get; }
        public string ItemName { get; }
        public int DueFrame { get; }
    }

    internal static bool TryHandleProjectileHit(
        Projectile projectile,
        Collider collider,
        Vector3 hitPoint,
        bool water,
        Vector3 normal)
    {
        BoomerangProjectileController? controller =
            projectile != null ? projectile.GetComponent<BoomerangProjectileController>() : null;
        return controller != null && controller.TryHandleHit(collider, hitPoint, water, normal);
    }
}

internal sealed class BoomerangProjectileController : MonoBehaviour
{
    private const float TwoPi = Mathf.PI * 2f;
    private const float ProjectileSpeed = 20f;
    private const float HitCooldown = 0.25f;
    private const int MaxReachRaycastHits = 128;
    private static readonly RaycastHit[] s_reachRaycastHits = new RaycastHit[MaxReachRaycastHits];
    private static int s_rayMaskSolids;

    private readonly Dictionary<Character, float> _characterHitTimes = new();
    private readonly Dictionary<Collider, float> _destructibleHitTimes = new();
    private Projectile? _projectile;
    private Attack? _sourceAttack;
    private BoomerangDefinition? _definition;
    private ZNetView? _nview;
    private Character? _owner;
    private Vector3 _origin;
    private Vector3 _axis;
    private Vector3 _side;
    private Vector3 _center;
    private float _longRadius;
    private float _shortRadius;
    private float _flightTime;
    private float _despawnDistance;
    private float _despawnDistanceSqr;
    private float _catchRadius;
    private float _catchRadiusSqr;
    private float _catchDelay;
    private float _baseAdrenaline;
    private float _elapsed;
    private float _originalGravity;
    private int _hitCount;
    private bool _autoEquipOnCatch;
    private bool _complete;

    internal void Configure(Projectile projectile, Attack sourceAttack, BoomerangDefinition definition)
    {
        _projectile = projectile;
        _sourceAttack = sourceAttack;
        _definition = definition;
        _nview = projectile.GetComponent<ZNetView>();
        _owner = ProjectileAccess.GetOwner(projectile) ?? sourceAttack.m_character;
        _origin = projectile.transform.position;
        _despawnDistance = Mathf.Max(0.1f, definition.DespawnDistance);
        _despawnDistanceSqr = _despawnDistance * _despawnDistance;
        _catchRadius = Mathf.Max(0f, definition.CatchRadius);
        _catchRadiusSqr = _catchRadius * _catchRadius;
        _catchDelay = Mathf.Max(0f, definition.CatchDelay);
        _autoEquipOnCatch = definition.AutoEquipOnCatch;
        _baseAdrenaline = Mathf.Max(0f, projectile.m_adrenaline);
        projectile.m_adrenaline = 0f;
        _elapsed = 0f;
        _hitCount = 0;
        _complete = false;
        _characterHitTimes.Clear();
        _destructibleHitTimes.Clear();

        Vector3 launchVelocity = projectile.GetVelocity();
        Vector3 aimDirection = launchVelocity.sqrMagnitude > 0.001f
            ? launchVelocity.normalized
            : projectile.transform.forward;
        if (aimDirection.sqrMagnitude < 0.001f)
        {
            aimDirection = Vector3.forward;
        }

        aimDirection.Normalize();
        float configuredMaxDistance = Mathf.Max(0.5f, definition.MaxDistance);
        System.Diagnostics.Stopwatch? reachPerf = SecondaryAttackPerformanceLog.Start();
        float maxReach = configuredMaxDistance;
        int reachHitCount = 0;
        int validReachHitCount = 0;
        bool resolvedReach = false;
        try
        {
            maxReach = ResolveMaxReach(
                projectile,
                aimDirection,
                configuredMaxDistance,
                out reachHitCount,
                out validReachHitCount);
            resolvedReach = true;
        }
        finally
        {
            SecondaryAttackPerformanceLog.Stop(
                reachPerf,
                "boomerang.resolveMaxReach",
                $"projectile={projectile.name} maxDistance={configuredMaxDistance:0.###} reach={maxReach:0.###} hits={reachHitCount} validHits={validReachHitCount} resolved={resolvedReach}");
        }
        _longRadius = Mathf.Max(0.1f, maxReach * 0.5f);
        _shortRadius = Mathf.Max(0f, _longRadius * Mathf.Max(0f, definition.CurveFactor));
        _flightTime = Mathf.Max(0.1f, EstimateEllipseCircumference(_longRadius, _shortRadius) / ProjectileSpeed);
        _axis = aimDirection;
        _center = _origin + _axis * _longRadius;
        _side = ResolveSideAxis(projectile.transform, _axis);
        if (definition.Side.Equals("left", StringComparison.OrdinalIgnoreCase))
        {
            _side = -_side;
        }

        _originalGravity = projectile.m_gravity;
        projectile.m_gravity = 0f;
        projectile.m_ttl = Mathf.Max(projectile.m_ttl, _flightTime + 1f);
    }

    internal bool TryHandleHit(Collider collider, Vector3 hitPoint, bool water, Vector3 normal)
    {
        if (_complete || _projectile == null || _definition == null || water || collider == null)
        {
            return false;
        }

        Character? owner = ResolveOwner();
        GameObject hitObject = Projectile.FindHitObject(collider);
        if (hitObject == null ||
            hitObject == _projectile.gameObject ||
            collider.transform.IsChildOf(_projectile.transform))
        {
            return true;
        }

        if (owner != null && hitObject == owner.gameObject)
        {
            if (_elapsed >= _catchDelay && owner is Humanoid humanoid)
            {
                TryReturnItemToOwner(humanoid);
            }

            return true;
        }

        Character? character = ProjectileRuntimeSystem.GetHitCharacter(collider);
        IDestructible? destructible = character != null ? character : hitObject.GetComponent<IDestructible>();
        if (destructible == null || !IsValidTarget(owner, character, destructible, _definition))
        {
            return false;
        }

        if (IsOnHitCooldown(character, collider, HitCooldown))
        {
            return true;
        }

        if (!ApplyHit(owner, character, destructible, collider, hitPoint, normal, _definition))
        {
            return false;
        }

        RegisterHitCooldown(character, collider);
        _hitCount++;
        return true;
    }

    private void FixedUpdate()
    {
        if (_complete || _projectile == null)
        {
            return;
        }

        if (_nview != null && _nview.IsValid() && !_nview.IsOwner())
        {
            return;
        }

        _elapsed += Time.fixedDeltaTime;
        Character? owner = ResolveOwner();
        if (TryCatchByOwner(owner))
        {
            return;
        }

        float t = Mathf.Clamp01(_elapsed / _flightTime);
        Vector3 target = EvaluateGuidedEllipsePoint(t, owner);
        Vector3 velocity = (target - transform.position) / Mathf.Max(0.001f, Time.fixedDeltaTime);
        ProjectileAccess.SetVelocity(_projectile!, velocity);

        if (t >= 1f ||
            (_elapsed > _flightTime * 0.5f && (transform.position - ResolveReturnEndpoint(owner)).sqrMagnitude <= _despawnDistanceSqr))
        {
            CompleteReturnAtCurrentPosition();
        }
    }

    private bool TryCatchByOwner(Character? owner)
    {
        if (_projectile == null || _catchRadius <= 0f || _elapsed < _catchDelay)
        {
            return false;
        }

        if (owner is not Humanoid humanoid || owner.IsDead())
        {
            return false;
        }

        if ((transform.position - owner.GetCenterPoint()).sqrMagnitude > _catchRadiusSqr)
        {
            return false;
        }

        return TryReturnItemToOwner(humanoid);
    }

    private bool ApplyHit(
        Character? owner,
        Character? character,
        IDestructible destructible,
        Collider collider,
        Vector3 hitPoint,
        Vector3 normal,
        BoomerangDefinition definition)
    {
        HitData? hitData = ProjectileAccess.GetOriginalHitData(_projectile!)?.Clone();
        if (hitData == null)
        {
            return false;
        }

        SecondaryAttackProjectileToolTierSystem.ApplyToHitData(
            hitData,
            _projectile,
            ProjectileAccess.GetWeapon(_projectile!),
            "MeleeBoomerangProjectileSystem.ApplyHit");
        float decayScale = Mathf.Pow(1f - definition.HitDamageDecay, _hitCount);
        float damageScale = definition.DamageFactor * decayScale;
        if (!Mathf.Approximately(damageScale, 1f))
        {
            hitData.m_damage.Modify(damageScale);
        }

        if (hitData.m_damage.GetTotalDamage() <= 0f && definition.PushFactor <= 0f)
        {
            return false;
        }

        if (character != null && hitData.m_dodgeable && character.IsDodgeInvincible())
        {
            if (character is Player dodgingPlayer)
            {
                dodgingPlayer.HitWhileDodging();
            }

            return true;
        }

        hitData.m_pushForce *= definition.PushFactor * decayScale;
        hitData.m_skillRaiseAmount = 0f;
        hitData.m_point = hitPoint;
        hitData.m_dir = ResolveHitDirection(owner, character, hitPoint, normal);
        hitData.m_hitCollider = collider;
        if (owner != null)
        {
            hitData.SetAttacker(owner);
        }

        destructible.Damage(hitData);
        if (owner != null &&
            character != null &&
            BaseAI.IsEnemy(owner, character) &&
            _sourceAttack != null &&
            _baseAdrenaline > 0f &&
            character.m_enemyAdrenalineMultiplier > 0f)
        {
            SecondaryAttackAdrenalineSystem.TryGrantOnceRaw(
                _sourceAttack,
                character,
                _baseAdrenaline,
                1f,
                "boomerang");
        }

        PlayHitEffects(owner, hitPoint, normal);
        return true;
    }

    private void PlayHitEffects(Character? owner, Vector3 hitPoint, Vector3 normal)
    {
        if (_projectile == null)
        {
            return;
        }

        Quaternion rotation = SecondaryAttackNamedEffectSystem.RotationFromNormal(normal);
        _projectile.m_hitEffects.Create(hitPoint, rotation);
        if (owner != null && _projectile.m_hitNoise > 0f)
        {
            owner.AddNoise(_projectile.m_hitNoise);
        }
    }

    private bool IsOnHitCooldown(Character? character, Collider collider, float cooldown)
    {
        if (cooldown <= 0f)
        {
            return false;
        }

        float now = Time.time;
        if (character != null)
        {
            return _characterHitTimes.TryGetValue(character, out float lastHit) && now - lastHit < cooldown;
        }

        return _destructibleHitTimes.TryGetValue(collider, out float lastColliderHit) && now - lastColliderHit < cooldown;
    }

    private void RegisterHitCooldown(Character? character, Collider collider)
    {
        float now = Time.time;
        if (character != null)
        {
            _characterHitTimes[character] = now;
            return;
        }

        _destructibleHitTimes[collider] = now;
    }

    private static bool IsValidTarget(
        Character? owner,
        Character? character,
        IDestructible destructible,
        BoomerangDefinition definition)
    {
        if (character != null)
        {
            if (owner == null || character == owner || character.IsDead())
            {
                return false;
            }

            if (BaseAI.IsEnemy(owner, character))
            {
                return true;
            }

            return owner.IsPlayer() &&
                   character.GetBaseAI() != null &&
                   character.GetBaseAI().IsAggravatable();
        }

        if (!definition.IncludeDestructibles)
        {
            return false;
        }

        DestructibleType type = destructible.GetDestructibleType();
        return type != DestructibleType.None && type != DestructibleType.Character;
    }

    private static Vector3 ResolveHitDirection(Character? owner, Character? character, Vector3 hitPoint, Vector3 normal)
    {
        if (owner != null)
        {
            Vector3 targetPoint = character != null ? character.GetCenterPoint() : hitPoint;
            Vector3 direction = targetPoint - owner.GetCenterPoint();
            if (direction.sqrMagnitude > 0.001f)
            {
                return direction.normalized;
            }
        }

        if (normal.sqrMagnitude > 0.001f)
        {
            return -normal.normalized;
        }

        return Vector3.forward;
    }

    private Vector3 EvaluateEllipsePoint(float t)
    {
        float angle = t * TwoPi;
        return _center - _axis * (_longRadius * Mathf.Cos(angle)) + _side * (_shortRadius * Mathf.Sin(angle));
    }

    private Vector3 EvaluateGuidedEllipsePoint(float t, Character? owner)
    {
        Vector3 point = EvaluateEllipsePoint(t);
        if (t <= 0.5f || _projectile == null)
        {
            return point;
        }

        if (owner == null || owner.IsDead())
        {
            return point;
        }

        float returnProgress = Mathf.InverseLerp(0.5f, 1f, t);
        float returnWeight = Mathf.SmoothStep(0f, 1f, returnProgress);
        return point + (owner.GetCenterPoint() - _origin) * returnWeight;
    }

    private Vector3 ResolveReturnEndpoint(Character? owner)
    {
        return owner != null && !owner.IsDead() ? owner.GetCenterPoint() : _origin;
    }

    private static float EstimateEllipseCircumference(float longRadius, float shortRadius)
    {
        if (shortRadius <= 0f)
        {
            return longRadius * 4f;
        }

        float a = Mathf.Max(longRadius, shortRadius);
        float b = Mathf.Min(longRadius, shortRadius);
        return Mathf.PI * (3f * (a + b) - Mathf.Sqrt((3f * a + b) * (a + 3f * b)));
    }

    private static float ResolveMaxReach(
        Projectile projectile,
        Vector3 aimDirection,
        float maxDistance,
        out int hitCount,
        out int validHitCount)
    {
        EnsureRayMask();
        hitCount = Physics.RaycastNonAlloc(
            projectile.transform.position,
            aimDirection,
            s_reachRaycastHits,
            maxDistance,
            s_rayMaskSolids,
            QueryTriggerInteraction.Ignore);
        validHitCount = 0;
        if (hitCount == 0)
        {
            return maxDistance;
        }

        Character? owner = ProjectileAccess.GetOwner(projectile);
        float nearestDistance = maxDistance;
        bool foundValidHit = false;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = s_reachRaycastHits[i];
            s_reachRaycastHits[i] = default;
            if (hit.collider == null ||
                hit.collider.gameObject == projectile.gameObject ||
                hit.collider.transform.IsChildOf(projectile.transform))
            {
                continue;
            }

            GameObject hitObject = Projectile.FindHitObject(hit.collider);
            if (hitObject == projectile.gameObject ||
                owner != null && hitObject == owner.gameObject)
            {
                continue;
            }

            if (hit.distance < nearestDistance)
            {
                nearestDistance = hit.distance;
                foundValidHit = true;
            }

            validHitCount++;
        }

        return foundValidHit ? Mathf.Clamp(nearestDistance, 0.5f, maxDistance) : maxDistance;
    }

    private Character? ResolveOwner()
    {
        if (_owner != null)
        {
            return _owner;
        }

        _owner = _projectile != null ? ProjectileAccess.GetOwner(_projectile) : null;
        if (_owner == null)
        {
            _owner = _sourceAttack?.m_character;
        }

        return _owner;
    }

    private static Vector3 ResolveSideAxis(Transform source, Vector3 axis)
    {
        Vector3 side = Vector3.ProjectOnPlane(source.right, axis);
        if (side.sqrMagnitude < 0.001f)
        {
            side = Vector3.Cross(Vector3.up, axis);
        }

        if (side.sqrMagnitude < 0.001f)
        {
            side = Vector3.Cross(Vector3.right, axis);
        }

        return side.sqrMagnitude > 0.001f ? side.normalized : Vector3.right;
    }

    private static void EnsureRayMask()
    {
        if (s_rayMaskSolids != 0)
        {
            return;
        }

        s_rayMaskSolids = LayerMask.GetMask(
            "Default",
            "static_solid",
            "Default_small",
            "piece",
            "piece_nonsolid",
            "terrain",
            "character",
            "character_net",
            "character_ghost",
            "hitbox",
            "character_noenv",
            "vehicle");
    }

    private bool TryReturnItemToOwner(Humanoid owner)
    {
        if (_projectile == null)
        {
            return false;
        }

        System.Diagnostics.Stopwatch? totalPerf = SecondaryAttackPerformanceLog.Start();
        string result = "completed";
        ItemDrop.ItemData? item = _projectile.m_spawnItem;
        string itemName = item?.m_dropPrefab?.name ?? item?.m_shared?.m_name ?? "<null>";
        try
        {
            System.Diagnostics.Stopwatch? stepPerf = SecondaryAttackPerformanceLog.Start();
            Inventory? inventory = owner.GetInventory();
            bool canAdd = item != null && inventory != null && inventory.CanAddItem(item);
            SecondaryAttackPerformanceLog.Stop(
                stepPerf,
                "boomerang.return.canAdd",
                () => $"owner={owner.name} item={itemName} canAdd={canAdd} inventory={inventory != null}");
            if (item == null || inventory == null || !canAdd)
            {
                result = "fallbackComplete";
                CompleteReturnAtCurrentPosition();
                return true;
            }

            item.m_equipped = false;
            stepPerf = SecondaryAttackPerformanceLog.Start();
            bool added = inventory.AddItem(item);
            SecondaryAttackPerformanceLog.Stop(
                stepPerf,
                "boomerang.return.addItem",
                () => $"owner={owner.name} item={itemName} added={added}");
            if (!added)
            {
                result = "fallbackComplete";
                CompleteReturnAtCurrentPosition();
                return true;
            }

            _complete = true;
            _projectile.m_respawnItemOnHit = false;
            _projectile.m_spawnItem = null;
            _projectile.m_spawnOnTtl = false;
            _projectile.m_gravity = _originalGravity;
            ProjectileAccess.SetVelocity(_projectile, Vector3.zero);
            if (_autoEquipOnCatch)
            {
                stepPerf = SecondaryAttackPerformanceLog.Start();
                MeleeBoomerangProjectileSystem.QueueReturnAutoEquip(owner, item, itemName);
                SecondaryAttackPerformanceLog.Stop(
                    stepPerf,
                    "boomerang.return.equipDispatch",
                    () => $"owner={owner.name} item={itemName} dueFrame={Time.frameCount + MeleeBoomerangProjectileSystem.ReturnAutoEquipDelayFrames}");
            }

            stepPerf = SecondaryAttackPerformanceLog.Start();
            ProjectileRuntimeSystem.DestroyProjectileObject(_projectile.gameObject);
            SecondaryAttackPerformanceLog.Stop(
                stepPerf,
                "boomerang.return.destroy",
                () => $"owner={owner.name} item={itemName}");
            enabled = false;
            return true;
        }
        finally
        {
            SecondaryAttackPerformanceLog.Stop(
                totalPerf,
                "boomerang.return.total",
                () => $"owner={owner.name} item={itemName} result={result} autoEquip={_autoEquipOnCatch}");
        }
    }

    private void CompleteReturnAtCurrentPosition()
    {
        _complete = true;
        if (_projectile == null)
        {
            return;
        }

        Projectile projectile = _projectile;
        projectile.m_gravity = _originalGravity;
        ProjectileAccess.SetVelocity(projectile, Vector3.zero);
        System.Diagnostics.Stopwatch? perf = SecondaryAttackPerformanceLog.Start();
        projectile.OnHit(null, transform.position, false, Vector3.up);
        SecondaryAttackPerformanceLog.Stop(
            perf,
            "boomerang.return.completeAtPosition",
            () => $"projectile={projectile.name}");
        enabled = false;
    }

}
