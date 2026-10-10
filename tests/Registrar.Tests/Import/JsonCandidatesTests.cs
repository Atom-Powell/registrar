using Registrar.Core.Import;
using Xunit;

namespace Registrar.Tests.Import;

public class JsonCandidatesTests
{
    [Fact]
    public void A_bare_json_paste_is_a_candidate()
    {
        var candidates = JsonCandidates.Collect("{\"a\": 1}");

        Assert.Contains(candidates, c => c.Text == "{\"a\": 1}");
    }

    [Fact]
    public void A_fenced_block_is_a_candidate_without_its_fence()
    {
        var paste = "Here you go:\n\n```json\n{\"a\": 1}\n```\n\nLet me know.";

        var candidates = JsonCandidates.Collect(paste);

        Assert.Contains(candidates, c => c.Text.Trim() == "{\"a\": 1}");
        Assert.DoesNotContain(candidates, c => c.Text.Contains("```", StringComparison.Ordinal));
        Assert.DoesNotContain(candidates, c => c.Text.Contains("Let me know", StringComparison.Ordinal));
    }

    [Fact]
    public void An_untagged_fence_is_still_a_candidate()
    {
        var paste = "```\n{\"a\": 1}\n```";

        var candidates = JsonCandidates.Collect(paste);

        Assert.Contains(candidates, c => c.Text.Trim() == "{\"a\": 1}");
    }

    [Fact]
    public void Candidates_are_ordered_longest_first()
    {
        var paste = "A fragment:\n\n```json\n{\"a\": 1}\n```\n\nThe envelope:\n\n```json\n{\"aaaaaaaaaaaaaaaa\": 1}\n```";

        var candidates = JsonCandidates.Collect(paste);

        Assert.True(candidates.Count >= 2);
        Assert.Contains("aaaaaaaaaaaaaaaa", candidates[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_paste_with_no_braces_yields_no_candidates()
    {
        var candidates = JsonCandidates.Collect("I could not generate that artifact.");

        Assert.Empty(candidates);
    }

    [Fact]
    public void An_unbalanced_span_is_collected_and_reported_as_truncated()
    {
        var paste = "```json\n{\"a\": 1, \"b\": {\"c\": 2}";

        var candidates = JsonCandidates.Collect(paste);

        Assert.NotEmpty(candidates);
        Assert.True(JsonCandidates.LooksTruncated(candidates[0].Text));
    }

    [Fact]
    public void A_balanced_span_is_not_truncated()
    {
        Assert.False(JsonCandidates.LooksTruncated("{\"a\": 1}"));
    }

    [Fact]
    public void A_brace_inside_a_string_does_not_affect_balance()
    {
        Assert.False(JsonCandidates.LooksTruncated("{\"a\": \"a { brace and a \\\" quote\"}"));
    }

    // Provenance is what lets ImportParser apply its quotation-mark gate to the fallback
    // span alone. Without it a fenced envelope whose delimiters were smart-quoted is
    // indistinguishable from prose that happens to mention a brace.
    [Fact]
    public void A_fenced_candidate_is_marked_fenced_and_a_prose_span_is_not()
    {
        var fenced = JsonCandidates.Collect("Here you go:\n\n```json\n{\"a\": 1}\n```\n\nDone.");
        Assert.All(fenced, c => Assert.True(c.Fenced));

        var prose = JsonCandidates.Collect("Some systems use { for grouping. Nothing else.");
        Assert.All(prose, c => Assert.False(c.Fenced));
    }

    // A reply whose only content is one fenced envelope collects that text twice, once from
    // the fence and once from the brace-span fallback. The surviving candidate must keep
    // the fenced provenance, or deduplication would quietly downgrade it.
    [Fact]
    public void A_span_found_both_fenced_and_bare_keeps_its_fenced_provenance()
    {
        var candidates = JsonCandidates.Collect("```json\n{\"a\": 1}\n```");

        var candidate = Assert.Single(candidates);
        Assert.Equal("{\"a\": 1}", candidate.Text);
        Assert.True(candidate.Fenced);
    }
}
