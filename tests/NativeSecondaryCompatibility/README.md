# Native secondary compatibility checks

From the repository root:

```powershell
dotnet build tests/NativeSecondaryCompatibility/NativeSecondaryCompatibility.csproj -c Debug
foreach ($mode in @('absent', 'wizardry', 'magic', 'both', 'fallback', 'onehanded', 'magicplugin', 'magicplugin-missing')) {
    dotnet tests/NativeSecondaryCompatibility/bin/Debug/net9.0/NativeSecondaryCompatibility.dll $mode
    if ($LASTEXITCODE -ne 0) { throw "Compatibility checks failed: $mode" }
}
```

These checks compile the production compatibility helper and ObjectDB snapshot store directly. Each process starts with a fresh plugin/asset cache. Dependency doubles represent plugin metadata, Unity prefabs, the private Wizardry ItemManager registry and the Magic Supremacy asset bundle. They check optional plugin isolation, exact ownership, discovery retry/caching, fallback names, native attack/cost/payload restoration after an explicit replacement, shared prefabs across ObjectDB changes, and empty native attacks remaining eligible after automatic replacement.

The one-handed scenarios also compile the production weapon-family resolver and MagicPlugin cost adapter. They check primary Projectile requirements, one-handed precedence over staff animations, shared cooldown grouping, native preservation, identity-scoped restoration of empty/native attacks across ObjectDBs, and no stale restoration after giving ownership back to another mod. MagicPlugin scenarios read changing configuration entry doubles through the actual reflection adapter and verify Stamina/Eitr/Both costs, absent-plugin behavior, unknown enum values and one-time failure logging.

Reviewed MagicPlugin 2.2.0 DLL SHA-256: `1413E513DDEDEE09AC7980BB00F0E96C7AA9FCF9ABE471AE820011854DFB2F99`. Its public `MagicPlugin.Functions.ConfigSetup` fields `_{flame,ice,lightning}ScepterSource` and `_{flame,ice,lightning}ScepterMagicSourceSecondary` supply live native costs. No MagicPlugin assembly reference or additional event subscription is required. The adapter does not rerun StatsSetup or alter totem cooldown guards.

The supplied Wizardry 1.2.0 DLL has GUID `Therzie.Wizardry`, private static `ItemManager.PrefabManager.prefabs` (`List<GameObject>`) in that assembly, and registers it before the game's ObjectDB lifecycle. Reviewed DLL SHA-256: `FB4F8D27278335CB2840A7E08DFADB4C58914F88C71668960627ECB111CE2D8B`. Reflection is scoped to the plugin instance's assembly and uses no Wizardry compile reference. If that private contract changes, known 1.2.0 staff names remain supported and a warning is emitted.

The existing ObjectDB store can emit CS8600 under .NET 9's nullable annotations; this is separate from the .NET Framework production build.

These are isolated checks, not Unity or Harmony execution. YAML normalization/world application, actual prefab registration order, destroyed-object semantics, custom animation RPCs and multiplayer effects still require game verification. In game, check all five elemental staves, explicit YAML override then `preset: none` and removal of the entry, world re-entry, Surtling/Golem automatic secondary attacks, remote observer animations, and operation with either/both/neither optional mod installed. Existing `StructureRegression` checks cover the real definition compiler's `none` handling and runtime inventory rebinding separately.

For MagicPlugin, additionally check Wand + shield firing, switching the server-synced group to Off/another preset, live Scepter cost changes then YAML restoration, observer projectiles, and host/dedicated-server use. The FireProjectileBurst prefix order preserves MagicPlugin's existing per-call tuning before SecondaryAttacks' custom handler. MagicPlugin itself multiplies attack velocity and subscribes setting handlers on each burst; possible cumulative velocity on repeated Burst attacks is a pre-existing third-party interaction, not fixed by this patch or covered by these isolated tests. Default Barrage uses one immediate burst.
