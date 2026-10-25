using System;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace SecondaryAttacks;

// Keep optional PoV contracts here; node formulas and progression remain PoV's.
internal static class PathOfValheimanCompat
{
    internal const string PluginGuid = "treextr.pathofvalheiman";
    private const string HarmonyId = "sighsorry.SecondaryAttacks.PathOfValheiman";
    private static Api? _api;
    private static readonly AccessTools.FieldRef<Attack, Humanoid> Character = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
    private static readonly AccessTools.FieldRef<Attack, int> BurstsFired = AccessTools.FieldRefAccess<Attack, int>("m_projectileBurstsFired");
    private static ConditionalWeakTable<Attack, BurstPlan> _bursts = new();
    private static ConditionalWeakTable<Attack, FixedVolley> _fixedVolleys = new();
    private static ConditionalWeakTable<Attack, ReloadState> _reloads = new();
    [ThreadStatic] private static Attack? _manualCall;
    [ThreadStatic] private static Attack? _emitting;
    internal static bool Enabled => _api != null;
    internal static bool FreezesSkills => _api?.FrozenSkills.Value == true;

    internal static void Initialize()
    {
        Dispose();
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out BepInEx.PluginInfo? plugin) || plugin?.Instance == null)
            return;
        try
        {
            Api api = new(plugin.Instance.GetType().Assembly);
            new Harmony(HarmonyId).Patch(api.Keeper, prefix: new HarmonyMethod(typeof(PathOfValheimanCompat), nameof(ResolveKeeper)));
            foreach (MethodInfo callback in new[] { api.RecoilMethod, api.VarietyMethod })
                new Harmony(HarmonyId).Patch(callback, prefix: new HarmonyMethod(typeof(PathOfValheimanCompat), nameof(AllowBurstCallback)));
            if (api.VolleyResources != null)
                new Harmony(HarmonyId).Patch(api.VolleyResources, prefix: new HarmonyMethod(typeof(PathOfValheimanCompat), nameof(AllowVolleyResources)));
            if (api.NextShot != null)
                new Harmony(HarmonyId).Patch(api.NextShot, prefix: new HarmonyMethod(typeof(PathOfValheimanCompat), nameof(AllowMagazineChain)));
            _api = api;
            SecondaryAttacksPlugin.ModLogger.LogInfo($"Path of Valheiman {plugin.Metadata.Version} compatibility enabled.");
        }
        catch (Exception exception)
        {
            Dispose();
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Path of Valheiman {plugin.Metadata.Version} compatibility unavailable: {exception.Message}");
        }
    }

    internal static void Dispose()
    {
        _api = null;
        _bursts = new();
        _fixedVolleys = new();
        _reloads = new();
        new Harmony(HarmonyId).UnpatchSelf();
    }

    private static bool ResolveKeeper(Component __0, ref Player? __result)
    {
        if (__0 == null) return true;
        ZNetView? view = __0.GetComponent<ZNetView>();
        ZDO? zdo = view != null && view.IsValid() ? view.GetZDO() : null;
        if (zdo == null || zdo.GetLong("pt_tamer", 0L) != 0L) return true;
        if (!SubSummonSystem.TryResolveSummoner(__0.GetComponent<Character>(), out Player? player)) return true;
        __result = player; // A known, offline owner must not turn into the nearest stranger.
        return false;
    }

    internal static void ModifyConfiguredEitr(Attack attack, Character character, ItemDrop.ItemData weapon, ref float cost)
    {
        _api?.Eitr(character, weapon, ref cost);
    }

    internal static bool UsesConfiguredProjectileCost(Attack attack, ItemDrop.ItemData weapon)
    {
        return _api != null && _api.Secondary(attack) &&
            SecondaryAttackRuntimeFacade.TryGetDefinition(weapon, out SecondaryAttackDefinition definition) &&
            definition.Behavior is ProjectileSecondaryBehavior;
    }

    internal static bool HandlesBurst(Attack attack) => _api != null && Character(attack) == Player.m_localPlayer &&
        SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? active) &&
        active?.Definition.Behavior is ProjectileSecondaryBehavior { Preset: SecondaryAttackPreset.Burst } &&
        !string.IsNullOrEmpty(attack.GetWeapon()?.m_shared.m_ammoType);

    // Spawn-point queries also run while aiming, before BeginEmission. SA owns
    // configured costs/counts for these attacks, including fixed formations.
    private static bool AllowVolleyResources(Attack __0) => _api == null || Character(__0) != Player.m_localPlayer ||
        !SecondaryAttackRuntimeContext.TryGetActiveAttack(__0, out ActiveSecondaryAttack? active) ||
        active?.Definition.Behavior is not ProjectileSecondaryBehavior;

    internal static bool OwnsMagazineReload(Player player, ItemDrop.ItemData weapon) =>
        player == Player.m_localPlayer && _api?.MagazineIs?.Invoke(weapon) == true;

    private static readonly AccessTools.FieldRef<Humanoid, Attack> CurrentAttack = AccessTools.FieldRefAccess<Humanoid, Attack>("m_currentAttack");
    private static bool AllowMagazineChain(Player __0) => _api == null ||
        CurrentAttack(__0) is not Attack attack || !ProjectileRuntimeSystem.ShouldDeferBurstFireReloadReset(attack);

    // Start has already applied PoV's rolls and speed modifiers. Adopt only its
    // count reservation, so empty vanilla Fire callbacks cannot charge it again.
    internal static void RegisterAttack(Attack attack, SecondaryAttackDefinition definition)
    {
        Api? api = _api;
        if (api == null || Character(attack) != Player.m_localPlayer || definition.Behavior is not ProjectileSecondaryBehavior) return;
        if (!ReferenceEquals(api.LastAttack.GetValue(null), attack)) return;
        int added = Math.Max(0, (int)api.AddedShots.GetValue(null)!);
        int native = Math.Max(1, (int)api.NativeShots.GetValue(null)!);
        bool separateBursts = api.BurstFor != null && ReferenceEquals(api.BurstFor.GetValue(null), attack);
        api.LastAttack.SetValue(null, null);
        api.AddedShots.SetValue(null, 0);
        if (separateBursts)
        {
            api.BurstFor!.SetValue(null, null);
            attack.m_projectileBursts = Math.Max(1, attack.m_projectileBursts - added);
        }
        if (HandlesBurst(attack))
        {
            _bursts.Remove(attack);
            _bursts.Add(attack, new BurstPlan(separateBursts ? added : 0, separateBursts ? 0 : added, native));
        }
        else if (!separateBursts) attack.m_projectiles = native;
    }

    private sealed class BurstPlan
    {
        internal readonly int ExtraBursts, ExtraProjectiles, Native;
        internal BurstPlan(int bursts, int projectiles, int native) { ExtraBursts = bursts; ExtraProjectiles = projectiles; Native = native; }
    }
    internal static int ExtraBursts(Attack attack) => HandlesBurst(attack) && _bursts.TryGetValue(attack, out BurstPlan plan) ? plan.ExtraBursts : 0;
    internal static int ExtraAmmo(Attack attack) => HandlesBurst(attack) && _bursts.TryGetValue(attack, out BurstPlan plan) ? plan.ExtraProjectiles : 0;

    internal static int RollAmmoCost(Attack attack, int count)
    {
        if (!HandlesBurst(attack)) return count;
        Api api = _api!;
        object? usingAmmo = api.UsingAmmo.GetValue(null), saved = api.AmmoSaved.GetValue(null);
        object? weapon = api.AmmoWeapon?.GetValue(null);
        try
        {
            api.UsingAmmo.SetValue(null, true);
            api.AmmoWeapon?.SetValue(null, attack.GetWeapon());
            int paid = 0;
            for (int i = 0; i < count; i++)
            {
                api.AmmoSaved.SetValue(null, false);
                bool result = false;
                if (api.PayAmmo(ref result)) paid++;
            }
            return paid;
        }
        finally { api.UsingAmmo.SetValue(null, usingAmmo); api.AmmoSaved.SetValue(null, saved); api.AmmoWeapon?.SetValue(null, weapon); }
    }

    private static bool AllowBurstCallback(Attack __0) => _manualCall == __0 || !HandlesBurst(__0);

    internal static void AfterBurstShot(Attack attack, int paidAmmo)
    {
        if (!HandlesBurst(attack)) return;
        if (attack.m_requiresReload && _api!.MagazineIs?.Invoke(attack.GetWeapon()) == true && !_reloads.TryGetValue(attack, out _))
        {
            // PoV consumes one magazine position per trigger. Only the first
            // successful shot represents that trigger; repeats still pay SA's
            // configured ammo, but cannot consume/re-fill the magazine again.
            SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? active);
            bool saved = paidAmmo == 0 && active?.Definition.Behavior is ProjectileSecondaryBehavior { AmmoConsumption: > 0 };
            using ReloadScope scope = new(_api, attack, saved, out bool preserve);
            _reloads.Add(attack, new ReloadState { Reset = !preserve });
        }
        Attack? previous = _manualCall;
        int fired = BurstsFired(attack);
        try
        {
            _manualCall = attack;
            // SA already applies the base recoil once per real shot.
            BurstsFired(attack) = 0;
            _api!.Recoil(attack);
            _api.Variety(attack);
        }
        finally { _manualCall = previous; BurstsFired(attack) = fired; }
    }

    private sealed class ReloadState { internal bool Reset, Completed; }

    // The magazine was consumed at the trigger, where PoV records shot timing.
    // Defer only the physical unload until SA's configured Burst finishes.
    internal static IDisposable? BeginReloadConsumption(Attack attack, bool conserved, out bool preserve)
    {
        preserve = conserved;
        Api? api = _api;
        if (api?.MagazineShot == null || !_reloads.TryGetValue(attack, out ReloadState state)) return null;
        preserve = state.Completed || !state.Reset || api.MagazineRemaining!(attack.GetWeapon()) > 0;
        state.Completed = true;
        return preserve ? null : new ReloadScope(api);
    }

    private sealed class ReloadScope : IDisposable
    {
        private readonly Api _api;
        private readonly object? _saved, _firing;
        private bool _disposed;
        internal ReloadScope(Api api)
        {
            _api = api;
            _saved = api.AmmoSaved.GetValue(null);
            _firing = api.MagazineFiring!.GetValue(null);
            // KeepLoaded already ran; the physical reset must not consume again.
            api.MagazineFiring.SetValue(null, null);
        }
        internal ReloadScope(Api api, Attack attack, bool saved, out bool preserve) : this(api)
        {
            try
            {
                api.AmmoSaved.SetValue(null, saved);
                preserve = !api.MagazineShot!(attack, out _) || !api.MagazineKeepLoaded!((Player)Character(attack));
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _api.MagazineFiring!.SetValue(null, _firing);
            _api.AmmoSaved.SetValue(null, _saved);
        }
    }

    private static Func<Attack, IDisposable> CreatePositionScope<T>(MethodInfo position, MethodInfo push, MethodInfo pop, Func<ItemDrop.ItemData, bool> isCrossbow)
    {
        var get = (Func<ItemDrop.ItemData, T>)Delegate.CreateDelegate(typeof(Func<ItemDrop.ItemData, T>), position);
        var begin = (Func<T, T>)Delegate.CreateDelegate(typeof(Func<T, T>), push);
        var end = (Action<T>)Delegate.CreateDelegate(typeof(Action<T>), pop);
        var positions = new ConditionalWeakTable<Attack, PositionValue<T>>();
        return attack =>
        {
            T tag = default!;
            if (isCrossbow(attack.GetWeapon()))
                tag = positions.GetValue(attack, a => new PositionValue<T> { Tag = get(a.GetWeapon()) }).Tag;
            return new PositionScope<T>(begin(tag), end);
        };
    }
    private sealed class PositionValue<T> { internal T Tag = default!; }
    private sealed class PositionScope<T> : IDisposable
    {
        private readonly T _previous;
        private Action<T>? _end;
        internal PositionScope(T previous, Action<T> end) { _previous = previous; _end = end; }
        public void Dispose() { Action<T>? end = _end; _end = null; end?.Invoke(_previous); }
    }

    internal static IDisposable? BeginEmission(Attack attack, int paidAmmo = 0)
        => _api != null && Character(attack) == Player.m_localPlayer ? new EmissionScope(_api, attack, paidAmmo) : null;

    internal static IDisposable? BeginProjectile(Attack attack)
        => _emitting == attack ? BeginAttack(attack, projectile: true) : BeginEmission(attack);

    internal static void UsePresetCollision(Projectile projectile)
    {
        Api? api = _api;
        if (api == null || projectile == null) return;
        Component? modifiers = projectile.GetComponent(api.ProjectileModifiers);
        if (modifiers == null) return;
        // Piercing/Scatter already own continuation. Keep PoV's volley/ignore
        // filters and damage state, but do not create a second continuation.
        foreach (FieldInfo field in api.Continuations) field.SetValue(modifiers, 0);
    }

    private sealed class FixedVolley { internal int Id; }

    private sealed class EmissionScope : IDisposable
    {
        private readonly Api _api;
        private readonly object?[] _values;
        private readonly IDisposable _origin;
        private readonly Attack? _previous;
        private readonly IDisposable? _position;
        private bool _disposed;
        internal EmissionScope(Api api, Attack attack, int paidAmmo)
        {
            _api = api;
            _values = api.EmissionFields.Select(f => f.GetValue(null)).ToArray();
            _origin = new AttackScope(api, attack, true);
            _previous = _emitting;
            _emitting = attack;
            try
            {
                _position = api.BeginPosition?.Invoke(attack);
                bool extras = HandlesBurst(attack) && _bursts.TryGetValue(attack, out BurstPlan? plan) && plan.ExtraProjectiles > 0;
                int native = Math.Max(1, attack.m_projectiles);
                if (extras && _bursts.TryGetValue(attack, out BurstPlan extraPlan))
                    native = extraPlan.Native * Math.Max(1, attack.m_projectiles / (extraPlan.Native + extraPlan.ExtraProjectiles));
                if (HandlesBurst(attack)) api.OpenBurst(native, 0f);
                else
                {
                    if (!_fixedVolleys.TryGetValue(attack, out FixedVolley volley))
                    {
                        api.OpenBurst(native, 0f);
                        volley = new FixedVolley { Id = (int)api.EmissionFields[6].GetValue(null)! };
                        _fixedVolleys.Add(attack, volley);
                    }
                    api.EmissionFields[6].SetValue(null, volley.Id); // volleyNow
                }
                // Fixed formations and EpicLoot-only clusters own their spread.
                api.EmissionFields[2].SetValue(null, extras); // inBurst
                api.EmissionFields[3].SetValue(null, extras); // countingBurst
                api.Recoverable.SetValue(null, Math.Max(0, paidAmmo));
            }
            catch { Dispose(); throw; }
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _position?.Dispose();
            _emitting = _previous;
            for (int i = 0; i < _values.Length; i++) _api.EmissionFields[i].SetValue(null, _values[i]);
            _origin.Dispose();
        }
    }

    internal static IDisposable? BeginAttack(Attack attack, bool projectile = false)
    {
        return _api != null && Character(attack) == Player.m_localPlayer
            ? new AttackScope(_api, attack, projectile) : null;
    }

    private sealed class AttackScope : IDisposable
    {
        private readonly Api _api;
        private readonly object? _secondary, _charged, _projectile, _weapon;
        private readonly bool _isProjectile;
        private bool _disposed;
        internal AttackScope(Api api, Attack attack, bool projectile)
        {
            _api = api;
            _isProjectile = projectile;
            _secondary = api.SecondaryOrigin.GetValue(null);
            _charged = api.ChargeOrigin.GetValue(null);
            _projectile = api.ProjectileOrigin.GetValue(null);
            _weapon = api.WeaponOverride.GetValue(null);
            api.SecondaryOrigin.SetValue(null, attack);
            api.ChargeOrigin.SetValue(null, attack);
            if (projectile) api.ProjectileOrigin.SetValue(null, attack);
            api.WeaponOverride.SetValue(null, attack.GetWeapon());
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _api.SecondaryOrigin.SetValue(null, _secondary);
            _api.ChargeOrigin.SetValue(null, _charged);
            if (_isProjectile) _api.ProjectileOrigin.SetValue(null, _projectile);
            _api.WeaponOverride.SetValue(null, _weapon);
        }
    }

    private sealed class Api
    {
        internal delegate void EitrModifier(Character character, ItemDrop.ItemData weapon, ref float cost);
        internal delegate bool AmmoPayment(ref bool result);
        internal delegate bool MagazineShotPrefix(Attack attack, out Attack previous);
        internal readonly MethodInfo Keeper;
        internal readonly EitrModifier Eitr;
        internal readonly Func<Attack, bool> Secondary;
        internal readonly FieldInfo SecondaryOrigin, ChargeOrigin, ProjectileOrigin, WeaponOverride;
        internal readonly FieldInfo LastAttack, AddedShots, NativeShots, UsingAmmo, AmmoSaved, Recoverable;
        internal readonly FieldInfo? BurstFor, AmmoWeapon, MagazineFiring;
        internal readonly MethodInfo? VolleyResources;
        internal readonly MethodInfo? NextShot;
        internal readonly Func<ItemDrop.ItemData, int>? MagazineRemaining;
        internal readonly MagazineShotPrefix? MagazineShot;
        internal readonly Func<Player, bool>? MagazineKeepLoaded;
        internal readonly Func<ItemDrop.ItemData, bool>? MagazineIs;
        internal readonly Func<Attack, IDisposable>? BeginPosition;
        internal readonly FieldInfo[] EmissionFields;
        internal readonly AmmoPayment PayAmmo;
        internal readonly Action<int, float> OpenBurst;
        internal readonly MethodInfo RecoilMethod, VarietyMethod;
        internal readonly Action<Attack> Recoil, Variety;
        internal readonly Type ProjectileModifiers;
        internal readonly FieldInfo[] Continuations;
        internal readonly ConfigEntry<bool> FrozenSkills;
        internal Api(Assembly assembly)
        {
            Keeper = Method(assembly, "PassiveTree.TamingHunting", "Keeper", typeof(Player), typeof(Component));
            Secondary = (Func<Attack, bool>)Delegate.CreateDelegate(typeof(Func<Attack, bool>), Method(assembly,
                "PassiveTree.SecondaryAttacks", "Secondary", typeof(bool), typeof(Attack)));
            Eitr = (EitrModifier)Delegate.CreateDelegate(typeof(EitrModifier), Method(assembly,
                "PassiveTree.Effects+Attack_GetAttackEitr", "Postfix", typeof(void),
                typeof(Character), typeof(ItemDrop.ItemData), typeof(float).MakeByRefType()));
            SecondaryOrigin = Field(assembly, "PassiveTree.SecondaryAttacks", "producing", typeof(Attack));
            ChargeOrigin = Field(assembly, "PassiveTree.ChargedAttacks", "producing", typeof(Attack));
            ProjectileOrigin = Field(assembly, "PassiveTree.ProjectileMods", "creatingProjectile", typeof(Attack));
            ProjectileModifiers = assembly.GetType("PassiveTree.ProjMods", true)!;
            if (!typeof(Component).IsAssignableFrom(ProjectileModifiers)) throw new TypeLoadException("PoV ProjMods is not a component.");
            Continuations = new[] { "Pierce", "Chain", "Bounce" }.Select(name =>
            {
                FieldInfo? field = ProjectileModifiers.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field == null || field.FieldType != typeof(int) || field.IsInitOnly || field.IsLiteral) throw new MissingFieldException(ProjectileModifiers.FullName, name);
                return field;
            }).ToArray();
            WeaponOverride = Field(assembly, "PassiveTree.WeaponAffixes", "Override", typeof(ItemDrop.ItemData));
            const string projectiles = "PassiveTree.ProjectileMods";
            LastAttack = Field(assembly, projectiles, "lastAttack", typeof(Attack));
            AddedShots = Field(assembly, projectiles, "addedShots", typeof(int));
            NativeShots = Field(assembly, projectiles, "nativeShots", typeof(int));
            // Detect the complete new contract, not a version/hash allowlist.
            if (assembly.GetType(projectiles + "+VolleyResources") == null)
                BurstFor = Field(assembly, projectiles, "burstFor", typeof(Attack));
            else
            {
                AmmoWeapon = Field(assembly, projectiles, "ammoWeapon", typeof(ItemDrop.ItemData));
                VolleyResources = Method(assembly, projectiles + "+VolleyResources", "Postfix", typeof(void), typeof(Attack));
                RequirePatch(AccessTools.DeclaredMethod(typeof(Attack), "GetProjectileSpawnPoint"), VolleyResources, HarmonyPatchType.Postfix);
                const string magazine = "PassiveTree.CrossbowMagazine";
                MagazineFiring = Field(assembly, magazine, "firing", typeof(Attack));
                NextShot = Method(assembly, magazine + "+NextShot", "Postfix", typeof(void), typeof(Player), typeof(bool).MakeByRefType());
                RequirePatch(AccessTools.DeclaredMethod(typeof(Player), "HaveQueuedChain"), NextShot, HarmonyPatchType.Postfix);
                MagazineRemaining = (Func<ItemDrop.ItemData, int>)Delegate.CreateDelegate(typeof(Func<ItemDrop.ItemData, int>),
                    Method(assembly, magazine, "Remaining", typeof(int), typeof(ItemDrop.ItemData)));
                MagazineIs = (Func<ItemDrop.ItemData, bool>)Delegate.CreateDelegate(typeof(Func<ItemDrop.ItemData, bool>),
                    Method(assembly, magazine, "Is", typeof(bool), typeof(ItemDrop.ItemData)));
                MethodInfo shot = Method(assembly, magazine + "+Shot", "Prefix", typeof(bool), typeof(Attack), typeof(Attack).MakeByRefType());
                MethodInfo keep = Method(assembly, magazine + "+KeepLoaded", "Prefix", typeof(bool), typeof(Player));
                RequirePatch(AccessTools.DeclaredMethod(typeof(Attack), "OnAttackTrigger"), shot, HarmonyPatchType.Prefix);
                RequirePatch(AccessTools.DeclaredMethod(typeof(Player), "ResetLoadedWeapon"), keep, HarmonyPatchType.Prefix);
                RequireInstalled(assembly, "CrossbowMagazine+Shot", "Finalizer", typeof(Attack), "OnAttackTrigger");
                RequireInstalled(assembly, "CrossbowMagazine+Fill", "Prefix", typeof(Player), "SetWeaponLoaded");
                RequireInstalled(assembly, "CrossbowMagazine+Fill", "Postfix", typeof(Player), "SetWeaponLoaded");
                RequireInstalled(assembly, "CrossbowMagazine+Restore", "Prefix", typeof(Player), "UpdateWeaponLoading");
                RequireInstalled(assembly, "CrossbowMagazine+Gate", "Prefix", typeof(Humanoid), "StartAttack");
                RequireInstalled(assembly, "CrossbowMagazine+Gate", "Postfix", typeof(Humanoid), "StartAttack");
                MagazineShot = (MagazineShotPrefix)Delegate.CreateDelegate(typeof(MagazineShotPrefix), shot);
                MagazineKeepLoaded = (Func<Player, bool>)Delegate.CreateDelegate(typeof(Func<Player, bool>), keep);
                Type tag = assembly.GetType("PassiveTree.HitTag", true)!;
                if (!tag.IsEnum) throw new TypeLoadException("PoV HitTag is not an enum.");
                MethodInfo position = Method(assembly, magazine + "+Position", "Prefix", typeof(void), typeof(Attack), tag.MakeByRefType());
                MethodInfo endPosition = Method(assembly, magazine + "+Position", "Finalizer", typeof(void), tag);
                RequirePatch(AccessTools.DeclaredMethod(typeof(Attack), "FireProjectileBurst"), position, HarmonyPatchType.Prefix);
                RequirePatch(AccessTools.DeclaredMethod(typeof(Attack), "FireProjectileBurst"), endPosition, HarmonyPatchType.Finalizer);
                BeginPosition = (Func<Attack, IDisposable>)typeof(PathOfValheimanCompat).GetMethod(nameof(CreatePositionScope), BindingFlags.NonPublic | BindingFlags.Static)!
                    .MakeGenericMethod(tag).Invoke(null, new object[] {
                        Method(assembly, magazine, "ShotPosition", tag, typeof(ItemDrop.ItemData)),
                        Method(assembly, "PassiveTree.HitContext", "Push", tag, tag),
                        Method(assembly, "PassiveTree.HitContext", "Pop", typeof(void), tag), MagazineIs })!;
            }
            UsingAmmo = Field(assembly, projectiles, "usingAmmo", typeof(bool));
            AmmoSaved = Field(assembly, projectiles, "ammoSaved", typeof(bool));
            Recoverable = Field(assembly, projectiles, "recoverable", typeof(int));
            EmissionFields = new[] { NativeShots, Recoverable,
                Field(assembly, projectiles, "inBurst", typeof(bool)),
                Field(assembly, projectiles, "countingBurst", typeof(bool)),
                Field(assembly, projectiles, "shotIndex", typeof(int)),
                Field(assembly, projectiles, "ownSpread", typeof(float)),
                Field(assembly, projectiles, "volleyNow", typeof(int)) };
            OpenBurst = (Action<int, float>)Delegate.CreateDelegate(typeof(Action<int, float>),
                Method(assembly, projectiles, "OpenBurst", typeof(void), typeof(int), typeof(float)));
            MethodInfo payment = Method(assembly, projectiles + "+Inventory_RemoveItem", "Prefix", typeof(bool), typeof(bool).MakeByRefType());
            PayAmmo = (AmmoPayment)Delegate.CreateDelegate(typeof(AmmoPayment), payment);
            RecoilMethod = Method(assembly, "PassiveTree.CrossbowRecoil+Burst", "Postfix", typeof(void), typeof(Attack));
            VarietyMethod = Method(assembly, "PassiveTree.AmmoVariety+Fired", "Postfix", typeof(void), typeof(Attack));
            Recoil = (Action<Attack>)Delegate.CreateDelegate(typeof(Action<Attack>), RecoilMethod);
            Variety = (Action<Attack>)Delegate.CreateDelegate(typeof(Action<Attack>), VarietyMethod);
            RequirePatch(AccessTools.DeclaredMethod(typeof(Inventory), "RemoveItem", new[] { typeof(ItemDrop.ItemData), typeof(int) }), payment, HarmonyPatchType.Prefix);
            RequirePatch(AccessTools.DeclaredMethod(typeof(Attack), "FireProjectileBurst"), RecoilMethod, HarmonyPatchType.Postfix);
            RequirePatch(AccessTools.DeclaredMethod(typeof(Attack), "FireProjectileBurst"), VarietyMethod, HarmonyPatchType.Postfix);
            RequirePatch(AccessTools.DeclaredMethod(typeof(Attack), "GetAttackEitr", new[] { typeof(Character), typeof(ItemDrop.ItemData) }), Eitr.Method, HarmonyPatchType.Postfix);
            RequireInstalled(assembly, "ProjectileMods+ClassifyBeforeDamage", "Prefix", typeof(SEMan), "ModifyAttack");
            RequireInstalled(assembly, "ProjectileMods+Attack_Start", "Postfix", typeof(Attack), "Start");
            RequireInstalled(assembly, "ProjectileMods+Projectile_Setup", "Prefix", typeof(Projectile), "Setup");
            RequireInstalled(assembly, "ProjectileMods+Projectile_Setup", "Postfix", typeof(Projectile), "Setup");
            RequireInstalled(assembly, "SecondaryAttacks+Humanoid_StartAttack", "Prefix", typeof(Humanoid), "StartAttack");
            RequireInstalled(assembly, "SecondaryAttacks+Attack_Start_Origin", "Prefix", typeof(Attack), "Start");
            RequireInstalled(assembly, "ChargedAttacks+ReleaseAttack", "Prefix", typeof(Attack), "OnAttackTrigger");
            RequireInstalled(assembly, "Blasts+Attack_OnAttackTrigger_Area", "Prefix", typeof(Attack), "OnAttackTrigger");
            FrozenSkills = (ConfigEntry<bool>)Field(assembly, "PassiveTree.Plugin", "DisableVanillaSkills", typeof(ConfigEntry<bool>)).GetValue(null)!;
            if (FrozenSkills == null) throw new InvalidOperationException("PoV settings are not initialized.");
        }
    }

    private static void RequireInstalled(Assembly assembly, string type, string name, Type targetType, string targetName)
    {
        MethodInfo callback = assembly.GetType("PassiveTree." + type, true)!.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly)
            ?? throw new MissingMethodException(type, name);
        RequirePatch(AccessTools.DeclaredMethod(targetType, targetName), callback,
            name == "Prefix" ? HarmonyPatchType.Prefix : name == "Finalizer" ? HarmonyPatchType.Finalizer : HarmonyPatchType.Postfix);
    }

    private static void RequirePatch(MethodInfo target, MethodInfo callback, HarmonyPatchType kind)
    {
        Patches? patches = target == null ? null : Harmony.GetPatchInfo(target);
        bool present = kind == HarmonyPatchType.Prefix ? patches?.Prefixes.Any(p => p.PatchMethod == callback && p.owner == PluginGuid) == true
            : kind == HarmonyPatchType.Finalizer ? patches?.Finalizers.Any(p => p.PatchMethod == callback && p.owner == PluginGuid) == true
            : patches?.Postfixes.Any(p => p.PatchMethod == callback && p.owner == PluginGuid) == true;
        if (!present) throw new NotSupportedException($"PoV callback is not registered: {callback.DeclaringType?.FullName}.{callback.Name}");
    }

    private static MethodInfo Method(Assembly assembly, string type, string name, Type result, params Type[] args)
    {
        MethodInfo? method = assembly.GetType(type, true)!.GetMethod(name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, args, null);
        if (method == null || method.ReturnType != result || method.ContainsGenericParameters)
            throw new MissingMethodException(type, name);
        return method;
    }

    private static FieldInfo Field(Assembly assembly, string type, string name, Type expected)
    {
        FieldInfo? field = assembly.GetType(type, true)!.GetField(name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (field == null || field.FieldType != expected || field.IsInitOnly || field.IsLiteral)
            throw new MissingFieldException(type, name);
        return field;
    }
}
