using System.Text.RegularExpressions;
using Registrar.Core;
using Xunit;

namespace Registrar.Tests.Design;

// app.css states in its own header that it declares no color, because a hand-maintained
// color is the first crack in "the screen is on brand because it reads the brand". These
// tests make that sentence enforceable instead of aspirational.
public class AppCssTests
{
    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static string CssWithoutComments()
    {
        var css = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Registrar.App", "wwwroot", "app.css"));
        return Regex.Replace(css, @"/\*.*?\*/", "", RegexOptions.Singleline);
    }

    [Fact]
    public void app_css_declares_no_hex_or_functional_colors()
    {
        var css = CssWithoutComments();

        Assert.DoesNotMatch(@"#[0-9a-fA-F]{3,8}\b", css);
        Assert.DoesNotMatch(@"\b(rgb|rgba|hsl|hsla)\(", css);
    }

    [Fact]
    public void app_css_uses_no_named_colors()
    {
        // Property values only, so "white-space" and class names are not matched.
        var css = CssWithoutComments();
        var offenders = Regex.Matches(css, @"(?<=:\s*)(?:[^;{}]*\s)?(white|black|transparent|red|blue|gray|grey|green|yellow|orange)\b(?=[^-])")
            .Select(m => m.Value.Trim()).ToList();

        Assert.True(offenders.Count == 0, "Named colors in app.css: " + string.Join(" | ", offenders));
    }

    [Fact]
    public void every_token_app_css_references_exists_in_the_generated_tokens()
    {
        var tokens = File.ReadAllText(Path.Combine(RepoRoot(), "config", "generated", "org-tokens.css"));
        var declared = Regex.Matches(tokens, @"(--org-[a-z0-9-]+)\s*:").Select(m => m.Groups[1].Value).ToHashSet();
        var used = Regex.Matches(CssWithoutComments(), @"var\((--org-[a-z0-9-]+)\)").Select(m => m.Groups[1].Value).ToHashSet();

        var missing = used.Where(u => !declared.Contains(u)).ToList();

        Assert.True(missing.Count == 0, "app.css uses undeclared tokens: " + string.Join(", ", missing));
    }
}
