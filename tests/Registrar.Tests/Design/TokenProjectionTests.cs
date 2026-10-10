using Registrar.Core.Config;
using Registrar.Core.Design;
using Xunit;

namespace Registrar.Tests.Design;

public class TokenProjectionTests
{
    private static string Render()
        => TokenProjection.Render(SemanticTestSupport.Config(), "abc123");

    [Theory]
    [InlineData("danger", "--org-danger")]
    [InlineData("primaryBlue", "--org-primary-blue")]
    [InlineData("headerRightCellFill", "--org-header-right-cell-fill")]
    public void A_token_name_is_derived_from_its_config_key(string key, string expected)
    {
        Assert.Equal(expected, TokenProjection.TokenName(key));
    }

    [Fact]
    public void Every_document_color_becomes_a_token()
    {
        var config = SemanticTestSupport.Config();
        var css = Render();

        foreach (var (key, value) in config.Style.Colors)
            Assert.Contains($"{TokenProjection.TokenName(key)}: {value};", css, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_screen_color_becomes_a_token()
    {
        var config = SemanticTestSupport.Config();
        var css = Render();

        foreach (var (key, value) in config.Screen.Colors)
            Assert.Contains($"{TokenProjection.TokenName(key)}: {value};", css, StringComparison.Ordinal);
    }

    // The grouping is the guidance, so it is asserted rather than left to survive by
    // habit. Sorted into one alphabetical run, --org-border (3.47:1 on white) and
    // --org-border-neutral (1.82:1) land on consecutive lines under equally official
    // names and the file says nothing about which one a control boundary wants.
    [Fact]
    public void The_screen_tokens_form_a_labelled_group_ahead_of_the_document_tokens()
    {
        var css = Render();

        var screenLabel = css.IndexOf("Screen tokens.", StringComparison.Ordinal);
        var documentLabel = css.IndexOf("Document tokens.", StringComparison.Ordinal);
        var border = css.IndexOf("--org-border:", StringComparison.Ordinal);
        var borderNeutral = css.IndexOf("--org-border-neutral:", StringComparison.Ordinal);

        Assert.True(screenLabel >= 0, "the screen group is not labelled");
        Assert.True(documentLabel > screenLabel, "the document group does not follow the screen group");
        Assert.InRange(border, screenLabel, documentLabel);
        Assert.True(borderNeutral > documentLabel, "--org-border-neutral is not inside the document group");
    }

    // A consumer picking a token has to be told, in the file, that half of it is a print
    // palette. The wording can be reworded; the warning cannot be dropped.
    [Fact]
    public void Each_group_comment_says_how_to_treat_the_group()
    {
        var css = Render();

        Assert.Contains("chosen for interactive UI", css, StringComparison.Ordinal);
        Assert.Contains("fail WCAG as interactive foreground", css, StringComparison.Ordinal);
    }

    // Grouping is presentational. Both blocks still land in one flat custom-property
    // namespace, so the sort inside each group is what keeps the render byte-stable for
    // the staleness guard.
    [Fact]
    public void Each_group_is_sorted_and_the_render_is_stable()
    {
        Assert.Equal(Render(), Render(), StringComparer.Ordinal);

        var names = Render()
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("--org-", StringComparison.Ordinal))
            .ToList();

        var screenCount = SemanticTestSupport.Config().Screen.Colors.Count;
        var screenGroup = names.Take(screenCount).ToList();
        var documentGroup = names.Skip(screenCount).ToList();

        // Sorted by token name, not by whole line: the renderer orders names, and a name
        // that prefixes another (danger, danger-hover) would otherwise compare on the
        // ':' that follows it.
        static string Name(string line) => line[..line.IndexOf(':')];
        Assert.Equal(screenGroup.OrderBy(Name, StringComparer.Ordinal), screenGroup);
        Assert.Equal(documentGroup.OrderBy(Name, StringComparer.Ordinal), documentGroup);
    }

    [Fact]
    public void The_output_records_the_config_it_came_from()
    {
        var css = Render();

        Assert.Contains("registrar emit-tokens", css, StringComparison.Ordinal);
        Assert.Contains("abc123", css, StringComparison.Ordinal);
    }

    [Fact]
    public void The_output_uses_line_feeds_only()
    {
        Assert.DoesNotContain("\r", Render(), StringComparison.Ordinal);
    }

    // The derivation cannot tell whether a leading capital or a run of consecutive
    // capitals is an acronym or two separate words, so it must reject the key rather
    // than guess a name nobody intended.
    [Fact]
    public void A_key_starting_with_a_capital_letter_fails_closed()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => TokenProjection.TokenName("PrimaryBlue"));

        Assert.Contains("PrimaryBlue", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_key_with_consecutive_capitals_fails_closed()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => TokenProjection.TokenName("brandURLColor"));

        Assert.Contains("brandURLColor", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_empty_key_fails_closed()
    {
        Assert.Throws<InvalidOperationException>(() => TokenProjection.TokenName(string.Empty));
    }

    // The guard must not be over-tight: every real config color key has to keep
    // deriving successfully. Iterated from the actual dictionaries, not a hardcoded
    // list, so this test tracks config rather than falling out of step with it.
    [Fact]
    public void Every_real_config_color_key_still_derives_successfully()
    {
        var config = SemanticTestSupport.Config();
        var keys = config.Style.Colors.Keys.Concat(config.Screen.Colors.Keys).ToList();

        Assert.NotEmpty(keys);
        foreach (var key in keys)
            TokenProjection.TokenName(key);
    }

    // Both dictionaries feed one flat namespace. A key in both would emit the property
    // twice and the last one would quietly win, which is a palette change nobody made.
    [Fact]
    public void A_token_name_claimed_by_both_blocks_fails_closed()
    {
        var config = SemanticTestSupport.Config();
        var colliding = config with
        {
            Screen = new ScreenTokens
            {
                Colors = new Dictionary<string, string> { ["primaryBlue"] = "#123456" }
            }
        };

        var ex = Assert.Throws<InvalidOperationException>(
            () => TokenProjection.Render(colliding, "abc123"));

        Assert.Contains("--org-primary-blue", ex.Message, StringComparison.Ordinal);
    }
}
