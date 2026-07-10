using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

internal static class RiftTrailSystem
{
    internal const string ObserverFallbackVisualRpcName = "SecondaryAttacks_RiftTrailVisual";
    private const bool HitThroughWalls = false;
    private const int MaxTrailSamples = 64;
    private const float MinTrailSampleDistance = 0.025f;
    private const int MaxTrailSubdivisions = 8;
    private const int MaxObserverTrailSamples = 32;
    private const float PostTriggerVisualCaptureSeconds = 0.18f;
    private const float VisualFadeSeconds = 1f;
    private const float FanStepDegrees = 4f;
    private const string ObserverSamplePayloadType = "S";
    private const string ObserverFallbackPayloadType = "F";
    private static readonly FieldInfo AttackVisEquipmentField = AccessTools.Field(typeof(Attack), "m_visEquipment")!;
    private static readonly FieldInfo VisRightItemInstanceField = AccessTools.Field(typeof(VisEquipment), "m_rightItemInstance")!;
    private static readonly FieldInfo TrailBaseField = AccessTools.Field(typeof(MeleeWeaponTrail), "_base")!;
    private static readonly FieldInfo TrailTipField = AccessTools.Field(typeof(MeleeWeaponTrail), "_tip")!;
    private static readonly FieldInfo TrailMaterialField = AccessTools.Field(typeof(MeleeWeaponTrail), "_material")!;
    private static readonly FieldInfo TrailColorsField = AccessTools.Field(typeof(MeleeWeaponTrail), "_colors")!;
    private static readonly FieldInfo TrailSizesField = AccessTools.Field(typeof(MeleeWeaponTrail), "_sizes")!;
    private static readonly FieldInfo TrailLifeTimeField = AccessTools.Field(typeof(MeleeWeaponTrail), "_lifeTime")!;
    private static readonly FieldInfo TrailSubdivisionsField = AccessTools.Field(typeof(MeleeWeaponTrail), "subdivisions")!;
    private static readonly RaycastHit[] SweepHits = new RaycastHit[128];
    private static readonly HashSet<GameObject> HitObjects = new();
    private static readonly List<RiftTrailHitTarget> HitTargets = new();
    private static readonly Dictionary<Attack, RiftTrailController> ControllersByAttack = new();
    private static Material? _riftMaterial;
    private static int _attackMask;
    private static int _environmentMask;

    internal static bool CanHandle(Attack attack, SecondaryAttackDefinition definition)
    {
        return attack != null &&
               definition?.RiftTrail != null &&
               attack.m_character != null &&
               attack.m_weapon?.m_shared != null &&
               IsOneHandedSwordSecondaryAttack(attack);
    }

    private static bool IsOneHandedSwordSecondaryAttack(Attack attack)
    {
        return attack.m_attackType == Attack.AttackType.Horizontal &&
               attack.m_attackProjectile == null &&
               attack.m_attackRange > 0f &&
               attack.m_attackRayWidth > 0f &&
               string.Equals(attack.m_attackAnimation, "sword_secondary", System.StringComparison.OrdinalIgnoreCase);
    }

    internal static void BeginSampling(Attack attack, SecondaryAttackDefinition definition)
    {
        if (!CanHandle(attack, definition) ||
            !SecondaryAttackManager.HasCharacterAuthority(attack.m_character) ||
            ControllersByAttack.ContainsKey(attack))
        {
            return;
        }

        RiftTrailController controller = attack.m_character.gameObject.AddComponent<RiftTrailController>();
        controller.Initialize(attack, definition.RiftTrail!);
        ControllersByAttack[attack] = controller;
    }

    internal static void Trigger(Attack attack, SecondaryAttackDefinition definition)
    {
        if (!CanHandle(attack, definition) || !SecondaryAttackManager.HasCharacterAuthority(attack.m_character))
        {
            return;
        }

        if (!ControllersByAttack.TryGetValue(attack, out RiftTrailController? controller) || controller == null)
        {
            controller = attack.m_character.gameObject.AddComponent<RiftTrailController>();
            controller.Initialize(attack, definition.RiftTrail!);
            ControllersByAttack[attack] = controller;
        }

        controller.Activate();
    }

    internal static void HandleObserverFallbackVisualRpc(Character character, ZNetView? nview, Vector3 origin, Vector3 forward, string payload)
    {
        if (character == null || nview == null || !nview.IsValid() || nview.IsOwner())
        {
            return;
        }

        if (TryParseObserverSampleVisualPayload(payload, out RiftTrailObserverSampleVisualData sampleData))
        {
            RiftTrailObserverFallbackController sampleController =
                character.GetComponent<RiftTrailObserverFallbackController>() ??
                character.gameObject.AddComponent<RiftTrailObserverFallbackController>();
            sampleController.InitializeSampled(character, sampleData);
            return;
        }

        if (!TryParseObserverFallbackVisualPayload(payload, out RiftTrailObserverVisualData data))
        {
            return;
        }

        RiftTrailObserverFallbackController controller =
            character.GetComponent<RiftTrailObserverFallbackController>() ??
            character.gameObject.AddComponent<RiftTrailObserverFallbackController>();
        controller.Initialize(origin, forward, data);
    }

    private static void SendObserverFallbackVisual(Attack attack, RiftTrailDefinition riftTrail)
    {
        if (attack?.m_character == null ||
            riftTrail.VisualScaleFactor <= 0f ||
            !SecondaryAttackManager.TryGetCharacterZdo(attack.m_character, out ZNetView? nview, out _) ||
            ZRoutedRpc.instance == null)
        {
            return;
        }

        ResolveAttackShape(attack, out Vector3 origin, out Vector3 forward, out float range, out float angle, out float width);
        string payload = CreateObserverFallbackVisualPayload(
            range,
            angle,
            width,
            riftTrail.Duration,
            riftTrail.VisualScaleFactor,
            riftTrail.VisualForwardOffset,
            riftTrail.VisualAlphaFactor,
            riftTrail.VisualTint);
        SendObserverVisualPayload(attack, origin, forward, payload);
    }

    private static bool SendObserverVisualPayload(Attack attack, Vector3 origin, Vector3 forward, string payload)
    {
        if (string.IsNullOrWhiteSpace(payload) ||
            attack?.m_character == null ||
            !SecondaryAttackManager.TryGetCharacterZdo(attack.m_character, out ZNetView? nview, out _) ||
            ZRoutedRpc.instance == null)
        {
            return false;
        }

        nview!.InvokeRPC(ZNetView.Everybody, ObserverFallbackVisualRpcName, origin, forward, payload);
        return true;
    }

    private static void UnregisterController(Attack? attack, RiftTrailController controller)
    {
        if (attack != null &&
            ControllersByAttack.TryGetValue(attack, out RiftTrailController? registeredController) &&
            registeredController == controller)
        {
            ControllersByAttack.Remove(attack);
        }
    }

    private static void ApplyTick(RiftTrailController controller)
    {
        Attack attack = controller.Attack;
        RiftTrailDefinition riftTrail = controller.RiftTrail;
        Character attacker = attack.m_character;
        ItemDrop.ItemData weapon = attack.m_weapon;
        if (attacker == null || weapon?.m_shared == null)
        {
            return;
        }

        HitObjects.Clear();
        HitTargets.Clear();
        try
        {
            float skillFactor = attacker.GetRandomSkillFactor(weapon.m_shared.m_skillType);
            GatherSweepTargets(controller, attack);

            float effectiveSkillFactor = skillFactor * ResolveMultiTargetPenalty(HitTargets.Count);
            foreach (RiftTrailHitTarget target in HitTargets)
            {
                HitData hitData = CreateHitData(attack, target.Collider, target.Point, target.HitDirection, effectiveSkillFactor, riftTrail);
                attacker.GetSEMan().ModifyAttack(weapon.m_shared.m_skillType, ref hitData);
                target.Destructible.Damage(hitData);
                CreateHitEffects(attack, weapon, target.Point, target.HitDirection);

                if (target.Character != null &&
                    attack.m_attackHealthReturnHit > 0f &&
                    target.IsEnemy)
                {
                    attacker.Heal(attack.m_attackHealthReturnHit);
                }

                controller.DrainDurabilityOnce();
            }
        }
        finally
        {
            HitObjects.Clear();
            HitTargets.Clear();
            System.Array.Clear(SweepHits, 0, SweepHits.Length);
        }
    }

