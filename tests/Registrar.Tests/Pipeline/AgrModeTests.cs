using Registrar.Core;

namespace Registrar.Tests.Pipeline;

public class AgrModeTests
{
    private static string Fixture(string name)
        => Path.Combine(Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName, "fixtures", name);

    [Fact]
    public void TryRead_returns_the_declared_mode()
        => Assert.Equal("draft", AgrMode.TryRead(Fixture("agr-sample-ds-draft.json")));

    [Fact]
    public void TryRead_returns_null_for_a_missing_file()
        => Assert.Null(AgrMode.TryRead(Path.Combine(TempDir.Create(), "nope.json")));

    [Fact]
    public void TryRead_returns_null_for_text_that_is_not_an_agr()
    {
        var path = Path.Combine(TempDir.Create(), "junk.json");
        File.WriteAllText(path, "this is not json");
        Assert.Null(AgrMode.TryRead(path));
    }

    [Theory]
    [InlineData("draft", "Draft: a working copy. Warnings are allowed.")]
    [InlineData("promotion", "Promotion: the controlled copy. A clean pass is required.")]
    [InlineData(null, "Mode unknown. Press Check and the report will say what is wrong with this file.")]
    [InlineData("something-else", "Mode unknown. Press Check and the report will say what is wrong with this file.")]
    public void Describe_explains_the_mode_in_plain_words(string? mode, string expected)
        => Assert.Equal(expected, AgrMode.Describe(mode));
}
