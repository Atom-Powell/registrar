using System.Text.Json;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Pipeline;

// The file-based validate and build, lifted out of Registrar.Cli so a second caller (the
// operator window) can run them without a console. This is a port, not a redesign: every
// branch, message and ordering below is what CommandSupport, ValidateCommand and
// BuildCommand did, with the writes to stdout and stderr recorded instead of performed.
// The CLI commands are now wrappers that replay the recording, which is what makes the CLI
// provably unchanged rather than merely tested. A second copy of these rules would be a
// second engine.
public static class AgrFileRun
{
    // Exit codes: 0 pass, 1 validation fail, 2 usage/IO error.
    public static AgrFileRunResult Validate(string agrPath, IClock clock, string? registryPath = null)
    {
        var rec = new Recorder();
        var prepared = Prepare(agrPath, clock, registryPath, rec);
        if (prepared is null) return rec.Finish(2);

        rec.OutputDirectory = prepared.Dir;

        var write = WriteValidation(prepared.Outcome, prepared.Dir, rec);
        if (write is not null) return rec.Finish(write.Value);

        var report = prepared.Outcome.Report;

        var failCount = report.Checks.Count(c => c.Result == CheckResultValue.Fail);
        var warnCount = report.Checks.Count(c => c.Result == CheckResultValue.Warn);
        rec.Out(
            $"{report.OverallResult}: {report.DocumentId} ({failCount} failing, {warnCount} warning check(s)). Report: {Path.Combine(prepared.Dir, "validation-report.json")}");

        return rec.Finish(report.OverallResult == OverallResult.Fail ? 1 : 0);
    }