    private static void GatherSweepTargets(RiftTrailController controller, Attack attack)
    {
        float halfAngle = controller.Angle * 0.5f;
        int steps = Mathf.Max(1, Mathf.CeilToInt(controller.Angle / FanStepDegrees));
        float radius = Mathf.Max(0.01f, controller.Width);
        float distance = Mathf.Max(0.01f, controller.Range - radius);
        int layerMask = GetAttackMask();

        for (int i = 0; i <= steps; i++)
        {
            float angle = Mathf.Lerp(-halfAngle, halfAngle, i / (float)steps);
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * controller.Forward;
            if (direction.sqrMagnitude < 0.001f)
            {
                continue;
            }

            direction.Normalize();
            int count = Physics.SphereCastNonAlloc(
                controller.Origin,
                radius,
                direction,
                SweepHits,
                distance,
                layerMask,
                QueryTriggerInteraction.UseGlobal);
            System.Array.Sort(SweepHits, 0, count, RaycastHitDistanceComparer.Instance);

            for (int hitIndex = 0; hitIndex < count; hitIndex++)
            {
                TryAddSweepHit(controller, attack, SweepHits[hitIndex], direction, out bool blocked);
                if (blocked)
                {
                    break;
                }
            }
        }
    }

    private static bool TryAddSweepHit(
        RiftTrailController controller,
        Attack attack,
        RaycastHit hit,
        Vector3 direction,
        out bool blocked)
    {
        blocked = false;
        Collider collider = hit.collider;
        Character attacker = attack.m_character;
        if (collider == null || collider.gameObject == attacker.gameObject)
        {
            return false;
        }

        GameObject hitObject = Projectile.FindHitObject(collider);
        if (hitObject == null || hitObject == attacker.gameObject)
        {
            blocked = !HitThroughWalls && IsEnvironmentBlocker(collider);
            return false;
        }

        IDestructible? destructible = hitObject.GetComponent<IDestructible>();
        Character? character = destructible as Character;
        if (destructible == null || character == null)
        {
            blocked = !HitThroughWalls && (IsEnvironmentBlocker(collider) || destructible != null);
            return false;
        }

        if (!IsValidCharacterTarget(attack, character, out bool isEnemy) || !HitObjects.Add(hitObject))
        {
            return false;
        }

        Vector3 point = ResolveSweepHitPoint(controller, hit, direction);
        Vector3 hitDirection = ResolveHitDirection(controller, point);
        HitTargets.Add(new RiftTrailHitTarget(destructible, character, collider, point, hitDirection, isEnemy));
        return true;
    }

    private static Vector3 ResolveSweepHitPoint(RiftTrailController controller, RaycastHit hit, Vector3 direction)
    {
        if (hit.point.sqrMagnitude > 0f && (hit.point - controller.Origin).sqrMagnitude > 0.0001f)
        {
            return hit.point;
        }

        float distance = Mathf.Clamp(hit.distance, 0f, Mathf.Max(0f, controller.Range));
        return controller.Origin + direction * distance;
    }

    private static bool IsEnvironmentBlocker(Collider collider)
    {
        int layerMask = 1 << collider.gameObject.layer;
        return (GetEnvironmentMask() & layerMask) != 0;
    }

    private static float ResolveMultiTargetPenalty(int hitCount)
    {
        return hitCount > 1
            ? 1f / (hitCount * 0.75f)
            : 1f;
    }

    private static void CreateHitEffects(Attack attack, ItemDrop.ItemData weapon, Vector3 point, Vector3 hitDirection)
    {
        Quaternion rotation = hitDirection.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(hitDirection.normalized)
            : Quaternion.identity;
        weapon.m_shared.m_hitEffect.Create(point, rotation);
        attack.m_hitEffect.Create(point, rotation);
    }

    private static HitData CreateHitData(
        Attack attack,
        Collider collider,
        Vector3 hitPoint,
        Vector3 hitDirection,
        float skillFactor,
        RiftTrailDefinition riftTrail)
    {
        return SecondaryAttackHitDataFactory.CreateMeleeHit(
            attack,
            collider,
            hitPoint,
            hitDirection,
            skillFactor,
            riftTrail.DamageFactor,
            riftTrail.PushFactor);
    }

    private static bool IsValidCharacterTarget(Attack attack, Character target, out bool isEnemy)
    {
        Character attacker = attack.m_character;
        isEnemy = IsEnemy(attacker, target);
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

    private static bool IsEnemy(Character attacker, Character target)
    {
        return BaseAI.IsEnemy(attacker, target) ||
               (target.GetBaseAI() != null && target.GetBaseAI().IsAggravatable() && attacker.IsPlayer());
    }

    private static Vector3 ResolveHitDirection(RiftTrailController controller, Vector3 hitPoint)
    {
        Vector3 direction = Vector3.ProjectOnPlane(hitPoint - controller.Origin, Vector3.up);
        if (direction.sqrMagnitude < 0.001f)
        {
            direction = controller.Forward;
        }

        return direction.normalized;
    }

    private static int GetEnvironmentMask()
    {
        if (_environmentMask == 0)
        {
            _environmentMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "terrain");
        }

        return _environmentMask;
    }

    private static int GetAttackMask()
    {
        if (Attack.m_attackMask != 0)
        {
            return Attack.m_attackMask;
        }

        if (_attackMask == 0)
        {
            _attackMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid", "character", "character_net", "character_ghost", "hitbox", "character_noenv", "vehicle");
        }

        return _attackMask;
    }

    private sealed class RaycastHitDistanceComparer : IComparer<RaycastHit>
    {
        internal static readonly RaycastHitDistanceComparer Instance = new();

        public int Compare(RaycastHit left, RaycastHit right)
        {
            return left.distance.CompareTo(right.distance);
        }
    }

    private static Material? GetRiftMaterial()
    {
        if (_riftMaterial != null)
        {
            return _riftMaterial;
        }

        Shader shader = Shader.Find("Sprites/Default") ??
                        Shader.Find("Legacy Shaders/Particles/Alpha Blended") ??
                        Shader.Find("Standard");
        if (shader == null)
        {
            return null;
        }

        _riftMaterial = new Material(shader)
        {
            color = new Color(0.55f, 0.9f, 1f, 0.65f)
        };
        return _riftMaterial;
    }

    private static GameObject? GetRightItemInstance(Attack attack)
    {
        VisEquipment? visEquipment = AttackVisEquipmentField.GetValue(attack) as VisEquipment;
        GameObject? rightItemInstance = visEquipment != null
            ? VisRightItemInstanceField.GetValue(visEquipment) as GameObject
            : null;
        if (rightItemInstance != null)
        {
            return rightItemInstance;
        }

        return GetRightItemInstance(attack.m_character);
    }

    private static GameObject? GetRightItemInstance(Character? character)
    {
        VisEquipment? visEquipment = character != null ? character.GetComponent<VisEquipment>() : null;
        return visEquipment != null ? VisRightItemInstanceField.GetValue(visEquipment) as GameObject : null;
    }

    private static List<ObserverTrailMetadata> ResolveObserverTrailMetadata(Character character)
    {
        List<ObserverTrailMetadata> metadata = new();
        GameObject? rightItemInstance = GetRightItemInstance(character);
        if (rightItemInstance == null)
        {
            return metadata;
        }

        foreach (MeleeWeaponTrail trail in rightItemInstance.GetComponentsInChildren<MeleeWeaponTrail>(includeInactive: true))
        {
            metadata.Add(new ObserverTrailMetadata(
                TrailMaterialField.GetValue(trail) as Material,
                TrailColorsField.GetValue(trail) as Color[],
                TrailSizesField.GetValue(trail) as float[]));
        }

        return metadata;
    }

