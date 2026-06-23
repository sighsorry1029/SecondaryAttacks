using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SecondaryAttacks;

internal static class CopiedThrowProjectileVisualSystem
{
    private const string CopiedThrowProjectileMarkerKey = "SecondaryAttacks_CopiedThrowProjectile";
    private const string CopiedThrowProjectileSpinAxisKey = "SecondaryAttacks_CopiedThrowSpinAxis";
    private const string CopiedThrowProjectileRotationOffsetKey = "SecondaryAttacks_CopiedThrowRotationOffset";
    private const string CopiedThrowProjectileVisualRootName = "SecondaryAttacks_CopiedThrowVisualRoot";
    private const string CopiedThrowProjectileSpinRootName = "SecondaryAttacks_CopiedThrowSpinRoot";
    private const float SpinStateEpsilonSqr = 0.0001f;

    private static readonly List<Attack> ActiveCopiedThrowBursts = new();
    private static readonly List<Renderer> RendererBuffer = new();

    internal readonly struct BurstScope
    {
        public BurstScope(Attack attack)
        {
            Attack = attack;
        }

        public Attack? Attack { get; }

        public bool Active => Attack != null;
    }

    internal readonly struct SpawnedProjectileVisualContext
    {
        public SpawnedProjectileVisualContext(
            ItemDrop.ItemData weapon,
            string visualPrefabName,
            GameObject? attachPrefab,
            EffectList.EffectData[]? hitEffectPrefabs,
            ThrowProjectileVisualSpin.AxisMode spinAxisMode,
            Vector3 visualRotationOffset,
            bool skipVisualSwap)
        {
            Weapon = weapon;
            VisualPrefabName = visualPrefabName;
            AttachPrefab = attachPrefab;
            HitEffectPrefabs = hitEffectPrefabs;
            SpinAxisMode = spinAxisMode;
            VisualRotationOffset = visualRotationOffset;
            SkipVisualSwap = skipVisualSwap;
        }

        public ItemDrop.ItemData? Weapon { get; }

        public string VisualPrefabName { get; }

        public GameObject? AttachPrefab { get; }

        public EffectList.EffectData[]? HitEffectPrefabs { get; }

        public ThrowProjectileVisualSpin.AxisMode SpinAxisMode { get; }

        public Vector3 VisualRotationOffset { get; }

        public bool SkipVisualSwap { get; }

        public bool Active => Weapon?.m_dropPrefab != null && !string.IsNullOrEmpty(VisualPrefabName);
    }

    internal static BurstScope BeginBurst(Attack attack)
    {
        if (!ShouldApplyCopiedThrowVisuals(attack))
        {
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"burst skipped weapon={attack?.m_weapon?.m_dropPrefab?.name ?? "<null>"} attackType={attack?.m_attackType.ToString() ?? "<null>"} projectile={attack?.m_attackProjectile?.name ?? "<null>"}.");
            return default;
        }

