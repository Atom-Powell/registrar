using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Tests.Model;

public class AgrParserTests
{
    private static string GoldenText()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        return File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));
    }

    [Fact]
    public void Parse_maps_identity_and_policy()
    {
        var agr = AgrParser.Parse(GoldenText());

        Assert.Equal("DS", agr.Identity.ArtifactType);
        Assert.Equal("ORG-GOV-DS-016", agr.Identity.DocumentId);
        Assert.Equal("draft", agr.ValidationPolicy.Mode);
    }

    [Fact]
    public void Parse_maps_placeholder_map_and_sections()
    {
        var agr = AgrParser.Parse(GoldenText());

        Assert.True(agr.DataInputs.PlaceholderMap.ContainsKey("[TOOL / CONNECTOR NAME]"));
        Assert.NotEmpty(agr.DataInputs.Sections);
        Assert.All(agr.DataInputs.Sections, s => Assert.NotEmpty(s.Blocks));
    }

    [Fact]
    public void Parses_block_body_fields_and_descriptor_line()
    {
        const string json = """
        {
          "schemaVersion": "1.0.0",
          "artifactIdentity": { "artifactType": "DS", "documentId": "ORG-GOV-DS-016",
            "version": "v1.0", "classification": "Client-Facing", "approvalStatus": "Draft", "layer": "Governance" },
          "context": { "owner": "T", "audience": "A", "preparedBy": "P" },
          "sourceWorkflowContext": { "workflowName": "W", "phase": "P", "executionId": "E" },
          "dataInputs": { "placeholderMap": {}, "sections": [
            { "title": "Purpose", "descriptorLine": "An intro line", "blocks": [
              { "blockType": "paragraph", "text": "Body text." },
              { "blockType": "bulletList", "items": ["one", "two"] },
              { "blockType": "table", "columnHeaders": ["A", "B"], "rows": [["1", "2"]] }
            ] }
          ] },
          "renderingOptions": { "packageName": "Pkg", "outputFileName": "ORG - X - v1.0.docx" },
          "validationPolicy": { "mode": "draft" }
        }
        """;
        var agr = Registrar.Core.Model.AgrParser.Parse(json);
        var section = agr.DataInputs.Sections[0];
        Assert.Equal("An intro line", section.DescriptorLine);
        Assert.Equal("Body text.", section.Blocks[0].Text);
        Assert.Equal(new[] { "one", "two" }, section.Blocks[1].Items);
        Assert.Equal("A", section.Blocks[2].ColumnHeaders![0]);
        Assert.Equal("2", section.Blocks[2].Rows![0][1]);
    }

    [Fact]
    public void Parses_radio_question_block()
    {
        const string json = """
        {
          "blockType": "radioQuestion",
          "question": {
            "number": 3,
            "prompt": "Did the user complete the prompting walkthrough?",
            "required": true,
            "options": ["Yes, reviewed and acknowledged",
                        "Yes, partial review, follow-up scheduled",
                        "No, blocked by access or timing",
                        "Other or Not applicable"]
          }
        }
        """;

        var block = System.Text.Json.JsonSerializer.Deserialize<Registrar.Core.Model.AgrBlock>(
            json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        Assert.Equal("radioQuestion", block.BlockType);
        Assert.NotNull(block.Question);
        Assert.Equal(3, block.Question!.Number);
        Assert.True(block.Question.Required);
        Assert.Equal(4, block.Question.Options.Count);
        Assert.Equal("Did the user complete the prompting walkthrough?", block.Question.Prompt);
    }

    [Fact]
    public void Parses_the_dependencies_array()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-aup-draft.json");
        var agr = AgrParser.Parse(text);

        Assert.NotNull(agr.Dependencies);
        var dep = Assert.Single(agr.Dependencies!);
        Assert.Equal("ORG-MST-SCL", dep.DependencyId);
        Assert.Equal("clause-library", dep.Kind);
        Assert.True(dep.Blocking);
    }

    [Fact]
    public void Dependencies_is_null_when_absent()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-aup-draft.json")
            .Replace("\"dependencies\":", "\"dependenciesRenamedAway\":");
        var agr = AgrParser.Parse(text);

        Assert.Null(agr.Dependencies);
    }
}
