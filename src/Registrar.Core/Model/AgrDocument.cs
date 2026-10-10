using System.Text.Json.Serialization;

namespace Registrar.Core.Model;

public sealed record AgrDocument
{
    [JsonPropertyName("schemaVersion")]         public required string SchemaVersion { get; init; }
    [JsonPropertyName("artifactIdentity")]      public required AgrIdentity Identity { get; init; }
    [JsonPropertyName("context")]               public required AgrContext Context { get; init; }
    [JsonPropertyName("sourceWorkflowContext")] public required AgrSourceWorkflowContext SourceWorkflowContext { get; init; }
    [JsonPropertyName("dataInputs")]            public required AgrDataInputs DataInputs { get; init; }
    [JsonPropertyName("validationPolicy")]      public required AgrValidationPolicy ValidationPolicy { get; init; }
    [JsonPropertyName("renderingOptions")]      public required AgrRenderingOptions RenderingOptions { get; init; }

    // Every fixture carries these two explicitly as null rather than omitting them, so
    // JsonIgnoreCondition.Never overrides AgrSerializer's document-wide WhenWritingNull:
    // without it, a null value here is indistinguishable from an absent property to the
    // schema, but not to a byte-for-byte-lossless round trip.
    [JsonPropertyName("dependencies")]          [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<AgrDependency>? Dependencies { get; init; }
    [JsonPropertyName("evidenceReferences")]    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public AgrEvidenceReferences? EvidenceReferences { get; init; }
}

// Mirrors agr.schema.json's top-level evidenceReferences. Never observed with actual
// values in the current fixture set (always null), so the nested list properties keep
// the document-wide WhenWritingNull default; there is no fixture evidence yet that any
// of them is ever present-but-null rather than simply absent.
public sealed record AgrEvidenceReferences
{
    [JsonPropertyName("readinessWorkbookRefs")]    public IReadOnlyList<string>? ReadinessWorkbookRefs { get; init; }
    [JsonPropertyName("riskRegisterRefs")]         public IReadOnlyList<string>? RiskRegisterRefs { get; init; }
    [JsonPropertyName("assetInventoryRefs")]       public IReadOnlyList<string>? AssetInventoryRefs { get; init; }
    [JsonPropertyName("approvedNarrativeInputs")]  public IReadOnlyList<string>? ApprovedNarrativeInputs { get; init; }
}

// Mirrors $defs.evidenceItem, used by dataInputs.normalizedFindings/normalizedRisks/
// normalizedInventoryEntities. No fixture carries actual items today (the arrays are
// always null), so SourceRef/Severity keep the document-wide null-omission default.
public sealed record AgrEvidenceItem
{
    [JsonPropertyName("id")]        public required string Id { get; init; }
    [JsonPropertyName("summary")]   public required string Summary { get; init; }
    [JsonPropertyName("sourceRef")] public string? SourceRef { get; init; }
    [JsonPropertyName("severity")]  public string? Severity { get; init; }
}

// Mirrors agr.schema.json's top-level sourceWorkflowContext, required so the
// generated artifact, its logs, and its validation report correlate back to the
// Governor session that produced it.
public sealed record AgrSourceWorkflowContext
{
    [JsonPropertyName("workflowName")] public required string WorkflowName { get; init; }
    [JsonPropertyName("phase")]        public required string Phase { get; init; }
    [JsonPropertyName("executionId")]  public required string ExecutionId { get; init; }
}

public sealed record AgrIdentity
{
    [JsonPropertyName("artifactType")]   public required string ArtifactType { get; init; }
    [JsonPropertyName("documentId")]     public required string DocumentId { get; init; }
    [JsonPropertyName("version")]        public required string Version { get; init; }
    [JsonPropertyName("classification")] public required string Classification { get; init; }
    [JsonPropertyName("approvalStatus")] public required string ApprovalStatus { get; init; }
    [JsonPropertyName("layer")]          public required string Layer { get; init; }
}

public sealed record AgrContext
{
    // Every fixture carries reviewDate and reviewDateOverride explicitly as null in
    // draft mode (real values appear at promotion); Never keeps that explicit null
    // through the round trip instead of dropping the property.
    [JsonPropertyName("reviewDate")]         [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? ReviewDate { get; init; }
    [JsonPropertyName("reviewDateOverride")] [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public AgrReviewOverride? ReviewDateOverride { get; init; }
    [JsonPropertyName("owner")]      public required string Owner { get; init; }
    [JsonPropertyName("audience")]   public required string Audience { get; init; }
    [JsonPropertyName("preparedBy")] public required string PreparedBy { get; init; }
    // No fixture exercises client/engagement as null, but CLAUDE.md documents null as
    // the expected shape for MST and internal-only artifacts (e.g. PIG, classified
    // Internal Working Document), so this is a documented case without a fixture yet,
    // not a speculative one. Given the same treatment as reviewDate/reviewDateOverride
    // rather than left inconsistent with them.
    [JsonPropertyName("client")]     [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Client { get; init; }
    [JsonPropertyName("engagement")] [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? Engagement { get; init; }
}

public sealed record AgrReviewOverride
{
    [JsonPropertyName("approvedBy")]   public required string ApprovedBy { get; init; }
    [JsonPropertyName("approvalDate")] public required string ApprovalDate { get; init; }
    [JsonPropertyName("justification")] public required string Justification { get; init; }
}

public sealed record AgrDataInputs
{
    [JsonPropertyName("placeholderMap")] public required IReadOnlyDictionary<string, string?> PlaceholderMap { get; init; }
    [JsonPropertyName("sections")]       public required IReadOnlyList<AgrSection> Sections { get; init; }

    // Every fixture carries all three explicitly as null; no fixture yet exercises a
    // populated array, so item-level nullability comes from the schema/$defs shape
    // alone (AgrEvidenceItem), not from observed data.
    [JsonPropertyName("normalizedFindings")]          [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<AgrEvidenceItem>? NormalizedFindings { get; init; }
    [JsonPropertyName("normalizedRisks")]             [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<AgrEvidenceItem>? NormalizedRisks { get; init; }
    [JsonPropertyName("normalizedInventoryEntities")] [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<AgrEvidenceItem>? NormalizedInventoryEntities { get; init; }
}

public sealed record AgrSection
{
    [JsonPropertyName("title")]          public required string Title { get; init; }
    // Every fixture carries descriptorLine explicitly as null when a section has no
    // intro line, rather than omitting it.
    [JsonPropertyName("descriptorLine")] [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? DescriptorLine { get; init; }
    [JsonPropertyName("blocks")]         public required IReadOnlyList<AgrBlock> Blocks { get; init; }
}

public sealed record AgrBlock
{
    [JsonPropertyName("blockType")]     public required string BlockType { get; init; }
    [JsonPropertyName("clauseId")]      public string? ClauseId { get; init; }
    [JsonPropertyName("aggregationSource")] public string? AggregationSource { get; init; }
    [JsonPropertyName("text")]          public string? Text { get; init; }
    [JsonPropertyName("internalOnly")]  public bool? InternalOnly { get; init; }
    [JsonPropertyName("items")]         public IReadOnlyList<string>? Items { get; init; }
    [JsonPropertyName("columnHeaders")] public IReadOnlyList<string>? ColumnHeaders { get; init; }
    [JsonPropertyName("rows")]          public IReadOnlyList<IReadOnlyList<string>>? Rows { get; init; }
    [JsonPropertyName("question")]      public AgrQuestion? Question { get; init; }
}

// Mirrors $defs.contentBlock.question in agr.schema.json. Required when
// blockType=radioQuestion; the schema cannot enforce that (contentBlock is one
// flat object with no per-blockType if/then), so RadioQuestionLayer does.
public sealed record AgrQuestion
{
    [JsonPropertyName("number")]   public required int Number { get; init; }
    [JsonPropertyName("prompt")]   public required string Prompt { get; init; }
    [JsonPropertyName("required")] public required bool Required { get; init; }
    [JsonPropertyName("options")]  public required IReadOnlyList<string> Options { get; init; }
}

public sealed record AgrValidationPolicy
{
    [JsonPropertyName("mode")]             public required string Mode { get; init; }
    // Every fixture carries both explicitly as null.
    [JsonPropertyName("warningThreshold")] [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int? WarningThreshold { get; init; }
    [JsonPropertyName("additionalRequiredCheckIds")] [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyList<string>? AdditionalRequiredCheckIds { get; init; }
}

public sealed record AgrRenderingOptions
{
    [JsonPropertyName("packageName")]    public required string PackageName { get; init; }
    [JsonPropertyName("outputFileName")] public required string OutputFileName { get; init; }
    // Every fixture carries this explicitly as null.
    [JsonPropertyName("optionalSectionToggles")] [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public IReadOnlyDictionary<string, bool>? OptionalSectionToggles { get; init; }
    // Always a real value ("org-v1.2") in every fixture; never observed null, so the
    // document-wide default is fine, but the property itself was entirely unmodeled
    // before this fix - AgrSerializer dropped it on every single artifact.
    [JsonPropertyName("stylingProfile")]         public string? StylingProfile { get; init; }
}

public sealed record AgrDependency
{
    [JsonPropertyName("dependencyId")] public required string DependencyId { get; init; }
    [JsonPropertyName("kind")]         public required string Kind { get; init; }
    [JsonPropertyName("blocking")]     public required bool Blocking { get; init; }
}
