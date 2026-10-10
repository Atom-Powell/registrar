using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

// Every change the editor can make to a draft, as pure AgrDraft -> AgrDraft functions.
// The app calls these and decides nothing, so every rule about what an edit does is
// testable in the fast suite with no UI.
public static class DraftEdits
{
    // Materializes every section the artifact type declares, in config order, keeping any
    // section the draft carries that the type does not declare at the end.
    //
    // This exists to collapse three indices into one. ToDocument reports a section gap at
    // the config-title index, but emits document sections at their position among the
    // populated ones; those agree only when nothing is missing. After normalizing, the
    // draft's own index is the config-title index, so the pointer an input carries is the
    // pointer a check will report at, with no translation anywhere.
    //
    // A materialized empty section is invisible to ToDocument, which tests the block count
    // rather than the section's presence, so this changes no gap and no check.
    //
    // Nothing is dropped. The editor writes the normalized draft back to State.Draft and
    // autosaves it, so a section this function discarded would be deleted from the
    // operator's file for good. Every section the draft carried survives: the first
    // instance of each declared title fills that title's materialized slot, and everything
    // else (an undeclared title, or a second instance of a declared one) is appended after
    // the declared block in the order it arrived. ToDocument then reports each survivor as
    // an explicit gap naming the title, which is what the technician needs to see.
    public static AgrDraft NormalizeSections(AgrDraft draft, ArtifactTypeConfig type)
    {
        var titles = type.SectionSchema?.OrderedSections;
        if (titles is null) return draft;

        var declared = new HashSet<string>(titles, StringComparer.Ordinal);
        var byTitle = new Dictionary<string, AgrSection>(StringComparer.Ordinal);
        var leftovers = new List<AgrSection>();

        foreach (var section in draft.Sections)
        {
            // TryAdd is what makes this first-wins for the materialized slot: it fills the
            // slot with the first instance and reports false for every later one, which
            // then falls through to the leftovers rather than being lost.
            if (declared.Contains(section.Title) && byTitle.TryAdd(section.Title, section)) continue;
            leftovers.Add(section);
        }

        var ordered = new List<AgrSection>(titles.Count + leftovers.Count);
        foreach (var title in titles)
        {
            ordered.Add(byTitle.TryGetValue(title, out var existing)
                ? existing
                : new AgrSection { Title = title, DescriptorLine = null, Blocks = Array.Empty<AgrBlock>() });
        }

        ordered.AddRange(leftovers);

        return draft with { Sections = ordered };
    }

    private static readonly IReadOnlySet<string> ListBlockTypes =
        new HashSet<string>(StringComparer.Ordinal) { "bulletList" };

    // Which prose block types carry items rather than text. Public because the editor
    // needs the same answer: it decides whether the box it renders edits items or text,
    // and names the pointer field accordingly, and a pointer is what a rail item resolves
    // against. Two owners of that decision would eventually give two answers.
    public static bool IsList(string blockType) => ListBlockTypes.Contains(blockType);

    public static AgrBlock NewProse(string blockType)
        => IsList(blockType)
            ? new AgrBlock { BlockType = blockType, Items = Array.Empty<string>() }
            : new AgrBlock { BlockType = blockType, Text = "" };

    // The number a new question in this section should carry. Questions are numbered
    // within their section, so this counts the radioQuestion blocks already there, not
    // every block: a section holding a descriptor paragraph and three questions gets a
    // fourth question, not a fifth. q.Number is document content, emitted by the renderer
    // and quoted in QA-SF-10's messages, and no validation layer checks the numbering, so
    // a wrong number would ship to the client with nothing to catch it.
    public static int NextQuestionNumber(AgrDraft draft, int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= draft.Sections.Count) return 1;

