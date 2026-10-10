using Registrar.Core.Model;

namespace Registrar.Core.Authoring;

public enum RailSeverity
{
    // An unfinished field, not a governance violation. Distinct from Failure because a
    // blank box and a rejected value are different problems for the technician.
    Gap,
    Warning,
    Failure,
    EngineError
}

public sealed record RailItem(
    string? JsonPath,
    string? ElementId,
    RailSeverity Severity,
    string Headline,
    string? Detail);

public sealed record RailState(
    IReadOnlyList<RailItem> Items,
    bool BuildEnabled,
    string BuildBlockedReason);

// The right-hand rail, and the sentence under the Build button.
//
// A draft is in exactly one of three states, so the rail never mixes them: it will not
// convert (gaps), it converted and the engine reported (checks), or the engine could not
// run (error). Gaps and checks cannot both be present, because ToDocument returns no
// document when it returns gaps.
public static class EditorRail
{
    public static RailState FromGaps(IReadOnlyList<AuthoringGap> gaps)
    {
        var items = gaps
            .Select(g => new RailItem(g.JsonPath, FieldPath.ElementId(g.JsonPath), RailSeverity.Gap, g.Message, null))
            .ToList();

        var reason = items.Count == 1
            ? "1 field still needs filling in before this can be built."
            : $"{items.Count} fields still need filling in before this can be built.";

        return new RailState(items, false, reason);
    }

    public static RailState FromChecks(IReadOnlyList<Check> checks, string overallResult)
    {
        var items = checks
            .Where(c => c.Result is CheckResultValue.Fail or CheckResultValue.Warn)
            .Select(c => new RailItem(
                c.JsonPath,
                c.JsonPath is null ? null : FieldPath.ElementId(c.JsonPath),
                c.Result == CheckResultValue.Fail ? RailSeverity.Failure : RailSeverity.Warning,
                c.Message,
                c.Remediation))
            .ToList();

        var blocking = checks
            .Where(c => c.Result == CheckResultValue.Fail && c.SeverityLevel == Severity.Blocking)
            .Select(c => c.CheckId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var enabled = overallResult != OverallResult.Fail && blocking.Count == 0;

        var reason = enabled
            ? ""
            : blocking.Count > 0
                ? "Blocked by " + string.Join(", ", blocking) + "."
                : "Blocked by " + items.Count(i => i.Severity == RailSeverity.Warning) + " warnings exceeding the configured threshold for this run.";

        return new RailState(items, enabled, reason);
    }

    // Never let a blank rail read as a clean one.
    public static RailState FromEngineError(string message)
        => new(
            new[] { new RailItem(null, null, RailSeverity.EngineError, "The engine could not check this draft.", message) },
            false,
            "This draft could not be checked, so Build is not available.");
}
