using SecondaryAttacks;
using UnityEngine;

internal static class Program
{
    private static int _passed;
    private static int _failed;
    private static readonly string[] Types = { "summonEmpower", "shieldConvert", "projectile", "aftershock", "fractureLine", "copy" };

    private static int Main()
    {
        CompilerTests();
        RebindTests();
        Console.WriteLine($"RESULT: {_passed} passed, {_failed} failed");
        return _failed == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        SecondaryAttackManager.Reset();
        try
        {
            test();
            _passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception exception)
        {
            _failed++;
            Console.WriteLine("FAIL " + name + ": " + exception.Message);
        }
    }

    private static void Equal<T>(T expected, T actual, string label)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"{label}: expected [{expected}], actual [{actual}]");
    }

    private static void Calls(params string[] expected) => Equal(string.Join("|", expected), string.Join("|", SecondaryAttackManager.Calls), "dependency calls");

    private static NormalizedWeaponConfig Config(string? type, bool effects = false) => new()
    {
        HasEnabledMeleeFeatureConfig = effects,
        Secondary = type == null ? null : new NormalizedSecondaryModeConfig
        {
            Type = type,
            Projectile = new NormalizedProjectileSecondaryConfig()
        }
    };

    private static void Compile(NormalizedWeaponConfig config, string? kind, string? warning = null, ItemDrop? item = null, bool emitMissingWarnings = true)
    {
        bool result = SecondaryAttackDefinitionCompiler.TryCreateDefinition(
            new SecondaryAttackDefinitionBuildContext(new ObjectDB(), emitMissingWarnings),
            "Weapon", item ?? new ItemDrop(), config, out SecondaryAttackDefinition? definition);
        Equal(kind != null, result, "result");
        Equal(kind, definition?.Kind, "definition kind");
        Equal(warning == null ? "" : "Skipping Weapon: " + warning,
            string.Join("|", SecondaryAttacksPlugin.ModLogger.Warnings), "warnings");
    }

    private static void CompilerTests()
    {
        Run("compiler missing shared data", () =>
        {
            Compile(Config("copy", true), null, item: new ItemDrop { m_itemData = new() { m_shared = null! } });
            Calls();
        });
        Run("compiler missing item data", () =>
        {
            Compile(Config("copy", true), null, item: new ItemDrop { m_itemData = null! });
            Calls();
        });
        Run("compiler disabled suppresses effects and warnings", () =>
        {
            var config = Config("", true);
            config.Enabled = false;
            Compile(config, null);
            Calls();
        });
        foreach (string type in new[] { "none", "NoNe" })
            Run("compiler opt-out " + type, () => { Compile(Config(type, true), null); Calls(); });
        foreach (string type in new[] { "projectile", "PROJECTILE" })
            Run("compiler projectile opt-out " + type, () =>
            {
                var config = Config(type, true);
                config.Secondary!.Projectile!.Preset = "NoNe";
                Compile(config, null);
                Calls();
            });
        Run("compiler padded none retains legacy unsupported policy", () =>
        {
            Compile(Config(" none "), null, "unsupported secondary.type 'none'.");
            Calls();
        });
        Run("compiler padded projectile bypasses untrimmed opt-out", () =>
        {
            var config = Config(" projectile ");
            config.Secondary!.Projectile!.Preset = "none";
            Compile(config, "projectile");
            Calls("projectile");
        });
        Run("compiler padded projectile preset is delegated", () =>
        {
            var config = Config("projectile");
            config.Secondary!.Projectile!.Preset = " none ";
            Compile(config, "projectile");
            Calls("projectile");
        });

        foreach (bool effects in new[] { false, true })
        {
            string? fallback = effects ? "effect" : null;
            Run($"compiler no secondary effects={effects}", () => { Compile(Config(null, effects), fallback); Calls(effects ? new[] { "effect" } : Array.Empty<string>()); });
            foreach (string? type in new string?[] { "", "  ", null })
                Run($"compiler empty type [{type ?? "null"}] effects={effects}", () =>
                {
                    var config = Config("copy", effects);
                    config.Secondary!.Type = type!;
                    Compile(config, fallback, "a secondary behavior preset is required.");
                    Calls(effects ? new[] { "effect" } : Array.Empty<string>());
                });
            foreach (string? preset in new string?[] { null, "", "  " })
                Run($"compiler missing projectile preset [{preset ?? "object-null"}] effects={effects}", () =>
                {
                    var config = Config("projectile", effects);
                    config.Secondary!.Projectile = preset == null ? null : new NormalizedProjectileSecondaryConfig { Preset = preset };
                    Compile(config, fallback, "ranged secondary requires preset.", new ItemDrop { m_itemData = new() { m_shared = new() { m_attack = null! } } });
                    Calls(effects ? new[] { "effect" } : Array.Empty<string>());
                });
            Run($"compiler nonprojectile primary effects={effects}", () =>
            {
                var item = new ItemDrop();
                item.m_itemData.m_shared.m_attack.m_attackType = Attack.AttackType.Melee;
                Compile(Config("projectile", effects), fallback, "primary attack is not projectile-based.", item);
                Calls(effects ? new[] { "effect" } : Array.Empty<string>());
            });
            Run($"compiler unsupported effects={effects}", () =>
            {
                Compile(Config("futureType", effects), fallback, effects ? null : "unsupported secondary.type 'futureType'.");
                Calls(effects ? new[] { "effect" } : Array.Empty<string>());
            });
            foreach (bool emitWarnings in new[] { false, true })
                Run($"compiler missing copy effects={effects} emit={emitWarnings}", () =>
                {
                    SecondaryAttackManager.ResolveCopy = false;
                    Compile(Config("copy", effects), fallback, emitWarnings ? "copy source missing." : null, emitMissingWarnings: emitWarnings);
                    Calls(effects ? new[] { "resolve-copy", "effect" } : new[] { "resolve-copy" });
                });
        }

        foreach (string type in Types)
        {
            Run("compiler dispatch " + type, () =>
            {
                Compile(Config(type), type);
                Calls(type == "copy" ? new[] { "resolve-copy", "copy" } : new[] { type });
            });
            Run("compiler trimmed dispatch " + type, () => Compile(Config(" " + type + " "), type));
            Run("compiler case-sensitive dispatch " + type, () =>
            {
                Compile(Config(type.ToUpperInvariant()), null, $"unsupported secondary.type '{type.ToUpperInvariant()}'.");
                Calls();
            });
            foreach (bool effects in new[] { false, true })
                foreach (bool missingAttack in new[] { false, true })
                    Run($"compiler missing primary {type} effects={effects} null={missingAttack}", () =>
                    {
                        var item = new ItemDrop();
                        item.m_itemData.m_shared.m_attack = missingAttack ? null! : new Attack { m_attackAnimation = "  " };
                        Compile(Config(type, effects), effects ? "effect" : null, "primary attack is missing.", item);
                        Calls(effects ? new[] { "effect" } : Array.Empty<string>());
                    });
            Run("compiler builder rejection " + type, () =>
            {
                SecondaryAttackManager.BuilderResult = false;
                Compile(Config(type, true), null);
                Calls(type == "copy" ? new[] { "resolve-copy", "copy" } : new[] { type });
            });
        }
        foreach (string source in new[] { "", "  ", " OtherWeapon " })
            Run("compiler copy source [" + source + "]", () =>
            {
                var config = Config("copy");
                config.Secondary!.CopyFrom = source;
                Compile(config, "copy");
                Equal(source.Trim().Length == 0 ? "Weapon" : "OtherWeapon", SecondaryAttackManager.LastCopySource, "copy source");
            });
    }

    private static ItemDrop.ItemData Weapon()
    {
        var prefab = new GameObject { Component = new ItemDrop() };
        return new ItemDrop.ItemData { m_dropPrefab = prefab };
    }

    private static SecondaryAttackAppliedWorldSnapshot Snapshot(int revision = 0, SecondaryAttackDefinition? definition = null)
    {
        var snapshot = new SecondaryAttackAppliedWorldSnapshot { ApplyRevision = revision };
        if (definition != null) snapshot.DefinitionsByPrefabName.Add("Weapon", definition);
        return snapshot;
    }

    private static void RebindTests()
    {
        Run("rebind null weapon or prefab is ignored", () =>
        {
            SecondaryAttackRuntimeWeaponRebind.Apply(null, Snapshot());
            SecondaryAttackRuntimeWeaponRebind.Apply(new ItemDrop.ItemData(), Snapshot());
            Calls();
        });
        Run("rebind first revision applies and same revision skips", () =>
        {
            var weapon = Weapon();
            var snapshot = Snapshot(definition: new());
            SecondaryAttackRuntimeWeaponRebind.Apply(weapon, snapshot);
            Attack first = weapon.m_shared.m_secondaryAttack;
            SecondaryAttackRuntimeWeaponRebind.Apply(weapon, snapshot);
            Calls("resolve-rebind", "build-rebind", "normalize-rebind");
            Equal(true, ReferenceEquals(first, weapon.m_shared.m_secondaryAttack), "same revision attack reference");
        });
        Run("rebind changed revision reapplies", () =>
        {
            var weapon = Weapon();
            SecondaryAttackRuntimeWeaponRebind.Apply(weapon, Snapshot(0, new()));
            Attack first = weapon.m_shared.m_secondaryAttack;
            SecondaryAttackRuntimeWeaponRebind.Apply(weapon, Snapshot(1, new()));
            Equal(false, ReferenceEquals(first, weapon.m_shared.m_secondaryAttack), "new revision attack reference");
            Equal(2, SecondaryAttackManager.Calls.Count(x => x == "build-rebind"), "build count");
        });
        Run("rebind missing component does not record revision", () =>
        {
            var weapon = Weapon();
            weapon.m_dropPrefab!.Component = null;
            SecondaryAttackRuntimeWeaponRebind.Apply(weapon, Snapshot());
            Calls();
            weapon.m_dropPrefab.Component = new ItemDrop();
            SecondaryAttackRuntimeWeaponRebind.Apply(weapon, Snapshot());
            Calls("clone:original");
        });
        Run("rebind failed build retries same revision without replacing attack", () =>
        {
            var weapon = Weapon();
            var snapshot = Snapshot(definition: new());
            Attack original = weapon.m_shared.m_secondaryAttack;
            SecondaryAttackManager.ThrowDuringBuild = true;
            bool threw = false;
            try { SecondaryAttackRuntimeWeaponRebind.Apply(weapon, snapshot); }
            catch (InvalidOperationException) { threw = true; }
            Equal(true, threw, "exception propagation");
            Equal(true, ReferenceEquals(original, weapon.m_shared.m_secondaryAttack), "failed attack reference");
            SecondaryAttackManager.ThrowDuringBuild = false;
            SecondaryAttackRuntimeWeaponRebind.Apply(weapon, snapshot);
            Calls("resolve-rebind", "build-rebind", "resolve-rebind", "build-rebind", "normalize-rebind");
            Equal("configured", weapon.m_shared.m_secondaryAttack.Label, "retried attack");
        });
        Run("rebind distinct item instances sharing prefab each apply", () =>
        {
            var first = Weapon();
            var second = new ItemDrop.ItemData { m_dropPrefab = first.m_dropPrefab };
            var snapshot = Snapshot(definition: new());
            SecondaryAttackRuntimeWeaponRebind.Apply(first, snapshot);
            SecondaryAttackRuntimeWeaponRebind.Apply(second, snapshot);
            Equal(2, SecondaryAttackManager.Calls.Count(x => x == "build-rebind"), "build count");
            Equal(false, ReferenceEquals(first.m_shared.m_secondaryAttack, second.m_shared.m_secondaryAttack), "per-item attack");
        });
        foreach (string mode in new[] { "no-definition", "effect-only", "no-objectdb" })
            Run("rebind prefab fallback " + mode, () =>
            {
                var weapon = Weapon();
                var definition = mode == "no-definition" ? null : new SecondaryAttackDefinition { AppliesSecondaryOverride = mode != "effect-only" };
                if (mode == "no-objectdb") ObjectDB.instance = null;
                var snapshot = Snapshot(definition: definition);
                SecondaryAttackRuntimeWeaponRebind.Apply(weapon, snapshot);
                SecondaryAttackRuntimeWeaponRebind.Apply(weapon, snapshot);
                Calls("clone:original");
                Equal("original", weapon.m_shared.m_secondaryAttack.Label, "fallback attack");
                Equal(false, ReferenceEquals(((ItemDrop)weapon.m_dropPrefab!.Component!).m_itemData.m_shared.m_secondaryAttack, weapon.m_shared.m_secondaryAttack), "fallback clone reference");
            });
        foreach (bool explicitFallback in new[] { false, true })
            Run("rebind dynamic original fallback explicit=" + explicitFallback, () =>
            {
                var weapon = Weapon();
                var definition = new SecondaryAttackDefinition
                {
                    DynamicOriginalSecondary = true,
                    CooldownFallbackSecondaryAttack = explicitFallback ? new Attack { Label = "cooldown" } : null
                };
                SecondaryAttackRuntimeWeaponRebind.Apply(weapon, Snapshot(definition: definition));
                string expected = explicitFallback ? "cooldown" : "original";
                Equal(expected, weapon.m_shared.m_secondaryAttack.Label, "dynamic fallback attack");
                Calls("resolve-rebind", "build-rebind", "normalize-rebind", "clone:" + expected);
            });
        Run("rebind inventory refresh handles absent player and inventory", () =>
        {
            SecondaryAttackRuntimeWeaponRebind.RefreshLocalPlayerInventory(Snapshot());
            Player.m_localPlayer = new Player();
            SecondaryAttackRuntimeWeaponRebind.RefreshLocalPlayerInventory(Snapshot());
            Calls();
        });
        Run("rebind inventory refresh applies each valid item once", () =>
        {
            var inventory = new Inventory();
            inventory.Items.AddRange(new[] { Weapon(), Weapon(), new ItemDrop.ItemData() });
            Player.m_localPlayer = new Player { Inventory = inventory };
            var snapshot = Snapshot();
            SecondaryAttackRuntimeWeaponRebind.RefreshLocalPlayerInventory(snapshot);
            SecondaryAttackRuntimeWeaponRebind.RefreshLocalPlayerInventory(snapshot);
            Calls("clone:original", "clone:original");
        });
    }
}