    private static void ResolveAttackShape(
        Attack attack,
        out Vector3 origin,
        out Vector3 forward,
        out float range,
        out float angle,
        out float width)
    {
        Transform attackerTransform = attack.m_character.transform;
        Vector3 projectedForward = Vector3.ProjectOnPlane(attackerTransform.forward, Vector3.up);
        forward = projectedForward.sqrMagnitude > 0.001f ? projectedForward.normalized : attackerTransform.forward;
        origin = attackerTransform.position +
                 Vector3.up * Mathf.Max(0f, attack.m_attackHeight) +
                 attackerTransform.right * attack.m_attackOffset;
        range = Mathf.Max(0.1f, attack.m_attackRange);
        angle = Mathf.Clamp(attack.m_attackAngle, 1f, 360f);
        width = Mathf.Max(0.01f, attack.m_attackRayWidth + Mathf.Max(0f, attack.m_attackRayWidthCharExtra));
    }

    private static string CreateObserverFallbackVisualPayload(
        float range,
        float angle,
        float width,
        float duration,
        float visualScaleFactor,
        float visualForwardOffset,
        float visualAlphaFactor,
        string visualTint)
    {
        CultureInfo culture = CultureInfo.InvariantCulture;
        return string.Join(
            "|",
            ObserverFallbackPayloadType,
            Mathf.Max(0.1f, range).ToString("R", culture),
            Mathf.Clamp(angle, 1f, 360f).ToString("R", culture),
            Mathf.Max(0.01f, width).ToString("R", culture),
            Mathf.Max(0.05f, duration).ToString("R", culture),
            Mathf.Max(0.01f, visualScaleFactor).ToString("R", culture),
            visualForwardOffset.ToString("R", culture),
            Mathf.Max(0f, visualAlphaFactor).ToString("R", culture),
            (visualTint ?? "#ffffff").Replace("|", string.Empty));
    }

