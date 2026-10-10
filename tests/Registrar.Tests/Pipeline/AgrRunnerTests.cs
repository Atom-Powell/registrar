using Registrar.Core;

namespace Registrar.Tests.Pipeline;

public class AgrRunnerTests
{
    private static string Copy(string fixtureName)
    {
        var root = Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;
        var dest = Path.Combine(TempDir.Create(), fixtureName);
        File.Copy(Path.Combine(root, "fixtures", fixtureName), dest);
        return dest;
    }

    private static AgrRunner Runner() => new(new SystemClock());

    [Fact]
    public void Check_writes_a_report_and_no_document()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var r = Runner().Check(path, null);

        Assert.Equal(0, r.ExitCode);
        Assert.NotNull(r.Report);
        Assert.Null(r.DocumentFileName);
        Assert.Contains("validation-report.json", r.Written);
    }

    [Fact]
    public void Build_writes_the_document()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var r = Runner().Build(path, null);

        Assert.Equal(0, r.ExitCode);
        Assert.NotNull(r.DocumentFileName);
        Assert.True(File.Exists(Path.Combine(r.OutputDirectory, r.DocumentFileName!)));
    }

    [Fact]
    public void A_missing_file_is_a_readable_exit_2_not_an_exception()
    {
        var r = Runner().Check(Path.Combine(TempDir.Create(), "nope.json"), null);

        Assert.Equal(2, r.ExitCode);
        Assert.Null(r.Report);
        Assert.StartsWith("error: AGR file not found:", r.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void Check_with_a_missing_registry_file_is_a_usage_error_naming_it()
    {
        var path = Copy("agr-sample-ds-draft.json");

        var result = Runner().Check(path, "does-not-exist.csv");

        Assert.Equal(2, result.ExitCode);
        Assert.StartsWith("error: registry file not found:", result.ErrorText, StringComparison.Ordinal);
    }
}
