using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SecondaryAttacks;

internal static class SecondaryAttackConfigLoader
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public static void EnsureLocalFilesExist()
    {
        Directory.CreateDirectory(SecondaryAttackYamlDomainRegistry.ConfigDirectoryPath);
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            if (!File.Exists(domain.FilePath))
            {
                File.WriteAllText(domain.FilePath, domain.GetDefaultContents());
            }
        }
    }

    public static SecondaryAttackYamlTexts ReadLocalYamlTexts()
    {
        Dictionary<SecondaryAttackYamlDomainId, string> texts = new();
        foreach (SecondaryAttackYamlDomain domain in SecondaryAttackYamlDomainRegistry.Domains)
        {
            texts[domain.Id] = File.ReadAllText(domain.FilePath);
        }

        return new SecondaryAttackYamlTexts(texts);
    }

    public static bool TryCompileSnapshot(
        int snapshotId,
        SecondaryAttackYamlTexts yamlTexts,
        out SecondaryAttackCompiledSnapshot? snapshot)
    {
        snapshot = null;
        if (!TryParseYamlTexts(yamlTexts, out SecondaryAttackParsedYaml? parsedYaml))
        {
            return false;
        }

        snapshot = SecondaryAttackConfigCompiler.Compile(snapshotId, parsedYaml!);
        return true;
    }

    private static bool TryParseYamlTexts(SecondaryAttackYamlTexts yamlTexts, out SecondaryAttackParsedYaml? parsedYaml)
    {
        parsedYaml = null;
        if (!TryParseDictionary<RangedWeaponConfig>(
                SecondaryAttackYamlDomainId.Ranged,
                yamlTexts.Get(SecondaryAttackYamlDomainId.Ranged),
                out Dictionary<string, RangedWeaponConfig>? ranged))
        {
            return false;
        }

        if (!TryParseDictionary<MeleeWeaponConfig>(
                SecondaryAttackYamlDomainId.Melee,
                yamlTexts.Get(SecondaryAttackYamlDomainId.Melee),
                out Dictionary<string, MeleeWeaponConfig>? melee))
        {
            return false;
        }

        if (!TryParseDictionary<BloodMagicWeaponConfig>(
                SecondaryAttackYamlDomainId.BloodMagic,
                yamlTexts.Get(SecondaryAttackYamlDomainId.BloodMagic),
                out Dictionary<string, BloodMagicWeaponConfig>? bloodMagic))
        {
            return false;
        }

        parsedYaml = new SecondaryAttackParsedYaml
        {
            Ranged = ranged!,
            Melee = melee!,
            BloodMagic = bloodMagic!,
            Effects = new Dictionary<string, EffectBehaviorConfig>(StringComparer.OrdinalIgnoreCase)
        };
        return true;
    }

    private static bool TryParseDictionary<T>(
        SecondaryAttackYamlDomainId domainId,
        string yamlText,
        out Dictionary<string, T>? parsed)
    {
        parsed = null;
        if (string.IsNullOrWhiteSpace(yamlText))
        {
            parsed = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            return true;
        }

        try
        {
            parsed = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
            Parser parser = new(new StringReader(yamlText));
            if (!TryMoveToRootMapping(parser, out bool hasRootMapping))
            {
                return true;
            }

            if (!hasRootMapping)
            {
                return true;
            }

            SecondaryAttackYamlDomain domain = SecondaryAttackYamlDomainRegistry.Get(domainId);
            while (parser.MoveNext())
            {
                if (parser.Current is Comment)
                {
                    continue;
                }

                if (parser.Current is MappingEnd)
                {
                    break;
                }

                if (parser.Current is not Scalar keyScalar)
                {
                    throw new InvalidDataException("Root keys must be scalar prefab names.");
                }

                string rootKey = keyScalar.Value?.Trim() ?? "";
                if (!TryMoveToNextNode(parser))
                {
                    throw new InvalidDataException($"Root block '{rootKey}' is missing a value.");
                }

                List<ParsingEvent> nodeEvents = CollectCurrentNode(parser);
                if (string.IsNullOrWhiteSpace(rootKey))
                {
                    continue;
                }

                if (!rootKey.Equals("Global", StringComparison.OrdinalIgnoreCase) &&
                    IsRootEntryDisabled(nodeEvents))
                {
                    continue;
                }

                try
                {
                    if (parsed.ContainsKey(rootKey))
                    {
                        SecondaryAttacksPlugin.ModLogger.LogError(
                            $"Failed to parse {domain.FileName}: Duplicate enabled key {rootKey}. Keep only one enabled entry for a prefab; entries with enabled: false are ignored before duplicate checks.");
                        return false;
                    }

                    parsed[rootKey] = DeserializeRootEntry<T>(domain, rootKey, nodeEvents) ?? Activator.CreateInstance<T>();
                }
                catch (Exception entryException)
                {
                    SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {domain.FileName} block '{rootKey}': {entryException.Message}");
                }
            }

            return true;
        }
        catch (Exception exception)
        {
            SecondaryAttackYamlDomain domain = SecondaryAttackYamlDomainRegistry.Get(domainId);
            SecondaryAttacksPlugin.ModLogger.LogError($"Failed to parse {domain.FileName}: {exception.Message}");
            return false;
        }
    }

    private static bool TryMoveToRootMapping(Parser parser, out bool hasRootMapping)
    {
        hasRootMapping = false;
        while (parser.MoveNext())
        {
            if (parser.Current is StreamStart or DocumentStart or Comment)
            {
                continue;
            }

            if (parser.Current is StreamEnd or DocumentEnd)
            {
                return true;
            }

            hasRootMapping = parser.Current is MappingStart;
            return true;
        }

        return true;
    }

    private static bool TryMoveToNextNode(Parser parser)
    {
        while (parser.MoveNext())
        {
            if (parser.Current is not Comment)
            {
                return true;
            }
        }

        return false;
    }

    private static List<ParsingEvent> CollectCurrentNode(Parser parser)
    {
        ParsingEvent current = parser.Current ?? throw new InvalidDataException("YAML parser is not positioned on a node.");
        List<ParsingEvent> events = new() { current };
        if (current is not MappingStart and not SequenceStart)
        {
            return events;
        }

        int depth = 1;
        while (depth > 0 && parser.MoveNext())
        {
            current = parser.Current ?? throw new InvalidDataException("YAML parser produced a null event.");
            events.Add(current);
            if (current is MappingStart or SequenceStart)
            {
                depth++;
            }
            else if (current is MappingEnd or SequenceEnd)
            {
                depth--;
            }
        }

        if (depth != 0)
        {
            throw new InvalidDataException("YAML node ended before its mapping or sequence was closed.");
        }

        return events;
    }

    private static bool IsRootEntryDisabled(IReadOnlyList<ParsingEvent> nodeEvents)
    {
        if (nodeEvents.Count == 0 || nodeEvents[0] is not MappingStart)
        {
            return false;
        }

        int index = 1;
        while (index < nodeEvents.Count)
        {
            if (nodeEvents[index] is Comment)
            {
                index++;
                continue;
            }

            if (nodeEvents[index] is MappingEnd)
            {
                return false;
            }

            if (nodeEvents[index] is not Scalar keyScalar)
            {
                return false;
            }

            string key = keyScalar.Value?.Trim() ?? "";
            index++;
            while (index < nodeEvents.Count && nodeEvents[index] is Comment)
            {
                index++;
            }

            if (index >= nodeEvents.Count)
            {
                return false;
            }

            int valueStartIndex = index;
            int valueEndIndex = GetNodeEndIndex(nodeEvents, valueStartIndex);
            if (key.Equals("enabled", StringComparison.OrdinalIgnoreCase) &&
                nodeEvents[valueStartIndex] is Scalar valueScalar &&
                IsFalseScalar(valueScalar.Value))
            {
                return true;
            }

            index = valueEndIndex;
        }

        return false;
    }

    private static int GetNodeEndIndex(IReadOnlyList<ParsingEvent> nodeEvents, int startIndex)
    {
        if (nodeEvents[startIndex] is not MappingStart and not SequenceStart)
        {
            return startIndex + 1;
        }

        int depth = 1;
        int index = startIndex + 1;
        while (index < nodeEvents.Count && depth > 0)
        {
            if (nodeEvents[index] is MappingStart or SequenceStart)
            {
                depth++;
            }
            else if (nodeEvents[index] is MappingEnd or SequenceEnd)
            {
                depth--;
            }

            index++;
        }

        return index;
    }

    private static bool IsFalseScalar(string? value)
    {
        string normalized = value?.Trim() ?? "";
        return normalized.Equals("false", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("off", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("no", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("0", StringComparison.OrdinalIgnoreCase);
    }

    private static T? DeserializeRootEntry<T>(
        SecondaryAttackYamlDomain domain,
        string rootKey,
        IReadOnlyList<ParsingEvent> nodeEvents)
    {
        YamlNode node = DeserializeYamlNode(nodeEvents);
        return DeserializeRootEntry<T>(domain, rootKey, node);
    }

    private static T? DeserializeRootEntry<T>(SecondaryAttackYamlDomain domain, string rootKey, YamlNode node)
    {
        if (typeof(T) == typeof(MeleeWeaponConfig))
        {
            return (T)(object)DeserializeMeleeWeaponConfig(domain, rootKey, node);
        }

        return DeserializeYamlNode<T>(node);
    }

    private static MeleeWeaponConfig DeserializeMeleeWeaponConfig(SecondaryAttackYamlDomain domain, string rootKey, YamlNode node)
    {
        if (node is not YamlMappingNode mapping)
        {
            return DeserializeYamlNode<MeleeWeaponConfig>(node) ?? new MeleeWeaponConfig();
        }

        MeleeWeaponConfig config = new();
        foreach (KeyValuePair<YamlNode, YamlNode> child in mapping.Children)
        {
            string key = (child.Key as YamlScalarNode)?.Value?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            string blockPath = $"{rootKey}.{key}";
            switch (key)
            {
                case "enabled":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out bool? enabled))
                    {
                        config.Enabled = enabled;
                    }
                    break;
                case "preset":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out string? preset))
                    {
                        config.Preset = preset ?? "";
                    }
                    break;
                case "copyFrom":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out string? copyFrom))
                    {
                        config.CopyFrom = copyFrom ?? "";
                    }
                    break;
                case "animation":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out string? animation))
                    {
                        config.Animation = animation ?? "";
                    }
                    break;
                case "resourceMultiplier":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out float resourceMultiplier))
                    {
                        config.ResourceMultiplier = resourceMultiplier;
                    }
                    break;
                case "outputMultiplier":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out float outputMultiplier))
                    {
                        config.OutputMultiplier = outputMultiplier;
                    }
                    break;
                case "durabilityFactor":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out float? durabilityFactor))
                    {
                        config.DurabilityFactor = durabilityFactor;
                    }
                    break;
                case "sneakAmbush":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out SneakAmbushConfig? sneakAmbush))
                    {
                        config.SneakAmbush = sneakAmbush;
                    }
                    break;
                case "cleavingThrust":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out CleavingThrustConfig? cleavingThrust))
                    {
                        config.CleavingThrust = cleavingThrust;
                    }
                    break;
                case "spearRain":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out MeleeOnProjectileHitConfig? spearRain))
                    {
                        config.SpearRain = spearRain;
                    }
                    break;
                case "impactBurst":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out ImpactBurstConfig? impactBurst))
                    {
                        config.ImpactBurst = impactBurst;
                    }
                    break;
                case "boomerang":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out BoomerangConfig? boomerang))
                    {
                        config.Boomerang = boomerang;
                    }
                    break;
                case "spinningSweep":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out SpinningSweepConfig? spinningSweep))
                    {
                        config.SpinningSweep = spinningSweep;
                    }
                    break;
                case "launchSlam":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out LaunchSlamConfig? launchSlam))
                    {
                        config.LaunchSlam = launchSlam;
                    }
                    break;
                case "knockbackChain":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out KnockbackChainConfig? knockbackChain))
                    {
                        config.KnockbackChain = knockbackChain;
                    }
                    break;
                case "aftershock":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out AftershockConfig? aftershock))
                    {
                        config.Aftershock = aftershock;
                    }
                    break;
                case "riftTrail":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out RiftTrailConfig? riftTrail))
                    {
                        config.RiftTrail = riftTrail;
                    }
                    break;
                case "fractureLine":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out FractureLineConfig? fractureLine))
                    {
                        config.FractureLine = fractureLine;
                    }
                    break;
                case "harvestSweep":
                    if (TryDeserializeBlock(domain, blockPath, child.Value, out HarvestSweepConfig? harvestSweep))
                    {
                        config.HarvestSweep = harvestSweep;
                    }
                    break;
                default:
                    SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {domain.FileName} block '{blockPath}': unknown field.");
                    break;
            }
        }

        return config;
    }

    private static bool TryDeserializeBlock<T>(SecondaryAttackYamlDomain domain, string blockPath, YamlNode node, out T? value)
    {
        try
        {
            value = DeserializeYamlNode<T>(node);
            return true;
        }
        catch (Exception exception)
        {
            value = default;
            SecondaryAttacksPlugin.ModLogger.LogWarning($"Skipping {domain.FileName} block '{blockPath}': {exception.Message}");
            return false;
        }
    }

    private static T? DeserializeYamlNode<T>(YamlNode node)
    {
        using StringWriter writer = new();
        YamlStream stream = new(new YamlDocument(node));
        stream.Save(writer, assignAnchors: false);
        return Deserializer.Deserialize<T>(writer.ToString());
    }

    private static YamlNode DeserializeYamlNode(IReadOnlyList<ParsingEvent> nodeEvents)
    {
        YamlStream stream = new();
        stream.Load(new BufferedYamlParser(nodeEvents));
        return stream.Documents.Count > 0
            ? stream.Documents[0].RootNode
            : new YamlMappingNode();
    }

    private sealed class BufferedYamlParser : IParser
    {
        private readonly List<ParsingEvent> _events;
        private int _index = -1;

        public BufferedYamlParser(IReadOnlyList<ParsingEvent> nodeEvents)
        {
            _events = new List<ParsingEvent>(nodeEvents.Count + 4)
            {
                new StreamStart(),
                new DocumentStart()
            };
            _events.AddRange(nodeEvents);
            _events.Add(new DocumentEnd(isImplicit: true));
            _events.Add(new StreamEnd());
        }

        public ParsingEvent Current => _index >= 0 && _index < _events.Count ? _events[_index] : null!;

        public bool MoveNext()
        {
            if (_index + 1 >= _events.Count)
            {
                return false;
            }

            _index++;
            return true;
        }
    }
}