    private static bool TryParseObserverFallbackVisualPayload(string payload, out RiftTrailObserverVisualData data)
    {
        data = default;
        string[] parts = payload.Split('|');
        int offset = parts.Length > 0 && string.Equals(parts[0], ObserverFallbackPayloadType, System.StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (parts.Length < offset + 7)
        {
            return false;
        }

        CultureInfo culture = CultureInfo.InvariantCulture;
        if (!float.TryParse(parts[offset], NumberStyles.Float, culture, out float range) ||
            !float.TryParse(parts[offset + 1], NumberStyles.Float, culture, out float angle) ||
            !float.TryParse(parts[offset + 2], NumberStyles.Float, culture, out float width) ||
            !float.TryParse(parts[offset + 3], NumberStyles.Float, culture, out float duration) ||
            !float.TryParse(parts[offset + 4], NumberStyles.Float, culture, out float visualScaleFactor) ||
            !float.TryParse(parts[offset + 5], NumberStyles.Float, culture, out float visualForwardOffset) ||
            !float.TryParse(parts[offset + 6], NumberStyles.Float, culture, out float visualAlphaFactor))
        {
            return false;
        }

        data = new RiftTrailObserverVisualData(
            Mathf.Max(0.1f, range),
            Mathf.Clamp(angle, 1f, 360f),
            Mathf.Max(0.01f, width),
            Mathf.Max(0.05f, duration),
            Mathf.Max(0.01f, visualScaleFactor),
            visualForwardOffset,
            Mathf.Max(0f, visualAlphaFactor),
            parts.Length >= offset + 8 ? parts[offset + 7] : "#ffffff");
        return true;
    }

    private static string CreateObserverSampleVisualPayload(
        RiftTrailDefinition riftTrail,
        IReadOnlyList<RiftTrailObserverSampleRibbonData> ribbons)
    {
        if (ribbons.Count == 0)
        {
            return string.Empty;
        }

        CultureInfo culture = CultureInfo.InvariantCulture;
        List<string> parts = new(8 + ribbons.Count * MaxObserverTrailSamples * 7)
        {
            ObserverSamplePayloadType,
            Mathf.Max(0.05f, riftTrail.Duration).ToString("R", culture),
            Mathf.Max(0.01f, riftTrail.VisualScaleFactor).ToString("R", culture),
            riftTrail.VisualForwardOffset.ToString("R", culture),
            Mathf.Max(0f, riftTrail.VisualAlphaFactor).ToString("R", culture),
            (riftTrail.VisualTint ?? "#ffffff").Replace("|", string.Empty),
            ribbons.Count.ToString(culture)
        };

        foreach (RiftTrailObserverSampleRibbonData ribbon in ribbons)
        {
            parts.Add(ribbon.Samples.Count.ToString(culture));
            foreach (RiftTrailObserverSample sample in ribbon.Samples)
            {
                parts.Add(sample.Base.x.ToString("R", culture));
                parts.Add(sample.Base.y.ToString("R", culture));
                parts.Add(sample.Base.z.ToString("R", culture));
                parts.Add(sample.Tip.x.ToString("R", culture));
                parts.Add(sample.Tip.y.ToString("R", culture));
                parts.Add(sample.Tip.z.ToString("R", culture));
                parts.Add(Mathf.Clamp01(sample.NormalizedAge).ToString("R", culture));
            }
        }

        return string.Join("|", parts);
    }

    private static bool TryParseObserverSampleVisualPayload(string payload, out RiftTrailObserverSampleVisualData data)
    {
        data = default;
        string[] parts = payload.Split('|');
        if (parts.Length < 7 ||
            !string.Equals(parts[0], ObserverSamplePayloadType, System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        CultureInfo culture = CultureInfo.InvariantCulture;
        if (!float.TryParse(parts[1], NumberStyles.Float, culture, out float duration) ||
            !float.TryParse(parts[2], NumberStyles.Float, culture, out float visualScaleFactor) ||
            !float.TryParse(parts[3], NumberStyles.Float, culture, out float visualForwardOffset) ||
            !float.TryParse(parts[4], NumberStyles.Float, culture, out float visualAlphaFactor) ||
            !int.TryParse(parts[6], NumberStyles.Integer, culture, out int ribbonCount) ||
            ribbonCount <= 0 ||
            ribbonCount > 8)
        {
            return false;
        }

        List<RiftTrailObserverSampleRibbonData> ribbons = new(ribbonCount);
        int index = 7;
        for (int ribbonIndex = 0; ribbonIndex < ribbonCount; ribbonIndex++)
        {
            if (index >= parts.Length ||
                !int.TryParse(parts[index++], NumberStyles.Integer, culture, out int sampleCount) ||
                sampleCount < 2 ||
                sampleCount > MaxObserverTrailSamples)
            {
                return false;
            }

            List<RiftTrailObserverSample> samples = new(sampleCount);
            for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                if (index + 6 >= parts.Length ||
                    !float.TryParse(parts[index++], NumberStyles.Float, culture, out float baseX) ||
                    !float.TryParse(parts[index++], NumberStyles.Float, culture, out float baseY) ||
                    !float.TryParse(parts[index++], NumberStyles.Float, culture, out float baseZ) ||
                    !float.TryParse(parts[index++], NumberStyles.Float, culture, out float tipX) ||
                    !float.TryParse(parts[index++], NumberStyles.Float, culture, out float tipY) ||
                    !float.TryParse(parts[index++], NumberStyles.Float, culture, out float tipZ) ||
                    !float.TryParse(parts[index++], NumberStyles.Float, culture, out float normalizedAge))
                {
                    return false;
                }

                samples.Add(new RiftTrailObserverSample(
                    new Vector3(baseX, baseY, baseZ),
                    new Vector3(tipX, tipY, tipZ),
                    Mathf.Clamp01(normalizedAge)));
            }

            ribbons.Add(new RiftTrailObserverSampleRibbonData(samples));
        }

        data = new RiftTrailObserverSampleVisualData(
            Mathf.Max(0.05f, duration),
            Mathf.Max(0.01f, visualScaleFactor),
            visualForwardOffset,
            Mathf.Max(0f, visualAlphaFactor),
            parts[5],
            ribbons);
        return true;
    }

    private static Color ResolveVisualTint(RiftTrailDefinition riftTrail)
    {
        return ResolveVisualTint(riftTrail.VisualTint);
    }

    private static Color ResolveVisualTint(string visualTint)
    {
        if (!string.IsNullOrWhiteSpace(visualTint) &&
            ColorUtility.TryParseHtmlString(visualTint.Trim(), out Color tint))
        {
            return tint;
        }

        return Color.white;
    }

    private static Color ApplyVisualTint(Color color, Color tint, float alphaFactor)
    {
        color.r *= tint.r;
        color.g *= tint.g;
        color.b *= tint.b;
        color.a *= tint.a * alphaFactor;
        return color;
    }

    private static Color EvaluateTrailColor(Color[]? colors, float normalizedAge)
    {
        if (colors == null || colors.Length == 0)
        {
            return Color.Lerp(Color.white, Color.clear, normalizedAge);
        }

        if (colors.Length == 1)
        {
            return colors[0];
        }

        float scaledIndex = Mathf.Clamp01(normalizedAge) * (colors.Length - 1);
        int left = Mathf.Clamp(Mathf.FloorToInt(scaledIndex), 0, colors.Length - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(scaledIndex), 0, colors.Length - 1);
        float t = Mathf.InverseLerp(left, right, scaledIndex);
        return Color.Lerp(colors[left], colors[right], t);
    }

    private static float EvaluateTrailSize(float[]? sizes, float normalizedAge)
    {
        if (sizes == null || sizes.Length == 0)
        {
            return 1f;
        }

        if (sizes.Length == 1)
        {
            return sizes[0];
        }

        float scaledIndex = Mathf.Clamp01(normalizedAge) * (sizes.Length - 1);
        int left = Mathf.Clamp(Mathf.FloorToInt(scaledIndex), 0, sizes.Length - 1);
        int right = Mathf.Clamp(Mathf.CeilToInt(scaledIndex), 0, sizes.Length - 1);
        float t = Mathf.InverseLerp(left, right, scaledIndex);
        return Mathf.Lerp(sizes[left], sizes[right], t);
    }

    private readonly struct RiftTrailObserverVisualData
    {
        public RiftTrailObserverVisualData(
            float range,
            float angle,
            float width,
            float duration,
            float visualScaleFactor,
            float visualForwardOffset,
            float visualAlphaFactor,
            string visualTint)
        {
            Range = range;
            Angle = angle;
            Width = width;
            Duration = duration;
            VisualScaleFactor = visualScaleFactor;
            VisualForwardOffset = visualForwardOffset;
            VisualAlphaFactor = visualAlphaFactor;
            VisualTint = visualTint;
        }

        public float Range { get; }

        public float Angle { get; }

        public float Width { get; }

        public float Duration { get; }

        public float VisualScaleFactor { get; }

        public float VisualForwardOffset { get; }

        public float VisualAlphaFactor { get; }

        public string VisualTint { get; }
    }

    private readonly struct RiftTrailObserverSampleVisualData
    {
        public RiftTrailObserverSampleVisualData(
            float duration,
            float visualScaleFactor,
            float visualForwardOffset,
            float visualAlphaFactor,
            string visualTint,
            IReadOnlyList<RiftTrailObserverSampleRibbonData> ribbons)
        {
            Duration = duration;
            VisualScaleFactor = visualScaleFactor;
            VisualForwardOffset = visualForwardOffset;
            VisualAlphaFactor = visualAlphaFactor;
            VisualTint = visualTint;
            Ribbons = ribbons;
        }

        public float Duration { get; }

        public float VisualScaleFactor { get; }

        public float VisualForwardOffset { get; }

        public float VisualAlphaFactor { get; }

        public string VisualTint { get; }

        public IReadOnlyList<RiftTrailObserverSampleRibbonData> Ribbons { get; }
    }

    private readonly struct RiftTrailObserverSampleRibbonData
    {
        public RiftTrailObserverSampleRibbonData(IReadOnlyList<RiftTrailObserverSample> samples)
        {
            Samples = samples;
        }

        public IReadOnlyList<RiftTrailObserverSample> Samples { get; }
    }

    private readonly struct RiftTrailObserverSample
    {
        public RiftTrailObserverSample(Vector3 basePosition, Vector3 tipPosition, float normalizedAge)
        {
            Base = basePosition;
            Tip = tipPosition;
            NormalizedAge = normalizedAge;
        }

        public Vector3 Base { get; }

        public Vector3 Tip { get; }

        public float NormalizedAge { get; }
    }

    private readonly struct ObserverTrailMetadata
    {
        public ObserverTrailMetadata(Material? material, Color[]? colors, float[]? sizes)
        {
            Material = material;
            Colors = colors;
            Sizes = sizes;
        }

        public Material? Material { get; }

        public Color[]? Colors { get; }

        public float[]? Sizes { get; }
    }

    private sealed class RiftTrailObserverFallbackController : MonoBehaviour
    {
        private readonly List<ObserverRibbonVisual> _visualRibbons = new();
        private GameObject? _visualObject;
        private float _endTime;
        private float _visualEndTime;

        internal void InitializeSampled(Character character, RiftTrailObserverSampleVisualData data)
        {
            DestroyVisuals();
            if (data.VisualScaleFactor <= 0f || data.Ribbons.Count == 0)
            {
                Destroy(this);
                return;
            }

            _visualObject = new GameObject("SecondaryAttacks_RiftTrailObserverSampled");
            List<ObserverTrailMetadata> metadata = ResolveObserverTrailMetadata(character);
            for (int i = 0; i < data.Ribbons.Count; i++)
            {
                ObserverTrailMetadata trailMetadata = i < metadata.Count
                    ? metadata[i]
                    : default;
                CreateSampledRibbonVisual(data, data.Ribbons[i], trailMetadata);
            }

            if (_visualRibbons.Count == 0)
            {
                DestroyVisuals();
                Destroy(this);
                return;
            }

            _endTime = Time.time + data.Duration;
            _visualEndTime = _endTime + VisualFadeSeconds;
            UpdateVisualFade();
            enabled = true;
        }

        internal void Initialize(Vector3 origin, Vector3 forward, RiftTrailObserverVisualData data)
        {
            DestroyVisuals();
            if (forward.sqrMagnitude <= 0.001f || data.VisualScaleFactor <= 0f)
            {
                Destroy(this);
                return;
            }

            Material? baseMaterial = GetRiftMaterial();
            if (baseMaterial == null)
            {
                Destroy(this);
                return;
            }

            forward = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (forward.sqrMagnitude <= 0.001f)
            {
                forward = transform.forward;
            }

            forward.Normalize();
            _visualObject = new GameObject("SecondaryAttacks_RiftTrailObserverFallback");
            CreateFallbackRibbonVisual(baseMaterial, origin, forward, data);
            if (_visualRibbons.Count == 0)
            {
                DestroyVisuals();
                Destroy(this);
                return;
            }

            _endTime = Time.time + data.Duration;
            _visualEndTime = _endTime + VisualFadeSeconds;
            UpdateVisualFade();
            enabled = true;
        }

        private void CreateSampledRibbonVisual(
            RiftTrailObserverSampleVisualData data,
            RiftTrailObserverSampleRibbonData ribbon,
            ObserverTrailMetadata trailMetadata)
        {
            if (_visualObject == null || ribbon.Samples.Count < 2)
            {
                return;
            }

            Material? baseMaterial = trailMetadata.Material != null ? trailMetadata.Material : GetRiftMaterial();
            if (baseMaterial == null)
            {
                return;
            }

            GameObject ribbonObject = new("RiftTrailObserverSampledRibbon");
            ribbonObject.transform.SetParent(_visualObject.transform, worldPositionStays: false);
            MeshFilter meshFilter = ribbonObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = ribbonObject.AddComponent<MeshRenderer>();
            Material material = new(baseMaterial);
            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }

            meshRenderer.material = material;

            int sampleCount = ribbon.Samples.Count;
            Vector3[] vertices = new Vector3[sampleCount * 2];
            Vector2[] uvs = new Vector2[sampleCount * 2];
            Color[] colors = new Color[sampleCount * 2];
            int[] triangles = new int[(sampleCount - 1) * 6];
            float visualScale = Mathf.Max(0.01f, data.VisualScaleFactor);
            Color visualTint = ResolveVisualTint(data.VisualTint);
            float visualAlphaFactor = Mathf.Max(0f, data.VisualAlphaFactor);
            for (int i = 0; i < sampleCount; i++)
            {
                RiftTrailObserverSample sample = ribbon.Samples[i];
                Color color = ApplyVisualTint(EvaluateTrailColor(trailMetadata.Colors, sample.NormalizedAge), visualTint, visualAlphaFactor);
                float size = Mathf.Max(0.01f, EvaluateTrailSize(trailMetadata.Sizes, sample.NormalizedAge)) * visualScale;
                Vector3 width = sample.Tip - sample.Base;
                vertices[i * 2] = sample.Base - width * (size * 0.5f);
                vertices[i * 2 + 1] = sample.Tip + width * (size * 0.5f);
                colors[i * 2] = color;
                colors[i * 2 + 1] = color;
                float u = i / (float)sampleCount;
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }

            int triangleIndex = 0;
            for (int i = 1; i < sampleCount; i++)
            {
                triangles[triangleIndex++] = i * 2 - 2;
                triangles[triangleIndex++] = i * 2 - 1;
                triangles[triangleIndex++] = i * 2;
                triangles[triangleIndex++] = i * 2 + 1;
                triangles[triangleIndex++] = i * 2;
                triangles[triangleIndex++] = i * 2 - 1;
            }

            Mesh mesh = new()
            {
                vertices = vertices,
                uv = uvs,
                colors = colors,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;
            Color materialColor = material.HasProperty("_Color") ? material.color : Color.white;
            materialColor = ApplyVisualTint(materialColor, visualTint, visualAlphaFactor);
            if (material.HasProperty("_Color"))
            {
                material.color = materialColor;
            }

            _visualRibbons.Add(new ObserverRibbonVisual(mesh, material, colors, materialColor));
        }

        private void CreateFallbackRibbonVisual(Material baseMaterial, Vector3 origin, Vector3 forward, RiftTrailObserverVisualData data)
        {
            if (_visualObject == null)
            {
                return;
            }

            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(data.Angle / 8f) + 2, 4, 24);
            float halfAngle = data.Angle * 0.5f;
            Vector3 visualOffset = forward * data.VisualForwardOffset;
            float outerRadius = Mathf.Max(0.4f, data.Range);
            float bandWidth = Mathf.Clamp(Mathf.Max(0.25f, data.Width * 1.25f) * data.VisualScaleFactor, 0.2f, outerRadius * 0.75f);
            float innerRadius = Mathf.Max(0.1f, outerRadius - bandWidth);
            Color visualTint = ResolveVisualTint(data.VisualTint);
            Color baseColor = ApplyVisualTint(new Color(0.9f, 0.95f, 1f, 0.65f), visualTint, data.VisualAlphaFactor);

            GameObject ribbonObject = new("RiftTrailObserverFallbackRibbon");
            ribbonObject.transform.SetParent(_visualObject.transform, worldPositionStays: false);
            MeshFilter meshFilter = ribbonObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = ribbonObject.AddComponent<MeshRenderer>();
            Material material = new(baseMaterial);
            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }

            if (material.HasProperty("_Color"))
            {
                material.color = baseColor;
            }

            meshRenderer.material = material;

            Vector3[] vertices = new Vector3[sampleCount * 2];
            Vector2[] uvs = new Vector2[sampleCount * 2];
            Color[] colors = new Color[sampleCount * 2];
            int[] triangles = new int[(sampleCount - 1) * 6];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector3 direction = (Quaternion.AngleAxis(angle, Vector3.up) * forward).normalized;
                vertices[i * 2] = origin + visualOffset + direction * innerRadius;
                vertices[i * 2 + 1] = origin + visualOffset + direction * outerRadius;
                Color color = baseColor;
                color.a *= Mathf.Lerp(0.35f, 1f, Mathf.Sin(t * Mathf.PI));
                colors[i * 2] = color;
                colors[i * 2 + 1] = color;
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }

            int triangleIndex = 0;
            for (int i = 1; i < sampleCount; i++)
            {
                triangles[triangleIndex++] = i * 2 - 2;
                triangles[triangleIndex++] = i * 2 - 1;
                triangles[triangleIndex++] = i * 2;
                triangles[triangleIndex++] = i * 2 + 1;
                triangles[triangleIndex++] = i * 2;
                triangles[triangleIndex++] = i * 2 - 1;
            }

            Mesh mesh = new()
            {
                vertices = vertices,
                uv = uvs,
                colors = colors,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;
            _visualRibbons.Add(new ObserverRibbonVisual(mesh, material, colors, baseColor));
        }

        private void Update()
        {
            UpdateVisualFade();
            if (Time.time >= _visualEndTime)
            {
                Destroy(this);
            }
        }

        private void UpdateVisualFade()
        {
            if (_visualRibbons.Count == 0)
            {
                return;
            }

            float alpha = Time.time <= _endTime
                ? 1f
                : Mathf.Clamp01((_visualEndTime - Time.time) / VisualFadeSeconds);
            foreach (ObserverRibbonVisual ribbon in _visualRibbons)
            {
                if (ribbon.Mesh == null || ribbon.Material == null)
                {
                    continue;
                }

                for (int i = 0; i < ribbon.WorkingColors.Length; i++)
                {
                    Color color = ribbon.BaseColors[i];
                    color.a *= alpha;
                    ribbon.WorkingColors[i] = color;
                }

                ribbon.Mesh.colors = ribbon.WorkingColors;
                if (ribbon.Material.HasProperty("_Color"))
                {
                    Color materialColor = ribbon.BaseMaterialColor;
                    materialColor.a *= alpha;
                    ribbon.Material.color = materialColor;
                }
            }
        }

        private void OnDestroy()
        {
            DestroyVisuals();
        }

        private void DestroyVisuals()
        {
            if (_visualObject != null)
            {
                Destroy(_visualObject);
                _visualObject = null;
            }

            foreach (ObserverRibbonVisual ribbon in _visualRibbons)
            {
                if (ribbon.Material != null)
                {
                    Destroy(ribbon.Material);
                }

                if (ribbon.Mesh != null)
                {
                    Destroy(ribbon.Mesh);
                }
            }

            _visualRibbons.Clear();
        }

        private sealed class ObserverRibbonVisual
        {
            public ObserverRibbonVisual(Mesh mesh, Material material, Color[] baseColors, Color baseMaterialColor)
            {
                Mesh = mesh;
                Material = material;
                BaseColors = baseColors;
                WorkingColors = new Color[baseColors.Length];
                BaseMaterialColor = baseMaterialColor;
            }

            public Mesh Mesh { get; }

            public Material Material { get; }

            public Color[] BaseColors { get; }

            public Color[] WorkingColors { get; }

            public Color BaseMaterialColor { get; }
        }
    }

