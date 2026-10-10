using System.Linq;
using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class PlaceholderLayerTests
{
    [Fact]
    public void QA_SPEC_07_emits_only_the_coverage_check_for_fully_resolved_canonical_placeholders()
    {
        var checks = new PlaceholderLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        // A clean AGR produces no per-token findings, only the informational coverage rollup.
        var coverage = Assert.Single(checks);
        Assert.Equal("QA-SPEC-07", coverage.CheckId);
        Assert.Equal(CheckResultValue.Pass, coverage.Result);
        Assert.Equal(Severity.Info, coverage.SeverityLevel);
        Assert.Null(coverage.ErrorCategory);
        // DS requires 10 tokens; this fixture provides exactly those 10.
        Assert.Contains("10 required, 10 provided, 0 missing, 0 beyond the required set", coverage.Message);
        Assert.Contains("Missing: none", coverage.Message);
        Assert.Contains("Beyond required: none", coverage.Message);
    }

    [Fact]
    public void QA_SPEC_07_fails_on_known_drift_alias_in_both_modes()
    {
        var draft = new PlaceholderLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-placeholder-drift.json", Mode.Draft));

        var check = Assert.Single(draft, c => c.Actual == "[CLIENT]");
        Assert.Equal("QA-SPEC-07", check.CheckId);
        Assert.Equal(Layer.Content, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("[CLIENT ORGANIZATION NAME]", check.Expected);
    }

    [Fact]
    public void QA_SPEC_07_warns_on_missing_value_in_draft_and_fails_in_promotion()
    {
        // A canonical token present with a null value.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[DATE]\": \"2026-07-03\"", "\"[DATE]\": null");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);
        var config = SemanticTestSupport.Config();

        var draft = new PlaceholderLayer().Validate(new SemanticContext(agr, config, Mode.Draft, null));
        var promo = new PlaceholderLayer().Validate(new SemanticContext(agr, config, Mode.Promotion, null));

        Assert.Equal(CheckResultValue.Warn, Assert.Single(draft, c => c.Actual == "null").Result);
        Assert.Equal(CheckResultValue.Fail, Assert.Single(promo, c => c.Actual == "null").Result);
    }

    [Fact]
    public void QA_SPEC_07_fails_when_required_placeholder_is_absent()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[VERSION]\": \"v1.0\"", "\"[NONREQUIRED TOKEN]\": \"x\"");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var checks = new PlaceholderLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        Assert.Contains(checks, c => c.Result == CheckResultValue.Fail
            && c.Message.Contains("[VERSION]") && c.Message.Contains("missing"));
    }

    [Fact]
    public void QA_SPEC_07_warns_on_unknown_token_in_draft_and_fails_in_promotion()
    {
        // A key that is neither a canonical token nor a known drift alias.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[DATE]\": \"2026-07-03\"", "\"[DATE]\": \"2026-07-03\", \"[ZZ NOT A REAL TOKEN]\": \"x\"");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);
        var config = SemanticTestSupport.Config();

        var draft = new PlaceholderLayer().Validate(new SemanticContext(agr, config, Mode.Draft, null));
        var promo = new PlaceholderLayer().Validate(new SemanticContext(agr, config, Mode.Promotion, null));

        Assert.Equal(CheckResultValue.Warn,
            Assert.Single(draft, c => c.Actual == "[ZZ NOT A REAL TOKEN]").Result);
        Assert.Equal(CheckResultValue.Fail,
            Assert.Single(promo, c => c.Actual == "[ZZ NOT A REAL TOKEN]").Result);
    }

    [Fact]
    public void QA_SPEC_07_coverage_names_a_missing_required_token()
    {
        // Drop one DS required token; keep the map otherwise complete. Removing the
        // key-value pair with its trailing comma (but not the indentation or newline, so the
        // match is line-ending agnostic) leaves surrounding whitespace, which is valid JSON.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[OUT OF SCOPE USES]\": \"Processing regulated client financial data\",", "");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var checks = new PlaceholderLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        var coverage = Assert.Single(checks, c => c.Result == CheckResultValue.Pass && c.SeverityLevel == Severity.Info);
        Assert.Contains("10 required, 9 provided, 1 missing", coverage.Message);
        Assert.Contains("Missing: [OUT OF SCOPE USES]", coverage.Message);
        // The pre-existing per-token fail for the missing required token is still present.
        Assert.Contains(checks, c => c.Result == CheckResultValue.Fail && c.Message.Contains("[OUT OF SCOPE USES]") && c.Message.Contains("missing"));
    }

    [Fact]
    public void QA_SPEC_07_coverage_counts_extra_canonical_token_as_beyond_required_but_not_a_drift_alias()
    {
        // [SUPPORT CONTACT] is canonical and NOT a DS required token; add it as an extra.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[VERSION]\": \"v1.0\"", "\"[VERSION]\": \"v1.0\", \"[SUPPORT CONTACT]\": \"ORG Service Desk\"");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var coverage = Assert.Single(
            new PlaceholderLayer().Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null)),
            c => c.Result == CheckResultValue.Pass && c.SeverityLevel == Severity.Info);
        Assert.Contains("10 required, 11 provided, 0 missing, 1 beyond the required set", coverage.Message);
        Assert.Contains("Beyond required: [SUPPORT CONTACT]", coverage.Message);

        // A drift alias added instead is a fail, and must NOT be counted as beyond-required.
        var aliasText = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[VERSION]\": \"v1.0\"", "\"[VERSION]\": \"v1.0\", \"[CLIENT]\": \"x\"");
        var aliasAgr = Registrar.Core.Model.AgrParser.Parse(aliasText);
        var aliasCoverage = Assert.Single(
            new PlaceholderLayer().Validate(new SemanticContext(aliasAgr, SemanticTestSupport.Config(), Mode.Draft, null)),
            c => c.Result == CheckResultValue.Pass && c.SeverityLevel == Severity.Info);
        Assert.Contains("0 beyond the required set", aliasCoverage.Message);
        Assert.Contains("Beyond required: none", aliasCoverage.Message);
    }

    [Fact]
    public void QA_SPEC_07_coverage_handles_a_type_with_no_required_placeholders()
    {
        // UQR has requiredPlaceholders: null. This fixture provides 6 canonical tokens.
        var coverage = Assert.Single(
            new PlaceholderLayer().Validate(SemanticTestSupport.Context("agr-sample-uqr-draft.json", Mode.Draft)),
            c => c.Result == CheckResultValue.Pass && c.SeverityLevel == Severity.Info);
        Assert.Contains("0 required, 6 provided, 0 missing, 6 beyond the required set", coverage.Message);
        Assert.Contains("Missing: none", coverage.Message);
    }

    [Fact]
    public void QA_SPEC_07_coverage_check_does_not_change_overall_result()
    {
        var coverage = new PlaceholderLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        // The clean AGR yields only the info/pass coverage line; on its own it must not fail.
        Assert.Equal(OverallResult.Pass, OverallResultPolicy.Compute(coverage, null));

        // Adding the coverage check to a set that already has a real warning must not suppress
        // it, and adding it to a set with a real blocking failure must not rescue it. The check
        // is inert either way: the outcome is driven entirely by the other checks.
        var warn = new Check
        {
            CheckId = "TEST", LayerName = Layer.Content, Message = "synthetic warning",
            Result = CheckResultValue.Warn, SeverityLevel = Severity.Warning
        };
        var blockingFail = new Check
        {
            CheckId = "TEST", LayerName = Layer.Content, Message = "synthetic blocking failure",
            Result = CheckResultValue.Fail, SeverityLevel = Severity.Blocking
        };

        Assert.Equal(OverallResult.PassWithWarnings,
            OverallResultPolicy.Compute(coverage.Concat(new[] { warn }).ToList(), null));
        Assert.Equal(OverallResult.Fail,
            OverallResultPolicy.Compute(coverage.Concat(new[] { blockingFail }).ToList(), null));
    }
}
