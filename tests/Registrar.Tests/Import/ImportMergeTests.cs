using Registrar.Core.Authoring;
using Registrar.Core.Import;
using Registrar.Core.Model;

namespace Registrar.Tests.Import;

// What a pasted reply is allowed to change. Content, and nothing else. The Document ID was
// issued by Registry Owner and chosen by the operator; a chat reply must not move it,
// and CLAUDE.md rule 5 plus the division of labor say the engine never trusts the Governor.
public class ImportMergeTests
{

    private static AgrSection Section(string title, string text) => new()
    {
        Title = title,
        DescriptorLine = null,
        Blocks = new[] { new AgrBlock { BlockType = "paragraph", Text = text } }
    };

    private static AgrDraft Open() => new()
    {
        ArtifactType = "DS",
        DocumentId = "ORG-GOV-DS-016",
        Owner = "Registry Owner",
        Client = "Talewind CPA"
    };

    [Fact]
    public void Sections_and_placeholders_are_applied()
    {
        var config = SemanticTestSupport.Config();
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var imported = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[] { Section(titles[0], "drafted by the Governor") },
            PlaceholderMap = new Dictionary<string, string?> { ["[TOOL / CONNECTOR NAME]"] = "Copilot Chat" }
        };

        var result = ImportMerge.Apply(Open(), imported, type);

        var firstSection = result.Draft.Sections.First(s => s.Title == titles[0]);
        Assert.Equal("drafted by the Governor", firstSection.Blocks.Single().Text);
        Assert.Equal("Copilot Chat", result.Draft.PlaceholderMap["[TOOL / CONNECTOR NAME]"]);
    }

    [Fact]
    public void A_disagreeing_document_id_is_reported_and_not_applied()
    {
        var config = SemanticTestSupport.Config();
        var type = config.ArtifactTypesByCode["DS"];

        var imported = new AgrDraft { ArtifactType = "DS", DocumentId = "ORG-GOV-DS-999" };

        var result = ImportMerge.Apply(Open(), imported, type);

        Assert.Equal("ORG-GOV-DS-016", result.Draft.DocumentId);
        Assert.Contains(result.Findings, f =>
            f.JsonPath == FieldPath.DocumentId && f.Message.Contains("ORG-GOV-DS-999", StringComparison.Ordinal));
    }

    [Fact]
    public void A_disagreeing_artifact_type_is_reported_and_not_applied()
    {
        var config = SemanticTestSupport.Config();
        var type = config.ArtifactTypesByCode["DS"];

        var imported = new AgrDraft { ArtifactType = "AUP" };

        var result = ImportMerge.Apply(Open(), imported, type);

        Assert.Equal("DS", result.Draft.ArtifactType);
        Assert.Contains(result.Findings, f => f.JsonPath == FieldPath.ArtifactType);
    }

    // A reply that simply did not echo a field is not a disagreement. Reporting one would
    // train the operator to ignore the findings list.
    [Fact]
    public void A_field_the_reply_omitted_is_not_a_finding()
    {
        var config = SemanticTestSupport.Config();
        var type = config.ArtifactTypesByCode["DS"];

        var result = ImportMerge.Apply(Open(), new AgrDraft(), type);

        Assert.Empty(result.Findings);
        Assert.Equal("ORG-GOV-DS-016", result.Draft.DocumentId);
        Assert.Equal("Registry Owner", result.Draft.Owner);
    }

    // An echo that matches is not a disagreement either.
    [Fact]
    public void A_field_the_reply_echoed_correctly_is_not_a_finding()
    {
        var config = SemanticTestSupport.Config();
        var type = config.ArtifactTypesByCode["DS"];

        var imported = new AgrDraft { ArtifactType = "DS", DocumentId = "ORG-GOV-DS-016" };

        var result = ImportMerge.Apply(Open(), imported, type);

        Assert.Empty(result.Findings);
    }

    // Regression guard: the merge must use 'open with { ... }' not 'imported with { ... }',
    // so that non-content fields from the open draft survive. A future refactor that
    // inverted this would silently drop ReviewDate, Mode, Dependencies, and other fields
    // the operator set, and every content-checking test would still pass because none of
    // them populate these fields on the Open() helper.
    [Fact]
    public void Non_content_fields_from_the_open_draft_survive_the_merge()
    {
        var config = SemanticTestSupport.Config();
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var openWithFields = new AgrDraft
        {
            ArtifactType = "DS",
            DocumentId = "ORG-GOV-DS-016",
            Owner = "Registry Owner",
            Client = "Talewind CPA",
            ReviewDate = "2026-09-15",
            Mode = Registrar.Core.Mode.Promotion,
            Dependencies = new[]
            {
                new AgrDependency { DependencyId = "ORG-MST-SCL", Kind = "clause-library", Blocking = true }
            }
        };

        var imported = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[] { Section(titles[0], "new content from the Governor") },
            ReviewDate = "2026-10-01",
            Mode = Registrar.Core.Mode.Draft,
            Dependencies = new[]
            {
                new AgrDependency { DependencyId = "ORG-MST-OTHER", Kind = "other", Blocking = false }
            }
        };

        var result = ImportMerge.Apply(openWithFields, imported, type);

        Assert.Equal("2026-09-15", result.Draft.ReviewDate);
        Assert.Equal(Registrar.Core.Mode.Promotion, result.Draft.Mode);
        Assert.NotNull(result.Draft.Dependencies);
        var dep = Assert.Single(result.Draft.Dependencies);
        Assert.Equal("ORG-MST-SCL", dep.DependencyId);
        Assert.Equal("clause-library", dep.Kind);
    }
}
