using Registrar.Cli;
using Registrar.Core;

namespace Registrar.Tests.Cli;

// The complete set of files a build leaves behind, per outcome. BuildCommandTests asserts
// that the files each case cares about exist; nothing asserts that nothing ELSE appeared or
// that nothing quietly stopped appearing. An extraction of the output-package writing could
// therefore emit an extra file, or drop one on a branch no test names, and stay green.
//
// Written against the CLI before that extraction and never edited afterward. If a change
// makes one of these fail, the change altered what the CLI produces, which the owner has
// ruled must not happen: the CLI is the fallback.
public class BuildOutputFileSetTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    // Returns the file names present after the build, sorted, plus the exit code.
    private static (int Exit, string[] Files) Build(string fixtureName, string? mode = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "file-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var source = Path.Combine(RepoRoot(), "fixtures", fixtureName);
        var agrPath = Path.Combine(dir, fixtureName);
        File.Copy(source, agrPath);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var registryPath = mode == "promotion" ? Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv") : null;
        var exit = BuildCommand.Run(agrPath, new FixedClock(), stdout, stderr, mode, registryPath);

        var files = Directory.GetFiles(dir)
            .Select(Path.GetFileName)
            .Where(n => n != fixtureName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();

        return (exit, files);
    }

    [Fact]
    public void A_passing_draft_build_writes_report_log_manifest_and_document()
    {
        var (exit, files) = Build("agr-sample-ds-draft.json");

        Assert.Equal(0, exit);
        Assert.Equal(
            new[]
            {
                "ORG - Deployment Summary - Talewind CPA - v1.0.docx",
                "artifact-manifest.json",
                "generation-log.json",
                "validation-report.json"
            },
            files);
    }

    [Fact]
    public void A_pre_render_failure_writes_the_report_and_nothing_else()
    {
        var (exit, files) = Build("agr-sample-ds-invalid-schema.json");

        Assert.Equal(1, exit);
        Assert.Equal(new[] { "validation-report.json" }, files);
    }

    [Fact]
    public void A_passing_promotion_build_also_writes_the_payload()
    {
        var (exit, files) = Build("agr-sample-ds-promotion.json", mode: "promotion");

        Assert.Equal(0, exit);
        Assert.Equal(
            new[]
            {
                "ORG - Deployment Summary - Talewind CPA - v1.0.docx",
                "artifact-manifest.json",
                "generation-log.json",
                "promotion-payload.json",
                "validation-report.json"
            },
            files);
    }

    // Review finding on the OutputPackage extraction: before it, BuildCommand wrote the
    // DOCX and the manifest in two separate try/catch blocks, so a manifest failure that
    // followed a successful DOCX write still printed the document line to stdout ahead of
    // the stderr error. OutputPackage.WriteArtifacts now performs both writes before
    // returning, so BuildCommand has to reconstruct that stdout line from what the package
    // reports as written rather than from its own now-absent DOCX try/catch. This pins the
    // owner's byte-identical-stdout ruling on that specific path.
    [Fact]
    public void A_manifest_write_failure_after_a_successful_docx_still_prints_the_document_line()
    {
        var dir = Path.Combine(Path.GetTempPath(), "file-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        const string fixtureName = "agr-sample-ds-draft.json";
        var source = Path.Combine(RepoRoot(), "fixtures", fixtureName);
        var agrPath = Path.Combine(dir, fixtureName);
        File.Copy(source, agrPath);

        // A directory standing where the manifest must go makes the write fail without
        // depending on file permissions, which vary by machine and by CI image.
        Directory.CreateDirectory(Path.Combine(dir, "artifact-manifest.json"));

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = BuildCommand.Run(agrPath, new FixedClock(), stdout, stderr);

        Assert.Equal(2, exit);
        Assert.Contains(
            $"{OverallResult.Pass}: ORG-GOV-DS-016 - ORG - Deployment Summary - Talewind CPA - v1.0.docx",
            stdout.ToString());
        Assert.Contains("error: cannot write artifact manifest:", stderr.ToString());
    }
}
