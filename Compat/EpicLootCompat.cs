using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace SecondaryAttacks;

// EpicLoot owns its proc chances, effect configuration and resource modifiers.
// SA owns Burst scheduling and cross-stack ammo payment. Bridge at each actual shot.
internal static class EpicLootCompat
{
    internal const string PluginGuid = "randyknapp.mods.epicloot";
    private const string HarmonyId = "sighsorry.SecondaryAttacks.EpicLootBurst";
    private static readonly AccessTools.FieldRef<Attack, Humanoid> Character =
        AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
    private static Api? _api;
    private static Attack? _manualCall;

    internal static void Initialize()
    {
        Dispose();
        if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out BepInEx.PluginInfo? plugin) ||
            plugin?.Instance == null)
        {
            return;
        }

        try
        {
            if (plugin.Metadata.Version != new System.Version(0, 14, 13))
                throw new NotSupportedException($"Unreviewed EpicLoot version {plugin.Metadata.Version}.");

            Api api = new(plugin.Instance.GetType().Assembly);
            Harmony harmony = new(HarmonyId);
            harmony.Patch(api.RollMethod, prefix: new HarmonyMethod(typeof(EpicLootCompat), nameof(RunAutomaticRoll)));
            harmony.Patch(api.PrepareMethod, prefix: new HarmonyMethod(typeof(EpicLootCompat), nameof(RunAutomaticPrepare)));
            _api = api;
            SecondaryAttacksPlugin.ModLogger.LogInfo("EpicLoot Burst compatibility enabled: per-shot multishot and ammo conservation.");
        }
        catch (Exception exception)
        {
            Dispose();
            SecondaryAttacksPlugin.ModLogger.LogWarning($"EpicLoot Burst compatibility unavailable: {exception.Message}");
        }
    }

    internal static void Dispose()
    {
        _api = null;
        _manualCall = null;
        new Harmony(HarmonyId).UnpatchSelf();
    }

    internal static bool HandlesAttack(Attack attack)
    {
        return _api != null && attack != null &&
               Character(attack) is Player player && player == Player.m_localPlayer &&
               !string.IsNullOrWhiteSpace(attack.GetWeapon()?.m_shared.m_ammoType) &&
               SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? active) &&
               active?.Definition.Behavior is ProjectileSecondaryBehavior { Preset: SecondaryAttackPreset.Burst };
    }

    // HarmonyX runs later prefixes even when SA skips the game method. Suppress
    // only these two EpicLoot callbacks, never its normal attacks or other effects.
    private static bool RunAutomaticRoll(Attack __0) => ReferenceEquals(_manualCall, __0) || !HandlesAttack(__0);

    private static bool RunAutomaticPrepare(Attack __0, ref HitData.DamageTypes? __1)
    {
        if (ReferenceEquals(_manualCall, __0) || !HandlesAttack(__0)) return true;
        __1 = null; // EpicLoot's original finalizer must not restore another shot's damage.
        return false;
    }

    internal static Shot? BeginShot(Attack attack) => HandlesAttack(attack) ? new Shot(_api!, attack) : null;

    internal static bool PreservesReload(Attack attack) => HandlesAttack(attack) &&
        SecondaryAttackRuntimeContext.TryGetActiveAttack(attack, out ActiveSecondaryAttack? active) &&
        active != null && active.BurstAmmoConserved;

    internal sealed class Shot : IDisposable
    {
        private readonly Api _api;
        private readonly Attack _attack;
        private readonly ItemDrop.ItemData.SharedData _shared;
        private readonly HitData.DamageTypes _damage;
        private readonly int _projectiles;
        private readonly float _accuracy;
        private readonly object? _pending;
        private readonly object? _triple;
        private readonly object? _count;
        private bool _disposed;

        internal Shot(Api api, Attack attack)
        {
            _api = api;
            _attack = attack;
            _shared = attack.GetWeapon().m_shared;
            _damage = _shared.m_damages;
            _projectiles = attack.m_projectiles;
            _accuracy = attack.m_projectileAccuracy;
            _pending = api.Pending.GetValue(null);
            _triple = api.Triple.GetValue(null);
            _count = api.Count.GetValue(null);
            Attack? previous = _manualCall;
            try
            {
                _manualCall = attack;
                api.Roll(attack);
                AmmoMultiplier = (bool)api.Triple.GetValue(null)!
                    ? Math.Max(1, (int)api.Count.GetValue(null)!) : 1;
                // Match UseAmmo's consumption of the ammo flags; keep pending
                // effect data for Prepare, isolated until this shot is disposed.
                api.Triple.SetValue(null, false);
                api.Count.SetValue(null, 0);
            }
            catch
            {
                Dispose();
                throw;
            }
            finally { _manualCall = previous; }
        }

        internal int AmmoMultiplier { get; }

        internal void PrepareProjectiles()
        {
            Attack? previous = _manualCall;
            try
            {
                _manualCall = _attack;
                HitData.DamageTypes? state = null;
                _api.Prepare(_attack, ref state);
            }
            finally { _manualCall = previous; }
        }

        internal void Complete(ItemDrop.ItemData? ammo, bool paidAmmo)
        {
            bool previous = (bool)_api.SkipReload.GetValue(null)!;
            try
            {
                _api.SkipReload.SetValue(null, false);
                if (paidAmmo && ammo != null)
                {
                    bool used = true;
                    _api.Conserve(_attack, ref used, ammo);
                }

                if (SecondaryAttackRuntimeContext.TryGetActiveAttack(_attack, out ActiveSecondaryAttack? active) && active != null)
                    active.BurstAmmoConserved = (bool)_api.SkipReload.GetValue(null)!;
            }
            finally
            {
                // Burst finalization, not the next Player.Update, owns this
                // reload. Keep unrelated EpicLoot pending reloads untouched.
                _api.SkipReload.SetValue(null, previous);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _shared.m_damages = _damage;
            _attack.m_projectiles = _projectiles;
            _attack.m_projectileAccuracy = _accuracy;
            _api.Pending.SetValue(null, _pending);
            _api.Triple.SetValue(null, _triple);
            _api.Count.SetValue(null, _count);
        }
    }

    internal sealed class Api
    {
        internal delegate void PrepareShot(Attack attack, ref HitData.DamageTypes? state);
        internal delegate void ConserveAmmo(Attack attack, ref bool result, ItemDrop.ItemData ammo);
        internal readonly MethodInfo RollMethod;
        internal readonly MethodInfo PrepareMethod;
        internal readonly Action<Attack> Roll;
        internal readonly PrepareShot Prepare;
        internal readonly ConserveAmmo Conserve;
        internal readonly FieldInfo Pending, Triple, Count, SkipReload;

        internal Api(Assembly assembly)
        {
            Type multi = assembly.GetType("EpicLoot.MagicItemEffects.MultiShot", true)!;
            Type conserve = assembly.GetType("EpicLoot.Magic.MagicItemEffects.AmmoConservation", true)!;
            RollMethod = multi.GetMethod("Attack_OnAttackTrigger_Prefix", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(multi.FullName, "Attack_OnAttackTrigger_Prefix");
            PrepareMethod = multi.GetMethod("Attack_FireProjectileBurst_Prefix", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(multi.FullName, "Attack_FireProjectileBurst_Prefix");
            Roll = (Action<Attack>)Delegate.CreateDelegate(typeof(Action<Attack>), RollMethod);
            Prepare = (PrepareShot)Delegate.CreateDelegate(typeof(PrepareShot), PrepareMethod);
            MethodInfo refund = conserve.GetNestedType("AmmoConservation_Attack_UseAmmo_Patch")?.GetMethod("Postfix")
                ?? throw new MissingMethodException(conserve.FullName, "AmmoConservation_Attack_UseAmmo_Patch.Postfix");
            Conserve = (ConserveAmmo)Delegate.CreateDelegate(typeof(ConserveAmmo), refund);
            Pending = RequireField(multi, "_pendingShot", null, false);
            if (!Pending.FieldType.IsEnum || string.Join(",", Enum.GetNames(Pending.FieldType)) != "None,TripleBow,DoubleMagic")
                throw new MissingFieldException("EpicLoot pending shot contract changed.");
            Triple = RequireField(multi, "IsTripleShotActive", typeof(bool), true);
            Count = RequireField(multi, "ShotProjectiles", typeof(int), true);
            SkipReload = RequireField(conserve, "skipReload", typeof(bool), false);
        }

        private static FieldInfo RequireField(Type type, string name, Type? fieldType, bool isPublic)
        {
            FieldInfo? field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null || field.IsPublic != isPublic || field.IsInitOnly ||
                (fieldType != null && field.FieldType != fieldType))
                throw new MissingFieldException(type.FullName, name);
            return field;
        }
    }
}
