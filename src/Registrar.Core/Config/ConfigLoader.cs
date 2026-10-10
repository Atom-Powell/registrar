using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;

namespace Registrar.Core.Config;

public static class ConfigLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ConfigBundle Load(string searchBaseDir)
    {
        var artifactTypesText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "artifact-types.json"));
        var vocabulariesText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "vocabularies.json"));
        var clauseLibraryText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "clause-library.json"));
        var placeholderText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "placeholder-dictionary.json"));
        var repositoryTargetsText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "repository-targets.json"));
        var styleRulesText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "style-rules.json"));
        var qaChecklistText = File.ReadAllText(ConfigLocator.FindConfig(searchBaseDir, "qa-checklist.json"));

        var artifactTypesFile = JsonSerializer.Deserialize<ArtifactTypesFile>(artifactTypesText, Options)!;
        var clauseFile = JsonSerializer.Deserialize<ClauseLibraryFile>(clauseLibraryText, Options)!;
        var placeholderFile = JsonSerializer.Deserialize<PlaceholderDictionaryFile>(placeholderText, Options)!;
        var (statuses, transitions) = ParseApprovalStatus(vocabulariesText);
        var styleRoot = JsonNode.Parse(styleRulesText)!;
        var style = styleRoot["visualIdentity"]!.Deserialize<StyleRules>(Options)!;
        var contentLint = styleRoot["contentLint"]!.Deserialize<ContentLint>(Options)!;
        var screen = styleRoot["screen"]!.Deserialize<ScreenTokens>(Options)!;
        var radioPattern = styleRoot["radioOptionPattern"]!.Deserialize<RadioOptionPattern>(Options)!;
        var warnCallout = styleRoot["warnCallout"]!.Deserialize<WarnCalloutStyle>(Options)!;
        var copyReadyMessage = styleRoot["copyReadyMessage"]!.Deserialize<CopyReadyMessageStyle>(Options)!;

        return new ConfigBundle
        {
            ArtifactTypesByCode = artifactTypesFile.ArtifactTypes.ToDictionary(t => t.Code, t => t),
            ReviewIntervalMonthsByCategory = ParseReviewIntervals(vocabulariesText),
            ClauseIds = clauseFile.Clauses.Select(c => c.ClauseId).ToHashSet(),
            ClausesById = clauseFile.Clauses.ToDictionary(c => c.ClauseId, c => c),
            Clauses = clauseFile.Clauses,
            CanonicalTokens = placeholderFile.CanonicalTokens,
            Hashes = new Dictionary<string, string>
            {
                ["artifact-types.json"] = Hashing.Sha256Hex(artifactTypesText),
                ["vocabularies.json"] = Hashing.Sha256Hex(vocabulariesText),
                ["clause-library.json"] = Hashing.Sha256Hex(clauseLibraryText),
                ["placeholder-dictionary.json"] = Hashing.Sha256Hex(placeholderText),
                ["repository-targets.json"] = Hashing.Sha256Hex(repositoryTargetsText),
                ["style-rules.json"] = Hashing.Sha256Hex(styleRulesText)
            },
            ApprovalStatuses = statuses,
            Transitions = transitions,
            Vocabulary = ParseVocabulary(vocabulariesText),
            RepositoryTargetsByCategory = ParseRepositoryTargets(repositoryTargetsText),
            Style = style,
            Screen = screen,
            ContentLint = contentLint,
            // Parsed here, at load, so a config release carrying a malformed "U+XXXX" key
            // is rejected with every other config defect instead of loading cleanly and
            // then throwing inside ImportParser, which is contracted never to throw.
            TransportRepairs = contentLint.ParseTransportRepairs(),
            RadioPattern = radioPattern,
            WarnCallout = warnCallout,
            CopyReadyMessage = copyReadyMessage,
            // qa-checklist.json is the authoritative source for each check's ruleSource and
            // errorCategory (stamped onto checks as the report is assembled). It is intentionally
            // NOT added to Hashes/configVersions: the report's recorded config set is unchanged,
            // so this externalization does not alter any report output.
            Qa = QaCatalog.Parse(qaChecklistText)
        };
    }

    // reviewIntervalMonths mixes a "$comment" string with integer category keys,
    // so a Dictionary<string,int> deserialization would throw. Walk it and keep
    // only the integer entries.
    private static IReadOnlyDictionary<string, int> ParseReviewIntervals(string vocabulariesText)
    {
        var node = JsonNode.Parse(vocabulariesText)!["category"]!["reviewIntervalMonths"]!.AsObject();
        var result = new Dictionary<string, int>();
        foreach (var (key, value) in node)
        {
            if (key.StartsWith('$')) continue;
            if (value is JsonValue v && v.TryGetValue<int>(out var months))
                result[key] = months;
        }
        return result;
    }

    private static IReadOnlyDictionary<string, string> ParseRepositoryTargets(string text)
    {
        var node = JsonNode.Parse(text)!["targetsByCategory"]!.AsObject();
        var result = new Dictionary<string, string>();
        foreach (var (key, value) in node)
            result[key] = (string)value!;
        return result;
    }

    private static (IReadOnlySet<string> Statuses, IReadOnlyDictionary<string, IReadOnlyList<string>> Transitions)
        ParseApprovalStatus(string vocabulariesText)
    {
        var approval = JsonNode.Parse(vocabulariesText)!["approvalStatus"]!;
        var statuses = approval["values"]!.AsArray().Select(n => (string)n!).ToHashSet();

        var transitions = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var (key, node) in approval["transitions"]!.AsObject())
        {
            if (key.StartsWith('$')) continue;
            transitions[key] = node!.AsArray().Select(n => (string)n!).ToList();
        }
        return (statuses, transitions);
    }

    private static VocabularyConfig ParseVocabulary(string vocabulariesText)
    {
        var root = JsonNode.Parse(vocabulariesText)!;
        static IReadOnlyList<string> Strings(JsonNode? node) =>
            node is null ? Array.Empty<string>() : node.AsArray().Select(n => (string)n!).ToList();

        return new VocabularyConfig
        {
            DocumentIdPattern = (string)root["documentId"]!["pattern"]!,
            VersionPattern = (string)root["version"]!["pattern"]!,
            Classifications = Strings(root["classification"]),
            FrameworkLayers = Strings(root["frameworkLayer"]!["values"]),
            Categories = Strings(root["category"]!["values"]),
            ProhibitedStatusAliases = Strings(root["approvalStatus"]!["prohibitedAliases"]),
            InitialState = (string)root["approvalStatus"]!["initialState"]!,
            InitialVersion = (string)root["version"]!["initial"]!
        };
    }
}