    private readonly struct RiftTrailHitTarget
    {
        public RiftTrailHitTarget(
            IDestructible destructible,
            Character? character,
            Collider collider,
            Vector3 point,
            Vector3 hitDirection,
            bool isEnemy)
        {
            Destructible = destructible;
            Character = character;
            Collider = collider;
            Point = point;
            HitDirection = hitDirection;
            IsEnemy = isEnemy;
        }

        public IDestructible Destructible { get; }

        public Character? Character { get; }

        public Collider Collider { get; }

        public Vector3 Point { get; }

        public Vector3 HitDirection { get; }

        public bool IsEnemy { get; }
    }

    internal sealed class RiftTrailController : MonoBehaviour
    {
        private float _endTime;
        private float _visualEndTime;
        private float _nextTickTime;
        private bool _registered;
        private bool _activated;
        private bool _durabilityDrained;
        private bool _finished;
        private bool _visualCreated;
        private bool _visualCreationFinalized;
        private bool _observerVisualSent;
        private float _visualCaptureUntil;
        private GameObject? _visualObject;
        private readonly List<TrailSampler> _trailSamplers = new();
        private readonly List<RibbonVisual> _visualRibbons = new();

        internal Attack Attack { get; private set; } = null!;

        internal RiftTrailDefinition RiftTrail { get; private set; } = null!;

