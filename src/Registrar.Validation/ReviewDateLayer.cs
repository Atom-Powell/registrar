using System.Globalization;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class ReviewDateLayer : ISemanticLayer
{
    public const string CheckId = "QA-SF-05";
    private const string RuleSourceValue = "ORG-MST-WFE-001 4.5 item 5";
    private readonly IClock _clock;

    public ReviewDateLayer(IClock clock) => _clock = clock;

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var reviewDate = context.Agr.Context.ReviewDate;

        if (reviewDate is null)
        {
            if (context.Mode == Mode.Promotion)
                return new[] { Fail("Review Date is required at promotion.",
                    "an ISO 8601 date", "null",
                    "Set context.reviewDate to an ISO 8601 date within the category review interval before promotion.") };
            return Array.Empty<Check>();
        }

        if (!DateOnly.TryParseExact(reviewDate, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return new[] { Fail("Review Date is not a valid ISO 8601 (yyyy-MM-dd) date.",
                "yyyy-MM-dd", reviewDate,
                "Correct context.reviewDate to an ISO 8601 date, for example 2026-12-01.") };

        var over = context.Agr.Context.ReviewDateOverride;
        if (over is not null)
        {
            // The override authorizes an interval beyond the category default, so a well-formed
            // one deliberately bypasses the interval check below. But its approvalDate reaches
            // this layer having passed only the schema's date regex (^\d{4}-\d{2}-\d{2}$), which
            // admits impossible dates like 2026-13-45. Strict-parse it so a malformed sign-off
            // date fails closed rather than waving the interval check through.
            if (!DateOnly.TryParseExact(over.ApprovalDate, "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return new[] { Fail(
                    "Review Date Override approvalDate is not a valid ISO 8601 (yyyy-MM-dd) date.",
                    "yyyy-MM-dd", over.ApprovalDate,
                    "Correct context.reviewDateOverride.approvalDate to a real ISO 8601 date, for example 2026-07-01.",
                    "/context/reviewDateOverride/approvalDate") };
            return Array.Empty<Check>();
        }

        // Deliberately fail-open here: a malformed or absent category on documentId is
        // already caught by schema validation (QA-SCHEMA-01) and MetadataLayer (QA-ENG-03),
        // so this layer does not double-report it.
        var category = CategoryOf(context.Agr.Identity.DocumentId);
        if (category is null ||
            !context.Config.ReviewIntervalMonthsByCategory.TryGetValue(category, out var months))
            return Array.Empty<Check>();

        var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
        var maxDate = today.AddMonths(months);
        if (parsed > maxDate)
        {
            var max = maxDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return new[] { Fail(
                $"Review Date {reviewDate} exceeds the {category} review interval of {months} months.",
                $"a date on or before {max}", reviewDate,
                $"Set context.reviewDate on or before {max}, or attach a reviewDateOverride approved by the documentation owner.") };
        }

        return Array.Empty<Check>();
    }

    private static string? CategoryOf(string documentId)
    {
        var parts = documentId.Split('-');
        return parts.Length >= 2 ? parts[1] : null;
    }

    private static Check Fail(string message, string expected, string actual, string remediation,
        string jsonPath = "/context/reviewDate") => new()
    {
        CheckId = CheckId,
        LayerName = Layer.Input,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.MetadataViolation,
        Message = message,
        JsonPath = jsonPath,
        Expected = expected,
        Actual = actual,
        Remediation = remediation,
        RuleSource = RuleSourceValue
    };
}
