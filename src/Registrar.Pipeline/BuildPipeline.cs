using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Rendering;
using Registrar.Validation;

namespace Registrar.Pipeline;

// The build orchestration extracted from Registrar.Cli.BuildCommand. Touches no file
// system, no console, no exit codes: that is what lets a GUI reuse it. Validation is a
// separate stage that already happened by the time Run is called - the caller supplies
// the outcome - so a GUI that validates continuously to drive an authoring rail does
// not pay to re-validate on every Build press, and there is only one clock read behind
// the report the pipeline works from.
public static class BuildPipeline
{
    public static BuildResult Run(
        ValidationOutcome outcome,
        AgrDocument agr,
        EngineContext engine,
        byte[] logoBytes)
    {
        var report = outcome.Report;

        // Pre-render failure: no document, nothing further to compute.
        if (report.OverallResult == OverallResult.Fail)
            return new BuildResult(report, outcome.Payload, null, null, null, false, ReachedRendering: false);

        var preEligible = report.OverallResult == OverallResult.Pass
            || (report.OverallResult == OverallResult.PassWithWarnings && report.RequestedMode == Mode.Draft);

        if (!preEligible)
            return new BuildResult(report, outcome.Payload, null, null, null, false, ReachedRendering: false);

        var rendered = new ArtifactRenderer().Render(agr, engine.Config, logoBytes);

        var merged = PostRenderMerge.Apply(outcome, rendered.PostRenderChecks, agr.ValidationPolicy.WarningThreshold, engine.Config.Qa);

        // The artifact hash must describe a document that was actually produced, so
        // eligibility is decided BEFORE the report is written. Stamping a hash on a run
        // that emitted no DOCX would be a false attestation.
        var docxEligible = merged.Report.OverallResult == OverallResult.Pass
            || (merged.Report.OverallResult == OverallResult.PassWithWarnings
                && merged.Report.RequestedMode == Mode.Draft);

        if (docxEligible)
            merged = merged with
            {
                Report = merged.Report with { ArtifactSha256 = Hashing.Sha256Hex(rendered.Bytes) }
            };

        var mergedReport = merged.Report;

        // Built for every run that reached rendering - before any outcome branch - so it
        // records the run whether the DOCX is produced or a post-render check withholds
        // it. Pre-render failures return earlier and never reach this point, which is
        // the intended gating.
        var generationLog = GenerationLogBuilder.Build(mergedReport, rendered.ClauseAudits);

        if (!docxEligible)
            return new BuildResult(mergedReport, merged.Payload, null, generationLog, null, false,
                ReachedRendering: true);

        // The manifest describes the artifact just produced, so it is built here and
        // nowhere else: no DOCX means no manifest.
        var manifest = ArtifactManifestBuilder.Build(mergedReport, agr, merged.Payload);

        return new BuildResult(mergedReport, merged.Payload, rendered.Bytes, generationLog, manifest, true,
            ReachedRendering: true);
    }
}