        internal Vector3 Origin { get; private set; }

        internal Vector3 Forward { get; private set; }

        internal float Range { get; private set; }

        internal float Angle { get; private set; }

        internal float Width { get; private set; }

        private Vector3 VisualOffset => Forward * RiftTrail.VisualForwardOffset;

        internal void Initialize(Attack attack, RiftTrailDefinition riftTrail)
        {
            Attack = attack;
            RiftTrail = riftTrail;
            Transform attackerTransform = attack.m_character.transform;
            Vector3 forward = Vector3.ProjectOnPlane(attackerTransform.forward, Vector3.up);
            Forward = forward.sqrMagnitude > 0.001f ? forward.normalized : attackerTransform.forward;
            Origin = attackerTransform.position +
                     Vector3.up * Mathf.Max(0f, attack.m_attackHeight) +
                     attackerTransform.right * attack.m_attackOffset;
            Range = Mathf.Max(0.1f, attack.m_attackRange);
            Angle = Mathf.Clamp(attack.m_attackAngle, 1f, 360f);
            Width = Mathf.Max(0.01f, attack.m_attackRayWidth + Mathf.Max(0f, attack.m_attackRayWidthCharExtra));
            _registered = true;
            TryResolveTrailSamplers();
            SampleTrails();
            SecondaryAttackManager.RegisterAsyncSecondaryWork(attack.m_character);
            enabled = true;
        }

        internal void Activate()
        {
            if (_activated || _finished)
            {
                return;
            }

            RefreshAttackShape();
            SampleTrails();
            _activated = true;
            _endTime = Time.time + RiftTrail.Duration;
            _visualEndTime = _endTime + VisualFadeSeconds;
            _nextTickTime = Time.time + RiftTrail.TickInterval;
            _visualCaptureUntil = Time.time + PostTriggerVisualCaptureSeconds;
            TryCreateVisual();
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

            if (!_activated)
            {
                if (!IsCurrentAttack())
                {
                    Finish();
                    return;
                }

                SampleTrails();
                return;
            }

            TryCreateDelayedVisual();
            UpdateVisualFade();

            while (_nextTickTime <= _endTime && Time.time >= _nextTickTime)
            {
                ApplyTick(this);
                _nextTickTime += RiftTrail.TickInterval;
                break;
            }

            float finishTime = _visualRibbons.Count > 0 ? _visualEndTime : _endTime;
            if (Time.time >= finishTime && _nextTickTime > _endTime)
            {
                Finish();
            }
        }

        private bool IsCurrentAttack()
        {
            return Attack.m_character is not Humanoid humanoid || humanoid.m_currentAttack == Attack;
        }

        private void RefreshAttackShape()
        {
            Transform attackerTransform = Attack.m_character.transform;
            Vector3 forward = Vector3.ProjectOnPlane(attackerTransform.forward, Vector3.up);
            Forward = forward.sqrMagnitude > 0.001f ? forward.normalized : attackerTransform.forward;
            Origin = attackerTransform.position +
                     Vector3.up * Mathf.Max(0f, Attack.m_attackHeight) +
                     attackerTransform.right * Attack.m_attackOffset;
            Range = Mathf.Max(0.1f, Attack.m_attackRange);
            Angle = Mathf.Clamp(Attack.m_attackAngle, 1f, 360f);
            Width = Mathf.Max(0.01f, Attack.m_attackRayWidth + Mathf.Max(0f, Attack.m_attackRayWidthCharExtra));
        }

        internal void DrainDurabilityOnce()
        {
            if (_durabilityDrained)
            {
                return;
            }

            SecondaryAttackManager.DrainAttackDurability(Attack, RiftTrail.DurabilityFactor);
            _durabilityDrained = true;
        }

        private void TryCreateDelayedVisual()
        {
            if (_visualCreated || _visualCreationFinalized)
            {
                return;
            }

            bool canStillCapture = Time.time <= _visualCaptureUntil && IsCurrentAttack();
            if (canStillCapture)
            {
                SampleTrails();
            }

            if (HasEnoughTrailSamples() || !canStillCapture)
            {
                bool created = TryCreateVisual();
                if (!created && !canStillCapture)
                {
                    TryCreateFallbackVisual();
                    _visualCreationFinalized = true;
                }
            }
        }

        private bool TryCreateVisual()
        {
            if (RiftTrail.VisualScaleFactor <= 0f)
            {
                _visualCreationFinalized = true;
                return false;
            }

            if (_visualCreated)
            {
                return true;
            }

            if (!HasEnoughTrailSamples())
            {
                return false;
            }

            _visualObject = new GameObject("SecondaryAttacks_RiftTrail");
            for (int i = 0; i < _trailSamplers.Count; i++)
            {
                CreateRibbonVisual(_trailSamplers[i]);
            }

            if (_visualRibbons.Count == 0)
            {
                if (_visualObject != null)
                {
                    Destroy(_visualObject);
                    _visualObject = null;
                }

                return false;
            }

            _visualCreated = true;
            UpdateVisualFade();
            SendObserverSampleVisual();
            return true;
        }

        private bool TryCreateFallbackVisual()
        {
            if (RiftTrail.VisualScaleFactor <= 0f || _visualCreated)
            {
                return false;
            }

            Material? baseMaterial = GetRiftMaterial();
            if (baseMaterial == null)
            {
                return false;
            }

            _visualObject = new GameObject("SecondaryAttacks_RiftTrailFallback");
            CreateFallbackRibbonVisual(baseMaterial);
            if (_visualRibbons.Count == 0)
            {
                if (_visualObject != null)
                {
                    Destroy(_visualObject);
                    _visualObject = null;
                }

                return false;
            }

            _visualCreated = true;
            UpdateVisualFade();
            SendObserverFallbackVisualOnce();
            return true;
        }

        private void SendObserverSampleVisual()
        {
            if (_observerVisualSent)
            {
                return;
            }

            List<RiftTrailObserverSampleRibbonData> ribbons = BuildObserverSampleRibbons();
            if (ribbons.Count == 0)
            {
                return;
            }

            string payload = CreateObserverSampleVisualPayload(RiftTrail, ribbons);
            if (!SendObserverVisualPayload(Attack, Origin, Forward, payload))
            {
                return;
            }

            _observerVisualSent = true;
        }

        private void SendObserverFallbackVisualOnce()
        {
            if (_observerVisualSent)
            {
                return;
            }

            SendObserverFallbackVisual(Attack, RiftTrail);
            _observerVisualSent = true;
        }

        private List<RiftTrailObserverSampleRibbonData> BuildObserverSampleRibbons()
        {
            List<RiftTrailObserverSampleRibbonData> ribbons = new();
            Vector3 visualOffset = VisualOffset;
            foreach (TrailSampler trailSampler in _trailSamplers)
            {
                if (trailSampler.Samples.Count < 2)
                {
                    continue;
                }

                List<TrailSample> samples = BuildSmoothSamples(trailSampler.Samples, trailSampler.Subdivisions);
                if (samples.Count < 2)
                {
                    continue;
                }

                List<RiftTrailObserverSample> observerSamples = new(Mathf.Min(samples.Count, MaxObserverTrailSamples));
                int outputCount = Mathf.Min(samples.Count, MaxObserverTrailSamples);
                for (int i = 0; i < outputCount; i++)
                {
                    int sampleIndex = outputCount == samples.Count
                        ? i
                        : Mathf.Clamp(Mathf.RoundToInt(i * (samples.Count - 1) / (float)(outputCount - 1)), 0, samples.Count - 1);
                    TrailSample sample = samples[sampleIndex];
                    float age = Mathf.Max(0f, Time.time - sample.Time);
                    float normalizedAge = trailSampler.LifeTime > 0.001f
                        ? Mathf.Clamp01(age / trailSampler.LifeTime)
                        : outputCount <= 1 ? 1f : 1f - i / (float)(outputCount - 1);
                    observerSamples.Add(new RiftTrailObserverSample(
                        sample.Base + visualOffset,
                        sample.Tip + visualOffset,
                        normalizedAge));
                }

                if (observerSamples.Count >= 2)
                {
                    ribbons.Add(new RiftTrailObserverSampleRibbonData(observerSamples));
                }
            }

            return ribbons;
        }

