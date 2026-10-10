namespace Registrar.Core.Config;

public sealed record ConfigBundle
{
    public required IReadOnlyDictionary<string, ArtifactTypeConfig> ArtifactTypesByCode { get; init; }
    public required IReadOnlyDictionary<string, int> ReviewIntervalMonthsByCategory { get; init; }
    public required IReadOnlySet<string> ClauseIds { get; init; }
    public required IReadOnlyDictionary<string, ClauseEntry> ClausesById { get; init; }
    public required IReadOnlyList<ClauseEntry> Clauses { get; init; }
    public required IReadOnlyList<CanonicalToken> CanonicalTokens { get; init; }
    public required IReadOnlyDictionary<string, string> Hashes { get; init; }
    public required IReadOnlySet<string> ApprovalStatuses { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Transitions { get; init; }
    public required VocabularyConfig Vocabulary { get; init; }
    public required IReadOnlyDictionary<string, string> RepositoryTargetsByCategory { get; init; }
    public required StyleRules Style { get; init; }
    public required ScreenTokens Screen { get; init; }
    public required ContentLint ContentLint { get; init; }

    // contentLint.transportRepairs, already parsed from its "U+XXXX" keys to code points.
    // Parsed once at load so a malformed key fails the config release rather than the
    // import path, and so nothing downstream has to re-parse it per call. Empty when the
    // config declares no repairs.
    public required IReadOnlyDictionary<int, string> TransportRepairs { get; init; }
    public required RadioOptionPattern RadioPattern { get; init; }
    public required WarnCalloutStyle WarnCallout { get; init; }
    public required CopyReadyMessageStyle CopyReadyMessage { get; init; }
    public required QaCatalog Qa { get; init; }
}
