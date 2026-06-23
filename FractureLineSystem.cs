using System.Collections.Generic;
using UnityEngine;

namespace SecondaryAttacks;

internal static class FractureLineSystem
{
    private const float SurfaceDepthTolerance = 0.35f;
    private const float SurfaceHeight = 1.2f;
    private const float PushFactor = 0.3f;
    private const float VisualLineWidth = 0.02f;
    private const float VisualZigzagAmplitude = 0.45f;
    private const int VisualBranchCount = 7;
    private const float VisualBranchLengthFactor = 0.5f;
    private const float VisualFadeSeconds = 1f;
    private static readonly Collider[] Hits = new Collider[128];
    private static readonly HashSet<GameObject> HitObjects = new();
    private static readonly HashSet<Collider> HitColliders = new();
    private static readonly List<FractureLineHitTarget> HitTargets = new();
    private static Material? _fractureMaterial;
    private static int _attackMask;
    private static int _environmentMask;
    private static int _groundMask;

    internal static bool CanHandle(Attack attack, SecondaryAttackDefinition definition)
    {
        return attack != null &&
               definition?.FractureLine != null &&
               attack.m_character != null &&
               attack.m_weapon?.m_shared != null &&
               (attack.m_attackType == Attack.AttackType.Horizontal || attack.m_attackType == Attack.AttackType.Vertical);
    }

    internal static void Trigger(Attack attack, SecondaryAttackDefinition definition)
    {
        if (!CanHandle(attack, definition) || !SecondaryAttackManager.HasCharacterAuthority(attack.m_character))
        {
            return;
        }

        FractureLineController controller = attack.m_character.gameObject.AddComponent<FractureLineController>();
        controller.Initialize(attack, definition.FractureLine!);
    }

    private static void ApplyTick(FractureLineController controller)
    {
        Attack attack = controller.Attack;
        FractureLineDefinition fractureLine = controller.FractureLine;
        Character attacker = attack.m_character;
        ItemDrop.ItemData weapon = attack.m_weapon;
        if (attacker == null || weapon?.m_shared == null)
        {
            return;
        }

        HitObjects.Clear();
        HitColliders.Clear();
        HitTargets.Clear();
        float skillFactor = attacker.GetRandomSkillFactor(weapon.m_shared.m_skillType);
        float hitRadius = Mathf.Max(0.1f, fractureLine.HitSpacing);
        int sampleCount = Mathf.Max(2, Mathf.CeilToInt(controller.DamageLength / hitRadius) + 1);

        for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
        {
            float t = sampleCount <= 1 ? 0f : sampleIndex / (float)(sampleCount - 1);
            Vector3 samplePoint = controller.GetSurfacePoint(t);
            CollectSamplePoint(controller, samplePoint, hitRadius);
        }

        float effectiveSkillFactor = skillFactor * ResolveMultiTargetPenalty(HitTargets.Count);
        foreach (FractureLineHitTarget target in HitTargets)
        {
            ApplyCollectedHit(controller, target, effectiveSkillFactor);
        }

        HitTargets.Clear();
    }

    private static void CollectSamplePoint(
        FractureLineController controller,
        Vector3 samplePoint,
        float hitRadius)
    {
        int count = Physics.OverlapSphereNonAlloc(samplePoint, hitRadius, Hits, GetAttackMask(), QueryTriggerInteraction.UseGlobal);
        for (int i = 0; i < count; i++)
        {
            TryCollectSampleHit(controller, samplePoint, Hits[i]);
        }
    }