        private void CreateFallbackRibbonVisual(Material baseMaterial)
        {
            if (_visualObject == null)
            {
                return;
            }

            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(Angle / 8f) + 2, 4, 24);
            float halfAngle = Angle * 0.5f;
            float visualScale = Mathf.Max(0.01f, RiftTrail.VisualScaleFactor);
            Vector3 visualOffset = VisualOffset;
            float outerRadius = Mathf.Max(0.4f, Range);
            float bandWidth = Mathf.Clamp(Mathf.Max(0.25f, Width * 1.25f) * visualScale, 0.2f, outerRadius * 0.75f);
            float innerRadius = Mathf.Max(0.1f, outerRadius - bandWidth);
            Color visualTint = ResolveVisualTint(RiftTrail);
            Color baseColor = ApplyVisualTint(new Color(0.9f, 0.95f, 1f, 0.65f), visualTint, Mathf.Max(0f, RiftTrail.VisualAlphaFactor));

            GameObject ribbonObject = new("RiftTrailFallbackRibbon");
            ribbonObject.transform.SetParent(_visualObject.transform, worldPositionStays: false);
            MeshFilter meshFilter = ribbonObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = ribbonObject.AddComponent<MeshRenderer>();
            Material material = new(baseMaterial);
            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }

            if (material.HasProperty("_Color"))
            {
                material.color = baseColor;
            }

            meshRenderer.material = material;

