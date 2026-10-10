using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Cli;

public static class MenuCommand
{
    public static int Run(IConsole console, IOperatorShell shell, IClock clock, string settingsPath)
    {
        var store = new SettingsStore(settingsPath);
        var settings = store.Load();

        while (true)
        {
            console.WriteLine("");
            console.WriteLine($"registrar  |  working folder: {settings.LastFolder ?? "(not set)"}");
            console.WriteLine("  1) Validate an AGR");
            console.WriteLine("  2) Build an AGR  (validate, then render on pass)");
            console.WriteLine("  3) Change working folder");
            console.WriteLine("  0) Quit");
            console.Write("> ");

            switch ((console.ReadLine() ?? "0").Trim())
            {
                case "1": settings = RunOnAgr(console, shell, clock, store, settings, build: false); break;
                case "2": settings = RunOnAgr(console, shell, clock, store, settings, build: true); break;
                case "3": settings = ChangeFolder(console, store, settings); break;
                case "0": case "": return 0;
                default: console.WriteLine("That is not a valid choice.", LineStyle.Fail); break;
            }
        }
    }

    private static MenuSettings ChangeFolder(IConsole console, SettingsStore store, MenuSettings settings)
    {
        console.Write("Working folder path: ");
        var folder = (console.ReadLine() ?? "").Trim();
        if (folder.Length == 0) return settings;
        if (!Directory.Exists(folder)) { console.WriteLine("No such folder.", LineStyle.Fail); return settings; }
        settings = settings.WithFolder(folder);
        store.Save(settings);
        return settings;
    }

    private static MenuSettings RunOnAgr(IConsole console, IOperatorShell shell, IClock clock,
        SettingsStore store, MenuSettings settings, bool build)
    {
        var folder = settings.LastFolder ?? "";
        var candidates = AgrPicker.Candidates(folder, settings.RecentAgrs);
        if (candidates.Count == 0)
        {
            console.WriteLine("No .json AGRs found. Set a working folder (option 3) first.", LineStyle.Warn);
            return settings;
        }
        for (var i = 0; i < candidates.Count; i++)
            console.WriteLine($"  {i + 1}) {Path.GetFileName(candidates[i])}");
        console.Write("Pick an AGR (number, or Enter to cancel): ");
        var pick = (console.ReadLine() ?? "").Trim();
        if (!int.TryParse(pick, out var idx) || idx < 1 || idx > candidates.Count) return settings;
        var agrPath = candidates[idx - 1];

        var defaultMode = AgrMode.TryRead(agrPath) ?? "draft";
        console.Write($"Mode [draft/promotion] (Enter for {defaultMode}): ");
        var mode = (console.ReadLine() ?? "").Trim();
        if (mode.Length == 0) mode = defaultMode;

        console.Write("Registry CSV path (Enter for none): ");
        var registry = (console.ReadLine() ?? "").Trim();
        var registryPath = registry.Length == 0 ? null : registry;

        var err = new StringWriter();
        var exit = build
            ? BuildCommand.Run(agrPath, clock, TextWriter.Null, err, mode, registryPath)
            : ValidateCommand.Run(agrPath, clock, TextWriter.Null, err, registryPath);

        settings = settings.WithRecent(agrPath);
        store.Save(settings);

        // Exit 2 = usage/IO error (e.g. the chosen mode disagrees with the AGR's
        // declared mode): no report was written this run. Show the error and never
        // fall back to a possibly-stale report from a previous run in the same folder.
        if (exit == 2)
        {
            var message = err.ToString().Trim();
            console.WriteLine(message.Length > 0 ? message : "The run could not start (usage or IO error).", LineStyle.Fail);
            return settings;
        }

        var report = ReadReport(Path.GetDirectoryName(agrPath)!);
        if (report is null)
        {
            console.WriteLine("No report was written (unexpected).", LineStyle.Fail);
            return settings;
        }
        foreach (var line in ReportPresenter.Present(report)) console.WriteLine(line.Text, line.Style);

        PostRun(console, shell, agrPath, report, build);
        return settings;
    }

    private static void PostRun(IConsole console, IOperatorShell shell, string agrPath,
        ValidationReport report, bool build)
    {
        var hasFailures = report.Checks.Any(c => c.Result == CheckResultValue.Fail);
        while (true)
        {
            console.WriteLine("");
            if (build) console.WriteLine("  d) Open the document");
            console.WriteLine("  r) Open the validation report");
            if (hasFailures) console.WriteLine("  c) Copy failures for the Governor");
            console.WriteLine("  Enter) Back to menu");
            console.Write("> ");
            switch ((console.ReadLine() ?? "").Trim().ToLowerInvariant())
            {
                case "d" when build:
                    var docx = FindDocx(agrPath);
                    if (docx is not null)
                        Safely(console, "Could not open the document.", () => shell.OpenFile(docx));
                    else
                        console.WriteLine("No document was produced.", LineStyle.Warn);
                    break;
                case "r":
                    var reportPath = Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json");
                    Safely(console, "Could not open the report.", () => shell.OpenFile(reportPath));
                    break;
                case "c" when hasFailures:
                    Safely(console, "Could not copy to the clipboard.", () =>
                    {
                        shell.CopyToClipboard(GovernorClipboardText.Build(report));
                        console.WriteLine("Copied failures to the clipboard.", LineStyle.Normal);
                    });
                    break;
                case "":
                    return;
                default:
                    console.WriteLine("That is not a valid choice.", LineStyle.Fail);
                    break;
            }
        }
    }

    private static void Safely(IConsole console, string failMessage, Action action)
    {
        try { action(); }
        catch (Exception ex) { console.WriteLine($"{failMessage} ({ex.Message})", LineStyle.Fail); }
    }

    private static string? FindDocx(string agrPath)
    {
        try
        {
            var name = AgrParser.Parse(File.ReadAllText(agrPath)).RenderingOptions.OutputFileName;
            var path = Path.Combine(Path.GetDirectoryName(agrPath)!, name);
            return File.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    private static ValidationReport? ReadReport(string dir)
    {
        var path = Path.Combine(dir, "validation-report.json");
        if (!File.Exists(path)) return null;
        try { return ReportSerializer.FromJson(File.ReadAllText(path)); }
        catch { return null; }
    }
}
