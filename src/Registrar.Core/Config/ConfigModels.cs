namespace Registrar.Core.Config;

public sealed record ArtifactTypesFile
{
    public required IReadOnlyList<ArtifactTypeConfig> ArtifactTypes { get; init; }
}

public sealed record ArtifactTypeConfig
{
    public required string Code { get; init; }
    public required string Category { get; init; }
    // Bindable provenance only; the render gate is SectionSchema.Status == "defined", not this flag.
    public bool GenerationTarget { get; init; }
    public SectionSchemaConfig? SectionSchema { get; init; }
    public IReadOnlyList<string>? RequiredPlaceholders { get; init; }
    public IReadOnlyList<string>? RequiredClauses { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public string? IdPattern { get; init; }
    public string? AudienceDefault { get; init; }
    public string? Classification { get; init; }
    // Framework layer per ADR-002's four-layer model, assigned per artifact type by
    // owner ruling 2026-08-01. Null for types with no defined section schema: they are
    // already refused by QA-ENG-02, so there is nothing to validate a layer against.
    public string? Layer { get; init; }
    // Marks the artifact types the Governor may be asked to produce. Deliberately
    // narrower than "generable": SCL and PDX render end to end but are engine-generated
    // master-pack reference documents produced via the CLI, not client-engagement
    // artifacts a technician asks the Governor to draft. A new artifact type must opt
    // in explicitly; it is never inferred from SectionSchema.Status or any other flag.
    public bool GovernorScope { get; init; }
    // Set on TCL and DQR. Drives the four-option radio rule (QA-SF-10) and the
    // drafter's per-type guidance. Config that nothing read until GUI-2.
    public bool UsesRadioOptionPattern { get; init; }
}

public sealed record SectionSchemaConfig
{
    public required string Status { get; init; }
    public IReadOnlyList<string>? OrderedSections { get; init; }
}

public sealed record ClauseLibraryFile
{
    public required IReadOnlyList<ClauseEntry> Clauses { get; init; }
}

public sealed record ClauseEntry
{
    public required string ClauseId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string CanonicalText { get; init; } = string.Empty;
    public string CanonicalTextSha256 { get; init; } = string.Empty;
    public bool ContainsPlaceholders { get; init; }
}

public sealed record PlaceholderDictionaryFile
{
    public required IReadOnlyList<CanonicalToken> CanonicalTokens { get; init; }
}

public sealed record CanonicalToken
{
    public required string Token { get; init; }
    public IReadOnlyList<string> KnownDriftAliases { get; init; } = Array.Empty<string>();
    public string? Usage { get; init; }
}

// vocabularies.json beyond the approval-status and review-interval slices ConfigLoader
// already parsed. These are closed sets and patterns the Governor must be told about
// exactly, so they are bound rather than restated anywhere.
public sealed record VocabularyConfig
{
    public required string DocumentIdPattern { get; init; }
    public required string VersionPattern { get; init; }
    public required IReadOnlyList<string> Classifications { get; init; }
    public required IReadOnlyList<string> FrameworkLayers { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
    public required IReadOnlyList<string> ProhibitedStatusAliases { get; init; }
    public required string InitialState { get; init; }
    public required string InitialVersion { get; init; }
}
