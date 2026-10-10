using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;
using Json.Schema;

namespace Registrar.Tests;

public class ValidationServiceTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    private static (string schemaPath, string repoRoot) Paths()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        return (schemaPath, repoRoot);
    }

    [Fact]
    public void Valid_agr_yields_pass_report_with_no_fail_checks()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath).Report;

        Assert.Equal(OverallResult.Pass, report.OverallResult);
        Assert.DoesNotContain(report.Checks, c => c.Result == CheckResultValue.Fail);
        Assert.Equal("GOV-2026-07-03-001", report.ExecutionId);
        Assert.Equal("ORG-GOV-DS-016", report.DocumentId);
        Assert.Equal(Mode.Draft, report.RequestedMode);
        Assert.Equal("2026-07-03T12:00:00.0000000+00:00", report.TimestampUtc);
        Assert.True(report.ConfigVersions.ContainsKey("agr.schema.json"));
        Assert.Null(report.ArtifactSha256);
    }

    [Fact]
    public void Invalid_agr_yields_fail_report()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"))
            .Replace("ORG-GOV-DS-016", "ORG-GOV-DS-16");

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath).Report;

        Assert.Equal(OverallResult.Fail, report.OverallResult);
        Assert.Contains(report.Checks, c => c.Result == CheckResultValue.Fail);
    }

    [Fact]
    public void Invalid_agr_report_serializes_to_a_conformant_validation_report()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"))
            .Replace("ORG-GOV-DS-016", "ORG-GOV-DS-16");

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath).Report;
        Assert.Contains(report.Checks, c => c.Result == CheckResultValue.Fail);

        var reportSchemaPath = ConfigLocator.FindSchema(
            AppContext.BaseDirectory, "validation-report.schema.json");

        // See IsolatedSchema for why this must never build against the
        // default/global SchemaRegistry.
        var reportSchema = IsolatedSchema.FromFile(reportSchemaPath);

        var json = ReportSerializer.ToJson(report);
        using var doc = JsonDocument.Parse(json);

        var result = reportSchema.Evaluate(
            doc.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, "Invalid-AGR report failed to validate against validation-report.schema.json.");
    }

    [Fact]
    public void Semantic_failure_makes_report_fail_and_includes_semantic_check()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-section-mismatch.json"));

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath).Report;

        Assert.Equal(OverallResult.Fail, report.OverallResult);
        Assert.Contains(report.Checks, c => c.CheckId == "QA-ENG-02");
    }

    [Fact]
    public void Schema_failure_gates_out_semantic_layers()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-invalid-schema.json"));

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath).Report;

        Assert.Equal(OverallResult.Fail, report.OverallResult);
        Assert.All(report.Checks, c => Assert.Equal("QA-SCHEMA-01", c.CheckId));
    }

    [Fact]
    public void Config_hashes_are_recorded_in_config_versions()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));

        var report = new ValidationService(new FixedClock()).Validate(agr, schemaPath).Report;

        Assert.Equal(OverallResult.Pass, report.OverallResult);
        Assert.True(report.ConfigVersions.ContainsKey("artifact-types.json"));
        Assert.True(report.ConfigVersions.ContainsKey("clause-library.json"));
    }

    [Fact]
    public void Valid_agr_with_registry_passes()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));

        var report = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, SemanticTestSupport.SampleRegistryCsv).Report;

        Assert.Equal(OverallResult.Pass, report.OverallResult);
        Assert.DoesNotContain(report.Checks, c => c.Result == CheckResultValue.Fail);
    }

    [Fact]
    public void Unregistered_id_with_registry_fails_with_qa_sf_03()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-unregistered.json"));

        var report = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, SemanticTestSupport.SampleRegistryCsv).Report;

        Assert.Equal(OverallResult.Fail, report.OverallResult);
        Assert.Contains(report.Checks, c => c.CheckId == "QA-SF-03" && c.Result == CheckResultValue.Fail);
    }

    [Fact]
    public void Malformed_registry_fails_with_qa_sf_03()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));

        var report = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, "documentId,artifactType\nORG-GOV-DS-016,DS\n").Report;

        Assert.Equal(OverallResult.Fail, report.OverallResult);
        Assert.Contains(report.Checks,
            c => c.CheckId == "QA-SF-03" && c.Result == CheckResultValue.Fail
                 && c.ErrorCategory == ErrorCategory.RegistrySyncError);
    }

    [Fact]
    public void Promotion_pass_produces_payload()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-promotion.json"));

        var outcome = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, SemanticTestSupport.SampleRegistryCsv);

        Assert.Equal(OverallResult.Pass, outcome.Report.OverallResult);
        Assert.NotNull(outcome.Payload);
        Assert.Equal("Draft", outcome.Payload!.RegistryUpdate.FromStatus);
        Assert.Equal("In Review", outcome.Payload.RegistryUpdate.ToStatus);
        Assert.Equal("Controlled Documents/Governance", outcome.Payload.RepositoryTarget);
    }

    [Fact]
    public void Draft_pass_produces_no_payload()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-draft.json"));

        var outcome = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, SemanticTestSupport.SampleRegistryCsv);

        Assert.Equal(OverallResult.Pass, outcome.Report.OverallResult);
        Assert.Null(outcome.Payload);
    }

    [Fact]
    public void Promotion_fail_produces_no_payload()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-promotion.json"));

        // No registry in promotion -> QA-SF-03 fails the run.
        var outcome = new ValidationService(new FixedClock()).Validate(agr, schemaPath, null);

        Assert.Equal(OverallResult.Fail, outcome.Report.OverallResult);
        Assert.Null(outcome.Payload);
    }

    // NOTE: a "promotion pass-with-warnings" test previously lived here, exercised by
    // agr-sample-ds-promotion-warn.json (whose sole warning was a null-clauseId QA-ENG-04).
    // QA-ENG-04 null-clauseId is now blocking (tech-debt item 12), and it was the only
    // warn-severity check that survived promotion (PlaceholderLayer is blocking at
    // promotion), so pass-with-warnings at promotion is no longer reachable. The
    // pass-with-warnings state is now covered in Draft by agr-sample-ds-draft-warn.json.

    [Fact]
    public void Promotion_pass_report_conforms_to_report_schema()
    {
        var (schemaPath, repoRoot) = Paths();
        var agr = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "agr-sample-ds-promotion.json"));
        var outcome = new ValidationService(new FixedClock())
            .Validate(agr, schemaPath, SemanticTestSupport.SampleRegistryCsv);

        var reportSchemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "validation-report.schema.json");
        var reportSchema = IsolatedSchema.FromFile(reportSchemaPath);
        using var document = System.Text.Json.JsonDocument.Parse(ReportSerializer.ToJson(outcome.Report));

        var result = reportSchema.Evaluate(document.RootElement,
            new Json.Schema.EvaluationOptions { OutputFormat = Json.Schema.OutputFormat.List });

        Assert.True(result.IsValid, "Promotion-pass report (with the QA-SPEC-18 pass check) failed report-schema validation.");
    }

    // The overload exists so the editor can validate per keystroke without re-reading the
    // schema and re-parsing seven config files each time. It must produce exactly what
    // the path-based call produces, or the GUI and the CLI would disagree.
    [Fact]
    public void The_cached_overload_produces_the_same_report_as_the_path_based_call()
    {
        var repoRoot = SemanticTestSupport.RepoRoot();
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var registryCsv = File.ReadAllText(Path.Combine(repoRoot, "fixtures", "registry-sample.csv"));
        var engine = EngineContext.Load(repoRoot);
        var clock = new FixedClock();

        foreach (var path in Directory
                     .GetFiles(Path.Combine(repoRoot, "fixtures"), "agr-*.json")
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            var text = File.ReadAllText(path);
            var viaPath = new ValidationService(clock).Validate(text, schemaPath, registryCsv);
            var viaContext = new ValidationService(clock).Validate(text, engine, registryCsv);

            Assert.Equal(
                ReportSerializer.ToJson(viaPath.Report),
                ReportSerializer.ToJson(viaContext.Report));
        }
    }
}
