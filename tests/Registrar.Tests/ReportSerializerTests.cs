using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;
using Json.Schema;

namespace Registrar.Tests;

public class ReportSerializerTests
{
    private static ValidationReport SampleReport() => new()
    {
        ReportVersion = EngineInfo.ReportVersion,
        ExecutionId = "GOV-2026-07-03-001",
        DocumentId = "ORG-GOV-DS-016",
        RequestedMode = Mode.Draft,
        OverallResult = OverallResult.Pass,
        EngineVersion = EngineInfo.Version,
        ConfigVersions = new Dictionary<string, string> { ["agr.schema.json"] = "abc123" },
        TimestampUtc = "2026-07-03T12:00:00.0000000+00:00",
        ArtifactSha256 = null,
        Checks = Array.Empty<Check>()
    };

    [Fact]
    public void Serialized_report_uses_camelCase_schema_field_names()
    {
        var json = ReportSerializer.ToJson(SampleReport());
        var node = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("1.0.0", (string?)node["reportVersion"]);
        Assert.Equal("draft", (string?)node["requestedMode"]);
        Assert.Equal("pass", (string?)node["overallResult"]);
        Assert.True(node.ContainsKey("configVersions"));
        Assert.True(node.ContainsKey("timestampUtc"));
    }

    [Fact]
    public void Serialized_report_conforms_to_validation_report_schema()
    {
        var schemaPath = ConfigLocator.FindSchema(
            AppContext.BaseDirectory, "validation-report.schema.json");
        var schema = IsolatedSchema.FromFile(schemaPath);
        using var document = JsonDocument.Parse(ReportSerializer.ToJson(SampleReport()));
        var instance = document.RootElement;

        var result = schema.Evaluate(instance,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, "Emitted report failed report schema validation.");
    }

    [Fact]
    public void FromJson_round_trips_ToJson()
    {
        var report = new Registrar.Core.Model.ValidationReport
        {
            ReportVersion = "1.0", ExecutionId = "e", DocumentId = "ORG-GOV-DS-016",
            RequestedMode = "draft", OverallResult = "fail", EngineVersion = "v",
            ConfigVersions = new Dictionary<string, string> { ["a"] = "b" },
            TimestampUtc = "2026-07-06T00:00:00Z",
            Checks = new[]
            {
                new Registrar.Core.Model.Check
                {
                    CheckId = "QA-ENG-05", LayerName = "input", Result = "fail",
                    SeverityLevel = "blocking", Message = "bad char", Remediation = "fix it"
                }
            }
        };
        var json = Registrar.Core.ReportSerializer.ToJson(report);
        var back = Registrar.Core.ReportSerializer.FromJson(json);
        Assert.Equal("ORG-GOV-DS-016", back.DocumentId);
        Assert.Equal("fail", back.OverallResult);
        Assert.Single(back.Checks);
        Assert.Equal("QA-ENG-05", back.Checks[0].CheckId);
        Assert.Equal("fix it", back.Checks[0].Remediation);
    }
}
