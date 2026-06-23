using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;

namespace SecondaryAttacks;

internal enum SecondaryAttackYamlDomainId
{
    Ranged,
    Melee,
    BloodMagic
}

internal sealed class SecondaryAttackYamlDomain
{
    internal SecondaryAttackYamlDomain(
        SecondaryAttackYamlDomainId id,
        string fileName,
        string filePath,
        string syncedIdentifier,
        Func<string> getDefaultContents)
    {
        Id = id;
        FileName = fileName;
        FilePath = filePath;
        SyncedIdentifier = syncedIdentifier;
        GetDefaultContents = getDefaultContents;
    }

    public SecondaryAttackYamlDomainId Id { get; }

    public string FileName { get; }

    public string FilePath { get; }

    public string SyncedIdentifier { get; }

    public Func<string> GetDefaultContents { get; }
}

internal static class SecondaryAttackYamlDomainRegistry
{
    internal const string ConfigDirectoryName = "SecondaryAttacks";
    internal const string RangedYamlFileName = "SecondaryAttacks.Ranged.yml";
    internal const string MeleeYamlFileName = "SecondaryAttacks.Melee.yml";
    internal const string BloodMagicYamlFileName = "SecondaryAttacks.BloodMagic.yml";
    internal const string AnimationReferenceFileName = "SecondaryAttacks_AnimationReferences.txt";
    private const string SyncedRangedYamlIdentifier = "secondary_attack_ranged_yaml";
    private const string SyncedMeleeYamlIdentifier = "secondary_attack_melee_yaml";
    private const string SyncedBloodMagicYamlIdentifier = "secondary_attack_blood_magic_yaml";

    internal const long ReloadDelayTicks = TimeSpan.TicksPerSecond;

    internal static readonly string ConfigDirectoryPath = Path.Combine(Paths.ConfigPath, ConfigDirectoryName);
    internal static readonly string RangedYamlFilePath = Path.Combine(ConfigDirectoryPath, RangedYamlFileName);
    internal static readonly string MeleeYamlFilePath = Path.Combine(ConfigDirectoryPath, MeleeYamlFileName);
    internal static readonly string BloodMagicYamlFilePath = Path.Combine(ConfigDirectoryPath, BloodMagicYamlFileName);
    internal static readonly string AnimationReferenceFilePath = Path.Combine(ConfigDirectoryPath, AnimationReferenceFileName);

    private static readonly SecondaryAttackYamlDomain[] OrderedDomains =
    {
        new(
            SecondaryAttackYamlDomainId.Ranged,
            RangedYamlFileName,
            RangedYamlFilePath,
            SyncedRangedYamlIdentifier,
            () => SecondaryAttackDefaultYamlResources.Load(RangedYamlFileName)),
        new(
            SecondaryAttackYamlDomainId.Melee,
            MeleeYamlFileName,
            MeleeYamlFilePath,
            SyncedMeleeYamlIdentifier,
            () => SecondaryAttackDefaultYamlResources.Load(MeleeYamlFileName)),
        new(
            SecondaryAttackYamlDomainId.BloodMagic,
            BloodMagicYamlFileName,
            BloodMagicYamlFilePath,
            SyncedBloodMagicYamlIdentifier,
            () => SecondaryAttackDefaultYamlResources.Load(BloodMagicYamlFileName)),
    };

    private static readonly Dictionary<SecondaryAttackYamlDomainId, SecondaryAttackYamlDomain> DomainsById =
        OrderedDomains.ToDictionary(domain => domain.Id);

    public static IReadOnlyList<SecondaryAttackYamlDomain> Domains => OrderedDomains;

    public static SecondaryAttackYamlDomain Get(SecondaryAttackYamlDomainId id)
    {
        return DomainsById[id];
    }
}

internal sealed class SecondaryAttackYamlTexts
{
    private readonly Dictionary<SecondaryAttackYamlDomainId, string> _texts;

    public SecondaryAttackYamlTexts(IReadOnlyDictionary<SecondaryAttackYamlDomainId, string> texts)
    {
        _texts = new Dictionary<SecondaryAttackYamlDomainId, string>(texts);
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            _texts.TryAdd(domain.Id, string.Empty);
        }
    }

    public IReadOnlyDictionary<SecondaryAttackYamlDomainId, string> All => _texts;

    public string Get(SecondaryAttackYamlDomainId id)
    {
        return _texts.TryGetValue(id, out string? text) ? text : string.Empty;
    }

    public string GetContentFingerprint()
    {
        StringBuilder builder = new();
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            string text = Get(domain.Id);
            builder.Append((int)domain.Id)
                .Append(':')
                .Append(text.Length)
                .Append(':')
                .Append(text)
                .Append('\n');
        }

        return builder.ToString();
    }
}

internal sealed class SecondaryAttackParsedYaml
{
    public IReadOnlyDictionary<string, RangedWeaponConfig> Ranged { get; set; } =
        new Dictionary<string, RangedWeaponConfig>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, MeleeWeaponConfig> Melee { get; set; } =
        new Dictionary<string, MeleeWeaponConfig>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, BloodMagicWeaponConfig> BloodMagic { get; set; } =
        new Dictionary<string, BloodMagicWeaponConfig>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, EffectBehaviorConfig> Effects { get; set; } =
        new Dictionary<string, EffectBehaviorConfig>(StringComparer.OrdinalIgnoreCase);
}
