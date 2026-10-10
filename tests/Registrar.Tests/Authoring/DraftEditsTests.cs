using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Tests.Authoring;

public class DraftEditsTests
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

    private static AgrSection Section(string title, params AgrBlock[] blocks)
        => new() { Title = title, DescriptorLine = null, Blocks = blocks };

    private static AgrBlock Paragraph(string text)
        => new() { BlockType = "paragraph", Text = text };

    [Fact]
    public void Normalizing_materializes_every_config_section_in_config_order()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        // Only the last section has content, and it is the only one present.
        var draft = new AgrDraft { ArtifactType = "DS", Sections = new[] { Section(titles[^1], Paragraph("done")) } };

        var normalized = DraftEdits.NormalizeSections(draft, type);

        Assert.Equal(titles, normalized.Sections.Select(s => s.Title).ToList());
        Assert.Equal("done", normalized.Sections[^1].Blocks.Single().Text);
        Assert.All(normalized.Sections.Take(titles.Count - 1), s => Assert.Empty(s.Blocks));
    }

    [Fact]
    public void Normalizing_keeps_a_stray_section_rather_than_dropping_it()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[] { Section("Not A Real Section", Paragraph("keep me")), Section(titles[0], Paragraph("first")) }
        };

        var normalized = DraftEdits.NormalizeSections(draft, type);

        Assert.Equal(titles.Count + 1, normalized.Sections.Count);
        Assert.Equal(titles, normalized.Sections.Take(titles.Count).Select(s => s.Title).ToList());
        Assert.Equal("Not A Real Section", normalized.Sections[^1].Title);
        Assert.Equal("keep me", normalized.Sections[^1].Blocks.Single().Text);
    }

    [Fact]
    public void Normalizing_preserves_a_descriptor_line_a_section_already_carries()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                new AgrSection { Title = titles[0], DescriptorLine = "Intro line.", Blocks = new[] { Paragraph("a") } }
            }
        };

        var normalized = DraftEdits.NormalizeSections(draft, type);

        Assert.Equal("Intro line.", normalized.Sections[0].DescriptorLine);
    }

    // The whole point of normalizing: after it, the index a technician sees, the index the
    // pointer carries, and the index the converted document uses are the same number.
    [Fact]
    public void After_normalizing_a_complete_draft_indices_agree_with_the_converted_document()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = DraftEdits.NormalizeSections(
            new AgrDraft
            {
                ArtifactType = "DS",
                DocumentId = "ORG-GOV-DS-016",
                Owner = "Registry Owner",
                SourceWorkflow = new AgrSourceWorkflowContext
                {
                    WorkflowName = "gui", Phase = "authoring",
                    ExecutionId = "00000000-0000-0000-0000-000000000001"
                },
                Sections = titles.Select(t => Section(t, Paragraph("content for " + t))).ToList()
            },
            type);

        var result = AgrAuthoring.ToDocument(draft, config, Schema(), new FixedClock());

        Assert.Empty(result.Gaps);
        Assert.NotNull(result.Document);
        Assert.Equal(
            draft.Sections.Select(s => s.Title).ToList(),
            result.Document!.DataInputs.Sections.Select(s => s.Title).ToList());
    }

    // Normalizing must not change what ToDocument reports about section gaps. An empty
    // section that was materialized reports the same gap as one that was simply absent.
    // This test provides all required top-level fields so ToDocument executes the
    // per-section loop rather than short-circuiting, and verifies that the section
    // gap list is identical before and after normalization.
    [Fact]
    public void Materializing_an_empty_section_does_not_change_the_gap_it_reports()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        // Bare draft with only some sections populated (leaving gaps), but with valid
        // top-level fields so ToDocument executes the per-section loop.
        var bare = new AgrDraft
        {
            ArtifactType = "DS",
            DocumentId = "ORG-GOV-DS-017",
            Owner = "Registry Owner",
            SourceWorkflow = new AgrSourceWorkflowContext
            {
                WorkflowName = "gui", Phase = "authoring",
                ExecutionId = "00000000-0000-0000-0000-000000000002"
            },
            // Only populate the first and last sections; middle sections are absent.
            Sections = new[] { Section(titles[0], Paragraph("first")), Section(titles[^1], Paragraph("last")) }
        };

        var normalized = DraftEdits.NormalizeSections(bare, type);

        var before = AgrAuthoring.ToDocument(bare, config, Schema(), new FixedClock()).Gaps;
        var after = AgrAuthoring.ToDocument(normalized, config, Schema(), new FixedClock()).Gaps;

        // Gaps should be identical: both identify the same empty sections by their
        // config-title index. Normalization materializes those empty sections but does
        // not change the gap list because ToDocument tests block count, not presence.
        var beforeGapKeys = before.Select(g => g.JsonPath + "|" + g.Message).ToList();
        var afterGapKeys = after.Select(g => g.JsonPath + "|" + g.Message).ToList();

        Assert.Equal(beforeGapKeys, afterGapKeys);
    }

    // When a draft carries the same declared section title twice, the first occurrence
    // fills that title's materialized slot and the second survives as a stray at the end.
    // The editor writes the normalized draft back and autosaves it, so a dropped duplicate
    // would be deleted from the operator's file for good. Keeping it costs a stray section
    // and an explicit ToDocument gap naming the title, which is the outcome the technician
    // can act on.
    [Fact]
    public void Normalizing_keeps_a_duplicate_declared_section_as_a_stray()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        // Draft with the first declared section appearing twice.
        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                Section(titles[0], Paragraph("first instance")),
                Section(titles[0], Paragraph("second instance"))
            }
        };

        var normalized = DraftEdits.NormalizeSections(draft, type);

        // The materialized section at index 0 carries the first instance's content.
        Assert.Equal("first instance", normalized.Sections[0].Blocks.Single().Text);
        // The second instance is appended after the declared sections, content intact.
        Assert.Equal(titles.Count + 1, normalized.Sections.Count);
        Assert.Equal(titles[0], normalized.Sections[^1].Title);
        Assert.Equal("second instance", normalized.Sections[^1].Blocks.Single().Text);
    }

    // Idempotence is what makes normalizing on every editor rebuild free. A duplicate kept
    // as a stray is the case that could break it: run twice, the stray must not multiply
    // and must not be promoted back into the declared slot it lost.
    [Fact]
    public void Normalizing_a_draft_that_carries_a_duplicate_twice_changes_nothing_the_second_time()
    {
        var config = ConfigLoader.Load(RepoRoot());
        var type = config.ArtifactTypesByCode["DS"];
        var titles = type.SectionSchema!.OrderedSections!;

        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                Section(titles[0], Paragraph("first instance")),
                Section(titles[0], Paragraph("second instance"))
            }
        };

        var once = DraftEdits.NormalizeSections(draft, type);
        var twice = DraftEdits.NormalizeSections(once, type);

        Assert.Equal(
            once.Sections.Select(s => (s.Title, string.Join("|", s.Blocks.Select(b => b.Text)))).ToList(),
            twice.Sections.Select(s => (s.Title, string.Join("|", s.Blocks.Select(b => b.Text)))).ToList());
    }

    private static AgrDraft OneSection(params AgrBlock[] blocks)
        => new() { ArtifactType = "DS", Sections = new[] { Section("Purpose and Scope", blocks) } };

    [Fact]
    public void Setting_prose_writes_text_for_a_paragraph()
    {
        var edited = DraftEdits.SetProse(OneSection(Paragraph("old")), 0, 0, "new");

        Assert.Equal("new", edited.Sections[0].Blocks[0].Text);
        Assert.Null(edited.Sections[0].Blocks[0].Items);
    }

    // A bulletList carries items, not text. One editor serves both shapes, so the split is
    // decided here rather than in markup: one line per item, blank lines dropped.
    [Fact]
    public void Setting_prose_writes_items_for_a_bullet_list()
    {
        var list = new AgrBlock { BlockType = "bulletList", Items = new[] { "old" } };

        var edited = DraftEdits.SetProse(OneSection(list), 0, 0, "first\n\nsecond\n");

        Assert.Equal(new[] { "first", "second" }, edited.Sections[0].Blocks[0].Items);
        Assert.Null(edited.Sections[0].Blocks[0].Text);
    }

    // The shared multiline-to-list transform SetProse's bulletList path and the radio
    // options editor both use. One owner, one answer: blank lines dropped, each survivor
    // trimmed of surrounding whitespace (which also removes a trailing \r).
    [Fact]
    public void SplitLines_drops_blank_lines()
    {
        Assert.Equal(new[] { "first", "second" }, DraftEdits.SplitLines("first\n\nsecond\n"));
    }

    [Fact]
    public void SplitLines_drops_whitespace_only_lines()
    {
        Assert.Equal(new[] { "first", "second" }, DraftEdits.SplitLines("first\n   \nsecond\n\t\n"));
    }

    [Fact]
    public void SplitLines_trims_a_windows_line_ending_carriage_return()
    {
        Assert.Equal(new[] { "first", "second" }, DraftEdits.SplitLines("first\r\nsecond\r\n"));
    }

    [Fact]
    public void SplitLines_trims_surrounding_whitespace_on_each_surviving_line()
    {
        Assert.Equal(new[] { "first", "second" }, DraftEdits.SplitLines("  first  \n second "));
    }

    [Fact]
    public void SplitLines_of_an_empty_string_is_an_empty_list()
    {
        Assert.Empty(DraftEdits.SplitLines(""));
    }

    [Fact]
    public void Setting_a_clause_records_the_id_and_never_the_text()
    {
        var clause = new AgrBlock { BlockType = "clauseInsertion", ClauseId = "CL-001" };

        var edited = DraftEdits.SetClause(OneSection(clause), 0, 0, "CL-004");

        Assert.Equal("CL-004", edited.Sections[0].Blocks[0].ClauseId);
        Assert.Null(edited.Sections[0].Blocks[0].Text);
    }

    [Fact]
    public void Question_edits_change_one_field_and_leave_the_others()
    {
        var question = new AgrBlock
        {
            BlockType = "radioQuestion",
            Question = new AgrQuestion
            {
                Number = 3, Prompt = "Old prompt", Required = false,
                Options = new[] { "Yes", "No", "Partial", "Not applicable" }
            }
        };

        var draft = OneSection(question);
        var prompted = DraftEdits.SetQuestionPrompt(draft, 0, 0, "New prompt");
        var required = DraftEdits.SetQuestionRequired(prompted, 0, 0, true);
        var optioned = DraftEdits.SetQuestionOptions(required, 0, 0, new[] { "Yes", "No", "Partial", "Unknown" });

        var q = optioned.Sections[0].Blocks[0].Question!;
        Assert.Equal("New prompt", q.Prompt);
        Assert.True(q.Required);
        Assert.Equal(3, q.Number);
        Assert.Equal(new[] { "Yes", "No", "Partial", "Unknown" }, q.Options);
    }

    [Fact]
    public void Adding_removing_and_moving_blocks_changes_only_that_section()
    {
        var draft = new AgrDraft
        {
            ArtifactType = "DS",
            Sections = new[]
            {
                Section("Purpose and Scope", Paragraph("a"), Paragraph("b")),
                Section("Second", Paragraph("untouched"))
            }
        };

        var added = DraftEdits.AddBlock(draft, 0, DraftEdits.NewProse("paragraph"));
        Assert.Equal(3, added.Sections[0].Blocks.Count);
        Assert.Equal("", added.Sections[0].Blocks[2].Text);

        var moved = DraftEdits.MoveBlock(added, 0, 0, 1);
        Assert.Equal(new[] { "b", "a", "" }, moved.Sections[0].Blocks.Select(x => x.Text).ToArray());

        var removed = DraftEdits.RemoveBlock(moved, 0, 2);
        Assert.Equal(2, removed.Sections[0].Blocks.Count);
        Assert.Equal("untouched", removed.Sections[1].Blocks.Single().Text);
    }

    // Moving off either end is a no-op rather than an exception: the buttons stay clickable
    // and the app does not have to hold the condition.
    [Fact]
    public void Moving_a_block_past_either_end_does_nothing()
    {
        var draft = OneSection(Paragraph("a"), Paragraph("b"));

        Assert.Equal("a", DraftEdits.MoveBlock(draft, 0, 0, -1).Sections[0].Blocks[0].Text);
        Assert.Equal("b", DraftEdits.MoveBlock(draft, 0, 1, 1).Sections[0].Blocks[1].Text);
    }

    // The read-only fallback is only honest if an unsupported block survives edits made
    // around it. This is the test that proves nothing is quietly dropped.
    [Fact]
    public void An_unsupported_block_survives_an_edit_to_its_neighbour()
    {
        var table = new AgrBlock
        {
            BlockType = "table",
            ColumnHeaders = new[] { "Control", "Status" },
            Rows = new[] { new[] { "MFA", "In place" } }
        };

        var edited = DraftEdits.SetProse(OneSection(Paragraph("before"), table), 0, 0, "after");

        var survivor = edited.Sections[0].Blocks[1];
        Assert.Equal("table", survivor.BlockType);
        Assert.Equal(new[] { "Control", "Status" }, survivor.ColumnHeaders);
        Assert.Equal("MFA", survivor.Rows!.Single()[0]);
    }

    [Fact]
    public void Setting_internal_only_leaves_the_rest_of_the_block_alone()
    {
        var edited = DraftEdits.SetInternalOnly(OneSection(Paragraph("note")), 0, 0, true);

        Assert.True(edited.Sections[0].Blocks[0].InternalOnly);
        Assert.Equal("note", edited.Sections[0].Blocks[0].Text);
    }

    [Theory]
    [InlineData("bulletList", true)]
    [InlineData("paragraph", false)]
    [InlineData("h2", false)]
    [InlineData("h3", false)]
    [InlineData("somethingNewInV3", false)]
    public void List_block_types_are_named_in_one_place(string blockType, bool expected)
        => Assert.Equal(expected, DraftEdits.IsList(blockType));

    // Questions are numbered within their section, so the next number counts the questions
    // already there and nothing else. Counting every block instead (which is what the
    // section panel used to do) numbers the fourth question in a section that also holds a
    // descriptor paragraph and a clause as question 6. q.Number is document content that
    // no validation layer checks, so the wrong number ships to the client in silence.
    [Fact]
    public void The_next_question_number_counts_questions_not_blocks()
    {
        var question = DraftEdits.NewQuestion(1, new[] { "Yes", "No" });
        var section = OneSection(
            Paragraph("descriptor"),
            question,
            new AgrBlock { BlockType = "clauseInsertion", ClauseId = "CL-001" },
            question with { });

        Assert.Equal(3, DraftEdits.NextQuestionNumber(section, 0));
    }

    [Fact]
    public void The_first_question_in_a_section_is_number_one()
        => Assert.Equal(1, DraftEdits.NextQuestionNumber(OneSection(Paragraph("descriptor")), 0));

    // Out of range is 1 rather than a throw, matching every other index-taking edit here:
    // the app renders its buttons unconditionally and holds no condition of its own.
    [Fact]
    public void The_next_question_number_for_a_section_that_does_not_exist_is_one()
        => Assert.Equal(1, DraftEdits.NextQuestionNumber(OneSection(Paragraph("a")), 4));
}