            Vector3[] vertices = new Vector3[sampleCount * 2];
            Vector2[] uvs = new Vector2[sampleCount * 2];
            Color[] colors = new Color[sampleCount * 2];
            int[] triangles = new int[(sampleCount - 1) * 6];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount <= 1 ? 0f : i / (float)(sampleCount - 1);
                float angle = Mathf.Lerp(-halfAngle, halfAngle, t);
                Vector3 direction = (Quaternion.AngleAxis(angle, Vector3.up) * Forward).normalized;
                vertices[i * 2] = Origin + visualOffset + direction * innerRadius;
                vertices[i * 2 + 1] = Origin + visualOffset + direction * outerRadius;
                Color color = baseColor;
                color.a *= Mathf.Lerp(0.35f, 1f, Mathf.Sin(t * Mathf.PI));
                colors[i * 2] = color;
                colors[i * 2 + 1] = color;
                uvs[i * 2] = new Vector2(t, 0f);
                uvs[i * 2 + 1] = new Vector2(t, 1f);
            }

            int triangleIndex = 0;
            for (int i = 1; i < sampleCount; i++)
            {
                triangles[triangleIndex++] = i * 2 - 2;
                triangles[triangleIndex++] = i * 2 - 1;
                triangles[triangleIndex++] = i * 2;
                triangles[triangleIndex++] = i * 2 + 1;
                triangles[triangleIndex++] = i * 2;
                triangles[triangleIndex++] = i * 2 - 1;
            }

            Mesh mesh = new()
            {
                vertices = vertices,
                uv = uvs,
                colors = colors,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;
            _visualRibbons.Add(new RibbonVisual(mesh, material, colors, baseColor));
        }

        private bool HasEnoughTrailSamples()
        {
            TryResolveTrailSamplers();
            foreach (TrailSampler trailSampler in _trailSamplers)
            {
                if (trailSampler.Samples.Count >= 2)
                {
                    return true;
                }
            }

            return false;
        }

        private void TryResolveTrailSamplers()
        {
            if (_trailSamplers.Count > 0)
            {
                return;
            }

            GameObject? rightItemInstance = GetRightItemInstance(Attack);
            if (rightItemInstance == null)
            {
                return;
            }

            foreach (MeleeWeaponTrail trail in rightItemInstance.GetComponentsInChildren<MeleeWeaponTrail>(includeInactive: true))
            {
                Transform? baseTransform = TrailBaseField.GetValue(trail) as Transform;
                Transform? tipTransform = TrailTipField.GetValue(trail) as Transform;
                if (baseTransform == null || tipTransform == null)
                {
                    continue;
                }

                Material? material = TrailMaterialField.GetValue(trail) as Material;
                Color[]? colors = TrailColorsField.GetValue(trail) as Color[];
                float[]? sizes = TrailSizesField.GetValue(trail) as float[];
                float lifeTime = TrailLifeTimeField.GetValue(trail) is float trailLifeTime ? trailLifeTime : 0.2f;
                int subdivisions = TrailSubdivisionsField.GetValue(trail) is int trailSubdivisions ? trailSubdivisions : 0;
                _trailSamplers.Add(new TrailSampler(baseTransform, tipTransform, material, colors, sizes, lifeTime, subdivisions));
            }
        }

        private void SampleTrails()
        {
            TryResolveTrailSamplers();
            foreach (TrailSampler trailSampler in _trailSamplers)
            {
                trailSampler.Sample();
            }
        }

        private void CreateRibbonVisual(TrailSampler trailSampler)
        {
            if (trailSampler.Samples.Count < 2 || _visualObject == null)
            {
                return;
            }

            Material? baseMaterial = trailSampler.Material != null ? trailSampler.Material : GetRiftMaterial();
            if (baseMaterial == null)
            {
                return;
            }

            List<TrailSample> samples = BuildSmoothSamples(trailSampler.Samples, trailSampler.Subdivisions);
            int sampleCount = samples.Count;
            if (sampleCount < 2)
            {
                return;
            }

            GameObject ribbonObject = new("RiftTrailRibbon");
            ribbonObject.transform.SetParent(_visualObject.transform, worldPositionStays: false);
            MeshFilter meshFilter = ribbonObject.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = ribbonObject.AddComponent<MeshRenderer>();
            Material material = new(baseMaterial);
            if (material.HasProperty("_Cull"))
            {
                material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            }

            meshRenderer.material = material;

            Vector3[] vertices = new Vector3[sampleCount * 2];
            Vector2[] uvs = new Vector2[sampleCount * 2];
            Color[] colors = new Color[sampleCount * 2];
            int[] triangles = new int[(sampleCount - 1) * 6];
            float visualScale = Mathf.Max(0.01f, RiftTrail.VisualScaleFactor);
            Vector3 visualOffset = VisualOffset;
            Color visualTint = ResolveVisualTint(RiftTrail);
            float visualAlphaFactor = Mathf.Max(0f, RiftTrail.VisualAlphaFactor);
            float now = Time.time;
            for (int i = 0; i < sampleCount; i++)
            {
                TrailSample sample = samples[i];
                float age = Mathf.Max(0f, now - sample.Time);
                float normalizedAge = trailSampler.LifeTime > 0.001f
                    ? Mathf.Clamp01(age / trailSampler.LifeTime)
                    : 1f - i / (float)(sampleCount - 1);
                Color color = ApplyVisualTint(EvaluateColor(trailSampler.Colors, normalizedAge), visualTint, visualAlphaFactor);
                float size = Mathf.Max(0.01f, EvaluateSize(trailSampler.Sizes, normalizedAge)) * visualScale;
                Vector3 width = sample.Tip - sample.Base;
                vertices[i * 2] = sample.Base + visualOffset - width * (size * 0.5f);
                vertices[i * 2 + 1] = sample.Tip + visualOffset + width * (size * 0.5f);
                colors[i * 2] = color;
                colors[i * 2 + 1] = color;
                float u = i / (float)sampleCount;
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }

            int triangleIndex = 0;
            for (int i = 1; i < sampleCount; i++)
            {
                triangles[triangleIndex++] = i * 2 - 2;
                triangles[triangleIndex++] = i * 2 - 1;
                triangles[triangleIndex++] = i * 2;
                triangles[triangleIndex++] = i * 2 + 1;
                triangles[triangleIndex++] = i * 2;
                triangles[triangleIndex++] = i * 2 - 1;
            }

            Mesh mesh = new()
            {
                vertices = vertices,
                uv = uvs,
                colors = colors,
                triangles = triangles
            };
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;
            Color materialColor = material.HasProperty("_Color") ? material.color : Color.white;
            materialColor = ApplyVisualTint(materialColor, visualTint, visualAlphaFactor);
            if (material.HasProperty("_Color"))
            {
                material.color = materialColor;
            }

            _visualRibbons.Add(new RibbonVisual(mesh, material, colors, materialColor));
        }

        private static List<TrailSample> BuildSmoothSamples(IReadOnlyList<TrailSample> samples, int subdivisions)
        {
            int sampleCount = samples.Count;
            int steps = Mathf.Clamp(subdivisions, 0, MaxTrailSubdivisions) + 1;
            if (sampleCount < 4 || steps <= 1)
            {
                return new List<TrailSample>(samples);
            }

            List<TrailSample> smoothed = new(sampleCount * steps);
            for (int i = 0; i < sampleCount - 1; i++)
            {
                TrailSample p0 = samples[Mathf.Max(0, i - 1)];
                TrailSample p1 = samples[i];
                TrailSample p2 = samples[i + 1];
                TrailSample p3 = samples[Mathf.Min(sampleCount - 1, i + 2)];
                for (int step = 0; step < steps; step++)
                {
                    float t = step / (float)steps;
                    smoothed.Add(new TrailSample(
                        CatmullRom(p0.Base, p1.Base, p2.Base, p3.Base, t),
                        CatmullRom(p0.Tip, p1.Tip, p2.Tip, p3.Tip, t),
                        Mathf.Lerp(p1.Time, p2.Time, t)));
                }
            }

            smoothed.Add(samples[sampleCount - 1]);
            return smoothed;
        }

        private static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1) +
                           (-p0 + p2) * t +
                           (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                           (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static Color EvaluateColor(Color[]? colors, float normalizedAge)
        {
            if (colors == null || colors.Length == 0)
            {
                return Color.Lerp(Color.white, Color.clear, normalizedAge);
            }

            if (colors.Length == 1)
            {
                return colors[0];
            }

            float scaledIndex = normalizedAge * (colors.Length - 1);
            int left = Mathf.Clamp(Mathf.FloorToInt(scaledIndex), 0, colors.Length - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt(scaledIndex), 0, colors.Length - 1);
            float t = Mathf.InverseLerp(left, right, scaledIndex);
            return Color.Lerp(colors[left], colors[right], t);
        }

        private static float EvaluateSize(float[]? sizes, float normalizedAge)
        {
            if (sizes == null || sizes.Length == 0)
            {
                return 1f;
            }

            if (sizes.Length == 1)
            {
                return sizes[0];
            }

            float scaledIndex = normalizedAge * (sizes.Length - 1);
            int left = Mathf.Clamp(Mathf.FloorToInt(scaledIndex), 0, sizes.Length - 1);
            int right = Mathf.Clamp(Mathf.CeilToInt(scaledIndex), 0, sizes.Length - 1);
            float t = Mathf.InverseLerp(left, right, scaledIndex);
            return Mathf.Lerp(sizes[left], sizes[right], t);
        }

        private void UpdateVisualFade()
        {
            if (_visualRibbons.Count == 0)
            {
                return;
            }

            float alpha = Time.time <= _endTime
                ? 1f
                : Mathf.Clamp01((_visualEndTime - Time.time) / VisualFadeSeconds);
            foreach (RibbonVisual ribbon in _visualRibbons)
            {
                if (ribbon.Mesh == null || ribbon.Material == null)
                {
                    continue;
                }

                for (int i = 0; i < ribbon.WorkingColors.Length; i++)
                {
                    Color color = ribbon.BaseColors[i];
                    color.a *= alpha;
                    ribbon.WorkingColors[i] = color;
                }

                ribbon.Mesh.colors = ribbon.WorkingColors;
                if (ribbon.Material.HasProperty("_Color"))
                {
                    Color materialColor = ribbon.BaseMaterialColor;
                    materialColor.a *= alpha;
                    ribbon.Material.color = materialColor;
                }
            }
        }

        private void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            if (_visualObject != null)
            {
                Destroy(_visualObject);
                _visualObject = null;
            }
            DestroyVisualRibbons();

            Destroy(this);
        }

        private void OnDestroy()
        {
            if (_visualObject != null)
            {
                Destroy(_visualObject);
                _visualObject = null;
            }
            DestroyVisualRibbons();
            UnregisterController(Attack, this);

            if (_registered)
            {
                SecondaryAttackManager.UnregisterAsyncSecondaryWork(Attack?.m_character);
                _registered = false;
            }
        }

        private void DestroyVisualRibbons()
        {
            foreach (RibbonVisual ribbon in _visualRibbons)
            {
                if (ribbon.Material != null)
                {
                    Destroy(ribbon.Material);
                }

                if (ribbon.Mesh != null)
                {
                    Destroy(ribbon.Mesh);
                }
            }

            _visualRibbons.Clear();
        }

        private sealed class TrailSampler
        {
            private readonly Transform _baseTransform;
            private readonly Transform _tipTransform;
            private Vector3 _lastBase;
            private Vector3 _lastTip;
            private bool _hasLastSample;

            public TrailSampler(
                Transform baseTransform,
                Transform tipTransform,
                Material? material,
                Color[]? colors,
                float[]? sizes,
                float lifeTime,
                int subdivisions)
            {
                _baseTransform = baseTransform;
                _tipTransform = tipTransform;
                Material = material;
                Colors = colors;
                Sizes = sizes;
                LifeTime = Mathf.Max(0.01f, lifeTime);
                Subdivisions = Mathf.Max(0, subdivisions);
            }

            public List<TrailSample> Samples { get; } = new();

            public Material? Material { get; }

            public Color[]? Colors { get; }

            public float[]? Sizes { get; }

            public float LifeTime { get; }

            public int Subdivisions { get; }

            public void Sample()
            {
                if (_baseTransform == null || _tipTransform == null || Samples.Count >= MaxTrailSamples)
                {
                    return;
                }

                Vector3 basePosition = _baseTransform.position;
                Vector3 tipPosition = _tipTransform.position;
                if ((tipPosition - basePosition).sqrMagnitude < 0.0001f)
                {
                    return;
                }

                if (_hasLastSample &&
                    (basePosition - _lastBase).sqrMagnitude < MinTrailSampleDistance * MinTrailSampleDistance &&
                    (tipPosition - _lastTip).sqrMagnitude < MinTrailSampleDistance * MinTrailSampleDistance)
                {
                    return;
                }

                Samples.Add(new TrailSample(basePosition, tipPosition, Time.time));
                _lastBase = basePosition;
                _lastTip = tipPosition;
                _hasLastSample = true;
            }
        }

        private readonly struct TrailSample
        {
            public TrailSample(Vector3 basePosition, Vector3 tipPosition, float time)
            {
                Base = basePosition;
                Tip = tipPosition;
                Time = time;
            }

            public Vector3 Base { get; }

            public Vector3 Tip { get; }

            public float Time { get; }
        }

        private sealed class RibbonVisual
        {
            public RibbonVisual(Mesh mesh, Material material, Color[] baseColors, Color baseMaterialColor)
            {
                Mesh = mesh;
                Material = material;
                BaseColors = baseColors;
                WorkingColors = new Color[baseColors.Length];
                BaseMaterialColor = baseMaterialColor;
            }

            public Mesh Mesh { get; }

            public Material Material { get; }

            public Color[] BaseColors { get; }

            public Color[] WorkingColors { get; }

            public Color BaseMaterialColor { get; }
        }
    }
}
