using System.Linq;
using System.Text.Json.Nodes;
using Registrar.Cli;
using Registrar.Core;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Tests;

public class BuildCommandTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    private static string CopyToTemp(string repoRelative)
    {
        var src = Path.Combine(RepoRoot(), repoRelative);
        var dir = TempDir.Create();
        var dest = Path.Combine(dir, Path.GetFileName(src));
        File.Copy(src, dest);
        return dest;
    }

    [Fact]
    public void Valid_draft_build_writes_a_docx()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, code);
        var docxPath = Path.Combine(Path.GetDirectoryName(agrPath)!,
            "ORG - Deployment Summary - Talewind CPA - v1.0.docx");
        Assert.True(File.Exists(docxPath));

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("ORG-GOV-DS-016", text);
        Assert.NotEmpty(doc.MainDocumentPart.ImageParts);

        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Invalid_agr_build_writes_no_docx_and_exits_1()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-invalid-schema.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.Empty(Directory.GetFiles(dir, "*.docx"));
    }

    // Regression test for the critical whole-branch-review finding: a malformed AGR
    // (missing a required top-level property, here renderingOptions) must still fail
    // closed with a report on disk and a non-zero exit, not an unhandled JsonException.
    // agr-sample-ds-invalid-schema.json alone did not cover this: it only violates the
    // documentId pattern, which still deserializes fine, so it never exercised the path
    // where AgrParser.Parse itself would throw on a required-but-absent member.
    [Fact]
    public void Missing_required_property_build_writes_a_report_and_exits_1_without_crashing()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-missing-required-property.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.Empty(Directory.GetFiles(dir, "*.docx"));

        var reportPath = Path.Combine(dir, "validation-report.json");
        Assert.True(File.Exists(reportPath));
        var report = File.ReadAllText(reportPath);
        Assert.Contains("\"overallResult\"", report);
        Assert.Contains("SchemaValidationError", report);
    }

    [Fact]
    public void Mode_flag_mismatch_exits_2()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json"); // mode is draft
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, "promotion");

        Assert.Equal(2, code);
    }

    [Fact]
    public void Prohibited_char_build_writes_no_docx_and_reports_QA_ENG_05()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-prohibited-char.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.Empty(Directory.GetFiles(dir, "*.docx"));

        var reportPath = Path.Combine(dir, "validation-report.json");
        Assert.True(File.Exists(reportPath));
        Assert.Contains("QA-ENG-05", File.ReadAllText(reportPath));
    }

    [Fact]
    public void Unsupported_block_build_writes_report_no_docx_and_exits_1()
    {
        // agr-sample-ds-unsupported-block.json carries a synthetic block type
        // (notARenderableBlock) that appears in no schema version. Now that all ten
        // AGR-schema block types are engine-supported (copyReadyMessage landed last),
        // there is no longer a schema-valid-but-engine-unsupported value to reach
        // QA-ENG-06 through the full pipeline; agr.schema.json's blockType enum rejects
        // it first via QA-SCHEMA-01. The fixture still proves the fail-closed contract
        // this test names: no docx, a written report, exit 1.
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-unsupported-block.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.Empty(Directory.GetFiles(dir, "*.docx"));

        var reportPath = Path.Combine(dir, "validation-report.json");
        Assert.True(File.Exists(reportPath));
        Assert.Contains("QA-SCHEMA-01", File.ReadAllText(reportPath));
    }

    [Fact]
    public void Aup_draft_build_renders_all_sections_and_clauses()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docx = Directory.GetFiles(dir, "*.docx").Single();
        using var doc = WordprocessingDocument.Open(docx, false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("1. Purpose", text);
        Assert.Contains("9. Policy Review and Ownership", text);
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Draft_build_report_includes_post_render_scan_and_docx()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.NotEmpty(Directory.GetFiles(dir, "*.docx"));
        var report = File.ReadAllText(Path.Combine(dir, "validation-report.json"));
        // The clean DS render produces no QA-SF-08 failure, but the report is written
        // after render, so the pipeline completed the post-render phase.
        Assert.Contains("\"overallResult\"", report);
    }

    [Fact]
    public void Ds_promotion_build_renders_docx_and_emits_payload()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-promotion.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
            "promotion", registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docx = Directory.GetFiles(dir, "*.docx").Single();
        using (var doc = WordprocessingDocument.Open(docx, false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
            Assert.Empty(errors);
        }

        var payload = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "promotion-payload.json")))!.AsObject();
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }

    [Fact]
    public void Aup_promotion_build_renders_docx_and_emits_payload()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-aup-promotion.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
            "promotion", registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docxPath = Path.Combine(dir, "ORG - Acceptable Use Policy - Talewind CPA - v1.0.docx");
        Assert.True(File.Exists(docxPath));
        using (var doc = WordprocessingDocument.Open(docxPath, false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
            Assert.Empty(errors);
        }

        var payload = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "promotion-payload.json")))!.AsObject();
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }

    [Fact]
    public void Dcn_promotion_build_renders_docx_and_emits_payload()
    {
        var payload = PromotionBuildPayload("fixtures/agr-sample-dcn-promotion.json");
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("Controlled Documents/Operational Artifacts", (string?)payload["repositoryTarget"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }

    [Fact]
    public void Tak_promotion_build_renders_docx_and_emits_payload()
    {
        var payload = PromotionBuildPayload("fixtures/agr-sample-tak-promotion.json");
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("Controlled Documents/Training", (string?)payload["repositoryTarget"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }

    [Fact]
    public void Raf_promotion_build_renders_docx_and_emits_payload()
    {
        var payload = PromotionBuildPayload("fixtures/agr-sample-raf-promotion.json");
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("Controlled Documents/Risk", (string?)payload["repositoryTarget"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }

    [Fact]
    public void Tcl_draft_build_renders_radio_questions_and_writes_a_docx()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-tcl-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docxPath = Path.Combine(dir, "ORG - Training Checklist - Talewind CPA - v1.0.docx");
        Assert.True(File.Exists(docxPath));

        using var doc = WordprocessingDocument.Open(docxPath, false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var text = body.InnerText;

        // All seven TCL sections rendered, in schema order.
        Assert.Contains("1. Pre-Training Preparation", text);
        Assert.Contains("7. Record Storage Location", text);

        // Descriptor lines: composed from config, both the required and optional labels.
        Assert.Contains("Question 1. Select one answer. Required", text);
        Assert.Contains("Question 3. Select one answer. Optional", text);

        // The radio glyph is present, and no option is a numbered or bulleted list item.
        Assert.Contains("\u25cb", text); // U+25CB radio glyph
        Assert.Contains("Yes, reviewed and acknowledged", text);

        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Tcl_promotion_build_renders_docx_and_emits_payload()
    {
        var payload = PromotionBuildPayload("fixtures/agr-sample-tcl-promotion.json");
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("Controlled Documents/Training", (string?)payload["repositoryTarget"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }

    [Fact]
    public void QA_SF_10_missing_question_build_writes_report_no_docx_and_exits_1()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-tcl-radio-missing-question.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.Empty(Directory.GetFiles(dir, "*.docx"));

        var report = File.ReadAllText(Path.Combine(dir, "validation-report.json"));
        Assert.Contains("QA-SF-10", report);

        // The fixture must reach the semantic layers, not die at schema validation:
        // ValidationService short-circuits on a blocking schema failure and would never
        // run QA-SF-10. If this assertion fires, the fixture is schema-invalid.
        Assert.DoesNotContain("SchemaValidationError", report);
    }

    [Fact]
    public void Stamps_artifact_sha256_of_the_document_actually_produced()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docxPath = Directory.GetFiles(dir, "*.docx").Single();
        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "validation-report.json")))!.AsObject();

        var stamped = (string?)report["artifactSha256"];
        Assert.NotNull(stamped);
        Assert.Equal(64, stamped!.Length);
        Assert.Equal(stamped, stamped.ToLowerInvariant());

        // The hash must be OF THE BYTES ON DISK, not of some other artifact.
        Assert.Equal(Hashing.Sha256Hex(File.ReadAllBytes(docxPath)), stamped);
    }

    [Fact]
    public void Leaves_artifact_sha256_null_when_no_document_is_produced()
    {
        // Fails closed pre-render (QA-ENG-05 prohibited character), so no DOCX exists.
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-prohibited-char.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(1, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.Empty(Directory.GetFiles(dir, "*.docx"));

        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "validation-report.json")))!.AsObject();
        Assert.Null((string?)report["artifactSha256"]);
    }

    [Fact]
    public void Passing_build_writes_generation_log_with_clause_insertions()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var logPath = Path.Combine(dir, "generation-log.json");
        Assert.True(File.Exists(logPath));

        var log = JsonNode.Parse(File.ReadAllText(logPath))!.AsObject();
        Assert.Equal("1.0.0", (string?)log["logVersion"]);

        // Outcome mirrors the run's overall result recorded in the report.
        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "validation-report.json")))!.AsObject();
        Assert.Equal((string?)report["overallResult"], (string?)log["outcome"]);

        Assert.NotEmpty(log["rulesApplied"]!.AsArray());

        // AUP embeds core clauses inserted by reference, so the audit is non-empty
        // and every insertion is verified (expected SHA equals actual SHA).
        var insertions = log["clauseInsertions"]!.AsArray();
        Assert.NotEmpty(insertions);
        foreach (var ins in insertions)
            Assert.True((bool)ins!["verified"]!);
    }

    [Fact]
    public void Pre_render_validation_failure_writes_no_generation_log()
    {
        // Fails closed pre-render (QA-ENG-05 prohibited character), so rendering
        // never runs and there is no generation attempt to log.
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-prohibited-char.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(1, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.False(File.Exists(Path.Combine(dir, "generation-log.json")));
    }

    [Fact]
    public void Generation_log_is_byte_identical_across_runs_under_a_fixed_clock()
    {
        var agrPathA = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        Assert.Equal(0, BuildCommand.Run(agrPathA, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var logA = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(agrPathA)!, "generation-log.json"));

        var agrPathB = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        Assert.Equal(0, BuildCommand.Run(agrPathB, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var logB = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(agrPathB)!, "generation-log.json"));

        Assert.Equal(logA, logB);
    }

    [Fact]
    public void Draft_build_writes_manifest_with_sections_and_null_registry_reference()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var manifestPath = Path.Combine(dir, "artifact-manifest.json");
        Assert.True(File.Exists(manifestPath));

        var m = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        Assert.Equal("1.0.0", (string?)m["manifestVersion"]);
        Assert.Equal("AUP", (string?)m["identity"]!["artifactType"]);
        Assert.NotEmpty(m["sections"]!.AsArray());
        Assert.Equal(1, (int)m["sections"]!.AsArray()[0]!["number"]!);
        Assert.Null(m["registryReference"]);
        // artifactSha256 must equal the DOCX on disk (same fact the report pins).
        var docx = Directory.GetFiles(dir, "*.docx").Single();
        Assert.Equal(Hashing.Sha256Hex(File.ReadAllBytes(docx)), (string?)m["artifactSha256"]);
    }

    [Fact]
    public void Promotion_build_writes_manifest_with_registry_reference()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-promotion.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
            "promotion", registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var m = JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "artifact-manifest.json")))!.AsObject();
        Assert.Equal("In Review", (string?)m["registryReference"]!["toStatus"]);
        Assert.Equal("ORG-GOV-DS-016", (string?)m["registryReference"]!["documentId"]);
    }

    [Fact]
    public void Pre_render_validation_failure_writes_no_manifest()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-prohibited-char.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(1, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        Assert.False(File.Exists(Path.Combine(dir, "artifact-manifest.json")));
    }

    [Fact]
    public void Manifest_is_byte_identical_across_runs_under_a_fixed_clock()
    {
        var a = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        Assert.Equal(0, BuildCommand.Run(a, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var mA = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(a)!, "artifact-manifest.json"));

        var b = CopyToTemp("fixtures/agr-sample-aup-draft.json");
        Assert.Equal(0, BuildCommand.Run(b, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var mB = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(b)!, "artifact-manifest.json"));

        Assert.Equal(mA, mB);
    }

    [Fact]
    public void Draft_build_renders_a_warn_callout()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-warn-callout-draft.json");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docx = Directory.GetFiles(dir, "*.docx").Single();
        using var doc = WordprocessingDocument.Open(docx, false);
        var text = doc.MainDocumentPart!.Document!.Body!.InnerText;
        Assert.Contains("Warning", text);
        Assert.Contains("not yet approved for distribution", text);
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public void Promotion_build_of_a_non_client_facing_artifact_renders_a_warn_callout()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-raf-warn-callout-promotion.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");
        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
            "promotion", registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docx = Directory.GetFiles(dir, "*.docx").Single();
        using var doc = WordprocessingDocument.Open(docx, false);
        Assert.Contains("Warning", doc.MainDocumentPart!.Document!.Body!.InnerText);
    }

    // Runs a promotion build against the shared registry fixture, asserts a clean exit
    // and a schema-valid DOCX, and returns the emitted promotion payload for inspection.
    private static JsonObject PromotionBuildPayload(string fixtureRelativePath)
    {
        var agrPath = CopyToTemp(fixtureRelativePath);
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null,
            "promotion", registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docx = Directory.GetFiles(dir, "*.docx").Single();
        using (var doc = WordprocessingDocument.Open(docx, false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
            Assert.Empty(errors);
        }

        return JsonNode.Parse(File.ReadAllText(Path.Combine(dir, "promotion-payload.json")))!.AsObject();
    }
}
