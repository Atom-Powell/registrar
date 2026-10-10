using Registrar.Cli;
using Registrar.Core.Governor;

namespace Registrar.Tests.Governor;

public class KnowledgeProjectionTests
{
    // Delegates to the same LoadSource the CLI uses, so tests exercise the real
    // SourceHashes construction rather than a parallel, possibly-drifting copy of it.
    internal static KnowledgeSource Source() => EmitKnowledgeCommand.LoadSource(SemanticTestSupport.RepoRoot());

    [Fact]
    public void Intake_lists_every_artifact_type_including_non_generable_ones()
    {
        var text = new IntakeProjection().Render(Source());
        var config = SemanticTestSupport.Config();

        foreach (var code in config.ArtifactTypesByCode.Keys)
            Assert.Contains(code, text);

        // The Steward must be able to say "that one cannot be generated yet",
        // so non-generable types appear and are marked.
        Assert.Contains("TVR", text);
        Assert.Contains("SOP", text);
    }

    [Fact]
    public void Intake_marks_generable_and_non_generable_types_differently()
    {
        var text = new IntakeProjection().Render(Source());

        var ds = text.Split('\n').Single(l => l.StartsWith("| DS |"));
        var tvr = text.Split('\n').Single(l => l.StartsWith("| TVR |"));

        Assert.Contains("| yes |", ds);
        Assert.Contains("| no |", tvr);
    }

    [Fact]
    public void Every_projection_carries_the_config_provenance_header()
    {
        var source = Source();

        foreach (var projection in EmitKnowledgeCommand.Projections)
        {
            var text = projection.Render(source);

            Assert.Contains("artifact-types.json", text);
            Assert.Contains(source.SourceHashes["artifact-types.json"], text);
        }
    }

    [Fact]
    public void Projections_are_deterministic()
    {
        var source = Source();

        foreach (var projection in EmitKnowledgeCommand.Projections)
        {
            var a = projection.Render(source);
            var b = projection.Render(source);

            Assert.Equal(a, b);
        }
    }

    [Fact]
    public void Generated_knowledge_contains_no_prohibited_characters()
    {
        var source = Source();

        // Code points, never literal characters. The repo holds its own source to an
        // escapes-only rule (SourceHygieneTests), so a test that spells an em dash
        // literally would itself be the violation it is checking for. U+25CB (the
        // approved radio glyph, sourced from config and legitimately present in
        // drafter.md) is deliberately absent from this list.
        int[] prohibited = { 0x2014, 0x2013, 0x2018, 0x2019, 0x201C, 0x201D, 0x2026 };

        foreach (var projection in EmitKnowledgeCommand.Projections)
        {
            var text = projection.Render(source);

            foreach (var cp in prohibited)
                Assert.DoesNotContain((char)cp, text);
            Assert.DoesNotContain('\r', text);       // CR: line endings must be "\n" only
        }
    }

    [Fact]
    public void Drafter_carries_every_TCL_section_title_in_order()
    {
        var text = new DrafterProjection().Render(Source());
        var titles = SemanticTestSupport.Config()
            .ArtifactTypesByCode["TCL"].SectionSchema!.OrderedSections!;

        var lastIndex = -1;
        foreach (var title in titles)
        {
            var i = text.IndexOf(title, StringComparison.Ordinal);
            Assert.True(i > lastIndex, $"'{title}' is missing or out of order in drafter.md");
            lastIndex = i;
        }
    }

    [Fact]
    public void Drafter_names_required_clauses_with_their_canonical_names()
    {
        var text = new DrafterProjection().Render(Source());
        var config = SemanticTestSupport.Config();

        Assert.Contains("CL-003", text);
        Assert.Contains(config.ClausesById["CL-003"].Name, text);
        Assert.Contains("CL-004", text);
        Assert.Contains(config.ClausesById["CL-004"].Name, text);
    }

