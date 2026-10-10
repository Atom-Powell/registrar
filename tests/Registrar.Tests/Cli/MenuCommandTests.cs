using Registrar.Cli;
using Registrar.Core;

namespace Registrar.Tests.Cli;

public class MenuCommandTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class FakeConsole : IConsole
    {
        private readonly Queue<string> _input;
        public readonly List<(string Text, LineStyle Style)> Output = new();
        public FakeConsole(params string[] input) => _input = new Queue<string>(input);
        public void Write(string text) => Output.Add((text, LineStyle.Normal));
        public void WriteLine(string text) => Output.Add((text, LineStyle.Normal));
        public void WriteLine(string text, LineStyle style) => Output.Add((text, style));
        public string? ReadLine() => _input.Count > 0 ? _input.Dequeue() : "0";
        public string AllText => string.Join("\n", Output.Select(o => o.Text));
    }

    private sealed class FakeShell : IOperatorShell
    {
        public readonly List<string> Opened = new();
        public string? LastClipboard;
        public void OpenFile(string path) => Opened.Add(path);
        public void CopyToClipboard(string text) => LastClipboard = text;
    }

    private sealed class ThrowingShell : IOperatorShell
    {
        public void OpenFile(string path) => throw new InvalidOperationException("no association");
        public void CopyToClipboard(string text) => throw new InvalidOperationException("clipboard unavailable");
    }

    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    private static string WorkingFolderWith(string fixtureName)
    {
        var dir = TempDir.Create();
        File.Copy(Path.Combine(RepoRoot(), "fixtures", fixtureName), Path.Combine(dir, fixtureName));
        return dir;
    }

    private static string Settings() =>
        TempDir.File("settings.json");

    [Fact]
    public void Validate_clean_agr_prints_PASS_then_quits()
    {
        var folder = WorkingFolderWith("agr-sample-ds-draft.json");
        var console = new FakeConsole(
            "3", folder,   // 3) change working folder -> folder
            "1", "1",      // 1) validate -> pick candidate #1
            "",            // mode: accept default
            "",            // registry: none
            "",            // post-run: back (empty)
            "0");          // quit
        var code = MenuCommand.Run(console, new FakeShell(), new FixedClock(), Settings());
        Assert.Equal(0, code);
        Assert.Contains(console.Output, o => o.Style == LineStyle.Pass && o.Text.Contains("PASS"));
    }

    [Fact]
    public void Validate_failing_agr_offers_copy_failures_to_clipboard()
    {
        var folder = WorkingFolderWith("agr-sample-ds-prohibited-char.json");
        var shell = new FakeShell();
        var console = new FakeConsole(
            "3", folder,
            "1", "1",
            "",            // mode default
            "",            // no registry
            "c",           // post-run: copy failures
            "",            // post-run: back
            "0");          // quit
        var code = MenuCommand.Run(console, shell, new FixedClock(), Settings());
        Assert.Equal(0, code);
        Assert.Contains(console.Output, o => o.Style == LineStyle.Fail && o.Text.Contains("FAIL"));
        Assert.NotNull(shell.LastClipboard);
        Assert.Contains("QA-ENG-05", shell.LastClipboard!);
    }

    [Fact]
    public void Mode_override_mismatch_shows_error_not_a_stale_report()
    {
        var folder = WorkingFolderWith("agr-sample-ds-draft.json");
        // Pre-seed a stale report from a hypothetical prior run that "passed".
        File.WriteAllText(Path.Combine(folder, "validation-report.json"),
            "{\"reportVersion\":\"1.0\",\"executionId\":\"stale\",\"documentId\":\"ORG-GOV-DS-999\","
            + "\"requestedMode\":\"draft\",\"overallResult\":\"pass\",\"engineVersion\":\"v\","
            + "\"configVersions\":{},\"timestampUtc\":\"t\",\"checks\":[]}");
        var console = new FakeConsole(
            "3", folder,        // set working folder
            "2", "1",           // build -> pick candidate #1
            "promotion",        // override mode (AGR declares draft) -> usage error, exit 2
            "",                 // registry: none
            "",                 // post-run: back
            "0");               // quit
        var code = MenuCommand.Run(console, new FakeShell(), new FixedClock(), Settings());
        Assert.Equal(0, code);
        // The stale PASS for the other document must NOT be displayed.
        Assert.DoesNotContain(console.Output, o => o.Style == LineStyle.Pass);
        Assert.DoesNotContain(console.Output, o => o.Text.Contains("ORG-GOV-DS-999"));
        // An error IS shown for the mismatch.
        Assert.Contains(console.Output, o => o.Style == LineStyle.Fail);
    }

    [Fact]
    public void Bad_menu_input_reprompts_then_quits()
    {
        var console = new FakeConsole("99", "x", "0");
        var code = MenuCommand.Run(console, new FakeShell(), new FixedClock(), Settings());
        Assert.Equal(0, code);
        Assert.Contains(console.Output, o => o.Text.Contains("not a valid choice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Shell_failure_does_not_crash_the_menu()
    {
        var folder = WorkingFolderWith("agr-sample-ds-prohibited-char.json");
        var console = new FakeConsole(
            "3", folder,   // set working folder
            "1", "1",      // validate -> pick candidate #1 (this AGR fails validation)
            "",            // mode default
            "",            // no registry
            "c",           // copy failures -> ThrowingShell throws
            "",            // post-run: back
            "0");          // quit
        var code = MenuCommand.Run(console, new ThrowingShell(), new FixedClock(), Settings());
        Assert.Equal(0, code); // menu did not crash
        Assert.Contains(console.Output, o => o.Text.Contains("Could not copy", StringComparison.OrdinalIgnoreCase));
    }
}
