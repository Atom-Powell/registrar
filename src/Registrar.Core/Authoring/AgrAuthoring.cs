using System.Globalization;
using System.Text.Json.Nodes;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

// Every value the engine can work out for itself, so a technician never has to.
public sealed record AuthoringDefaults(
    string Classification,
    string Layer,
    string Audience,
    string Version,
    string ApprovalStatus,
    string PreparedBy,
    string ReviewDate);

// Where each AuthoringDefaults value comes from, in the words the New artifact review
// panel shows. It lives beside Defaults rather than in that screen's markup because the
// screen reads the values from Core and was spelling their origin from memory: change how
// a default is derived and the panel would confidently state the old source. SectionCount
// is carried here too, though it is not an AuthoringDefaults field, because the same panel
// shows it and the same drift applies to it.
public sealed record AuthoringDefaultSources(
    string Classification,
    string Layer,
    string Audience,
    string Version,
    string ApprovalStatus,
    string PreparedBy,
    string ReviewDate,
    string SectionCount);

// A derived value as it will actually be built, next to where it came from. The source is
// not decoration: an operator looking at "v1.0, Approved" needs to know whether that is
// config's answer for a new artifact of this type or a fact carried by the document in
// front of them, because only one of those is a value they should expect to change when
// config does.
public sealed record ResolvedValue(string Value, string Source);

// The six derived identity values AgrDraftOverrides can displace, resolved. ReviewDate is
// deliberately absent: it is an ordinary editable draft field with its own input, not a
// config default an override displaces, and ToDocument takes it from the draft as it
// stands rather than falling back to the derived one.
public sealed record EffectiveIdentity(
    ResolvedValue Classification,
    ResolvedValue Layer,
    ResolvedValue Audience,
    ResolvedValue Version,
    ResolvedValue ApprovalStatus,
    ResolvedValue PreparedBy);

public static class AgrAuthoring
{
    // The vocabulary the New artifact screen already uses to tag its review panel, so the
    // two screens describe the same provenance the same way.
    public const string SourceConfig = "config";
    public const string SourceSchemaDefault = "schema default";
    public const string SourceDocument = "this document";

    // What ToDocument will actually put in the Document Control Header, which is not what
    // Defaults returns whenever the draft carries an override. AgrAuthoring.FromDocument
    // fills all eight override fields from the source AGR and ToDocument prefers them, so
    // an existing AGR at v1.0, Approved, Internal was being shown to the operator as v0.1,
    // Draft, Client-Facing. In a tool whose purpose is header fidelity, showing a version
    // and an approval status the build will not use is a correctness failure, not a
    // cosmetic one. Resolved here rather than in markup so the fast suite covers it.
    public static EffectiveIdentity Effective(AgrDraft draft, AuthoringDefaults defaults)
    {
        var o = draft.Overrides;

        // Null-coalescing, not IsNullOrEmpty, so this matches ToDocument exactly. An
        // override that is present but empty is what ToDocument would put in the header,
        // and a pane that quietly showed config's value instead would be lying about the
        // build in precisely the case worth catching.
        static ResolvedValue Resolve(string? overridden, string derived, string derivedSource)
            => overridden is null
                ? new ResolvedValue(derived, derivedSource)
                : new ResolvedValue(overridden, SourceDocument);

        return new EffectiveIdentity(
            Classification: Resolve(o?.Classification, defaults.Classification, SourceConfig),
            Layer: Resolve(o?.Layer, defaults.Layer, SourceConfig),
            Audience: Resolve(o?.Audience, defaults.Audience, SourceConfig),
            Version: Resolve(o?.Version, defaults.Version, SourceConfig),
            ApprovalStatus: Resolve(o?.ApprovalStatus, defaults.ApprovalStatus, SourceConfig),
            PreparedBy: Resolve(o?.PreparedBy, defaults.PreparedBy, SourceSchemaDefault));
    }

    // The three origins a default can have. Named constants rather than loose strings so
    // the panel and this file cannot disagree about the spelling of one.
    public const string FromConfig = "config";
    public const string FromSchemaDefault = "schema default";
    public const string FromDerivation = "derived";