    // usesRadioOptionPattern is set on TCL and DQR, but DQR has no defined section schema
    // and must not appear at all. Iterate the generable set and read the flag within it;
    // do not iterate the flag.
    [Fact]
    public void Drafter_covers_generable_types_only_and_flags_the_radio_pattern_correctly()
    {
        var text = new DrafterProjection().Render(Source());

        Assert.Contains("four", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DQR", text);
    }

    // The converse invariant. Without it, a future in-scope type is silently absent from
    // the drafter's knowledge and the suite stays green. Iterates GovernorScope, not
    // defined-section-schema: SCL and PDX have a defined section schema but are
    // deliberately out of the Governor's scope, so they must not be required here.
    [Fact]
    public void Drafter_covers_every_type_in_governor_scope()
    {
        var text = new DrafterProjection().Render(Source());

        foreach (var t in SemanticTestSupport.Config().ArtifactTypesByCode.Values)
            if (t.GovernorScope)
                Assert.True(text.Contains(t.Code, StringComparison.Ordinal),
                    $"{t.Code} is in the Governor's scope but absent from drafter.md");
    }

    // Finding 1: SCL and PDX render end to end but are engine-generated master-pack
    // reference documents, not client-engagement artifacts the Governor may be asked to
    // produce. They must disappear from drafter.md while remaining visible in intake.md,
    // where the Steward needs to recognise and decline a request for one.
    [Fact]
    public void Drafter_excludes_SCL_and_PDX()
    {
        var text = new DrafterProjection().Render(Source());

        // Match the per-type section heading, not a bare substring: the schema-derived
        // aggregationSource field description legitimately mentions "the PDX slice" in
        // prose unrelated to the artifact type section.
        Assert.DoesNotContain("### SCL -", text);
        Assert.DoesNotContain("### PDX -", text);
    }

    [Fact]
    public void Intake_still_lists_SCL_and_PDX()
    {
        var text = new IntakeProjection().Render(Source());

        Assert.Contains("SCL", text);
        Assert.Contains("PDX", text);
    }

    [Fact]
    public void Payload_carries_the_identity_patterns_from_config()
    {
        var text = new PayloadProjection().Render(Source());
        var v = SemanticTestSupport.Config().Vocabulary;

        Assert.Contains(v.DocumentIdPattern, text);
        Assert.Contains(v.VersionPattern, text);
        Assert.Contains(v.InitialState, text);
    }

    // Every state name is already guaranteed present by the Identity section's status
    // list, so a whole-document Contains check would pass even if the transition table
    // paired the wrong states together. This checks the actual row for each "from"
    // state, so a scrambled pairing (e.g. Draft -> Superseded instead of Draft -> In
    // Review) fails the test.
    [Fact]
    public void Payload_carries_every_legal_lifecycle_transition()
    {
        var text = new PayloadProjection().Render(Source());
        var config = SemanticTestSupport.Config();
        var lines = text.Split('\n');

        foreach (var (from, tos) in config.Transitions)
        {
            var row = lines.Single(l => l.StartsWith($"| {from} |"));

            if (tos.Count == 0)
                Assert.Contains("terminal", row);
            else
                foreach (var to in tos)
                    Assert.Contains(to, row);
        }
    }

    [Fact]
    public void Payload_names_the_prohibited_status_aliases()
    {
        var text = new PayloadProjection().Render(Source());

        foreach (var alias in SemanticTestSupport.Config().Vocabulary.ProhibitedStatusAliases)
            Assert.Contains(alias, text);
    }

    // Finding 4: correction.md is generated entirely from qa-checklist.json, which
    // ConfigBundle.Hashes deliberately excludes. It must still see its own source's hash
    // in the provenance table it prints.
    [Fact]
    public void Correction_carries_the_qa_checklist_hash()
    {
        var source = Source();
        var text = new CorrectionProjection().Render(source);

        Assert.Contains("qa-checklist.json", text);
        Assert.Contains(source.SourceHashes["qa-checklist.json"], text);
    }

    [Fact]
    public void Correction_lists_every_check_in_the_catalogue()
    {
        var text = new CorrectionProjection().Render(Source());
        var entries = SemanticTestSupport.Config().Qa.Entries;
        var lines = text.Split('\n');

        // Find the Checks table by locating the "Rule sources" heading.
        // All lines before that heading are part of the Checks table.
        var ruleSourciesHeadingIndex = lines.IndexOf(lines.First(l => l.Contains("Rule sources")));
        var checksTableLines = ruleSourciesHeadingIndex > 0
            ? lines.Take(ruleSourciesHeadingIndex).ToList()
            : lines.ToList();

        Assert.NotEmpty(entries);
        foreach (var e in entries)
        {
            var prefix = $"| `{e.CheckId}` |";
            Assert.True(
                checksTableLines.Count(l => l.StartsWith(prefix)) == 1,
                $"{e.CheckId} is in the catalogue but missing its table row in correction.md");
        }
    }
}