        ActiveCopiedThrowBursts.Add(attack);
        MeleeProjectileHitCascadeSystem.LogDebug(
            $"burst begin weapon={attack.m_weapon?.m_dropPrefab?.name ?? "<null>"} projectile={attack.m_attackProjectile?.name ?? "<null>"} activeBursts={ActiveCopiedThrowBursts.Count}.");
        return new BurstScope(attack);
    }

    internal static void EndBurst(BurstScope scope)
    {
        if (!scope.Active)
        {
            return;
        }

        int lastIndex = ActiveCopiedThrowBursts.Count - 1;
        if (lastIndex >= 0 && ActiveCopiedThrowBursts[lastIndex] == scope.Attack)
        {
            ActiveCopiedThrowBursts.RemoveAt(lastIndex);
            return;
        }

        ActiveCopiedThrowBursts.Remove(scope.Attack!);
    }

    internal static void TryApplyToProjectileSetup(Projectile projectile, ItemDrop.ItemData item)
    {
        if (MeleeProjectileHitCascadeSystem.TryDescribeSpearRainFollowupProjectile(projectile, out string followupDescription))
        {
            MeleeProjectileHitCascadeSystem.LogDebug($"setup observed spearRain follow-up: {followupDescription} activeBursts={ActiveCopiedThrowBursts.Count} setupItem={item?.m_dropPrefab?.name ?? "<null>"}.");
        }

        if (projectile == null || ActiveCopiedThrowBursts.Count == 0)
        {
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"setup skipped projectile={projectile?.name ?? "<null>"} activeBursts={ActiveCopiedThrowBursts.Count}.");
            return;
        }

        Attack attack = ActiveCopiedThrowBursts[ActiveCopiedThrowBursts.Count - 1];
        if (attack == null)
        {
            MeleeProjectileHitCascadeSystem.LogDebug($"setup skipped projectile={projectile.name}: active attack is null.");
            return;
        }

        ItemDrop.ItemData? visualWeapon = attack.m_weapon ?? item;
        if (visualWeapon?.m_dropPrefab == null)
        {
            MeleeProjectileHitCascadeSystem.LogDebug($"setup skipped projectile={projectile.name}: visual weapon prefab is null.");
            return;
        }

        MeleeProjectileHitCascadeSystem.LogDebug(
            $"setup apply projectile={projectile.name} weapon={visualWeapon.m_dropPrefab.name} sourceProjectile={attack.m_attackProjectile?.name ?? "<null>"}.");
        MoveProjectileClearOfOwner(projectile, attack);

        SecondaryAttackDefinition? activeDefinition =
            ResolveCopiedThrowDefinition(attack, visualWeapon, visualWeapon.m_dropPrefab.name);
        SpawnedProjectileVisualContext visualContext =
            CreateSpawnedProjectileVisualContext(visualWeapon, attack.m_attackProjectile, activeDefinition, includeHitEffects: false);
        ApplyCurrentWeaponVisual(projectile, visualContext);
        ApplyCurrentWeaponHitEffects(projectile, visualWeapon);
        ApplyCopiedThrowAttribution(projectile, visualWeapon);

        if (SecondaryAttackStartAttackDispatch.ShouldSkipProjectilePresetEffectsForCooldown(attack, out _))
        {
            MeleeProjectileHitCascadeSystem.LogDebug(
                $"setup skipped preset effects projectile={projectile.name} weapon={visualWeapon.m_dropPrefab.name}: cooldown fallback copiedSecondary.");
            return;
        }

        MeleeBoomerangProjectileSystem.TryApplyToProjectileSetup(projectile, attack, visualWeapon);
        MeleeProjectileHitCascadeSystem.RegisterOnProjectileHitSource(projectile, attack, visualWeapon);
    }

    internal static void PrepareProjectileIfNeeded(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        ZNetView? nview = projectile.GetComponent<ZNetView>();
        if (nview == null ||
            !nview.IsValid() ||
            nview.GetZDO() == null ||
            !nview.GetZDO().GetBool(CopiedThrowProjectileMarkerKey))
        {
            return;
        }

        TryApplyHitEffectsFromSyncedVisual(projectile, nview);
        if (projectile.m_changedVisual)
        {
            return;
        }

        PrepareProjectileForVisualSwap(projectile);
    }

    internal static void EnsureProjectileVisualSpinIfNeeded(Projectile projectile)
    {
        if (!IsMarkedCopiedThrowProjectile(projectile))
        {
            return;
        }

        string? visualPrefabName = TryResolveSyncedVisualPrefabName(projectile);
        ApplyCopiedThrowVisualSpin(
            projectile,
            TryResolveSyncedVisualItem(visualPrefabName)?.m_itemData,
            visualPrefabName);
    }

    internal static void ApplyCurrentWeaponVisualForSpawnedProjectile(Projectile projectile, ItemDrop.ItemData weapon)
    {
        ApplyCurrentWeaponVisualForSpawnedProjectile(projectile, CreateSpawnedProjectileVisualContext(weapon));
    }

    internal static void ApplyCurrentWeaponVisualForSpawnedProjectile(Projectile projectile, SpawnedProjectileVisualContext context)
    {
        if (projectile == null || !context.Active)
        {
            return;
        }

        if (!context.SkipVisualSwap)
        {
            ApplyCurrentWeaponVisual(projectile, context);
        }

        ApplyHitEffects(projectile, context.HitEffectPrefabs);
    }

    internal static SpawnedProjectileVisualContext CreateSpawnedProjectileVisualContext(ItemDrop.ItemData weapon)
    {
        return CreateSpawnedProjectileVisualContext(weapon, includeHitEffects: true);
    }

    internal static SpawnedProjectileVisualContext CreateSpawnedProjectileVisualContext(
        ItemDrop.ItemData weapon,
        GameObject? sourceProjectilePrefab)
    {
        return CreateSpawnedProjectileVisualContext(weapon, sourceProjectilePrefab, includeHitEffects: true);
    }

    private static SpawnedProjectileVisualContext CreateSpawnedProjectileVisualContext(ItemDrop.ItemData weapon, bool includeHitEffects)
    {
        return CreateSpawnedProjectileVisualContext(weapon, sourceProjectilePrefab: null, includeHitEffects);
    }

    private static SpawnedProjectileVisualContext CreateSpawnedProjectileVisualContext(
        ItemDrop.ItemData weapon,
        GameObject? sourceProjectilePrefab,
        bool includeHitEffects)
    {
        return CreateSpawnedProjectileVisualContext(
            weapon,
            sourceProjectilePrefab,
            definition: null,
            includeHitEffects);
    }

    private static SpawnedProjectileVisualContext CreateSpawnedProjectileVisualContext(
        ItemDrop.ItemData weapon,
        GameObject? sourceProjectilePrefab,
        SecondaryAttackDefinition? definition,
        bool includeHitEffects)
    {
        if (weapon?.m_dropPrefab == null)
        {
            return default;
        }

        bool skipVisualSwap = UsesNativeProjectileVisual(weapon, sourceProjectilePrefab);
        GameObject? attachPrefab = null;
        if (!skipVisualSwap)
        {
            attachPrefab = ResolveAttachGameObject(weapon.m_dropPrefab);
        }

        definition ??= ResolveCopiedThrowDefinition(weapon, weapon.m_dropPrefab.name);
        ThrowProjectileVisualSpin.AxisMode spinAxisMode =
            ResolveConfiguredCopiedThrowSpinAxisMode(definition) ?? ThrowProjectileVisualSpin.AxisMode.None;
        return new SpawnedProjectileVisualContext(
            weapon,
            weapon.m_dropPrefab.name,
            attachPrefab,
            includeHitEffects ? CopyHitEffectPrefabs(weapon.m_shared?.m_hitEffect) : null,
            spinAxisMode,
            ResolveConfiguredCopiedThrowVisualRotationOffset(definition),
            skipVisualSwap);
    }

    internal static bool UsesNativeProjectileVisual(ItemDrop.ItemData weapon, GameObject? sourceProjectilePrefab)
    {
        if (weapon?.m_dropPrefab == null || sourceProjectilePrefab == null)
        {
            return false;
        }

        return IsSameProjectilePrefab(sourceProjectilePrefab, weapon.m_shared?.m_secondaryAttack?.m_attackProjectile);
    }

    private static bool IsSameProjectilePrefab(GameObject sourceProjectilePrefab, GameObject? weaponProjectilePrefab)
    {
        return weaponProjectilePrefab != null &&
               (ReferenceEquals(sourceProjectilePrefab, weaponProjectilePrefab) ||
                string.Equals(
                    sourceProjectilePrefab.name,
                    weaponProjectilePrefab.name,
                    StringComparison.OrdinalIgnoreCase));
    }

    private static GameObject? ResolveAttachGameObject(GameObject itemPrefab)
    {
        Transform? attach = itemPrefab != null ? itemPrefab.transform.Find("attach") : null;
        if (attach == null)
        {
            return null;
        }

        Transform? attachObject = attach.Find("attachobj");
        return attachObject != null ? attachObject.gameObject : attach.gameObject;
    }

    private static bool ShouldApplyCopiedThrowVisuals(Attack attack)
    {
        if (attack?.m_weapon?.m_dropPrefab == null ||
            attack.m_attackProjectile == null ||
            attack.m_attackType != Attack.AttackType.Projectile)
        {
            return false;
        }

        if (SecondaryAttackStartAttackDispatch.IsProjectilePresetOriginalCooldownFallback(attack, out _))
        {
            return false;
        }

        if (SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) &&
            activeAttack?.Definition.Behavior is CopiedSecondaryBehavior)
        {
            return true;
        }

        if (!IsConfiguredCopiedSecondaryAttack(attack))
        {
            return false;
        }

        return true;
    }

    private static bool IsConfiguredCopiedSecondaryAttack(Attack attack)
    {
        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(attack.m_weapon, out SecondaryAttackDefinition definition) ||
            definition.Behavior is not CopiedSecondaryBehavior)
        {
            return false;
        }

        if (attack.m_character is Humanoid humanoid && humanoid.m_currentAttack == attack)
        {
            return humanoid.m_currentAttackIsSecondary;
        }

        return ReferenceEquals(attack, attack.m_weapon.m_shared.m_secondaryAttack);
    }

    private static void MoveProjectileClearOfOwner(Projectile projectile, Attack attack)
    {
        Character? owner = ProjectileAccess.GetOwner(projectile) ?? attack.m_character;
        if (owner == null)
        {
            return;
        }

        Vector3 velocity = ProjectileAccess.GetVelocity(projectile);
        Vector3 direction = velocity.sqrMagnitude > 0.001f ? velocity.normalized : projectile.transform.forward;
        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        Vector3 ownerCenter = owner.GetCenterPoint();
        float clearDistance = Mathf.Max(owner.GetRadius() + Mathf.Max(0.1f, projectile.m_rayRadius) + 0.35f, 1.25f);
        Vector3 currentPosition = projectile.transform.position;
        float distanceAlongDirection = Vector3.Dot(currentPosition - ownerCenter, direction);
        if (distanceAlongDirection >= clearDistance)
        {
            return;
        }

        Vector3 adjustedPosition = currentPosition + direction * (clearDistance - distanceAlongDirection);
        projectile.transform.position = adjustedPosition;
        MeleeProjectileHitCascadeSystem.LogDebug(
            $"setup moved projectile clear of owner projectile={projectile.name} owner={owner.name} from={FormatVector(currentPosition)} to={FormatVector(adjustedPosition)} direction={FormatVector(direction)} clearDistance={clearDistance:0.###} previousDistance={distanceAlongDirection:0.###}.");
    }

    private static void ApplyCopiedThrowAttribution(Projectile projectile, ItemDrop.ItemData weapon)
    {
        if (!SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition) ||
            definition.Behavior is not CopiedSecondaryBehavior)
        {
            return;
        }

        SecondaryAttackRuntimeFacade.SetProjectileAttackAttribution(
            projectile,
            definition.PrefabName,
            secondaryAttack: true,
            definition,
            disableCurrentAttackFallback: false);
        SecondaryAttackProjectileToolTierSystem.ApplyToHitData(
            ProjectileAccess.GetOriginalHitData(projectile),
            projectile,
            weapon,
            "CopiedThrowProjectileVisualSystem.Setup");
    }

    private static void ApplyCurrentWeaponVisual(Projectile projectile, ItemDrop.ItemData weapon)
    {
        ApplyCurrentWeaponVisual(projectile, CreateSpawnedProjectileVisualContext(weapon, includeHitEffects: false));
    }

    private static void ApplyCurrentWeaponVisual(Projectile projectile, SpawnedProjectileVisualContext context)
    {
        if (!context.Active || context.SkipVisualSwap)
        {
            return;
        }

        System.Diagnostics.Stopwatch? totalPerf = SecondaryAttackPerformanceLog.Start();
        string path = "none";
        try
        {
            System.Diagnostics.Stopwatch? stepPerf = SecondaryAttackPerformanceLog.Start();
            PrepareProjectileForVisualSwap(projectile);
            SecondaryAttackPerformanceLog.Stop(
                stepPerf,
                "copiedThrow.visual.prepare",
                () => $"visual={context.VisualPrefabName} projectile={projectile.name} visualObject={projectile.m_visual?.name ?? "<null>"} canChange={projectile.m_canChangeVisuals}");

            stepPerf = SecondaryAttackPerformanceLog.Start();
            MarkCopiedThrowProjectile(projectile, context);
            SecondaryAttackPerformanceLog.Stop(
                stepPerf,
                "copiedThrow.visual.mark",
                () => $"visual={context.VisualPrefabName} projectile={projectile.name}");

            stepPerf = SecondaryAttackPerformanceLog.Start();
            ZNetView? nview = projectile.GetComponent<ZNetView>();
            bool nviewValid = nview != null && nview.IsValid();
            SecondaryAttackPerformanceLog.Stop(
                stepPerf,
                "copiedThrow.visual.getZNetView",
                () => $"visual={context.VisualPrefabName} projectile={projectile.name} valid={nviewValid}");

            if (projectile.m_canChangeVisuals && projectile.m_visual != null && nviewValid)
            {
                path = "updateVisual";
                if (nview!.IsOwner())
                {
                    stepPerf = SecondaryAttackPerformanceLog.Start();
                    nview.GetZDO().Set(ZDOVars.s_visual, context.VisualPrefabName);
                    SecondaryAttackPerformanceLog.Stop(
                        stepPerf,
                        "copiedThrow.visual.zdoSet",
                        () => $"visual={context.VisualPrefabName} projectile={projectile.name}");
                }

                stepPerf = SecondaryAttackPerformanceLog.Start();
                projectile.UpdateVisual();
                SecondaryAttackPerformanceLog.Stop(
                    stepPerf,
                    "copiedThrow.visual.updateVisual",
                    () => $"visual={context.VisualPrefabName} projectile={projectile.name} changed={projectile.m_changedVisual} visualObject={projectile.m_visual?.name ?? "<null>"}");

                stepPerf = SecondaryAttackPerformanceLog.Start();
                ApplyCopiedThrowVisualSpin(projectile, context);
                SecondaryAttackPerformanceLog.Stop(
                    stepPerf,
                    "copiedThrow.visual.spin",
                    () => $"visual={context.VisualPrefabName} projectile={projectile.name} axis={context.SpinAxisMode}");
                return;
            }

            path = "localFallback";
            stepPerf = SecondaryAttackPerformanceLog.Start();
            ApplyLocalFallbackVisual(projectile, context);
            SecondaryAttackPerformanceLog.Stop(
                stepPerf,
                "copiedThrow.visual.fallback",
                () => $"visual={context.VisualPrefabName} projectile={projectile.name} attachPrefab={context.AttachPrefab?.name ?? "<null>"} visualObject={projectile.m_visual?.name ?? "<null>"}");
        }
        finally
        {
            SecondaryAttackPerformanceLog.Stop(
                totalPerf,
                "copiedThrow.visual",
                () => $"visual={context.VisualPrefabName} projectile={projectile.name} path={path} visualObject={projectile.m_visual?.name ?? "<null>"}");
        }
    }

    private static void ApplyCurrentWeaponHitEffects(Projectile projectile, ItemDrop.ItemData weapon)
    {
        ApplyHitEffects(projectile, weapon.m_shared?.m_hitEffect);
    }

    private static void TryApplyHitEffectsFromSyncedVisual(Projectile projectile, ZNetView nview)
    {
        ItemDrop? itemDrop = TryResolveSyncedVisualItem(nview);
        ApplyHitEffects(projectile, itemDrop?.m_itemData?.m_shared?.m_hitEffect);
    }

    private static void ApplyHitEffects(Projectile projectile, EffectList? hitEffect)
    {
        if (hitEffect == null || !hitEffect.HasEffects())
        {
            return;
        }

        ApplyHitEffects(projectile, CopyHitEffectPrefabs(hitEffect));
    }

    private static void ApplyHitEffects(Projectile projectile, EffectList.EffectData[]? hitEffectPrefabs)
    {
        if (hitEffectPrefabs == null || hitEffectPrefabs.Length == 0)
        {
            return;
        }

        projectile.m_hitEffects = new EffectList
        {
            m_effectPrefabs = hitEffectPrefabs
        };
    }

    private static EffectList.EffectData[]? CopyHitEffectPrefabs(EffectList? hitEffect)
    {
        return hitEffect?.m_effectPrefabs != null && hitEffect.HasEffects()
            ? (EffectList.EffectData[])hitEffect.m_effectPrefabs.Clone()
            : null;
    }

    private static void MarkCopiedThrowProjectile(Projectile projectile, SpawnedProjectileVisualContext context)
    {
        ZNetView? nview = projectile.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid() || !nview.IsOwner() || nview.GetZDO() == null)
        {
            return;
        }

        ZDO zdo = nview.GetZDO();
        zdo.Set(CopiedThrowProjectileMarkerKey, true);
        zdo.Set(CopiedThrowProjectileSpinAxisKey, ToProjectileSpinAxisString(context.SpinAxisMode));
        zdo.Set(CopiedThrowProjectileRotationOffsetKey, SerializeVector3(context.VisualRotationOffset));
    }

    private static bool IsMarkedCopiedThrowProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return false;
        }

        ZNetView? nview = projectile.GetComponent<ZNetView>();
        return nview != null &&
               nview.IsValid() &&
               nview.GetZDO() != null &&
               nview.GetZDO().GetBool(CopiedThrowProjectileMarkerKey);
    }

    private static void PrepareProjectileForVisualSwap(Projectile projectile)
    {
        System.Diagnostics.Stopwatch? stepPerf = SecondaryAttackPerformanceLog.Start();
        Transform? existingVisualRoot = projectile.transform.Find(CopiedThrowProjectileVisualRootName);
        SecondaryAttackPerformanceLog.Stop(
            stepPerf,
            "copiedThrow.visual.prepare.findRoot",
            () => $"projectile={projectile.name} found={existingVisualRoot != null}");
        if (existingVisualRoot != null)
        {
            projectile.m_visual = existingVisualRoot.gameObject;
            projectile.m_canChangeVisuals = true;
            return;
        }

        stepPerf = SecondaryAttackPerformanceLog.Start();
        HideSourcePresentation(projectile);
        SecondaryAttackPerformanceLog.Stop(
            stepPerf,
            "copiedThrow.visual.prepare.hideSource",
            () => $"projectile={projectile.name}");

        stepPerf = SecondaryAttackPerformanceLog.Start();
        GameObject visualRoot = new(CopiedThrowProjectileVisualRootName);
        visualRoot.transform.SetParent(projectile.transform, false);
        visualRoot.layer = projectile.gameObject.layer;
        projectile.m_visual = visualRoot;
        projectile.m_canChangeVisuals = true;
        SecondaryAttackPerformanceLog.Stop(
            stepPerf,
            "copiedThrow.visual.prepare.createRoot",
            () => $"projectile={projectile.name} visualObject={visualRoot.name}");

    }

    private static void HideSourcePresentation(Projectile projectile)
    {
        RendererBuffer.Clear();
        projectile.GetComponentsInChildren(includeInactive: true, RendererBuffer);
        foreach (Renderer renderer in RendererBuffer)
        {
            if (renderer is TrailRenderer || renderer is ParticleSystemRenderer)
            {
                continue;
            }

            renderer.enabled = false;
        }

        RendererBuffer.Clear();
    }

    private static void ApplyLocalFallbackVisual(Projectile projectile, ItemDrop.ItemData weapon)
    {
        ApplyLocalFallbackVisual(projectile, CreateSpawnedProjectileVisualContext(weapon, includeHitEffects: false));
    }

    private static void ApplyLocalFallbackVisual(Projectile projectile, SpawnedProjectileVisualContext context)
    {
        ApplyLocalFallbackVisual(
            projectile,
            context,
            perfScopePrefix: "copiedThrow.visual.fallback");
    }

    private static void ApplyLocalFallbackVisual(
        Projectile projectile,
        SpawnedProjectileVisualContext context,
        string perfScopePrefix)
    {
        if (!context.Active || context.AttachPrefab == null)
        {
            return;
        }

        GameObject? previousVisual = projectile.m_visual;
        System.Diagnostics.Stopwatch? stepPerf = SecondaryAttackPerformanceLog.Start();
        GameObject visual = Object.Instantiate(context.AttachPrefab, projectile.transform, false);
        visual.name = $"{context.AttachPrefab.name}(ProjectileVisual)";
        SecondaryAttackPerformanceLog.Stop(
            stepPerf,
            $"{perfScopePrefix}.instantiate",
            () => $"visual={context.VisualPrefabName} projectile={projectile.name} attachPrefab={context.AttachPrefab.name}");
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        if (previousVisual != null && previousVisual != visual)
        {
            previousVisual.SetActive(false);
        }

        stepPerf = SecondaryAttackPerformanceLog.Start();
        visual.GetComponentInChildren<IEquipmentVisual>()?.Setup(context.Weapon!.m_variant);
        SecondaryAttackPerformanceLog.Stop(
            stepPerf,
            $"{perfScopePrefix}.equipmentSetup",
            () => $"visual={context.VisualPrefabName} projectile={projectile.name} variant={context.Weapon!.m_variant}");
        projectile.m_visual = visual;

        stepPerf = SecondaryAttackPerformanceLog.Start();
        ApplyCopiedThrowVisualSpin(projectile, context);
        SecondaryAttackPerformanceLog.Stop(
            stepPerf,
            $"{perfScopePrefix}.spin",
            () => $"visual={context.VisualPrefabName} projectile={projectile.name} axis={context.SpinAxisMode}");
    }

    private static void ApplyCopiedThrowVisualSpin(
        Projectile projectile,
        ItemDrop.ItemData? weapon,
        string? visualPrefabName = null)
    {
        if (projectile == null)
        {
            return;
        }

        SecondaryAttackDefinition? definition = ResolveCopiedThrowDefinition(weapon, visualPrefabName);
        bool hasSyncedSpin = TryResolveSyncedProjectileSpin(
            projectile,
            out ThrowProjectileVisualSpin.AxisMode syncedAxisMode,
            out Vector3 syncedRotationOffset);
        ThrowProjectileVisualSpin.AxisMode? configuredAxisMode = hasSyncedSpin
            ? syncedAxisMode
            : ResolveConfiguredCopiedThrowSpinAxisMode(definition);
        ThrowProjectileVisualSpin.AxisMode axisMode = configuredAxisMode ?? ThrowProjectileVisualSpin.AxisMode.None;
        Vector3 rotationOffset = hasSyncedSpin
            ? syncedRotationOffset
            : ResolveConfiguredCopiedThrowVisualRotationOffset(definition);
        Vector3 horizontalForward = axisMode == ThrowProjectileVisualSpin.AxisMode.HorizontalSide
            ? ResolveOwnerHorizontalForward(projectile)
            : Vector3.zero;
        ApplyCopiedThrowSpinConfiguration(projectile, axisMode, rotationOffset, horizontalForward);
    }

    private static void ApplyCopiedThrowVisualSpin(Projectile projectile, SpawnedProjectileVisualContext context)
    {
        if (projectile == null || !context.Active)
        {
            return;
        }

        Vector3 horizontalForward = context.SpinAxisMode == ThrowProjectileVisualSpin.AxisMode.HorizontalSide
            ? ResolveOwnerHorizontalForward(projectile)
            : Vector3.zero;
        ApplyCopiedThrowSpinConfiguration(projectile, context.SpinAxisMode, context.VisualRotationOffset, horizontalForward);
    }

    private static void ApplyCopiedThrowSpinConfiguration(
        Projectile projectile,
        ThrowProjectileVisualSpin.AxisMode axisMode,
        Vector3 rotationOffset,
        Vector3 horizontalForward)
    {
        GameObject? spinVisual = ResolveCopiedThrowSpinVisual(projectile.m_visual);
        if (spinVisual == null)
        {
            return;
        }

        CopiedThrowSpinState state =
            spinVisual.GetComponent<CopiedThrowSpinState>() ??
            spinVisual.AddComponent<CopiedThrowSpinState>();
        if (IsProjectileVisualSpinCleared(projectile) &&
            state.IsCurrent(axisMode, rotationOffset, horizontalForward))
        {
            return;
        }

        ClearProjectileVisualSpin(projectile);
        ThrowProjectileVisualRotationOffset.Ensure(spinVisual, rotationOffset);
        ThrowProjectileVisualSpin.Ensure(spinVisual, axisMode, horizontalForward);
        state.Configure(axisMode, rotationOffset, horizontalForward);
    }

    private static SecondaryAttackDefinition? ResolveCopiedThrowDefinition(
        Attack? attack,
        ItemDrop.ItemData? weapon,
        string? visualPrefabName)
    {
        if (attack != null &&
            SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? activeAttack) &&
            activeAttack?.Definition != null)
        {
            return activeAttack.Definition;
        }

        return ResolveCopiedThrowDefinition(weapon, visualPrefabName);
    }

    private static SecondaryAttackDefinition? ResolveCopiedThrowDefinition(
        ItemDrop.ItemData? weapon,
        string? visualPrefabName)
    {
        if (weapon != null &&
            SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition weaponDefinition))
        {
            return weaponDefinition;
        }

        string visualPrefab = visualPrefabName?.Trim() ?? "";
        if (visualPrefab.Length > 0 &&
            SecondaryAttackRuntimeFacade.TryGetDefinition(visualPrefab, out SecondaryAttackDefinition visualDefinition))
        {
            return visualDefinition;
        }

        return null;
    }

    private static ThrowProjectileVisualSpin.AxisMode? ResolveConfiguredCopiedThrowSpinAxisMode(SecondaryAttackDefinition? definition)
    {
        if (definition == null)
        {
            return null;
        }

        if (definition.Boomerang != null &&
            ProjectileSpinAxis.TryResolveAxisMode(definition.Boomerang.ProjectileSpinAxis, out ThrowProjectileVisualSpin.AxisMode boomerangAxisMode))
        {
            return boomerangAxisMode;
        }

        return definition.OnProjectileHit != null &&
               ProjectileSpinAxis.TryResolveAxisMode(definition.OnProjectileHit.ProjectileSpinAxis, out ThrowProjectileVisualSpin.AxisMode onHitAxisMode)
            ? onHitAxisMode
            : null;
    }

    private static Vector3 ResolveConfiguredCopiedThrowVisualRotationOffset(SecondaryAttackDefinition? definition)
    {
        if (definition?.Boomerang != null)
        {
            return definition.Boomerang.ProjectileVisualRotationOffset;
        }

        return definition?.OnProjectileHit?.ProjectileVisualRotationOffset ?? Vector3.zero;
    }

    private static GameObject? ResolveCopiedThrowSpinVisual(GameObject? visual)
    {
        if (visual == null)
        {
            return null;
        }

        Transform visualTransform = visual.transform;
        Transform? existingSpinRoot = visualTransform.Find(CopiedThrowProjectileSpinRootName);
        if (existingSpinRoot != null)
        {
            return existingSpinRoot.gameObject;
        }

        GameObject spinRoot = new(CopiedThrowProjectileSpinRootName);
        spinRoot.layer = visual.layer;
        Transform spinTransform = spinRoot.transform;
        spinTransform.SetParent(visualTransform, false);
        spinTransform.localPosition = Vector3.zero;
        spinTransform.localRotation = Quaternion.identity;
        spinTransform.localScale = Vector3.one;

        bool movedChild = false;
        for (int childIndex = visualTransform.childCount - 1; childIndex >= 0; childIndex--)
        {
            Transform child = visualTransform.GetChild(childIndex);
            if (child == spinTransform)
            {
                continue;
            }

            child.SetParent(spinTransform, false);
            movedChild = true;
        }

        if (movedChild)
        {
            return spinRoot;
        }

        Object.Destroy(spinRoot);
        return visual;
    }

    private static bool TryResolveSyncedProjectileSpin(
        Projectile projectile,
        out ThrowProjectileVisualSpin.AxisMode axisMode,
        out Vector3 rotationOffset)
    {
        axisMode = default;
        rotationOffset = Vector3.zero;
        ZNetView? nview = projectile != null ? projectile.GetComponent<ZNetView>() : null;
        if (nview == null ||
            !nview.IsValid() ||
            nview.GetZDO() == null ||
            !nview.GetZDO().GetString(CopiedThrowProjectileSpinAxisKey, out string rawAxis) ||
            !ProjectileSpinAxis.TryResolveAxisMode(rawAxis, out axisMode))
        {
            return false;
        }

        if (nview.GetZDO().GetString(CopiedThrowProjectileRotationOffsetKey, out string rawOffset) &&
            TryParseVector3(rawOffset, out Vector3 parsedOffset))
        {
            rotationOffset = parsedOffset;
        }

        return true;
    }

    private static string ToProjectileSpinAxisString(ThrowProjectileVisualSpin.AxisMode axisMode)
    {
        return axisMode switch
        {
            ThrowProjectileVisualSpin.AxisMode.HorizontalSide => ProjectileSpinAxis.Horizontal,
            ThrowProjectileVisualSpin.AxisMode.WorldUp => ProjectileSpinAxis.Vertical,
            _ => ProjectileSpinAxis.None
        };
    }

    private static string SerializeVector3(Vector3 value)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0},{1},{2}",
            value.x,
            value.y,
            value.z);
    }

    private static bool TryParseVector3(string raw, out Vector3 value)
    {
        value = Vector3.zero;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        string[] parts = raw.Split(',');
        if (parts.Length != 3)
        {
            return false;
        }

        if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
        {
            return false;
        }

        value = new Vector3(x, y, z);
        return true;
    }

    private static Vector3 ResolveOwnerHorizontalForward(Projectile projectile)
    {
        Character? owner = ProjectileAccess.GetOwner(projectile);
        Vector3 forward = owner != null ? owner.transform.forward : projectile.transform.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.zero;
    }

    private static void ClearProjectileVisualSpin(Projectile projectile)
    {
        projectile.m_rotateVisual = 0f;
        projectile.m_rotateVisualY = 0f;
        projectile.m_rotateVisualZ = 0f;
    }

    private static bool IsProjectileVisualSpinCleared(Projectile projectile)
    {
        return Mathf.Approximately(projectile.m_rotateVisual, 0f) &&
               Mathf.Approximately(projectile.m_rotateVisualY, 0f) &&
               Mathf.Approximately(projectile.m_rotateVisualZ, 0f);
    }

    private static string? TryResolveSyncedVisualPrefabName(Projectile projectile)
    {
        ZNetView? nview = projectile?.GetComponent<ZNetView>();
        return nview == null ? null : TryResolveSyncedVisualPrefabName(nview);
    }

    private static string? TryResolveSyncedVisualPrefabName(ZNetView nview)
    {
        if (nview == null ||
            !nview.IsValid() ||
            nview.GetZDO() == null ||
            !nview.GetZDO().GetString(ZDOVars.s_visual, out string visualPrefabName))
        {
            return null;
        }

        return visualPrefabName;
    }

    private static ItemDrop? TryResolveSyncedVisualItem(ZNetView nview)
    {
        return TryResolveSyncedVisualItem(TryResolveSyncedVisualPrefabName(nview));
    }

    private static ItemDrop? TryResolveSyncedVisualItem(string? visualPrefabName)
    {
        if (ObjectDB.instance == null || string.IsNullOrWhiteSpace(visualPrefabName))
        {
            return null;
        }

        return ObjectDB.instance.GetItemPrefab(visualPrefabName)?.GetComponent<ItemDrop>();
    }

    private static string FormatVector(Vector3 value)
    {
        return value.ToString("F2");
    }

    private sealed class CopiedThrowSpinState : MonoBehaviour
    {
        private ThrowProjectileVisualSpin.AxisMode _axisMode;
        private Vector3 _rotationOffset;
        private Vector3 _horizontalForward;
        private bool _configured;

        internal bool IsCurrent(
            ThrowProjectileVisualSpin.AxisMode axisMode,
            Vector3 rotationOffset,
            Vector3 horizontalForward)
        {
            return _configured &&
                   _axisMode == axisMode &&
                   (_rotationOffset - rotationOffset).sqrMagnitude <= SpinStateEpsilonSqr &&
                   (_horizontalForward - horizontalForward).sqrMagnitude <= SpinStateEpsilonSqr &&
                   ThrowProjectileVisualSpin.IsConfigured(gameObject, axisMode, horizontalForward);
        }

        internal void Configure(
            ThrowProjectileVisualSpin.AxisMode axisMode,
            Vector3 rotationOffset,
            Vector3 horizontalForward)
        {
            _axisMode = axisMode;
            _rotationOffset = rotationOffset;
            _horizontalForward = horizontalForward;
            _configured = true;
        }
    }
}
