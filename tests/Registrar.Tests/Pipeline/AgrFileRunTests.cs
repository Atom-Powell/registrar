using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Tests.Pipeline;

public class AgrFileRunTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    }

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

    [Fact]
    public void Validate_pass_returns_the_written_report_and_no_document()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var r = AgrFileRun.Validate(path, new FixedClock());

        Assert.Equal(0, r.ExitCode);
        Assert.NotNull(r.Report);
        Assert.Equal(OverallResult.Pass, r.Report!.OverallResult);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(path)), r.OutputDirectory);
        Assert.Contains("validation-report.json", r.Written);
        Assert.Null(r.DocumentFileName);
        Assert.Equal("", r.ErrorText);
    }

    [Fact]
    public void Validate_missing_file_reports_the_error_and_no_report()
    {
        var path = Path.Combine(TempDir.Create(), "nope.json");
        var r = AgrFileRun.Validate(path, new FixedClock());

        Assert.Equal(2, r.ExitCode);
        Assert.Null(r.Report);
        Assert.Equal($"error: AGR file not found: {path}", r.ErrorText);
        Assert.Empty(r.Written);
    }

    [Fact]
    public void Build_pass_names_the_document_and_lists_every_file_written()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var r = AgrFileRun.Build(path, new FixedClock());

        Assert.Equal(0, r.ExitCode);
        Assert.Equal("ORG - Deployment Summary - Talewind CPA - v1.0.docx", r.DocumentFileName);
        Assert.Equal(
            new[]
            {
                "ORG - Deployment Summary - Talewind CPA - v1.0.docx",
                "artifact-manifest.json",
                "generation-log.json",
                "validation-report.json"
            },
            r.Written.OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.True(File.Exists(Path.Combine(r.OutputDirectory, r.DocumentFileName!)));
    }

    [Fact]
    public void Build_pre_render_failure_yields_exit_1_a_failing_report_and_no_document()
    {
        var r = AgrFileRun.Build(Copy("agr-sample-ds-invalid-schema.json"), new FixedClock());

        Assert.Equal(1, r.ExitCode);
        Assert.Equal(OverallResult.Fail, r.Report!.OverallResult);
        Assert.Null(r.DocumentFileName);
        Assert.Equal(new[] { "validation-report.json" }, r.Written.ToArray());
        Assert.Contains(r.Messages, m => m.Stream == RunStream.Out && m.Text.StartsWith("fail: ")
            && m.Text.EndsWith("- no document produced (validation failed)."));
    }

    [Fact]
    public void Build_mode_disagreement_is_a_usage_error_with_no_report()
    {
        var path = Copy("agr-sample-ds-draft.json");
        var stale = Path.Combine(Path.GetDirectoryName(path)!, "validation-report.json");
        File.WriteAllText(stale, "{\"stale\":true}");
        var staleBytes = File.ReadAllBytes(stale);
        var r = AgrFileRun.Build(path, new FixedClock(), mode: "promotion");

        Assert.Equal(2, r.ExitCode);
        Assert.Null(r.Report);
        Assert.Contains("does not match the AGR validationPolicy.mode 'draft'", r.ErrorText);
        Assert.Empty(r.Written);
        Assert.Equal(staleBytes, File.ReadAllBytes(stale));
    }

    // Writing a file where a directory of the same name exists throws, which makes the
    // report write fail deterministically without any flaky IO.
    private static string CopyWithBlockedReport(string fixtureName)
    {
        var path = Copy(fixtureName);
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(path)!, "validation-report.json"));
        return path;
    }

    [Fact]
    public void Validate_report_write_failure_leaves_Report_null()
    {
        var r = AgrFileRun.Validate(CopyWithBlockedReport("agr-sample-ds-draft.json"), new FixedClock());

        Assert.Equal(2, r.ExitCode);
        Assert.Null(r.Report);
        Assert.NotEqual("", r.ErrorText);
        Assert.DoesNotContain("validation-report.json", r.Written);
    }

    [Fact]
    public void Build_report_write_failure_leaves_Report_null()
    {
        var r = AgrFileRun.Build(CopyWithBlockedReport("agr-sample-ds-draft.json"), new FixedClock());

        Assert.Equal(2, r.ExitCode);
        Assert.Null(r.Report);
        Assert.NotEqual("", r.ErrorText);
        Assert.Null(r.DocumentFileName);
    }

    [Fact]
    public void Replay_writes_stdout_and_stderr_messages_in_recorded_order()
    {
        var r = new AgrFileRunResult(
            0,
            new[]
            {
                new RunMessage(RunStream.Out, "one"),
                new RunMessage(RunStream.Err, "two"),
                new RunMessage(RunStream.Out, "three")
            },
            null, "", Array.Empty<string>(), Array.Empty<string>(), null);

        var combined = new StringWriter();
        r.Replay(combined, combined);

        Assert.Equal($"one{Environment.NewLine}two{Environment.NewLine}three{Environment.NewLine}", combined.ToString());
    }
}