    public static AgrFileRunResult Build(string agrPath, IClock clock, string? mode = null, string? registryPath = null)
    {
        var rec = new Recorder();
        var prepared = Prepare(agrPath, clock, registryPath, rec);
        if (prepared is null) return rec.Finish(2);

        rec.OutputDirectory = prepared.Dir;
        var outcome = prepared.Outcome;
        var report = outcome.Report;

        // mode, when supplied, must agree with the AGR's declared mode. This is a usage
        // error and must be caught before any rendering is attempted, and before any report
        // is written: Report stays null so no caller mistakes an older file for this run.
        if (mode is not null && !string.Equals(mode, report.RequestedMode, StringComparison.Ordinal))
        {
            rec.Err($"error: --mode '{mode}' does not match the AGR validationPolicy.mode '{report.RequestedMode}'.");
            return rec.Finish(2);
        }

        // The report is complete for every pre-render path, including a hard schema
        // failure, so it is written before AgrParser.Parse is ever called. AgrParser's own
        // contract is "call only after schema validation has passed": a malformed AGR
        // could otherwise throw a JsonException with no report on disk. Writing first and
        // returning on Fail before parsing keeps rule 7 (a report, never a stack trace) and
        // the "validation-report.json emitted on every run" contract true unconditionally.
        var writeInitial = WriteValidation(outcome, prepared.Dir, rec);
        if (writeInitial is not null) return rec.Finish(writeInitial.Value);

        if (report.OverallResult == OverallResult.Fail)
        {
            rec.Out($"fail: {report.DocumentId} - no document produced (validation failed).");
            return rec.Finish(1);
        }

        // Reaching here means schema validation passed, so ValidationService already
        // parsed this AGR once successfully. Re-parsing is therefore safe; the exception
        // guard below is defense in depth, not the primary fix.
        AgrDocument agr;
        BuildResult result;
        try
        {
            agr = AgrParser.Parse(prepared.AgrText);
            var logoBytes = File.ReadAllBytes(
                ConfigLocator.FindAsset(prepared.RepoRoot, prepared.Engine.Config.Style.Wordmark.Asset));
            result = BuildPipeline.Run(outcome, agr, prepared.Engine, logoBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or NotSupportedException or JsonException)
        {
            rec.Err($"error: cannot render document: {ex.Message}");
            return rec.Finish(2);
        }

        var mergedReport = result.Report;

        var writeMerged = WriteValidation(new ValidationOutcome(result.Report, result.Payload), prepared.Dir, rec);
        if (writeMerged is not null) return rec.Finish(writeMerged.Value);

        // No render was attempted (for example PassWithWarnings in promotion mode, which is
        // not eligible to render): the report above is unchanged from what was already
        // written, and there is nothing further to emit.
        if (!result.ReachedRendering)
        {
            rec.Out($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - no document (promotion requires a clean pass).");
            return rec.Finish(0);
        }

        var outputFileName = agr.RenderingOptions.OutputFileName;
        var package = OutputPackage.WriteArtifacts(result, prepared.Dir, outputFileName);
        rec.Note(package);
        if (package.Written.Contains(outputFileName)) rec.DocumentFileName = outputFileName;

        if (package.Failure is not null)
        {
            // A manifest-write failure lands here after the DOCX write already succeeded.
            // WriteArtifacts performs both writes before returning, so the stdout document
            // line the CLI printed at the point of that success is reconstructed here from
            // what the package reports as written, ahead of the stderr error.
            if (package.Written.Contains(outputFileName))
                rec.Out($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - {outputFileName}");

            rec.Err($"error: {package.Failure}");
            return rec.Finish(2);
        }

        if (mergedReport.OverallResult == OverallResult.Fail)
        {
            rec.Out($"fail: {mergedReport.DocumentId} - no document produced (post-render validation failed).");
            return rec.Finish(1);
        }

        if (result.DocxEligible)
            rec.Out($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - {outputFileName}");
        else
            rec.Out($"{mergedReport.OverallResult}: {mergedReport.DocumentId} - no document (promotion requires a clean pass).");

        return rec.Finish(0);
    }

    private sealed record Prepared(
        ValidationOutcome Outcome, string Dir, string RepoRoot, string AgrText, EngineContext Engine);

    // Port of CommandSupport.Validate. Returns null after recording the error; the caller
    // finishes the run with exit 2.
    private static Prepared? Prepare(string agrPath, IClock clock, string? registryPath, Recorder rec)
    {
        if (!File.Exists(agrPath))
        {
            rec.Err($"error: AGR file not found: {agrPath}");
            return null;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(agrPath))!;

        string schemaPath;
        try
        {
            schemaPath = ConfigLocator.FindSchema(dir, "agr.schema.json");
        }
        catch (FileNotFoundException)
        {
            try
            {
                schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
            }
            catch (FileNotFoundException ex)
            {
                rec.Err($"error: {ex.Message}");
                return null;
            }
        }
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;

        string agrText;
        try
        {
            agrText = File.ReadAllText(agrPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            rec.Err($"error: cannot read AGR: {ex.Message}");
            return null;
        }

        string? registryCsvText = null;
        if (registryPath is not null)
        {
            if (!File.Exists(registryPath))
            {
                rec.Err($"error: registry file not found: {registryPath}");
                return null;
            }
            try
            {
                registryCsvText = File.ReadAllText(registryPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                rec.Err($"error: cannot read registry: {ex.Message}");
                return null;
            }
        }

        var engine = EngineContext.Load(repoRoot);
        var outcome = new ValidationService(clock).Validate(agrText, engine, registryCsvText);
        return new Prepared(outcome, dir, repoRoot, agrText, engine);
    }

    // Port of CommandSupport.WriteOutputs: null on success, exit 2 after recording a failure.
    private static int? WriteValidation(ValidationOutcome outcome, string dir, Recorder rec)
    {
        var package = OutputPackage.WriteValidation(outcome, dir);
        rec.Note(package);

        // Report means the report that actually reached disk this run, so it is set here,
        // from what the package says it wrote, and nowhere else.
        if (package.Written.Contains("validation-report.json")) rec.Report = outcome.Report;

        if (package.Failure is null) return null;

        rec.Err($"error: {package.Failure}");
        return 2;
    }

    private sealed class Recorder
    {
        private readonly List<RunMessage> _log = new();
        private readonly List<string> _written = new();
        private readonly List<string> _withheld = new();

        public ValidationReport? Report { get; set; }
        public string OutputDirectory { get; set; } = "";
        public string? DocumentFileName { get; set; }

        public void Out(string text) => _log.Add(new RunMessage(RunStream.Out, text));
        public void Err(string text) => _log.Add(new RunMessage(RunStream.Err, text));

        // The report is written twice on a build (pre-render, then merged), so a file name
        // can arrive more than once. The caller wants the set, not the history.
        public void Note(OutputPackageResult package)
        {
            foreach (var w in package.Written) if (!_written.Contains(w)) _written.Add(w);
            foreach (var w in package.Withheld) if (!_withheld.Contains(w)) _withheld.Add(w);
        }

        public AgrFileRunResult Finish(int exitCode)
            => new(exitCode, _log.ToList(), Report, OutputDirectory,
                _written.ToList(), _withheld.ToList(), DocumentFileName);
    }
}
