using System.Text;
using Registrar.Core;
using Registrar.Core.Config;

namespace Registrar.Tests;

public class CharacterLintTests
{
    private static CharacterPolicy Policy()
        => CharacterPolicy.FromConfig(new ContentLint { ApprovedGlyphAllowlist = new[] { "\u25cb" } });

    [Theory]
    [InlineData("Talewind CPA")]
    [InlineData("Pe\u00f1a")]   // n-tilde U+00F1, in the allowed Latin-1 range
    [InlineData("\u25cb")]      // approved radio glyph
    [InlineData("")]            // empty
    public void Scan_returns_no_offenses_for_allowed_text(string text)
    {
        Assert.Empty(CharacterLint.Scan(text, Policy()));
    }

    [Theory]
    [InlineData("A\u2014B", 0x2014, 2)]   // em dash at rune position 2
    [InlineData("\u2013", 0x2013, 1)]     // en dash
    [InlineData("\u2026", 0x2026, 1)]     // ellipsis
    [InlineData("\u2019", 0x2019, 1)]     // right single quote (smart quote)
    [InlineData("\u00a0", 0x00a0, 1)]     // NBSP (in Latin-1 but carved out)
    [InlineData("\u00ad", 0x00ad, 1)]     // soft hyphen (carved out)
    [InlineData("A\nB", 0x000a, 2)]       // newline control char
    [InlineData("A\tB", 0x0009, 2)]       // tab control char
    public void Scan_flags_single_prohibited_codepoint(string text, int codepoint, int position)
    {
        var offense = Assert.Single(CharacterLint.Scan(text, Policy()));
        Assert.Equal(codepoint, offense.Codepoint.Value);
        Assert.Equal(position, offense.Position);
    }

    [Theory]
    [InlineData("~")]   // U+007E last printable Basic Latin
    [InlineData("\u00a1")]   // U+00A1 first allowed Latin-1 Supplement
    [InlineData("\u00ff")]   // U+00FF last allowed Latin-1 Supplement
    public void Scan_allows_range_boundary_characters(string text)
    {
        Assert.Empty(CharacterLint.Scan(text, Policy()));
    }

    [Fact]
    public void Scan_rejects_del_just_past_printable_basic_latin()
    {
        var offense = Assert.Single(CharacterLint.Scan("\u007f", Policy())); // DEL U+007F
        Assert.Equal(0x7F, offense.Codepoint.Value);
    }

    [Fact]
    public void Scan_treats_astral_emoji_as_one_offense()
    {
        var offenses = CharacterLint.Scan("A\U0001F600", Policy()); // grinning face U+1F600
        var offense = Assert.Single(offenses);
        Assert.Equal(0x1F600, offense.Codepoint.Value);
        Assert.Equal(2, offense.Position);
    }

    [Fact]
    public void Describe_labels_known_codepoints()
    {
        Assert.Equal("U+2014 (em dash)", CharacterLint.Describe(new Rune(0x2014)));
        Assert.Equal("U+2660", CharacterLint.Describe(new Rune(0x2660))); // unlabeled falls back
    }

    [Fact]
    public void FormatActual_caps_at_five_with_overflow_suffix()
    {
        var offenses = Enumerable.Range(1, 6)
            .Select(i => new Offense(new Rune(0x2014), i)).ToList();

        var actual = CharacterLint.FormatActual(offenses);

        Assert.Contains("at position 5", actual);
        Assert.Contains("and 1 more", actual);
        Assert.DoesNotContain("at position 6", actual);
    }
}
