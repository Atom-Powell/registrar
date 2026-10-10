using Registrar.Cli;
using Registrar.Core;

namespace Registrar.Tests.Cli;

// The exact text, order and exit code the CLI produces, pinned before the logic moved into
// Registrar.Pipeline.AgrFileRun. BuildOutputFileSetTests pins which files land; this pins
// what the operator reads. Written against the unmodified CLI and never edited afterward.
// If one fails, the extraction changed what the CLI says, which the owner has ruled must
// not happen.
public class CliOutputStreamTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly string NL = Environment.NewLine;

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    private static string Copy(string fixtureName)
    {
        var dir = TempDir.Create();
        var dest = Path.Combine(dir, fixtureName);
        File.Copy(Path.Combine(RepoRoot(), "fixtures", fixtureName), dest);
        return dest;
    }

    private static (int Exit, string Out, string Err) Validate(string agrPath, string? registry = null)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var exit = ValidateCommand.Run(agrPath, new FixedClock(), o, e, registry);
        return (exit, o.ToString(), e.ToString());
    }

    private static (int Exit, string Out, string Err) Build(string agrPath, string? mode = null, string? registry = null)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var exit = BuildCommand.Run(agrPath, new FixedClock(), o, e, mode, registry);
        return (exit, o.ToString(), e.ToString());
    }

    [Fact]
    public void Validate_pass_prints_one_summary_line()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var (exit, o, e) = Validate(path);

        var report = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "validation-report.json");
        Assert.Equal(0, exit);
        Assert.Equal($"pass: ORG-GOV-DS-016 (0 failing, 0 warning check(s)). Report: {report}{NL}", o);
        Assert.Equal("", e);
    }

    [Fact]
    public void Validate_fail_prints_a_fail_summary_and_exits_1()
    {
        var (exit, o, e) = Validate(Copy("agr-sample-ds-invalid-schema.json"));

        Assert.Equal(1, exit);
        Assert.StartsWith("fail: ", o);
        Assert.Contains(" failing, ", o);
        Assert.Equal("", e);
    }

    [Fact]
    public void Validate_missing_file_prints_one_error_and_exits_2()
    {
        var path = Path.Combine(TempDir.Create(), "nope.json");
        var (exit, o, e) = Validate(path);

        Assert.Equal(2, exit);
        Assert.Equal("", o);
        Assert.Equal($"error: AGR file not found: {path}{NL}", e);
    }

    [Fact]
    public void Validate_missing_registry_prints_one_error_and_exits_2()
    {
        var (exit, o, e) = Validate(Copy("agr-sample-ds-draft.json"), "does-not-exist.csv");

        Assert.Equal(2, exit);
        Assert.Equal("", o);
        Assert.Equal($"error: registry file not found: does-not-exist.csv{NL}", e);
    }

    [Fact]
    public void Build_pass_prints_one_document_line()
    {
        var (exit, o, e) = Build(Copy("agr-sample-ds-draft.json"));

        Assert.Equal(0, exit);
        Assert.Equal($"pass: ORG-GOV-DS-016 - ORG - Deployment Summary - Talewind CPA - v1.0.docx{NL}", o);
        Assert.Equal("", e);
    }

    [Fact]
    public void Build_pre_render_failure_prints_the_no_document_line_and_exits_1()
    {
        var (exit, o, e) = Build(Copy("agr-sample-ds-invalid-schema.json"));

        Assert.Equal(1, exit);
        Assert.StartsWith("fail: ", o);
        Assert.EndsWith($" - no document produced (validation failed).{NL}", o);
        Assert.Equal("", e);
    }

    [Fact]
    public void Build_mode_disagreement_prints_one_error_exits_2_and_writes_no_report()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var (exit, o, e) = Build(path, mode: "promotion");

        Assert.Equal(2, exit);
        Assert.Equal("", o);
        Assert.Equal(
            $"error: --mode 'promotion' does not match the AGR validationPolicy.mode 'draft'.{NL}", e);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "validation-report.json")));
    }
}
