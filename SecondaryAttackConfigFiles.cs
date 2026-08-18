using System;
using System.Collections.Generic;
using System.Globalization;
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
        Func<string> getDefaultContents)
    {
        Id = id;
        FileName = fileName;
        FilePath = filePath;
        GetDefaultContents = getDefaultContents;
    }

    public SecondaryAttackYamlDomainId Id { get; }

    public string FileName { get; }

    public string FilePath { get; }

    public Func<string> GetDefaultContents { get; }
}

internal static class SecondaryAttackYamlDomainRegistry
{
    internal const string ConfigDirectoryName = "SecondaryAttacks";
    internal const string RangedYamlFileName = "SecondaryAttacks.Ranged.yml";
    internal const string MeleeYamlFileName = "SecondaryAttacks.Melee.yml";
    internal const string BloodMagicYamlFileName = "SecondaryAttacks.BloodMagic.yml";
    internal const string AnimationReferenceFileName = "SecondaryAttacks_AnimationReferences.txt";
    internal const string SyncedYamlEnvelopeIdentifier = "secondary_attack_yaml_envelope";

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
            () => SecondaryAttackDefaultYamlResources.Load(RangedYamlFileName)),
        new(
            SecondaryAttackYamlDomainId.Melee,
            MeleeYamlFileName,
            MeleeYamlFilePath,
            () => SecondaryAttackDefaultYamlResources.Load(MeleeYamlFileName)),
        new(
            SecondaryAttackYamlDomainId.BloodMagic,
            BloodMagicYamlFileName,
            BloodMagicYamlFilePath,
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
        _texts = new Dictionary<SecondaryAttackYamlDomainId, string>();
        foreach (KeyValuePair<SecondaryAttackYamlDomainId, string> pair in texts)
        {
            _texts[pair.Key] = pair.Value;
        }

        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            if (!_texts.ContainsKey(domain.Id))
            {
                _texts[domain.Id] = string.Empty;
            }
        }
    }

    public string Get(SecondaryAttackYamlDomainId id)
    {
        return _texts.TryGetValue(id, out string? text) ? text : string.Empty;
    }

    public string GetContentFingerprint()
    {
        return ToEnvelope();
    }

    internal string ToEnvelope()
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

    internal static bool TryFromEnvelope(string envelope, out SecondaryAttackYamlTexts? yamlTexts)
    {
        yamlTexts = null;
        if (string.IsNullOrEmpty(envelope))
        {
            return false;
        }

        Dictionary<SecondaryAttackYamlDomainId, string> texts = new();
        int offset = 0;
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            string domainPrefix = $"{(int)domain.Id}:";
            if (offset + domainPrefix.Length > envelope.Length ||
                !string.Equals(
                    envelope.Substring(offset, domainPrefix.Length),
                    domainPrefix,
                    StringComparison.Ordinal))
            {
                return false;
            }

            offset += domainPrefix.Length;
            int lengthSeparator = envelope.IndexOf(':', offset);
            if (lengthSeparator < 0 ||
                !int.TryParse(
                    envelope.Substring(offset, lengthSeparator - offset),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int textLength) ||
                textLength < 0)
            {
                return false;
            }

            offset = lengthSeparator + 1;
            if (textLength > envelope.Length - offset)
            {
                return false;
            }

            texts[domain.Id] = envelope.Substring(offset, textLength);
            offset += textLength;
            if (offset >= envelope.Length || envelope[offset] != '\n')
            {
                return false;
            }

            offset++;
        }

        if (offset != envelope.Length)
        {
            return false;
        }

        yamlTexts = new SecondaryAttackYamlTexts(texts);
        return true;
    }
}
