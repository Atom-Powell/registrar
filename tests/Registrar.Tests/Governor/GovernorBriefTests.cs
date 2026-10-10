using Registrar.Core.Config;
using Registrar.Core.Governor;
using Xunit;

namespace Registrar.Tests.Governor;

public class GovernorBriefTests
{
    private static ConfigBundle Config() => SemanticTestSupport.Config();

    private static ArtifactTypeConfig Type(string code) => Config().ArtifactTypesByCode[code];

    [Theory]
    [InlineData("AUP")]
    [InlineData("DS")]
    [InlineData("TCL")]
    [InlineData("UQR")]
    [InlineData("PIG")]
    [InlineData("DCN")]
    [InlineData("TAK")]
    [InlineData("RAF")]
    public void A_brief_carries_every_ordered_section_title(string code)
    {
        var type = Type(code);
        var brief = GovernorBrief.For(type, Config());

        foreach (var title in type.SectionSchema!.OrderedSections!)
            Assert.Contains(title, brief, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AUP")]
    [InlineData("TCL")]
    [InlineData("TAK")]
    [InlineData("RAF")]
    public void A_brief_carries_every_required_clause_id(string code)
    {
        var type = Type(code);
        var brief = GovernorBrief.For(type, Config());

        Assert.NotEmpty(type.RequiredClauses!);
        foreach (var id in type.RequiredClauses!)
            Assert.Contains(id, brief, StringComparison.Ordinal);
    }

    [Fact]
    public void A_brief_carries_every_required_placeholder_token()
    {
        var type = Type("AUP");
        var brief = GovernorBrief.For(type, Config());

        Assert.NotEmpty(type.RequiredPlaceholders!);
        foreach (var token in type.RequiredPlaceholders!)
            Assert.Contains(token, brief, StringComparison.Ordinal);
    }

    [Fact]
    public void The_radio_instruction_appears_only_for_types_that_use_the_pattern()
    {
        const string marker = "radio-option pattern";

        Assert.True(Type("TCL").UsesRadioOptionPattern);
        Assert.Contains(marker, GovernorBrief.For(Type("TCL"), Config()), StringComparison.Ordinal);

        Assert.False(Type("AUP").UsesRadioOptionPattern);
        Assert.DoesNotContain(marker, GovernorBrief.For(Type("AUP"), Config()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_outside_governor_scope_has_no_brief()
    {
        var scl = Type("SCL");

        Assert.False(scl.GovernorScope);
        var ex = Assert.Throws<ArgumentException>(() => GovernorBrief.For(scl, Config()));
        Assert.Contains("SCL", ex.Message, StringComparison.Ordinal);
    }

    // The anti-drift guard. If the brief were ever re-derived rather than extracted, this
    // is the test that catches it: the standalone brief must appear verbatim inside the
    // knowledge document the drafting agent reads.
    [Fact]
    public void A_brief_appears_verbatim_inside_the_generated_drafter_knowledge()
    {
        var drafter = new DrafterProjection().Render(KnowledgeProjectionTests.Source());
        var brief = GovernorBrief.For(Type("AUP"), Config());

        Assert.Contains(brief.TrimEnd('\n'), drafter, StringComparison.Ordinal);
    }
}