    private static void TryCollectSampleHit(
        FractureLineController controller,
        Vector3 samplePoint,
        Collider collider)
    {
        if (collider == null)
        {
            return;
        }

        Attack attack = controller.Attack;
        FractureLineDefinition fractureLine = controller.FractureLine;
        Character attacker = attack.m_character;
        if (collider.gameObject == attacker.gameObject)
        {
            return;
        }

        GameObject hitObject = Projectile.FindHitObject(collider);
        if (hitObject == null || hitObject == attacker.gameObject)
        {
            return;
        }

        IDestructible? destructible = hitObject.GetComponent<IDestructible>();
        if (destructible == null)
        {
            return;
        }

        Character? character = destructible as Character;
        bool isEnemy = false;
        if (character != null)
        {
            if (!IsValidCharacterTarget(attack, character, out isEnemy))
            {
                return;
            }
        }
        else if (!IsValidDestructibleTarget(attack, destructible))
        {
            return;
        }

        Vector3 point = ResolveTargetSurfacePoint(collider, destructible, samplePoint);
        if (!IsInsideSampleSurface(fractureLine, samplePoint, point))
        {
            return;
        }

        if (character != null)
        {
            if (!HitObjects.Add(hitObject))
            {
                return;
            }
        }
        else if (!HitColliders.Add(collider))
        {
            return;
        }

        HitTargets.Add(new FractureLineHitTarget(
            destructible,
            character,
            collider,
            point,
            ResolveHitDirection(controller, point),
            1f,
            isEnemy));
    }

    private static void ApplyCollectedHit(
        FractureLineController controller,
        FractureLineHitTarget target,
        float skillFactor)
    {
        Attack attack = controller.Attack;
        Character attacker = attack.m_character;
        ItemDrop.ItemData weapon = attack.m_weapon;
        if (attacker == null || weapon?.m_shared == null)
        {
            return;
        }

        HitData hitData = CreateHitData(
            attack,
            target.Collider,
            target.Point,
            target.HitDirection,
            skillFactor,
            controller.FractureLine,
            target.TargetFactor);
        attacker.GetSEMan().ModifyAttack(weapon.m_shared.m_skillType, ref hitData);
        target.Destructible.Damage(hitData);

        if (target.Character != null)
        {
            if (attack.m_attackHealthReturnHit > 0f && target.IsEnemy)
            {
                attacker.Heal(attack.m_attackHealthReturnHit);
            }
        }

        controller.DrainDurabilityOnce();
    }

    private static float ResolveMultiTargetPenalty(int hitCount)
    {
        return hitCount > 1
            ? 1f / (hitCount * 0.75f)
            : 1f;
    }

    private static HitData CreateHitData(
        Attack attack,
        Collider collider,
        Vector3 hitPoint,
        Vector3 hitDirection,
        float skillFactor,
        FractureLineDefinition fractureLine,
        float targetFactor)
    {
        return SecondaryAttackHitDataFactory.CreateMeleeHit(
            attack,
            collider,
            hitPoint,
            hitDirection,
            skillFactor,
            fractureLine.DamageFactor * targetFactor,
            PushFactor);
    }

    private static bool IsValidCharacterTarget(Attack attack, Character target, out bool isEnemy)
    {
        Character attacker = attack.m_character;
        isEnemy = BaseAI.IsEnemy(attacker, target) ||
                  (target.GetBaseAI() != null && target.GetBaseAI().IsAggravatable() && attacker.IsPlayer());
        if (!isEnemy)
        {
            return false;
        }

        if (((!attack.m_hitFriendly || attacker.IsTamed()) && !attacker.IsPlayer() && !isEnemy) ||
            (!attack.m_weapon.m_shared.m_tamedOnly && attacker.IsPlayer() && !attacker.IsPVPEnabled() && !isEnemy) ||
            (attack.m_weapon.m_shared.m_tamedOnly && !target.IsTamed()))
        {
            return false;
        }

        if (attack.m_weapon.m_shared.m_dodgeable && target.IsDodgeInvincible())
        {
            if (target.IsPlayer())
            {
                (target as Player)?.HitWhileDodging();
            }

            return false;
        }

        return true;
    }

    private static bool IsValidDestructibleTarget(Attack attack, IDestructible destructible)
    {
        if (attack.m_weapon.m_shared.m_tamedOnly)
        {
            return false;
        }

        DestructibleType type = destructible.GetDestructibleType();
        return type != DestructibleType.None && type != DestructibleType.Character;
    }

