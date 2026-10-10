using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

// Which editor a block gets. Four kinds, three of them editable: the fourth is how an
// unsupported block stays visible and unlosable rather than being hidden or dropped.
public enum BlockEditorKind
{
    Prose,
    Clause,
    Radio,
    ReadOnly
}

public sealed record FormBlock(
    int SectionIndex,
    int BlockIndex,
    string JsonPath,
    BlockEditorKind Kind,
    AgrBlock Block);

public sealed record FormSection(
    int Index,
    string Title,
    string? DescriptorLine,
    string BlocksJsonPath,
    IReadOnlyList<FormBlock> Blocks);

// A block type the editor offers to add, with the word the operator is shown for it.
public sealed record CreatableBlock(string BlockType, string Label);

// The shape of the editor screen, decided here so the app can render it without holding
// a rule. Titles come from config and are never editable: QA-ENG-02 rejects any deviation
// from the artifact type's ordered sections, so a title the technician could change is a
// build failure waiting to happen.
public sealed record EditorForm(IReadOnlyList<FormSection> Sections)
{
    // An ordered list rather than a dictionary literal, because the order is what the add
    // buttons render in and a dictionary does not promise one.
    //
    // Label is what a technician is asked to add. It is deliberately not the block type:
    // "clauseInsertion" and "radioQuestion" are the engine's names for its own data shapes,
    // and an operator thinks in clauses and questions. The two live in one table so a new
    // block type cannot arrive without someone deciding what to call it in front of a
    // technician. Labels are UI vocabulary rather than artifact structure, which is why they
    // are here and not in /config: no governance decision reads them, and QA-ENG-02 and the
    // renderer both key off BlockType, which is unchanged.
    private static readonly (string BlockType, BlockEditorKind Kind, string Label)[] Declared =
    {
        ("paragraph", BlockEditorKind.Prose, "paragraph"),
        ("h2", BlockEditorKind.Prose, "heading"),
        ("h3", BlockEditorKind.Prose, "subheading"),
        ("bulletList", BlockEditorKind.Prose, "list"),
        ("clauseInsertion", BlockEditorKind.Clause, "clause"),
        ("radioQuestion", BlockEditorKind.Radio, "question"),
        ("table", BlockEditorKind.ReadOnly, "table"),
        ("warnCallout", BlockEditorKind.ReadOnly, "warning callout"),
        ("aggregationTable", BlockEditorKind.ReadOnly, "generated table"),
        ("copyReadyMessage", BlockEditorKind.ReadOnly, "copy-ready message")
    };

    private static readonly IReadOnlyDictionary<string, BlockEditorKind> Kinds =
        Declared.ToDictionary(d => d.BlockType, d => d.Kind, StringComparer.Ordinal);

    // What the editor offers to add, derived from what the editor can edit rather than
    // restated. A block type the screen cannot edit must not be creatable through it: the
    // technician would produce something they immediately could not change. Stating the two
    // sets separately let them disagree, which is a UI that hands out dead ends.
    public static IReadOnlyList<CreatableBlock> CreatableBlocks { get; } =
        Declared.Where(d => d.Kind != BlockEditorKind.ReadOnly)
            .Select(d => new CreatableBlock(d.BlockType, d.Label))
            .ToList();

    // Unknown types are read-only rather than an exception. BlockRenderer's default branch
    // is already a guard for future types; this is the same guard on the authoring side.
    public static BlockEditorKind KindOf(string blockType)
        => Kinds.TryGetValue(blockType, out var kind) ? kind : BlockEditorKind.ReadOnly;

    public static EditorForm For(AgrDraft draft, ArtifactTypeConfig type)
    {
        if (type.SectionSchema?.OrderedSections is null)
            return new EditorForm(Array.Empty<FormSection>());

        var normalized = DraftEdits.NormalizeSections(draft, type);

        var sections = new List<FormSection>(normalized.Sections.Count);
        for (var s = 0; s < normalized.Sections.Count; s++)
        {
            var section = normalized.Sections[s];
            var blocks = new List<FormBlock>(section.Blocks.Count);
            for (var b = 0; b < section.Blocks.Count; b++)
            {
                var block = section.Blocks[b];
                blocks.Add(new FormBlock(s, b, FieldPath.Block(s, b), KindOf(block.BlockType), block));
            }

            sections.Add(new FormSection(
                s, section.Title, section.DescriptorLine, FieldPath.SectionBlocks(s), blocks));
        }

        return new EditorForm(sections);
    }
}
