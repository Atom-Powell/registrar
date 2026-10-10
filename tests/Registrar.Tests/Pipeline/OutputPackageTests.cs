using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Pipeline;
using Registrar.Validation;

namespace Registrar.Tests.Pipeline;

// The emit-when rules, which CLAUDE.md specifies and which lived inside BuildCommand until
// this unit. They are asserted here rather than only through the CLI so a second caller
// cannot restate them differently.
public class OutputPackageTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pkg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static ValidationReport Report(string overall) => new()
    {
        ReportVersion = "2.0.0",
        ExecutionId = "00000000-0000-0000-0000-000000000001",
        DocumentId = "ORG-GOV-DS-016",
        RequestedMode = Mode.Draft,
        OverallResult = overall,
        EngineVersion = "test",
        ConfigVersions = new Dictionary<string, string>(),
        TimestampUtc = "2026-08-04T12:00:00Z",
        Checks = Array.Empty<Check>()
    };

    private static string[] FilesIn(string dir)
        => Directory.GetFiles(dir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void Validation_writes_the_report_and_no_payload_when_there_is_none()
    {
        var dir = TempDir();

        var result = OutputPackage.WriteValidation(new ValidationOutcome(Report(OverallResult.Fail), null), dir);

        Assert.Null(result.Failure);
        Assert.Equal(new[] { "validation-report.json" }, FilesIn(dir));
    }

    // Mirrors the test above. That one proves the payload is absent when there is none;
    // on its own it would still pass an implementation that dropped the payload branch
    // entirely. A real ValidationOutcome for the promotion fixture BuildOutputFileSetTests
    // uses, rather than a hand-built PromotionPayload: ValidationService already emits one
    // for a non-fail promotion run against a resolvable registry record (see
    // ValidationService.Validate), so there is no need to fabricate the engine's own output.
    [Fact]
    public void Validation_writes_the_payload_alongside_the_report_when_there_is_one()
    {
        var dir = TempDir();
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;
        var engine = EngineContext.Load(repoRoot);
        var agrText = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-promotion.json"));
        var registryText = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "registry-sample.csv"));
        var outcome = new ValidationService(new FixedClock()).Validate(agrText, engine, registryText);
        Assert.NotNull(outcome.Payload);

        var result = OutputPackage.WriteValidation(outcome, dir);

        Assert.Null(result.Failure);
        Assert.Equal(new[] { "promotion-payload.json", "validation-report.json" }, FilesIn(dir));
    }

    [Fact]
    public void A_run_that_never_reached_rendering_emits_no_generation_log()
    {
        var dir = TempDir();
        var build = new BuildResult(Report(OverallResult.Fail), null, null, null, null, false, ReachedRendering: false);

        var result = OutputPackage.WriteArtifacts(build, dir, "out.docx");

        Assert.Null(result.Failure);
        Assert.Empty(FilesIn(dir));
    }

    // The generation log records every run that reached rendering, including one whose
    // document a post-render check withheld. That is the case the log exists for.
    // A real BuildResult, produced by actually running the pipeline over a fixture. Hand
    // building a GenerationLog and an ArtifactManifest would mean inventing values for
    // engine models this test has no business knowing the shape of, and a fabricated one
    // that drifted from the real thing would make these tests pass against a package the
    // engine would never produce.
    private static BuildResult RealBuild()
    {
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

        var engine = EngineContext.Load(repoRoot);
        var agrText = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));
        var outcome = new ValidationService(new FixedClock()).Validate(agrText, engine, null);
        var agr = AgrParser.Parse(agrText);
        var logo = File.ReadAllBytes(ConfigLocator.FindAsset(repoRoot, engine.Config.Style.Wordmark.Asset));

        return BuildPipeline.Run(outcome, agr, engine, logo);
    }

    [Fact]
    public void A_withheld_document_still_emits_the_generation_log_and_no_manifest()
    {
        var dir = TempDir();
        // Same real build, with only eligibility flipped: this is the post-render-withhold
        // shape, where a document was rendered and then held back.
        var build = RealBuild() with { DocxEligible = false };

        var result = OutputPackage.WriteArtifacts(build, dir, "out.docx");

        Assert.Equal(new[] { "generation-log.json" }, FilesIn(dir));
        Assert.Contains(result.Withheld, w => w.Contains("out.docx", StringComparison.Ordinal));
    }

    // No DOCX means no manifest: a manifest describes an artifact that was actually written.
    [Fact]
    public void An_eligible_document_emits_the_docx_and_the_manifest_together()
    {
        var dir = TempDir();
        var build = RealBuild();
        Assert.True(build.DocxEligible, "the fixture must build cleanly for this test to mean anything");

        var result = OutputPackage.WriteArtifacts(build, dir, "out.docx");

        Assert.Null(result.Failure);
        Assert.Equal(new[] { "artifact-manifest.json", "generation-log.json", "out.docx" }, FilesIn(dir));
    }

    // Rule 1, asserted against a BuildResult the pipeline cannot currently produce. That is
    // the point: the CLI used to check OverallResult itself and return before writing the
    // DOCX, that check went away when the writing moved into this type, and there are now
    // two callers plus a public entry point anyone can hand a BuildResult to. The repo's own
    // idiom is to keep and test a guard that is unreachable today (QA-ENG-06's block-type
    // fail path), so the impossible combination is constructed here on purpose.
    [Fact]
    public void A_failing_report_withholds_the_document_even_when_it_is_marked_eligible()
    {
        var dir = TempDir();
        var build = RealBuild() with { Report = Report(OverallResult.Fail) };
        Assert.True(build.DocxEligible, "the eligible flag must be set for this test to mean anything");
        Assert.NotNull(build.DocxBytes);

        var result = OutputPackage.WriteArtifacts(build, dir, "out.docx");

        Assert.Null(result.Failure);
        // The log still records the run that was attempted; the document and its manifest
        // do not reach disk.
        Assert.Equal(new[] { "generation-log.json" }, FilesIn(dir));
        Assert.Contains(result.Withheld, w => w.Contains("out.docx", StringComparison.Ordinal));
        Assert.Contains(result.Withheld, w => w.Contains("artifact-manifest.json", StringComparison.Ordinal));
        Assert.All(result.Withheld, w => Assert.Contains("validation failed", w, StringComparison.Ordinal));
    }

    // The folder name is a path segment, and its caller deletes the resulting folder
    // recursively. Both halves are gated, so neither can walk out of the engagement folder.
    [Theory]
    [InlineData("..\\..\\OtherClient\\ORG-GOV-DS-016", "v1.0")]
    [InlineData("ORG-GOV-DS-016", "..\\..\\OtherClient")]
    public void A_path_shaped_identity_cannot_become_a_package_folder_name(string documentId, string version)
    {
        var ex = Assert.Throws<ArgumentException>(() => OutputPackage.DirectoryNameFor(documentId, version));
        Assert.Contains("names a path rather than an identifier", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_identity_still_produces_the_expected_folder_name()
        => Assert.Equal("ORG-GOV-DS-016 - v1.0", OutputPackage.DirectoryNameFor("ORG-GOV-DS-016", "v1.0"));

    [Fact]
    public void A_write_that_fails_is_reported_rather_than_thrown()
    {
        var dir = TempDir();
        // A directory standing where the file must go makes the write fail without
        // depending on permissions, which vary by machine and by CI image.
        Directory.CreateDirectory(Path.Combine(dir, "out.docx"));

        var result = OutputPackage.WriteArtifacts(RealBuild(), dir, "out.docx");

        Assert.NotNull(result.Failure);
        Assert.Contains("cannot write document", result.Failure!, StringComparison.Ordinal);
        // What did land must still be reported, so a partial package never reads as complete.
        Assert.Contains("generation-log.json", result.Written);
    }
}
