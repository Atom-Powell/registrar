using System.Linq;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests.Authoring;

// The value of these tests is not that the strings look right. It is that the pointers
// FieldPath builds are the same pointers the validation layers emit for the same field.
// A layer that changes its spelling breaks a test here rather than quietly breaking rail
// navigation, which nothing else would notice.
public class FieldPathTests
{
    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static AgrDocument DocumentWithBlock(AgrBlock block) => new()
    {
        SchemaVersion = "2.0.0",
        Identity = new AgrIdentity
        {
            ArtifactType = "TCL", DocumentId = "ORG-OPS-TCL-001", Version = "0.1",
            Classification = "Client-Facing", ApprovalStatus = "Draft", Layer = "Delivery"
        },
        Context = new AgrContext
        {
            Owner = "Registry Owner", Audience = "Client", PreparedBy = "ORG",
            Client = null, Engagement = null, ReviewDate = null
        },
        SourceWorkflowContext = new AgrSourceWorkflowContext
        {
            WorkflowName = "test", Phase = "test", ExecutionId = "00000000-0000-0000-0000-000000000001"
        },
        DataInputs = new AgrDataInputs
        {
            PlaceholderMap = new Dictionary<string, string?>(),
            Sections = new[] { new AgrSection { Title = "Questions", DescriptorLine = null, Blocks = new[] { block } } }
        },
        ValidationPolicy = new AgrValidationPolicy { Mode = Mode.Draft },
        RenderingOptions = new AgrRenderingOptions
        {
            PackageName = "test", OutputFileName = "ORG - Technical Control List - v0.1.docx"
        }
    };

    [Fact]
    public void The_radio_layer_reports_at_the_pointer_FieldPath_builds()
    {
        var config = ConfigLoader.Load(RepoRoot());

        // One option short of the configured minimum, so QA-SF-10 reports on options.
        var options = config.RadioPattern.StandardOptionSet
            .Take(config.RadioPattern.MinimumOptions - 1).ToArray();

        var block = new AgrBlock
        {
            BlockType = "radioQuestion",
            Question = new AgrQuestion { Number = 1, Prompt = "Is it in place?", Required = true, Options = options }
        };

        var checks = new RadioQuestionLayer().Validate(
            new SemanticContext(DocumentWithBlock(block), config, Mode.Draft, null));

        var reported = checks.Single(c => c.Result == CheckResultValue.Fail);
        Assert.Equal(FieldPath.BlockField(0, 0, "question/options"), reported.JsonPath);
    }

    [Fact]
    public void The_block_type_layer_reports_at_the_pointer_FieldPath_builds()
    {
        var config = ConfigLoader.Load(RepoRoot());

        var block = new AgrBlock { BlockType = "aggregationTable", AggregationSource = "notASource" };

        var checks = new BlockTypeSupportLayer().Validate(
            new SemanticContext(DocumentWithBlock(block), config, Mode.Draft, null));

        var reported = checks.Single(c => c.Result == CheckResultValue.Fail);
        Assert.Equal(FieldPath.BlockField(0, 0, "aggregationSource"), reported.JsonPath);
    }

    // An id has to be usable in a CSS selector and as an anchor target, so the separator
    // cannot be a slash. Every pointer starts with one, so the prefix keeps the id from
    // beginning with the separator.
    [Fact]
    public void Element_ids_are_derived_from_the_pointer_and_are_selector_safe()
    {
        Assert.Equal("f-dataInputs-sections-2-blocks-0-text",
            FieldPath.ElementId(FieldPath.BlockField(2, 0, "text")));
        Assert.Equal("f-artifactIdentity-documentId", FieldPath.ElementId(FieldPath.DocumentId));
        Assert.DoesNotContain("/", FieldPath.ElementId(FieldPath.Block(1, 1)), StringComparison.Ordinal);
    }

    [Fact]
    public void Distinct_fields_never_collide_on_one_id()
    {
        var ids = new[]
        {
            FieldPath.ElementId(FieldPath.Block(0, 1)),
            FieldPath.ElementId(FieldPath.Block(1, 0)),
            FieldPath.ElementId(FieldPath.BlockField(0, 1, "text")),
            FieldPath.ElementId(FieldPath.SectionBlocks(0)),
            FieldPath.ElementId(FieldPath.Owner)
        };

        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }
}