        return draft.Sections[sectionIndex].Blocks
            .Count(b => string.Equals(b.BlockType, "radioQuestion", StringComparison.Ordinal)) + 1;
    }

    public static AgrBlock NewClause(string clauseId)
        => new() { BlockType = "clauseInsertion", ClauseId = clauseId };

    public static AgrBlock NewQuestion(int number, IReadOnlyList<string> options)
        => new()
        {
            BlockType = "radioQuestion",
            Question = new AgrQuestion { Number = number, Prompt = "", Required = true, Options = options }
        };

    // One line per entry, blank (or whitespace-only) lines dropped, each survivor trimmed
    // of surrounding whitespace including a trailing \r a Windows-authored value carries.
    // The single owner of "multiline text box to list of entries", used by both the
    // bulletList path of SetProse and the radio options editor: it is the same conceptual
    // transform (a technician typing one item per line), so it must not have two answers
    // in two layers. A blank bullet or a blank option would render as an empty entry, and
    // no technician means that.
    public static IReadOnlyList<string> SplitLines(string value)
        => value.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToList();

    // One prose editor serves paragraph, h2, h3, and bulletList, so the difference between
    // a text block and a list block is decided here rather than in markup.
    public static AgrDraft SetProse(AgrDraft draft, int sectionIndex, int blockIndex, string value)
        => MapBlock(draft, sectionIndex, blockIndex, block => IsList(block.BlockType)
            ? block with { Items = SplitLines(value), Text = null }
            : block with { Text = value });

    // Records the id only. Rule 4: the engine inserts canonical text from the clause
    // library and proves it with SHA-256, so a clause block that carried prose would be
    // carrying something the renderer is required to ignore.
    public static AgrDraft SetClause(AgrDraft draft, int sectionIndex, int blockIndex, string clauseId)
        => MapBlock(draft, sectionIndex, blockIndex, block => block with { ClauseId = clauseId, Text = null });

    public static AgrDraft SetQuestionPrompt(AgrDraft draft, int sectionIndex, int blockIndex, string prompt)
        => MapQuestion(draft, sectionIndex, blockIndex, q => q with { Prompt = prompt });

    public static AgrDraft SetQuestionRequired(AgrDraft draft, int sectionIndex, int blockIndex, bool required)
        => MapQuestion(draft, sectionIndex, blockIndex, q => q with { Required = required });

    public static AgrDraft SetQuestionOptions(
        AgrDraft draft, int sectionIndex, int blockIndex, IReadOnlyList<string> options)
        => MapQuestion(draft, sectionIndex, blockIndex, q => q with { Options = options.ToList() });

    public static AgrDraft SetInternalOnly(AgrDraft draft, int sectionIndex, int blockIndex, bool internalOnly)
        => MapBlock(draft, sectionIndex, blockIndex, block => block with { InternalOnly = internalOnly });

    public static AgrDraft AddBlock(AgrDraft draft, int sectionIndex, AgrBlock block)
        => MapBlocks(draft, sectionIndex, blocks => blocks.Append(block).ToList());

    public static AgrDraft RemoveBlock(AgrDraft draft, int sectionIndex, int blockIndex)
        => MapBlocks(draft, sectionIndex, blocks => blocks.Where((_, i) => i != blockIndex).ToList());

    // Off either end is a no-op rather than a throw, so the app can render the buttons
    // unconditionally and hold no condition of its own.
    public static AgrDraft MoveBlock(AgrDraft draft, int sectionIndex, int blockIndex, int delta)
        => MapBlocks(draft, sectionIndex, blocks =>
        {
            var target = blockIndex + delta;
            if (blockIndex < 0 || blockIndex >= blocks.Count || target < 0 || target >= blocks.Count)
                return blocks;

            var moved = blocks.ToList();
            (moved[blockIndex], moved[target]) = (moved[target], moved[blockIndex]);
            return moved;
        });

    private static AgrDraft MapQuestion(
        AgrDraft draft, int sectionIndex, int blockIndex, Func<AgrQuestion, AgrQuestion> map)
        => MapBlock(draft, sectionIndex, blockIndex,
            block => block.Question is null ? block : block with { Question = map(block.Question) });

    private static AgrDraft MapBlock(
        AgrDraft draft, int sectionIndex, int blockIndex, Func<AgrBlock, AgrBlock> map)
        => MapBlocks(draft, sectionIndex, blocks => blockIndex < 0 || blockIndex >= blocks.Count
            ? blocks
            : blocks.Select((block, i) => i == blockIndex ? map(block) : block).ToList());

    private static AgrDraft MapBlocks(
        AgrDraft draft, int sectionIndex, Func<IReadOnlyList<AgrBlock>, IReadOnlyList<AgrBlock>> map)
    {
        if (sectionIndex < 0 || sectionIndex >= draft.Sections.Count) return draft;

        var sections = draft.Sections
            .Select((section, i) => i == sectionIndex ? section with { Blocks = map(section.Blocks) } : section)
            .ToList();

        return draft with { Sections = sections };
    }
}
