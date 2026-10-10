namespace Registrar.Core.Model;

// A work-in-progress artifact. Deliberately NOT an AgrDocument: a half-filled artifact
// cannot satisfy the AGR schema, and a model that pretends otherwise poisons everything
// downstream.
//
// It reuses AgrSection and AgrBlock rather than mirroring them, because the sections a
// technician edits are already the right shape and a parallel nullable tree would have
// to be kept in step with the AGR model forever.
//
// Everything derivable is absent by design and computed on conversion: classification,
// layer, audience, review date, package name, output filename, and section titles.
public sealed record AgrDraft
{
    public string? ArtifactType { get; init; }
    public string? DocumentId { get; init; }
    public string? Client { get; init; }
    public string? Engagement { get; init; }

    // Required by the schema, not derivable from config, and unlike preparedBy it has no
    // schema-declared default. So it is a human input like the four above rather than an
    // override.
    public string? Owner { get; init; }

    // Not an override: the schema types context.reviewDate as nullable and explicitly
    // permits null in draft mode ("to be set on Approved"), so null here is a fact a
    // document can genuinely hold, not "no preference, derive one." Every other
    // AgrDraftOverrides field is schema-required and non-null, so for those null
    // unambiguously means "not overridden." ReviewDate cannot share that sentinel: if it
    // lived in Overrides, a document opened with a genuinely absent review date would have
    // one silently computed and written back on every save. AuthoringDefaults.ReviewDate
    // still derives a sensible starting value, but only once, at draft creation, where the
    // caller will read it and assign it here explicitly (draft creation is a GUI
    // responsibility that does not exist in this repo yet); a converted document never
    // invents a review date the draft does not already carry.
    public string? ReviewDate { get; init; }

    // Also not an override, and not derivable from config: a documentation-owner sign-off
    // that permits a review date beyond the category default. It is document content the
    // same way ReviewDate is, consumed by ReviewDateLayer for QA-SF-05. Null and
    // AgrReviewOverride are both schema-legal; carrying it here rather than dropping it is
    // exactly the risk AgrDraft exists to avoid.
    public AgrReviewOverride? ReviewDateOverride { get; init; }

    public string Mode { get; init; } = Registrar.Core.Mode.Draft;

    public IReadOnlyList<AgrSection> Sections { get; init; } = Array.Empty<AgrSection>();
    public IReadOnlyDictionary<string, string?> PlaceholderMap { get; init; }
        = new Dictionary<string, string?>();

    // Document content, not an override and not derivable from config: the artifact's
    // declared dependencies on other artifacts (for example a clause library). Most
    // generable types carry at least one; dropping it silently would understate what the
    // document actually depends on.
    public IReadOnlyList<AgrDependency>? Dependencies { get; init; }

    // Document content, not an override and not derivable from config: the workbook/
    // register references an artifact's findings, risks, or inventory entities were
    // sourced from. No current fixture carries a populated value (always null), which is
    // exactly the condition that let ReviewDateOverride go missing unnoticed; carried here
    // on the same reasoning rather than waiting for a fixture to prove the loss.
    public AgrEvidenceReferences? EvidenceReferences { get; init; }

    // Document content sourced from a readiness workbook or risk register, not derivable
    // from config. Every one of these three is null in every current fixture, the same
    // fixture-invisible shape as ReviewDateOverride and EvidenceReferences, carried on the
    // same reasoning rather than waiting for a fixture to prove the loss.
    public IReadOnlyList<AgrEvidenceItem>? NormalizedFindings { get; init; }
    public IReadOnlyList<AgrEvidenceItem>? NormalizedRisks { get; init; }
    public IReadOnlyList<AgrEvidenceItem>? NormalizedInventoryEntities { get; init; }

    // Document content, not derivable from config: an operator's deliberate tightening of
    // this run's validation policy beyond the artifact type's normal checks. Not an
    // override in the AgrDraftOverrides sense, since there is no derived default it departs
    // from; the derived value is simply absent (Mode is the only validation-policy field
    // config/schema ever supplies).
    public int? WarningThreshold { get; init; }
    public IReadOnlyList<string>? AdditionalRequiredCheckIds { get; init; }

    // Document content, not derivable from config: which optional sections a technician
    // has switched on or off for this run. No current fixture populates it.
    public IReadOnlyDictionary<string, bool>? OptionalSectionToggles { get; init; }

    // Required by the schema and supplied by the application rather than typed by a
    // technician: it correlates an artifact with its logs and its validation report.
    // Null here becomes a gap in Task 3, like any other missing required field.
    public AgrSourceWorkflowContext? SourceWorkflow { get; init; }

    public AgrDraftOverrides? Overrides { get; init; }
}

// The escape hatch. Every field null, the normal case, means "use what config derives".
// It exists so a deliberate departure from a derived value is representable and visible,
// rather than making the derived fields editable in the main model.
public sealed record AgrDraftOverrides
{
    public string? Version { get; init; }
    public string? PreparedBy { get; init; }
    public string? ApprovalStatus { get; init; }
    public string? PackageName { get; init; }
    public string? Classification { get; init; }
    public string? Layer { get; init; }
    public string? Audience { get; init; }
    public string? OutputFileName { get; init; }
}
