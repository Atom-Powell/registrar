using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class ReviewDateLayerTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void QA_SF_05_passes_when_review_date_null_in_draft()
    {
        var checks = new ReviewDateLayer(new FixedClock())
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SF_05_fails_when_review_date_null_in_promotion()
    {
        var checks = new ReviewDateLayer(new FixedClock())
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Promotion));

        var check = Assert.Single(checks);
        Assert.Equal("QA-SF-05", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
    }

    [Fact]
    public void QA_SF_05_fails_when_review_date_exceeds_category_interval()
    {
        var checks = new ReviewDateLayer(new FixedClock())
            .Validate(SemanticTestSupport.Context("agr-sample-ds-review-stale.json", Mode.Draft));

        var check = Assert.Single(checks);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal("2030-01-01", check.Actual);
    }

    [Fact]
    public void QA_SF_05_fails_when_review_date_is_not_iso_8601()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"reviewDate\": null", "\"reviewDate\": \"2026-13-40\"");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);
        var config = SemanticTestSupport.Config();

        var checks = new ReviewDateLayer(new FixedClock())
            .Validate(new SemanticContext(agr, config, Mode.Draft, null));

        var check = Assert.Single(checks);
        Assert.Equal("QA-SF-05", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("/context/reviewDate", check.JsonPath);
        Assert.Equal("2026-13-40", check.Actual);
    }

    [Fact]
    public void QA_SF_05_passes_when_review_date_override_present_despite_stale_date()
    {
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-review-stale.json")
            .Replace(
                "\"reviewDateOverride\": null",
                "\"reviewDateOverride\": { \"approvedBy\": \"Doc Owner\", \"approvalDate\": \"2026-07-01\", \"justification\": \"Extended per documentation-owner sign-off\" }");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);
        var config = SemanticTestSupport.Config();

        var checks = new ReviewDateLayer(new FixedClock())
            .Validate(new SemanticContext(agr, config, Mode.Draft, null));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_SF_05_fails_when_override_approval_date_is_not_a_valid_calendar_date()
    {
        // approvalDate passes the schema's date regex (^\d{4}-\d{2}-\d{2}$) but is not a
        // real calendar date. The override must not silently bypass the interval check on
        // a malformed sign-off date; the engine strict-parses it and fails closed.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-review-stale.json")
            .Replace(
                "\"reviewDateOverride\": null",
                "\"reviewDateOverride\": { \"approvedBy\": \"Doc Owner\", \"approvalDate\": \"2026-13-45\", \"justification\": \"Extended per documentation-owner sign-off\" }");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);
        var config = SemanticTestSupport.Config();

        var checks = new ReviewDateLayer(new FixedClock())
            .Validate(new SemanticContext(agr, config, Mode.Draft, null));

        var check = Assert.Single(checks);
        Assert.Equal("QA-SF-05", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("/context/reviewDateOverride/approvalDate", check.JsonPath);
        Assert.Equal("2026-13-45", check.Actual);
    }
}
