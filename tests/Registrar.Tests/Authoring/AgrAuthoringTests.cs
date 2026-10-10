using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests.Authoring;

public class AgrAuthoringTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);
    }

    private static JsonNode Schema()
    {
        var path = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    [Theory]
    [InlineData("DS", "Client-Facing", "Governance", "2027-02-02")]
    [InlineData("AUP", "Client-Facing", "Governance", "2027-02-02")]
    [InlineData("TCL", "Client-Facing", "Delivery", "2027-08-02")]
    [InlineData("RAF", "Controlled Signoff", "Operational", "2027-02-02")]
    [InlineData("PIG", "Internal Working Document", "Administration", "2027-08-02")]
    public void Defaults_derive_from_config(string code, string classification, string layer, string reviewDate)
    {
        var config = SemanticTestSupport.Config();

        var d = AgrAuthoring.Defaults(config.ArtifactTypesByCode[code], config, Schema(), new FixedClock());

        Assert.Equal(classification, d.Classification);
        Assert.Equal(layer, d.Layer);
        Assert.Equal(reviewDate, d.ReviewDate);
        Assert.Equal(config.ArtifactTypesByCode[code].AudienceDefault, d.Audience);
    }

    [Fact]
    public void Defaults_use_the_initial_approval_state_from_config()
    {
        var config = SemanticTestSupport.Config();

        var d = AgrAuthoring.Defaults(config.ArtifactTypesByCode["DS"], config, Schema(), new FixedClock());

        Assert.Equal(config.Vocabulary.InitialState, d.ApprovalStatus);
    }

    [Fact]
    public void Prepared_by_default_comes_from_the_schema()
    {
        var config = SemanticTestSupport.Config();

        var d = AgrAuthoring.Defaults(config.ArtifactTypesByCode["DS"], config, Schema(), new FixedClock());

        Assert.Equal("ORG Consulting, LLC", d.PreparedBy);
    }

    private static AgrDraft MinimalDraft() => new()
    {
        ArtifactType = "DS",
        DocumentId = "ORG-GOV-DS-016",
        Client = "Talewind CPA",
        Engagement = "M365 resident onboarding",
        Owner = "Registry Owner",
        Sections = SemanticTestSupport.Agr("agr-sample-ds-draft.json").DataInputs.Sections,
        PlaceholderMap = SemanticTestSupport.Agr("agr-sample-ds-draft.json").DataInputs.PlaceholderMap,
        SourceWorkflow = new AgrSourceWorkflowContext
        {
            WorkflowName = "Operator app",
            Phase = "Authoring",
            ExecutionId = "test-execution-1"
        }
    };

    [Fact]
    public void A_complete_draft_converts_and_reports_no_gaps()
    {
        var result = AgrAuthoring.ToDocument(MinimalDraft(), SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Empty(result.Gaps);
        Assert.NotNull(result.Document);
        Assert.Equal("DS", result.Document!.Identity.ArtifactType);
        Assert.Equal("Client-Facing", result.Document.Identity.Classification);
        Assert.Equal("Governance", result.Document.Identity.Layer);
    }

    [Theory]
    [InlineData(nameof(AgrDraft.ArtifactType), "/artifactIdentity/artifactType")]
    [InlineData(nameof(AgrDraft.DocumentId), "/artifactIdentity/documentId")]
    [InlineData(nameof(AgrDraft.Owner), "/context/owner")]
    public void A_missing_required_field_produces_a_gap_at_its_json_path(string field, string jsonPath)
    {
        var draft = MinimalDraft();
        draft = field switch
        {
            nameof(AgrDraft.ArtifactType) => draft with { ArtifactType = null },
            nameof(AgrDraft.DocumentId) => draft with { DocumentId = null },
            _ => draft with { Owner = null }
        };

        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Null(result.Document);
        Assert.Contains(result.Gaps, g => g.JsonPath == jsonPath);
        Assert.All(result.Gaps, g => Assert.False(string.IsNullOrWhiteSpace(g.Message)));
    }

    // Finding 2 (whole-branch review): the schema types context.client as
    // ["string", "null"] and documents null as expected for MST and internal-only
    // artifacts (SCL, PDX, PIG). ToDocument must not be stricter than the schema, so a
    // null client (and a null engagement, likewise never schema-required) must convert
    // cleanly rather than surfacing a gap.
    [Fact]
    public void A_null_client_and_engagement_convert_successfully()
    {
        var draft = MinimalDraft() with { Client = null, Engagement = null };

        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Empty(result.Gaps);
        Assert.NotNull(result.Document);
        Assert.Null(result.Document!.Context.Client);
        Assert.Null(result.Document.Context.Engagement);
    }

    // packageName has schema minLength: 1. With both Client and Engagement null, the
    // fallback chain must still land on something non-empty (the artifact type's name).
    [Fact]
    public void A_null_client_and_engagement_still_produce_a_non_empty_package_name()
    {
        var draft = MinimalDraft() with { Client = null, Engagement = null };

        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.NotNull(result.Document);
        Assert.False(string.IsNullOrWhiteSpace(result.Document!.RenderingOptions.PackageName));
    }

    // The output filename is built from only the non-null segments, so a null client
    // still produces a schema-valid filename (no empty or literal-null segment).
    [Fact]
    public void A_null_client_still_produces_an_output_filename_matching_the_schema_pattern()
    {
        var draft = MinimalDraft() with { Client = null, Engagement = null };

        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.NotNull(result.Document);
        var pattern = (string)Schema()["properties"]!["renderingOptions"]!["properties"]!
            ["outputFileName"]!["pattern"]!;
        Assert.Matches(pattern, result.Document!.RenderingOptions.OutputFileName);
        Assert.DoesNotContain("null", result.Document.RenderingOptions.OutputFileName);
    }

    [Fact]
    public void An_unknown_artifact_type_is_a_gap_rather_than_a_crash()
    {
        var draft = MinimalDraft() with { ArtifactType = "NOPE" };

        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Null(result.Document);
        Assert.Contains(result.Gaps, g => g.JsonPath == "/artifactIdentity/artifactType");
    }

    [Fact]
    public void Overrides_win_over_derived_values()
    {
        var draft = MinimalDraft() with
        {
            ReviewDate = "2026-12-01",
            Overrides = new AgrDraftOverrides { Version = "v2.1" }
        };

        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.NotNull(result.Document);
        Assert.Equal("v2.1", result.Document!.Identity.Version);
        // ReviewDate is no longer an override (it moved to AgrDraft directly once null
        // stopped being a safe "not overridden" sentinel for it), so this assertion now
        // proves direct pass-through, not override-wins-over-derived. That precedence
        // case is already covered by the Version assertion above; this one stays to prove
        // a draft-supplied review date reaches the document untouched.
        Assert.Equal("2026-12-01", result.Document.Context.ReviewDate);
    }

    // Sections are matched by title, not by position, so this proves the fix has not
    // broken the ordinary case: a draft whose sections are already titled and ordered
    // exactly like config's still converts cleanly, with titles coming from config.
    [Fact]
    public void Section_titles_and_order_match_config_when_the_draft_is_already_in_order()
    {
        var config = SemanticTestSupport.Config();

        var result = AgrAuthoring.ToDocument(MinimalDraft(), config, Schema(), new FixedClock());

        Assert.NotNull(result.Document);
        Assert.Empty(result.Gaps);
        Assert.Equal(
            config.ArtifactTypesByCode["DS"].SectionSchema!.OrderedSections!,
            result.Document!.DataInputs.Sections.Select(s => s.Title));
    }

    // With nothing overridden, the effective values are config's, tagged as config's.
    [Fact]
    public void Effective_identity_falls_back_to_the_derived_values_and_says_they_are_configs()
    {
        var config = SemanticTestSupport.Config();
        var defaults = AgrAuthoring.Defaults(config.ArtifactTypesByCode["DS"], config, Schema(), new FixedClock());

        var effective = AgrAuthoring.Effective(new AgrDraft { ArtifactType = "DS" }, defaults);

        Assert.Equal(defaults.Classification, effective.Classification.Value);
        Assert.Equal(AgrAuthoring.SourceConfig, effective.Classification.Source);
        Assert.Equal(defaults.Version, effective.Version.Value);
        Assert.Equal(AgrAuthoring.SourceConfig, effective.Version.Source);
        Assert.Equal(defaults.PreparedBy, effective.PreparedBy.Value);
        // preparedBy's derived value is the schema's default, not config's, and the two
        // screens that show it say so.
        Assert.Equal(AgrAuthoring.SourceSchemaDefault, effective.PreparedBy.Source);
    }

    // The case that made this a correctness bug rather than a cosmetic one: a draft
    // converted from an existing AGR carries that document's header values as overrides,
    // ToDocument prefers them, and the editor was showing config's answers instead. An
    // operator looking at an Approved v1.0 artifact was told it was a Draft at v0.1.
    [Fact]
    public void Effective_identity_prefers_what_the_document_carries_and_says_where_it_came_from()
    {
        var config = SemanticTestSupport.Config();
        var defaults = AgrAuthoring.Defaults(config.ArtifactTypesByCode["DS"], config, Schema(), new FixedClock());

        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Overrides = new AgrDraftOverrides
            {
                Version = "1.0",
                ApprovalStatus = "Approved",
                Classification = "Internal Working Document",
                Layer = "Operational",
                Audience = "Internal",
                PreparedBy = "Someone Else"
            }
        };

        var effective = AgrAuthoring.Effective(draft, defaults);

        Assert.Equal("1.0", effective.Version.Value);
        Assert.Equal("Approved", effective.ApprovalStatus.Value);
        Assert.Equal("Internal Working Document", effective.Classification.Value);
        Assert.Equal("Operational", effective.Layer.Value);
        Assert.Equal("Internal", effective.Audience.Value);
        Assert.Equal("Someone Else", effective.PreparedBy.Value);
        Assert.All(
            new[]
            {
                effective.Version, effective.ApprovalStatus, effective.Classification,
                effective.Layer, effective.Audience, effective.PreparedBy
            },
            v => Assert.Equal(AgrAuthoring.SourceDocument, v.Source));

        // The point of the resolution is that it agrees with what will be built.
        Assert.NotEqual(defaults.Version, effective.Version.Value);
    }

    // Resolution must be null-coalescing, exactly like ToDocument's. An override that is
    // present but empty is what the header will carry, so the pane must not quietly show
    // config's value in its place.
    [Fact]
    public void An_override_that_is_present_but_empty_is_still_the_documents_value()
    {
        var config = SemanticTestSupport.Config();
        var defaults = AgrAuthoring.Defaults(config.ArtifactTypesByCode["DS"], config, Schema(), new FixedClock());

        var draft = new AgrDraft { ArtifactType = "DS", Overrides = new AgrDraftOverrides { Audience = "" } };

        var effective = AgrAuthoring.Effective(draft, defaults);

        Assert.Equal("", effective.Audience.Value);
        Assert.Equal(AgrAuthoring.SourceDocument, effective.Audience.Source);
    }

    // A technician who has not started every section yet is the ordinary case, not an
    // edge case. Dropping the last section (its content, not merely its title) must
    // surface as a gap at the position config expects it, and must refuse to hand back
    // a document: the schema's blocks minItems: 1 means a silently empty section is a
    // document the engine itself would reject.
    [Fact]
    public void Fewer_draft_sections_than_config_reports_a_gap_and_produces_no_document()
    {
        var draft = MinimalDraft();
        var missingLastSection = draft.Sections.Take(draft.Sections.Count - 1).ToList();
        var withMissingSection = draft with { Sections = missingLastSection };

        var result = AgrAuthoring.ToDocument(
            withMissingSection, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Null(result.Document);
        Assert.Contains(result.Gaps, g => g.JsonPath == "/dataInputs/sections/7/blocks");
        Assert.All(result.Gaps, g => Assert.False(string.IsNullOrWhiteSpace(g.Message)));
    }

    // A section titled something this artifact type does not have must be reported, not
    // discarded. Losing content silently is worse than refusing to build.
    [Fact]
    public void More_draft_sections_than_config_reports_a_gap_and_does_not_silently_drop_content()
    {
        var draft = MinimalDraft();
        var stray = draft.Sections[0] with { Title = "Whatever The Technician Typed" };
        var withExtraSection = draft with { Sections = draft.Sections.Append(stray).ToList() };

        var result = AgrAuthoring.ToDocument(
            withExtraSection, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Null(result.Document);
        Assert.Contains(result.Gaps, g => g.JsonPath == "/dataInputs/sections"
            && g.Message.Contains("Whatever The Technician Typed"));
    }

    // Only one instance of a declared title can become the document's section, so a draft
    // carrying two must say so. NormalizeSections keeps the second instance rather than
    // deleting it from the operator's file, which is only safe if the loss it would
    // otherwise cause at build time is reported here.
    [Fact]
    public void A_declared_section_title_carried_twice_reports_a_gap_naming_the_title()
    {
        var draft = MinimalDraft();
        var duplicate = draft.Sections[0] with { Blocks = new[] { new AgrBlock { BlockType = "paragraph", Text = "second instance" } } };
        var withDuplicate = draft with { Sections = draft.Sections.Append(duplicate).ToList() };

        var result = AgrAuthoring.ToDocument(
            withDuplicate, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Null(result.Document);
        Assert.Contains(result.Gaps, g => g.JsonPath == FieldPath.Section(0)
            && g.Message.Contains(draft.Sections[0].Title, StringComparison.Ordinal)
            && g.Message.Contains("more than once", StringComparison.Ordinal));
    }

    // The address has to be the duplicated title's own section, not merely some indexed
    // element: a gap the rail cannot resolve is a finding the technician has to go hunting
    // for. Duplicating a later section proves the index is derived rather than always zero.
    [Fact]
    public void The_duplicate_section_gap_addresses_the_section_it_is_about()
    {
        var draft = MinimalDraft();
        var target = draft.Sections[2];
        var duplicate = target with { Blocks = new[] { new AgrBlock { BlockType = "paragraph", Text = "second instance" } } };
        var withDuplicate = draft with { Sections = draft.Sections.Append(duplicate).ToList() };

        var result = AgrAuthoring.ToDocument(
            withDuplicate, SemanticTestSupport.Config(), Schema(), new FixedClock());

        var gap = Assert.Single(result.Gaps, g =>
            g.Message.Contains("more than once", StringComparison.Ordinal));

        Assert.Equal(FieldPath.Section(2), gap.JsonPath);
    }

    // A stray title is deliberately excluded from this fix. It is not one of the artifact
    // type's sections, so it has no index in the built document to point at; the collection
    // pointer is the most precise address that exists for it.
    [Fact]
    public void A_stray_section_gap_stays_on_the_collection_because_it_has_no_element()
    {
        var draft = MinimalDraft();
        var stray = draft.Sections[0] with { Title = "Whatever The Technician Typed" };
        var withStray = draft with { Sections = draft.Sections.Append(stray).ToList() };

        var result = AgrAuthoring.ToDocument(
            withStray, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Contains(result.Gaps, g => g.JsonPath == "/dataInputs/sections"
            && g.Message.Contains("Whatever The Technician Typed", StringComparison.Ordinal));
    }

    // The sharpest failure mode of positional pairing: same section count, different
    // order, so content authored under one heading gets silently relabeled with another.
    // Matching by title must keep each section's content under its own title regardless
    // of the order the technician wrote them in.
    [Fact]
    public void Reordered_draft_sections_keep_their_content_under_their_own_title()
    {
        var draft = MinimalDraft();
        var reorderedSections = draft.Sections.ToList();
        (reorderedSections[0], reorderedSections[1]) = (reorderedSections[1], reorderedSections[0]);
        var reordered = draft with { Sections = reorderedSections };

        var result = AgrAuthoring.ToDocument(reordered, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.NotNull(result.Document);
        Assert.Empty(result.Gaps);

        var purposeText = draft.Sections.Single(s => s.Title == "Purpose").Blocks[0].Text;
        var scopeText = draft.Sections.Single(s => s.Title == "Deployment Scope").Blocks[0].Text;
        Assert.NotEqual(purposeText, scopeText);

        var outputPurpose = result.Document!.DataInputs.Sections.Single(s => s.Title == "Purpose");
        var outputScope = result.Document.DataInputs.Sections.Single(s => s.Title == "Deployment Scope");
        Assert.Equal(purposeText, outputPurpose.Blocks[0].Text);
        Assert.Equal(scopeText, outputScope.Blocks[0].Text);
    }

    [Fact]
    public void The_output_file_name_matches_the_schema_pattern()
    {
        var result = AgrAuthoring.ToDocument(MinimalDraft(), SemanticTestSupport.Config(), Schema(), new FixedClock());

        var pattern = (string)Schema()["properties"]!["renderingOptions"]!["properties"]!
            ["outputFileName"]!["pattern"]!;
        Assert.Matches(pattern, result.Document!.RenderingOptions.OutputFileName);
    }

    [Fact]
    public void The_document_carries_the_schemas_schema_version()
    {
        var result = AgrAuthoring.ToDocument(MinimalDraft(), SemanticTestSupport.Config(), Schema(), new FixedClock());

        var schemaVersion = (string)Schema()["properties"]!["schemaVersion"]!["const"]!;
        Assert.Equal(schemaVersion, result.Document!.SchemaVersion);
    }

    [Fact]
    public void A_null_source_workflow_produces_a_gap_rather_than_a_crash()
    {
        var draft = MinimalDraft() with { SourceWorkflow = null };

        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.Null(result.Document);
        Assert.Contains(result.Gaps, g => g.JsonPath == "/sourceWorkflowContext");
        Assert.All(result.Gaps, g => Assert.False(string.IsNullOrWhiteSpace(g.Message)));
    }

    [Fact]
    public void The_styling_profile_is_emitted_with_the_schemas_const_value()
    {
        var result = AgrAuthoring.ToDocument(MinimalDraft(), SemanticTestSupport.Config(), Schema(), new FixedClock());

        var stylingProfile = (string)Schema()["properties"]!["renderingOptions"]!
            ["properties"]!["stylingProfile"]!["const"]!;
        Assert.Equal(stylingProfile, result.Document!.RenderingOptions.StylingProfile);
    }

    // The strongest test available beyond the brief: a converted document ought to be
    // schema-valid, not merely internally consistent. If ToDocument ever produces
    // something the engine's own SchemaValidationLayer rejects, catching that here is
    // far cheaper than catching it after the GUI hands the document to the CLI.
    [Fact]
    public void A_converted_document_satisfies_the_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var schemaText = File.ReadAllText(schemaPath);
        var schema = IsolatedSchema.FromText(schemaText);
        var schemaRoot = JsonNode.Parse(schemaText)!;
        var layer = new SchemaValidationLayer();

        var result = AgrAuthoring.ToDocument(MinimalDraft(), SemanticTestSupport.Config(), Schema(), new FixedClock());
        Assert.NotNull(result.Document);

        var json = AgrSerializer.ToJson(result.Document!);
        var checks = layer.Validate(new SchemaValidationInput(json, schema, schemaRoot));

        Assert.False(
            checks.Any(c => c.Result == CheckResultValue.Fail),
            string.Join("; ", checks.Where(c => c.Result == CheckResultValue.Fail).Select(c => c.Message)));
    }

    // The test that catches the draft model quietly dropping a field. Every fixture is
    // parsed, reduced to a draft, and rebuilt. Values that disagree with what config
    // derives must survive through Overrides; if a fixture cannot round trip, the draft
    // model is wrong and the fix is the model, not this test.
    [Fact]
    public void Every_parseable_fixture_round_trips_through_a_draft()
    {
        var config = SemanticTestSupport.Config();
        var schema = Schema();
        var clock = new FixedClock();

        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var schemaText = File.ReadAllText(schemaPath);
        var isolatedSchema = IsolatedSchema.FromText(schemaText);
        var schemaRoot = JsonNode.Parse(schemaText)!;
        var schemaLayer = new SchemaValidationLayer();

        // A fixture already invalid against agr.schema.json was never a document the
        // engine would accept in the first place, the same precondition
        // AgrSerializerTests applies before its own round-trip checks, so it is out of
        // scope here rather than a failure to chase.
        bool IsSchemaValid(string text) =>
            !schemaLayer.Validate(new SchemaValidationInput(text, isolatedSchema, schemaRoot))
                .Any(c => c.Result == CheckResultValue.Fail);

        // QA-ENG-02's own rule, not one invented for this test: SectionSchemaLayer requires
        // a document's section titles to equal its artifact type's orderedSections exactly
        // and in order. ToDocument rebuilds sections from that same orderedSections list, so
        // a document whose sections disagree with it was never round-trippable by
        // construction, and the engine would refuse it at validation anyway.
        bool MatchesOrderedSections(AgrDocument agr, ArtifactTypeConfig type) =>
            agr.DataInputs.Sections.Select(s => s.Title)
                .SequenceEqual(type.SectionSchema!.OrderedSections!, StringComparer.Ordinal);

        var checkedCount = 0;
        var skipped = new List<string>();

        foreach (var path in Directory
                     .GetFiles(Path.Combine(SemanticTestSupport.RepoRoot(), "fixtures"), "agr-*.json")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            var text = File.ReadAllText(path);

            if (!IsSchemaValid(text)) { skipped.Add(name); continue; }

            AgrDocument original;
            try { original = AgrParser.Parse(text); }
            catch { skipped.Add(name); continue; }

            // Types with no approved section structure cannot be authored at all, by
            // design (rule 6), so they are out of scope for a round trip.
            if (!config.ArtifactTypesByCode.TryGetValue(original.Identity.ArtifactType, out var type)
                || type.SectionSchema?.OrderedSections is null)
            {
                skipped.Add(name);
                continue;
            }

            if (!MatchesOrderedSections(original, type)) { skipped.Add(name); continue; }

            var draft = AgrAuthoring.FromDocument(original);
            var result = AgrAuthoring.ToDocument(draft, config, schema, clock);

            Assert.True(result.Document is not null,
                $"{name} produced gaps on round trip: "
                    + string.Join("; ", result.Gaps.Select(g => g.JsonPath)));

            var rebuilt = result.Document!;

            // Identity and RenderingOptions are flat records of scalars, so whole-record
            // equality covers every current field and every future one automatically.
            Assert.Equal(original.Identity, rebuilt.Identity);
            Assert.Equal(original.RenderingOptions, rebuilt.RenderingOptions);

            // Context and ValidationPolicy are likewise flat records, so whole-record
            // equality is safe here too and, critically, does not require the test to name
            // each field by hand: a field-by-field list is invisible to a new schema field
            // forever, this is not. (This is what caught reviewDateOverride: it was never
            // asserted before, FromDocument never carried it, and every fixture happens to
            // leave it null, so a field-by-field list would never have named it either.)
            Assert.Equal(original.Context, rebuilt.Context);
            Assert.Equal(original.ValidationPolicy, rebuilt.ValidationPolicy);

            // DataInputs cannot use whole-record equality: Sections is an
            // IReadOnlyList<AgrSection>, and record-generated Equals compares
            // interface-typed collection members by reference, while ToDocument always
            // builds a fresh List<AgrSection>. So Sections is compared element-wise
            // (content equality, order-sensitive) below, and every other DataInputs member
            // is asserted explicitly instead of relying on whole-record equality.
            Assert.Equal(original.DataInputs.PlaceholderMap, rebuilt.DataInputs.PlaceholderMap);
            Assert.Equal(original.DataInputs.NormalizedFindings, rebuilt.DataInputs.NormalizedFindings);
            Assert.Equal(original.DataInputs.NormalizedRisks, rebuilt.DataInputs.NormalizedRisks);
            Assert.Equal(original.DataInputs.NormalizedInventoryEntities, rebuilt.DataInputs.NormalizedInventoryEntities);
            Assert.Equal(original.DataInputs.Sections.Count, rebuilt.DataInputs.Sections.Count);
            for (var i = 0; i < original.DataInputs.Sections.Count; i++)
                Assert.True(original.DataInputs.Sections[i] == rebuilt.DataInputs.Sections[i],
                    $"{name} section {i} changed on round trip");

            // The two top-level members not covered by any of the above. Dependencies is
            // what this pass of the fix actually caught: most generable fixtures declare
            // one, ToDocument never wrote it, and it was never asserted before either.
            // EvidenceReferences never fails here (no fixture carries a populated value),
            // so its own coverage is the constructed
            // Populated_evidence_references_survive_the_round_trip test below, not this
            // loop.
            Assert.Equal(original.Dependencies, rebuilt.Dependencies);
            Assert.Equal(original.EvidenceReferences, rebuilt.EvidenceReferences);

            Assert.Equal(original.SchemaVersion, rebuilt.SchemaVersion);
            Assert.Equal(original.SourceWorkflowContext, rebuilt.SourceWorkflowContext);

            checkedCount++;
        }

        Assert.True(checkedCount > 20,
            $"checked {checkedCount}, skipped {skipped.Count}; expected the bulk of them. Skipped: "
                + string.Join(", ", skipped));
    }

    // The regression guard for promoting ReviewDate out of Overrides: a document whose
    // review date is genuinely null in draft mode (schema-permitted, "to be set on
    // Approved") must come back null, not have one silently computed and written in.
    [Fact]
    public void A_document_with_no_review_date_round_trips_with_it_still_null()
    {
        var original = SemanticTestSupport.Agr("agr-sample-aup-draft.json");
        Assert.Null(original.Context.ReviewDate);

        var draft = AgrAuthoring.FromDocument(original);
        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.NotNull(result.Document);
        Assert.Null(result.Document!.Context.ReviewDate);
    }

    // No fixture in the repo carries a populated reviewDateOverride (every one leaves it
    // null), which is exactly the condition that let it go missing unnoticed for two
    // rounds of this task. The round-trip test can only ever prove null survives as null,
    // so this constructs the populated case directly.
    [Fact]
    public void A_populated_review_date_override_survives_the_round_trip()
    {
        var original = SemanticTestSupport.Agr("agr-sample-ds-draft.json");
        var withOverride = original with
        {
            Context = original.Context with
            {
                ReviewDateOverride = new AgrReviewOverride
                {
                    ApprovedBy = "Documentation Owner",
                    ApprovalDate = "2026-08-02",
                    Justification = "Deferred pending client sign-off."
                }
            }
        };

        var draft = AgrAuthoring.FromDocument(withOverride);
        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.NotNull(result.Document);
        Assert.Equal(withOverride.Context.ReviewDateOverride, result.Document!.Context.ReviewDateOverride);
    }

    // Same reasoning as the review date override above: no fixture carries a populated
    // evidenceReferences (always null), so only a constructed document can prove the
    // field survives rather than being silently dropped the way reviewDateOverride and
    // dependencies were.
    [Fact]
    public void Populated_evidence_references_survive_the_round_trip()
    {
        var original = SemanticTestSupport.Agr("agr-sample-ds-draft.json");
        var withReferences = original with
        {
            EvidenceReferences = new AgrEvidenceReferences
            {
                ReadinessWorkbookRefs = new[] { "REF-001" },
                RiskRegisterRefs = new[] { "RISK-001" },
                AssetInventoryRefs = new[] { "ASSET-001" },
                ApprovedNarrativeInputs = new[] { "NARR-001" }
            }
        };

        var draft = AgrAuthoring.FromDocument(withReferences);
        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.NotNull(result.Document);
        Assert.Equal(withReferences.EvidenceReferences, result.Document!.EvidenceReferences);
    }

    // The class-closing test. ReviewDateOverride, Dependencies, and EvidenceReferences were
    // each fixed only after a hand-written per-field assertion list happened to name them,
    // one at a time, across three rounds - a list can always omit a field no fixture
    // exercises. This instead populates every optional/nullable member of AgrDocument with
    // a distinctive non-null value and asserts the whole document survives FromDocument
    // then ToDocument, so there is no field left for a future assertion list to forget.
    [Fact]
    public void A_fully_populated_document_round_trips_losslessly()
    {
        var populated = SemanticTestSupport.FullyPopulatedAgr();

        var draft = AgrAuthoring.FromDocument(populated);
        var result = AgrAuthoring.ToDocument(draft, SemanticTestSupport.Config(), Schema(), new FixedClock());

        Assert.True(result.Document is not null,
            "produced gaps on round trip: " + string.Join("; ", result.Gaps.Select(g => g.JsonPath)));
        var rebuilt = result.Document!;

        // Whole-record equality wherever no member is a collection ToDocument rebuilds.
        Assert.Equal(populated.Identity, rebuilt.Identity);
        Assert.Equal(populated.Context, rebuilt.Context);
        Assert.Equal(populated.ValidationPolicy, rebuilt.ValidationPolicy);
        Assert.Equal(populated.RenderingOptions, rebuilt.RenderingOptions);
        Assert.Equal(populated.Dependencies, rebuilt.Dependencies);
        Assert.Equal(populated.EvidenceReferences, rebuilt.EvidenceReferences);

        // DataInputs itself still cannot use whole-record equality: Sections is the one
        // member ToDocument always rebuilds into a fresh list, so it is compared
        // element-wise; everything else on DataInputs is asserted explicitly.
        Assert.Equal(populated.DataInputs.PlaceholderMap, rebuilt.DataInputs.PlaceholderMap);
        Assert.Equal(populated.DataInputs.NormalizedFindings, rebuilt.DataInputs.NormalizedFindings);
        Assert.Equal(populated.DataInputs.NormalizedRisks, rebuilt.DataInputs.NormalizedRisks);
        Assert.Equal(populated.DataInputs.NormalizedInventoryEntities, rebuilt.DataInputs.NormalizedInventoryEntities);
        Assert.Equal(populated.DataInputs.Sections.Count, rebuilt.DataInputs.Sections.Count);
        for (var i = 0; i < populated.DataInputs.Sections.Count; i++)
            Assert.True(populated.DataInputs.Sections[i] == rebuilt.DataInputs.Sections[i],
                $"section {i} changed on round trip");

        Assert.Equal(populated.SchemaVersion, rebuilt.SchemaVersion);
        Assert.Equal(populated.SourceWorkflowContext, rebuilt.SourceWorkflowContext);
    }
}
