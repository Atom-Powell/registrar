using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Engagement;
using Registrar.Core.Model;

namespace Registrar.Tests.Authoring;

public class DraftCreationTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static JsonNode Schema()
        => JsonNode.Parse(File.ReadAllText(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json")))!;

    private static AgrDraft Create(string code)
    {
        var config = ConfigLoader.Load(RepoRoot());
        return DraftCreation.New(
            config.ArtifactTypesByCode[code], "ORG-GOV-DS-016", "Talewind CPA", "Q3 governance review",
            "Registry Owner", config, Schema(), new FixedClock(),
            "00000000-0000-0000-0000-000000000001");
    }

    [Theory]
    [InlineData("DS", "2027-02-04")]
    [InlineData("TCL", "2027-08-04")]
    public void Review_date_is_derived_from_the_category_interval(string code, string expected)
        => Assert.Equal(expected, Create(code).ReviewDate);

    [Fact]
    public void The_five_human_inputs_are_carried_and_nothing_else_is_invented()
    {
        var draft = Create("DS");

        Assert.Equal("DS", draft.ArtifactType);
        Assert.Equal("ORG-GOV-DS-016", draft.DocumentId);
        Assert.Equal("Talewind CPA", draft.Client);
        Assert.Equal("Q3 governance review", draft.Engagement);
        Assert.Equal("Registry Owner", draft.Owner);
        Assert.Equal(Mode.Draft, draft.Mode);

        // Derived values are not frozen into the draft. They are computed on conversion,
        // so nothing here departs from config without an explicit override.
        Assert.Null(draft.Overrides);
    }

    [Fact]
    public void A_new_draft_carries_the_workflow_context_the_app_supplies()
    {
        var draft = Create("DS");

        Assert.NotNull(draft.SourceWorkflow);
        Assert.Equal("00000000-0000-0000-0000-000000000001", draft.SourceWorkflow!.ExecutionId);
    }

    [Fact]
    public void A_new_draft_already_has_a_panel_for_every_section()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var titles = config.ArtifactTypesByCode["DS"].SectionSchema!.OrderedSections!;

        Assert.Equal(titles, Create("DS").Sections.Select(s => s.Title).ToList());
    }

    // The only gaps a brand new draft reports are the sections that have no content yet.
    // Anything else would mean creation left a required field unfilled.
    [Fact]
    public void A_new_draft_reports_only_empty_section_gaps()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var gaps = AgrAuthoring.ToDocument(Create("DS"), config, Schema(), new FixedClock()).Gaps;

        Assert.NotEmpty(gaps);
        Assert.All(gaps, g => Assert.EndsWith("has no content yet.", g.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void The_file_name_is_the_document_id_and_the_draft_extension()
        => Assert.Equal("ORG-GOV-DS-016" + DraftFile.Extension, DraftCreation.FileName("ORG-GOV-DS-016"));

    [Fact]
    public void An_issued_document_id_is_accepted()
        => Assert.Null(DraftCreation.DocumentIdRefusal("ORG-GOV-DS-016", Schema()));

    // The containment property. The Document ID becomes a file name inside the engagement
    // folder, and Path.Combine hands the whole path to a second argument that is rooted or
    // that walks upward, so one of these values writes into another client's folder or
    // anywhere on the machine. This is a desktop app inside an MSP network handling client
    // engagement data.
    [Theory]
    [InlineData(@"..\..\OtherClient\ORG-GOV-DS-016")]
    [InlineData("../../OtherClient/ORG-GOV-DS-016")]
    [InlineData(@"C:\Windows\Temp\ORG-GOV-DS-016")]
    [InlineData(@"subfolder\ORG-GOV-DS-016")]
    public void A_document_id_that_names_a_path_is_refused(string documentId)
    {
        var refusal = DraftCreation.DocumentIdRefusal(documentId, Schema());

        Assert.NotNull(refusal);
        Assert.Contains("path", refusal, StringComparison.Ordinal);
    }

    // Refused, never corrected: rule 5 gives the ID Registry the authority to mint IDs, so
    // an engine that trimmed a bad value into a good one would be minting one.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ORG-GOV-DS-16")]
    [InlineData("whatever the technician typed")]
    [InlineData("ORG-GOV-XX-016")]
    public void A_document_id_that_is_not_the_issued_shape_is_refused(string documentId)
        => Assert.NotNull(DraftCreation.DocumentIdRefusal(documentId, Schema()));

    // The last-ditch guard, for a caller that forgets the gate. Failing loudly is the only
    // acceptable alternative to writing outside the engagement folder.
    [Fact]
    public void Building_a_file_name_from_a_path_shaped_id_throws_rather_than_escaping_the_folder()
        => Assert.Throws<ArgumentException>(() => DraftCreation.FileName(@"..\..\Other\ORG-GOV-DS-016"));

    // Rule 3 makes config editable without a rebuild, so the documentId pattern is a value
    // a human can hand-edit between releases. A catastrophically backtracking pattern would
    // otherwise spin inside Regex.IsMatch with the UI thread blocked and nothing on screen.
    // The match timeout turns an unbounded hang into a refusal the operator can act on.
    //
    // This test does not assert elapsed time. It cannot hang without failing the run, which
    // is the property under test, and a wall-clock assertion here would be the same future
    // flake as the one in EditorShellComponentTests.
    [Fact]
    public void A_backtracking_document_id_pattern_is_refused_rather_than_hanging()
    {
        var schema = JsonNode.Parse("""
            {
              "properties": {
                "artifactIdentity": {
                  "properties": {
                    "documentId": { "pattern": "^(a+)+$" }
                  }
                }
              }
            }
            """)!;

        var refusal = DraftCreation.DocumentIdRefusal(new string('a', 40) + "!", schema);

        Assert.NotNull(refusal);
        Assert.Contains("configuration", refusal, StringComparison.OrdinalIgnoreCase);
    }

    // The refusal must not read as the operator's fault. They typed a Document ID somebody
    // else issued; the thing that failed is a config file they did not edit and cannot fix
    // from this screen, so the message has to say which is which.
    [Fact]
    public void The_backtracking_refusal_names_config_rather_than_the_typed_id()
    {
        var schema = JsonNode.Parse("""
            {
              "properties": {
                "artifactIdentity": {
                  "properties": {
                    "documentId": { "pattern": "^(a+)+$" }
                  }
                }
              }
            }
            """)!;

        var refusal = DraftCreation.DocumentIdRefusal(new string('a', 40) + "!", schema);

        Assert.NotNull(refusal);
        Assert.Contains("agr.schema.json", refusal, StringComparison.Ordinal);
    }

    // Rule 6: offering a type the engine will refuse would be a lie the engine then has to
    // tell. Creatable means the section schema is defined, nothing else.
    [Fact]
    public void Only_types_with_a_defined_section_schema_are_creatable()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var creatable = DraftCreation.CreatableTypes(config);

        Assert.NotEmpty(creatable);
        Assert.All(creatable, t => Assert.Equal("defined", t.SectionSchema!.Status));
        Assert.Contains(creatable, t => t.Code == "DS");
        Assert.Equal(creatable.Select(t => t.Code).OrderBy(c => c, StringComparer.Ordinal).ToList(),
            creatable.Select(t => t.Code).ToList());
    }
}