    // Kept in the same order as AuthoringDefaults, and directly above the method whose
    // body each entry describes, so a change to a derivation is made inches from the label
    // that claims where it came from.
    public static AuthoringDefaultSources DefaultSources { get; } = new(
        Classification: FromConfig,      // type.Classification, from artifact-types.json
        Layer: FromConfig,               // type.Layer, from artifact-types.json
        Audience: FromConfig,            // type.AudienceDefault, from artifact-types.json
        Version: FromConfig,             // vocabularies.json initialVersion
        ApprovalStatus: FromConfig,      // vocabularies.json initialState
        PreparedBy: FromSchemaDefault,   // agr.schema.json context.preparedBy.default
        ReviewDate: FromDerivation,      // clock plus the category's review interval
        SectionCount: FromConfig);       // type.SectionSchema.OrderedSections

    public static AuthoringDefaults Defaults(
        ArtifactTypeConfig type, ConfigBundle config, JsonNode agrSchema, IClock clock)
    {
        var months = config.ReviewIntervalMonthsByCategory.TryGetValue(type.Category, out var m) ? m : 12;
        var reviewDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime)
            .AddMonths(months)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var preparedBy = (string?)agrSchema["properties"]!["context"]!["properties"]!
            ["preparedBy"]!["default"] ?? "";