    private static Vector3 ResolveTargetSurfacePoint(Collider collider, IDestructible destructible, Vector3 samplePoint)
    {
        if (destructible is Character character)
        {
            return character.transform.position;
        }

        Vector3 surfaceProbe = samplePoint + Vector3.up * 0.25f;
        return SecondaryAttackManager.ResolveSafeClosestPoint(collider, surfaceProbe);
    }

    private static bool IsInsideSampleSurface(FractureLineDefinition fractureLine, Vector3 samplePoint, Vector3 point)
    {
        Vector3 horizontal = Vector3.ProjectOnPlane(point - samplePoint, Vector3.up);
        float hitRadius = Mathf.Max(0.1f, fractureLine.HitSpacing);
        if (horizontal.sqrMagnitude > hitRadius * hitRadius)
        {
            return false;
        }

        return point.y >= samplePoint.y - SurfaceDepthTolerance &&
               point.y <= samplePoint.y + SurfaceHeight;
    }

    private static Vector3 ResolveHitDirection(FractureLineController controller, Vector3 hitPoint)
    {
        Vector3 direction = Vector3.ProjectOnPlane(hitPoint - controller.Origin, Vector3.up);
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = controller.Forward;
        }

        return direction.normalized;
    }

    private static bool IsBlockedByEnvironment(Vector3 origin, Vector3 targetPoint, IDestructible allowedTarget)
    {
        if (_environmentMask == 0)
        {
            _environmentMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain");
        }

        Vector3 direction = targetPoint - origin;
        float distance = direction.magnitude;
        if (distance <= 0.01f)
        {
            return false;
        }

        RaycastHit[] hits = Physics.RaycastAll(origin, direction / distance, distance, _environmentMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider == null)
            {
                continue;
            }

            GameObject hitObject = Projectile.FindHitObject(hit.collider);
            if (hitObject != null && hitObject.GetComponent<IDestructible>() == allowedTarget)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static int GetAttackMask()
    {
        if (Attack.m_attackMask != 0)
        {
            return Attack.m_attackMask;
        }

        if (_attackMask == 0)
        {
            _attackMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
        }

        return _attackMask;
    }

    private static int GetGroundMask()
    {
        if (_groundMask == 0)
        {
            _groundMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain");
        }

        return _groundMask;
    }

    private static Vector3 SnapToGround(Vector3 point)
    {
        Vector3 rayOrigin = point + Vector3.up * 2f;
        return Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 8f, GetGroundMask(), QueryTriggerInteraction.Ignore)
            ? hit.point + Vector3.up * 0.04f
            : point + Vector3.up * 0.04f;
    }

    private static Material? GetFractureMaterial()
    {
        if (_fractureMaterial != null)
        {
            return _fractureMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default") ??
                        Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                        Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        _fractureMaterial = new Material(shader)
        {
            color = Color.white
        };
        return _fractureMaterial;
    }

    private readonly struct FractureLineHitTarget
    {
        public FractureLineHitTarget(
            IDestructible destructible,
            Character? character,
            Collider collider,
            Vector3 point,
            Vector3 hitDirection,
            float targetFactor,
            bool isEnemy)
        {
            Destructible = destructible;
            Character = character;
            Collider = collider;
            Point = point;
            HitDirection = hitDirection;
            TargetFactor = targetFactor;
            IsEnemy = isEnemy;
        }

        public IDestructible Destructible { get; }

        public Character? Character { get; }

        public Collider Collider { get; }

        public Vector3 Point { get; }

        public Vector3 HitDirection { get; }

        public float TargetFactor { get; }

        public bool IsEnemy { get; }
    }

    internal sealed class FractureLineController : MonoBehaviour
    {
        private readonly List<GameObject> _visualObjects = new();
        private readonly List<FractureLineVisualLine> _visualLines = new();
        private float _endTime;
        private float _visualEndTime;
        private float _nextTickTime;
        private bool _registered;
        private bool _durabilityDrained;
        private bool _finished;

        internal Attack Attack { get; private set; } = null!;

        internal FractureLineDefinition FractureLine { get; private set; } = null!;

        internal Vector3 Origin { get; private set; }

        internal Vector3 Forward { get; private set; }

        internal Vector3 DamageStart { get; private set; }

        internal Vector3 DamageEnd { get; private set; }

        internal Vector3 DamageForward { get; private set; }

        internal float DamageLength { get; private set; }

        internal Vector3 GetSurfacePoint(float t)
        {
            return SnapToGround(Vector3.Lerp(DamageStart, DamageEnd, Mathf.Clamp01(t)));
        }

        internal void Initialize(Attack attack, FractureLineDefinition fractureLine)
        {
            Attack = attack;
            FractureLine = fractureLine;
            Transform attackerTransform = attack.m_character.transform;
            Vector3 forward = Vector3.ProjectOnPlane(attackerTransform.forward, Vector3.up);
            Forward = forward.sqrMagnitude > 0.001f ? forward.normalized : attackerTransform.forward;
            Origin = attackerTransform.position +
                     Vector3.up * Mathf.Max(0f, attack.m_attackHeight) +
                     attackerTransform.right * attack.m_attackOffset;
            DamageStart = SnapToGround(Origin);
            DamageEnd = SnapToGround(Origin + Forward * fractureLine.Range);
            Vector3 damageForward = Vector3.ProjectOnPlane(DamageEnd - DamageStart, Vector3.up);
            DamageLength = damageForward.magnitude;
            DamageForward = DamageLength > 0.001f ? damageForward / DamageLength : Forward;
            DamageLength = Mathf.Max(0.1f, DamageLength);
            _endTime = Time.time + fractureLine.Duration;
            _visualEndTime = _endTime + VisualFadeSeconds;
            _nextTickTime = Time.time + fractureLine.TickInterval;
            _registered = true;
            CreateVisual();
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

            while (_nextTickTime <= _endTime && Time.time >= _nextTickTime)
            {
                ApplyTick(this);
                _nextTickTime += FractureLine.TickInterval;
                break;
            }

            UpdateVisualFade();

            float finishTime = _visualLines.Count > 0 ? _visualEndTime : _endTime;
            if (Time.time >= finishTime && _nextTickTime > _endTime)
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

            SecondaryAttackManager.DrainAttackDurability(Attack, FractureLine.DurabilityFactor);
            _durabilityDrained = true;
        }

        private void CreateVisual()
        {
            CreateLine(
                DamageStart,
                DamageEnd,
                VisualLineWidth,
                VisualZigzagAmplitude);
            int branchCount = VisualBranchCount;
            for (int i = 0; i < branchCount; i++)
            {
                float t = (i + 1f) / (branchCount + 1f);
                float side = i % 2 == 0 ? 1f : -1f;
                float angle = side * Mathf.Lerp(25f, 55f, (i % 3) / 2f);
                Vector3 start = Origin + Forward * (FractureLine.Range * t);
                Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * Forward;
                float length = FractureLine.Range * VisualBranchLengthFactor * Mathf.Lerp(0.45f, 1f, 1f - t);
                CreateLine(
                    SnapToGround(start),
                    SnapToGround(start + direction.normalized * length),
                    VisualLineWidth * 0.55f,
                    VisualZigzagAmplitude * 0.5f);
            }
        }

        private void CreateLine(Vector3 start, Vector3 end, float lineWidth, float zigzagAmplitude)
        {
            GameObject visualObject = new("SecondaryAttacks_FractureLine");
            _visualObjects.Add(visualObject);
            LineRenderer lineRenderer = visualObject.AddComponent<LineRenderer>();
            Material? material = GetFractureMaterial();
            if (material != null)
            {
                lineRenderer.material = material;
            }

            lineRenderer.useWorldSpace = true;
            List<Vector3> points = CreateJaggedLinePoints(start, end, zigzagAmplitude);
            lineRenderer.positionCount = points.Count;
            for (int i = 0; i < points.Count; i++)
            {
                lineRenderer.SetPosition(i, points[i]);
            }

            lineRenderer.widthMultiplier = lineWidth;
            lineRenderer.widthCurve = CreatePointedLineWidthCurve();
            lineRenderer.numCapVertices = 0;
            lineRenderer.numCornerVertices = 0;
            lineRenderer.startColor = new Color(0.12f, 0.075f, 0.055f, 1f);
            lineRenderer.endColor = new Color(0.07f, 0.045f, 0.035f, 0.95f);
            _visualLines.Add(new FractureLineVisualLine(lineRenderer, lineRenderer.startColor, lineRenderer.endColor));
        }

        private void UpdateVisualFade()
        {
            if (_visualLines.Count == 0)
            {
                return;
            }

            float alphaScale = Time.time <= _endTime
                ? 1f
                : Mathf.Clamp01((_visualEndTime - Time.time) / VisualFadeSeconds);
            foreach (FractureLineVisualLine visualLine in _visualLines)
            {
                if (visualLine.LineRenderer == null)
                {
                    continue;
                }

                Color startColor = visualLine.StartColor;
                Color endColor = visualLine.EndColor;
                startColor.a *= alphaScale;
                endColor.a *= alphaScale;
                visualLine.LineRenderer.startColor = startColor;
                visualLine.LineRenderer.endColor = endColor;
            }
        }

        private List<Vector3> CreateJaggedLinePoints(Vector3 start, Vector3 end, float zigzagAmplitude)
        {
            Vector3 segment = end - start;
            float length = segment.magnitude;
            if (length <= 0.01f)
            {
                return new List<Vector3> { start, end };
            }

            Vector3 direction = segment / length;
            Vector3 horizontalDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (horizontalDirection.sqrMagnitude < 0.001f)
            {
                horizontalDirection = Forward;
            }

            Vector3 side = Vector3.Cross(Vector3.up, horizontalDirection.normalized).normalized;
            int pointCount = Mathf.Clamp(Mathf.CeilToInt(length / 0.55f) + 1, 7, 15);
            float maxAmplitude = zigzagAmplitude;
            List<Vector3> points = new(pointCount);
            for (int i = 0; i < pointCount; i++)
            {
                float t = i / (float)(pointCount - 1);
                Vector3 point = Vector3.Lerp(start, end, t);
                if (i > 0 && i < pointCount - 1)
                {
                    float endpointFade = Mathf.Sin(t * Mathf.PI);
                    float endTaper = Mathf.Pow(1f - t, 0.85f);
                    float irregularity = 0.75f + 0.25f * Mathf.Sin(i * 1.79f + length * 0.33f);
                    float sign = i % 2 == 0 ? -1f : 1f;
                    point += side * (sign * maxAmplitude * endpointFade * endTaper * irregularity);
                }

                points.Add(SnapToGround(point));
            }

            return points;
        }

        private static AnimationCurve CreatePointedLineWidthCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.08f, 0.85f),
                new Keyframe(0.5f, 1f),
                new Keyframe(0.92f, 0.65f),
                new Keyframe(1f, 0f));
        }

        private void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            DestroyVisuals();
            Destroy(this);
        }

        private void DestroyVisuals()
        {
            foreach (GameObject visualObject in _visualObjects)
            {
                if (visualObject != null)
                {
                    Destroy(visualObject);
                }
            }

            _visualObjects.Clear();
            _visualLines.Clear();
        }

        private void OnDestroy()
        {
            DestroyVisuals();
            if (_registered)
            {
                SecondaryAttackManager.UnregisterAsyncSecondaryWork(Attack?.m_character);
                _registered = false;
            }
        }
    }

    private readonly struct FractureLineVisualLine
    {
        public FractureLineVisualLine(LineRenderer lineRenderer, Color startColor, Color endColor)
        {
            LineRenderer = lineRenderer;
            StartColor = startColor;
            EndColor = endColor;
        }

        public LineRenderer LineRenderer { get; }

        public Color StartColor { get; }

        public Color EndColor { get; }
    }
}
