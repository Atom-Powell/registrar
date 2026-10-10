using System.Collections.Generic;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Tests;

public class GenerationLogBuilderTests
{
    private static ValidationReport Report(params Check[] checks) => new()
    {
        ReportVersion = EngineInfo.ReportVersion,
        ExecutionId = "GOV-2026-07-15-002",
        DocumentId = "ORG-GOV-AUP-004",
        RequestedMode = "draft",
        OverallResult = "pass",
        EngineVersion = EngineInfo.Version,
        ConfigVersions = new Dictionary<string, string> { ["clause-library.json"] = "abc123" },
        TimestampUtc = "2026-07-15T12:00:00.0000000+00:00",
        Checks = checks
    };

    private static Check Check(string id) => new()
    {
        CheckId = id, LayerName = "content", Result = "pass",
        SeverityLevel = "info", Message = "ok"
    };

    [Fact]
    public void Carries_header_and_provenance_from_the_report()
    {
        var log = GenerationLogBuilder.Build(Report(Check("QA-SF-08")), new List<ClauseInsertionAudit>());

        Assert.Equal(EngineInfo.LogVersion, log.LogVersion);
        Assert.Equal("GOV-2026-07-15-002", log.ExecutionId);
        Assert.Equal("ORG-GOV-AUP-004", log.DocumentId);
        Assert.Equal("draft", log.RequestedMode);
        Assert.Equal("pass", log.Outcome);
        Assert.Equal("2026-07-15T12:00:00.0000000+00:00", log.TimestampUtc);
        Assert.Equal("abc123", log.ConfigVersions["clause-library.json"]);
    }

    [Fact]
    public void RulesApplied_is_the_distinct_checkId_set_in_order()
    {
        var log = GenerationLogBuilder.Build(
            Report(Check("QA-SF-08"), Check("QA-ENG-05"), Check("QA-SF-08")),
            new List<ClauseInsertionAudit>());

        Assert.Equal(new[] { "QA-SF-08", "QA-ENG-05" }, log.RulesApplied);
    }

    [Fact]
    public void Maps_clause_audits_one_to_one()
    {
        var audits = new List<ClauseInsertionAudit>
        {
            new("CL-001", "sha-a", "sha-a", true, 2),
            new("CL-004", "sha-b", "sha-c", false, 0)
        };

        var log = GenerationLogBuilder.Build(Report(Check("QA-SF-08")), audits);

        Assert.Equal(2, log.ClauseInsertions.Count);
        Assert.Equal("CL-001", log.ClauseInsertions[0].ClauseId);
        Assert.True(log.ClauseInsertions[0].Verified);
        Assert.Equal(2, log.ClauseInsertions[0].TokensResolved);
        Assert.Equal("CL-004", log.ClauseInsertions[1].ClauseId);
        Assert.False(log.ClauseInsertions[1].Verified);
    }

    [Fact]
    public void Empty_audits_yield_empty_clauseInsertions()
    {
        var log = GenerationLogBuilder.Build(Report(Check("QA-SF-08")), new List<ClauseInsertionAudit>());
        Assert.Empty(log.ClauseInsertions);
    }
}