        return new AuthoringDefaults(
            Classification: type.Classification ?? "",
            Layer: type.Layer ?? "",
            Audience: type.AudienceDefault ?? "",
            Version: config.Vocabulary.InitialVersion,
            ApprovalStatus: config.Vocabulary.InitialState,
            PreparedBy: preparedBy,
            ReviewDate: reviewDate);
    }

    public static AuthoringResult ToDocument(
        AgrDraft draft, ConfigBundle config, JsonNode agrSchema, IClock clock)
    {
        var gaps = new List<AuthoringGap>();

        void Require(string? value, string jsonPath, string message)
        {
            if (string.IsNullOrWhiteSpace(value)) gaps.Add(new AuthoringGap(jsonPath, message));
        }

        Require(draft.ArtifactType, "/artifactIdentity/artifactType", "Choose an artifact type.");
        Require(draft.DocumentId, "/artifactIdentity/documentId",
            "A Document ID must be issued by Registry Owner before this can be built.");
        // Client is intentionally not required here: the schema types context.client as
        // ["string", "null"] and documents null as expected for MST and internal-only
        // artifacts (SCL, PDX, PIG). No validation layer requires it either, so ToDocument
        // must not be stricter than the engine it feeds. Engagement is likewise document
        // content the schema never requires; a GUI that wants to insist on either for
        // client-facing work can do that in its own form.
        Require(draft.Owner, "/context/owner", "Owner is required, for example the accountable team.");

        if (draft.SourceWorkflow is null)
            gaps.Add(new AuthoringGap("/sourceWorkflowContext",
                "The application must supply a workflow context before this can be built."));

        ArtifactTypeConfig? type = null;
        if (!string.IsNullOrWhiteSpace(draft.ArtifactType)
            && !config.ArtifactTypesByCode.TryGetValue(draft.ArtifactType, out type))
            gaps.Add(new AuthoringGap("/artifactIdentity/artifactType",
                $"'{draft.ArtifactType}' is not an artifact type this engine knows."));

        if (type is not null && type.SectionSchema?.OrderedSections is null)
            gaps.Add(new AuthoringGap("/dataInputs/sections",
                $"'{draft.ArtifactType}' has no approved section structure, so it cannot be generated."));

        if (gaps.Count > 0 || type is null)
            return new AuthoringResult(null, gaps);

        var d = Defaults(type, config, agrSchema, clock);
        var o = draft.Overrides;

        var version = o?.Version ?? d.Version;
        var titles = type.SectionSchema!.OrderedSections!;

        // Section titles are config's to decide, never the draft's. The technician edits
        // the blocks inside a section; the title is fixed by the artifact type, and
        // QA-ENG-02 rejects any deviation. Matched by title (StringComparer.Ordinal), not
        // by position: a technician drafts sections in whatever order suits them, may not
        // have started every section yet, and may have typed a heading that is not one of
        // this artifact type's at all. Positional pairing would silently lose, mislabel, or
        // drop content in each of those cases; matching by title turns every one of them
        // into an explicit gap instead.
        var titleSet = new HashSet<string>(titles, StringComparer.Ordinal);
        foreach (var stray in draft.Sections.Where(s => !titleSet.Contains(s.Title)))
            gaps.Add(new AuthoringGap("/dataInputs/sections",
                $"'{stray.Title}' is not a section of a {type.Name}."));

        // A declared title carried twice is reported too. Only one instance can become the
        // document's section, so the other's blocks would otherwise be dropped from the
        // build with nothing said. Naming the title turns that into something the
        // technician resolves (merge the two, or empty one) instead of a silent loss.
        // NormalizeSections keeps the extra instance in the draft precisely so this gap has
        // something to report; deleting it there would make the loss permanent instead.
        // Addressed at the duplicated title's own section rather than at the collection.
        // The rail focuses the element a gap names, and a collection pointer resolves to
        // nothing, so the technician was told which title was wrong and then left to find
        // it. The index is the title's position in the built document, which is where the
        // surviving instance lands and therefore where the merge has to happen. The stray
        // gap above keeps the collection pointer on purpose: a stray title is not one of
        // this type's sections and has no element in the built document to point at.
        var indexByTitle = titles
            .Select((title, index) => (title, index))
            .ToDictionary(t => t.title, t => t.index, StringComparer.Ordinal);

        foreach (var duplicate in draft.Sections
                     .Where(s => titleSet.Contains(s.Title))
                     .GroupBy(s => s.Title, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
            gaps.Add(new AuthoringGap(FieldPath.Section(indexByTitle[duplicate.Key]),
                $"'{duplicate.Key}' appears more than once. A {type.Name} has one of each section."));

        var byTitle = draft.Sections
            .Where(s => titleSet.Contains(s.Title))
            .GroupBy(s => s.Title, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var sections = new List<AgrSection>();
        for (var i = 0; i < titles.Count; i++)
        {
            var title = titles[i];
            if (byTitle.TryGetValue(title, out var match) && match.Blocks.Count > 0)
            {
                sections.Add(new AgrSection
                {
                    Title = title,
                    DescriptorLine = match.DescriptorLine,
                    Blocks = match.Blocks
                });
            }
            else
            {
                gaps.Add(new AuthoringGap($"/dataInputs/sections/{i}/blocks",
                    $"{title} has no content yet."));
            }
        }

        if (gaps.Count > 0)
            return new AuthoringResult(null, gaps);

        var schemaVersion = (string)agrSchema["properties"]!["schemaVersion"]!["const"]!;
        var stylingProfile = (string)agrSchema["properties"]!["renderingOptions"]!
            ["properties"]!["stylingProfile"]!["const"]!;

        var document = new AgrDocument
        {
            SchemaVersion = schemaVersion,
            Identity = new AgrIdentity
            {
                ArtifactType = draft.ArtifactType!,
                DocumentId = draft.DocumentId!,
                Version = version,
                Classification = o?.Classification ?? d.Classification,
                ApprovalStatus = o?.ApprovalStatus ?? d.ApprovalStatus,
                Layer = o?.Layer ?? d.Layer
            },
            Context = new AgrContext
            {
                Owner = draft.Owner!,
                Audience = o?.Audience ?? d.Audience,
                PreparedBy = o?.PreparedBy ?? d.PreparedBy,
                Client = draft.Client,
                Engagement = draft.Engagement,
                ReviewDate = draft.ReviewDate,
                ReviewDateOverride = draft.ReviewDateOverride
            },
            SourceWorkflowContext = draft.SourceWorkflow!,
            DataInputs = new AgrDataInputs
            {
                PlaceholderMap = draft.PlaceholderMap,
                Sections = sections,
                NormalizedFindings = draft.NormalizedFindings,
                NormalizedRisks = draft.NormalizedRisks,
                NormalizedInventoryEntities = draft.NormalizedInventoryEntities
            },
            Dependencies = draft.Dependencies,
            EvidenceReferences = draft.EvidenceReferences,
            ValidationPolicy = new AgrValidationPolicy
            {
                Mode = draft.Mode,
                WarningThreshold = draft.WarningThreshold,
                AdditionalRequiredCheckIds = draft.AdditionalRequiredCheckIds
            },
            RenderingOptions = new AgrRenderingOptions
            {
                // packageName has schema minLength: 1, so the fallback chain must end in
                // something always non-empty. Engagement is the natural first choice (it is
                // what most technicians would expect in the footer), Client next, then the
                // artifact type's display name, and finally its Code: both are config data
                // and Code is required (never null), so the chain is provably non-null
                // without a null-forgiving operator.
                PackageName = o?.PackageName ?? draft.Engagement ?? draft.Client ?? type.Name ?? type.Code,
                OutputFileName = o?.OutputFileName
                    ?? BuildOutputFileName(type.Name ?? type.Code, draft.Client, version),
                OptionalSectionToggles = draft.OptionalSectionToggles,
                StylingProfile = stylingProfile
            }
        };

        return new AuthoringResult(document, Array.Empty<AuthoringGap>());
    }

    // Builds a generated-instance file name from only the non-null segments, so a null
    // client (schema-permitted for MST and internal-only artifacts) yields a shorter but
    // still schema-valid name, e.g. "ORG - Shared Clause Library - v1.0.docx", rather than
    // a filename with an empty or literal-null client segment. The schema's outputFileName
    // pattern (^ORG - .+ - v[0-9]+\.[0-9]+\.docx$) is satisfied either way.
    private static string BuildOutputFileName(string typeName, string? client, string version)
    {
        var segments = new List<string> { "ORG", typeName };
        if (!string.IsNullOrWhiteSpace(client)) segments.Add(client);
        segments.Add(version);
        return string.Join(" - ", segments) + ".docx";
    }

    // Reduces a finished AGR back to an editable draft. Every value that ToDocument would
    // otherwise derive is carried in Overrides rather than discarded, because an existing
    // document's stored values are facts about that document and must not be silently
    // recomputed when a technician opens it.
    public static AgrDraft FromDocument(AgrDocument agr) => new()
    {
        ArtifactType = agr.Identity.ArtifactType,
        DocumentId = agr.Identity.DocumentId,
        Client = agr.Context.Client,
        Engagement = agr.Context.Engagement,
        Owner = agr.Context.Owner,
        Mode = agr.ValidationPolicy.Mode,
        Sections = agr.DataInputs.Sections,
        PlaceholderMap = agr.DataInputs.PlaceholderMap,
        SourceWorkflow = agr.SourceWorkflowContext,
        ReviewDate = agr.Context.ReviewDate,
        ReviewDateOverride = agr.Context.ReviewDateOverride,
        Dependencies = agr.Dependencies,
        EvidenceReferences = agr.EvidenceReferences,
        NormalizedFindings = agr.DataInputs.NormalizedFindings,
        NormalizedRisks = agr.DataInputs.NormalizedRisks,
        NormalizedInventoryEntities = agr.DataInputs.NormalizedInventoryEntities,
        WarningThreshold = agr.ValidationPolicy.WarningThreshold,
        AdditionalRequiredCheckIds = agr.ValidationPolicy.AdditionalRequiredCheckIds,
        OptionalSectionToggles = agr.RenderingOptions.OptionalSectionToggles,
        Overrides = new AgrDraftOverrides
        {
            Version = agr.Identity.Version,
            Classification = agr.Identity.Classification,
            ApprovalStatus = agr.Identity.ApprovalStatus,
            Layer = agr.Identity.Layer,
            Audience = agr.Context.Audience,
            PreparedBy = agr.Context.PreparedBy,
            PackageName = agr.RenderingOptions.PackageName,
            OutputFileName = agr.RenderingOptions.OutputFileName
        }
    };
}
