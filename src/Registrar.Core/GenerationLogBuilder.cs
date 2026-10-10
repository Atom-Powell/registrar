using Registrar.Core.Model;

namespace Registrar.Core;

// Pure assembly of the generation log from the post-render merged report and the
// renderer's clause-insertion audits. No clock, no I/O: every field is carried
// from the report (already stamped from the injected IClock) or from the audits,
// so the log is reproducible under a fixed clock.
public static class GenerationLogBuilder
{
    public static GenerationLog Build(
        ValidationReport mergedReport, IReadOnlyList<ClauseInsertionAudit> audits)
        => new()
        {
            LogVersion = EngineInfo.LogVersion,
            ExecutionId = mergedReport.ExecutionId,
            DocumentId = mergedReport.DocumentId,
            EngineVersion = mergedReport.EngineVersion,
            RequestedMode = mergedReport.RequestedMode,
            Outcome = mergedReport.OverallResult,
            TimestampUtc = mergedReport.TimestampUtc,
            ConfigVersions = mergedReport.ConfigVersions,
            // Distinct() over LINQ-to-objects preserves first-seen order, so the
            // rule list is deterministic. Full per-check detail stays in the report.
            RulesApplied = mergedReport.Checks.Select(c => c.CheckId).Distinct().ToList(),
            ClauseInsertions = audits.Select(a => new ClauseInsertion
            {
                ClauseId = a.ClauseId,
                ExpectedSha = a.ExpectedSha,
                ActualSha = a.ActualSha,
                Verified = a.Verified,
                TokensResolved = a.TokensResolved
            }).ToList()
        };
}
