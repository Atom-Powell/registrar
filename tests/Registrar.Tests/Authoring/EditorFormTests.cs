using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Tests.Authoring;

public class EditorFormTests
{
    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static AgrBlock Block(string type) => new() { BlockType = type, Text = "x" };

    [Theory]
    [InlineData("paragraph", BlockEditorKind.Prose)]
    [InlineData("h2", BlockEditorKind.Prose)]
    [InlineData("h3", BlockEditorKind.Prose)]
    [InlineData("bulletList", BlockEditorKind.Prose)]
    [InlineData("clauseInsertion", BlockEditorKind.Clause)]
    [InlineData("radioQuestion", BlockEditorKind.Radio)]
    [InlineData("table", BlockEditorKind.ReadOnly)]
    [InlineData("warnCallout", BlockEditorKind.ReadOnly)]
    [InlineData("aggregationTable", BlockEditorKind.ReadOnly)]
    [InlineData("copyReadyMessage", BlockEditorKind.ReadOnly)]
    public void Every_block_type_maps_to_an_editor_kind(string blockType, BlockEditorKind expected)
        => Assert.Equal(expected, EditorForm.KindOf(blockType));

    // A type the engine does not know must not crash the screen, and must not be treated
    // as editable. Read-only is the fail-closed answer.
    [Fact]
    public void An_unknown_block_type_is_read_only_rather_than_an_exception()
        => Assert.Equal(BlockEditorKind.ReadOnly, EditorForm.KindOf("somethingNewInV3"));

    // The add buttons are derived from this list, so it must contain exactly the types the
    // editor can also edit. A creatable type the editor cannot edit is a block a technician
    // can make and then not change; an editable type missing from here is an editor for
    // something the screen offers no way to produce.
    [Fact]
    public void Creatable_block_types_are_exactly_the_editable_ones()
    {
        var offered = EditorForm.CreatableBlocks.Select(b => b.BlockType).ToList();

        Assert.NotEmpty(offered);
        Assert.All(offered, t => Assert.NotEqual(BlockEditorKind.ReadOnly, EditorForm.KindOf(t)));

        var editable = new[]
        {
            "paragraph", "h2", "h3", "bulletList", "clauseInsertion", "radioQuestion",
            "table", "warnCallout", "aggregationTable", "copyReadyMessage"
        }.Where(t => EditorForm.KindOf(t) != BlockEditorKind.ReadOnly).ToList();

        Assert.Equal(editable, offered);
    }

    // The invariant the whole indexing scheme rests on. Every panel mutation the editor
    // makes is applied to the draft at the FORM's section index (DraftEdits.AddBlock,
    // MoveBlock, SetProse and the rest all take a section index), so panel i and draft
    // section i must be the same section. That holds only if the draft the form was built
    // from is the NORMALIZED draft, which is why Editor.Rebuild assigns the normalized
    // result back to State.Draft rather than letting EditorForm.For normalize a copy and
    // throw it away. This test pins the pairing directly, for a draft that is out of order,
    // missing sections, carrying a stray, and carrying a duplicate all at once.
    [Fact]
    public void Every_panel_index_addresses_the_same_section_in_the_normalized_draft()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        AgrSection Section(string title, string text)
            => new() { Title = title, DescriptorLine = null, Blocks = new[] { new AgrBlock { BlockType = "paragraph", Text = text } } };

        var messy = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                Section(titles[2], "third"),
                Section("Overview", "stray"),
                Section(titles[0], "first"),
                Section(titles[0], "first again")
            }
        };

        var normalized = DraftEdits.NormalizeSections(messy, type);
        var form = EditorForm.For(normalized, type);

        Assert.Equal(normalized.Sections.Count, form.Sections.Count);
        for (var i = 0; i < form.Sections.Count; i++)
        {
            Assert.Equal(normalized.Sections[i].Title, form.Sections[i].Title);
            Assert.Equal(i, form.Sections[i].Index);
        }
    }

    [Fact]
    public void Panels_match_the_artifact_types_ordered_sections()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["AUP"];

        var form = EditorForm.For(new AgrDraft { ArtifactType = "AUP" }, type);

        Assert.Equal(type.SectionSchema!.OrderedSections!, form.Sections.Select(s => s.Title).ToList());
        Assert.All(form.Sections, s => Assert.Empty(s.Blocks));
    }

    [Fact]
    public void A_block_carries_the_pointer_its_own_position_implies()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                new AgrSection { Title = titles[1], DescriptorLine = null, Blocks = new[] { Block("paragraph"), Block("table") } }
            }
        };

        var form = EditorForm.For(draft, type);

        var second = form.Sections[1];
        Assert.Equal(1, second.Index);
        Assert.Equal(FieldPath.SectionBlocks(1), second.BlocksJsonPath);
        Assert.Equal(FieldPath.Block(1, 0), second.Blocks[0].JsonPath);
        Assert.Equal(FieldPath.Block(1, 1), second.Blocks[1].JsonPath);
        Assert.Equal(BlockEditorKind.Prose, second.Blocks[0].Kind);
        Assert.Equal(BlockEditorKind.ReadOnly, second.Blocks[1].Kind);
    }

    // The form is built over the normalized draft, so a section the draft does not carry
    // still gets a panel. Without this the technician has nowhere to author it.
    [Fact]
    public void A_section_the_draft_does_not_carry_still_gets_a_panel()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                new AgrSection { Title = titles[^1], DescriptorLine = null, Blocks = new[] { Block("paragraph") } }
            }
        };

        var form = EditorForm.For(draft, type);

        Assert.Equal(titles.Count, form.Sections.Count);
        Assert.Empty(form.Sections[0].Blocks);
        Assert.Single(form.Sections[^1].Blocks);
    }

    // Rule 6: a type with no approved section structure cannot be authored, so it gets no
    // panels rather than an invented one.
    [Fact]
    public void A_type_with_no_defined_section_schema_produces_no_panels()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var undefined = config.ArtifactTypesByCode.Values
            .First(t => t.SectionSchema is null || t.SectionSchema.Status != "defined");

        var form = EditorForm.For(new AgrDraft { ArtifactType = undefined.Code }, undefined);

        Assert.Empty(form.Sections);
    }

    // The add buttons used to be labelled with the block type itself, so a technician was
    // asked to "Add clauseInsertion" and "Add radioQuestion". Those are the engine's names
    // for its own data shapes, and the operator does not have them: they think in clauses
    // and questions, not in AGR block types.
    //
    // The rule this pins is the shape rather than a fixed list, so a new creatable block
    // type has to bring a label with it instead of falling back to its identifier. A
    // camelCase or numbered identifier is exactly what the defect looked like, and a label
    // is words.
    [Fact]
    public void Every_creatable_block_carries_an_operator_label_rather_than_its_identifier()
    {
        Assert.NotEmpty(EditorForm.CreatableBlocks);

        Assert.All(EditorForm.CreatableBlocks, block =>
        {
            Assert.False(string.IsNullOrWhiteSpace(block.Label));
            Assert.DoesNotContain(block.Label, c => char.IsUpper(c) || char.IsDigit(c));
        });
    }

}
