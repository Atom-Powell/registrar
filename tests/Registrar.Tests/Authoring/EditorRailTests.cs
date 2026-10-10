using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Model;

namespace Registrar.Tests.Authoring;

public class EditorRailTests
{
    private static Check Check(string result, string severity, string message, string? path)
        => new()
        {
            CheckId = "QA-SF-08", LayerName = "content", Result = result, SeverityLevel = severity,
            Message = message, JsonPath = path, Remediation = "Fix it."
        };

    [Fact]
    public void Gaps_block_the_build_and_say_how_many_remain()
    {
        var state = EditorRail.FromGaps(new[]
        {
            new AuthoringGap(FieldPath.Owner, "Owner is required."),
            new AuthoringGap(FieldPath.SectionBlocks(2), "Controls has no content yet.")
        });

        Assert.False(state.BuildEnabled);
        Assert.Equal(2, state.Items.Count);
        Assert.All(state.Items, i => Assert.Equal(RailSeverity.Gap, i.Severity));
        Assert.Contains("2 fields", state.BuildBlockedReason, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rail_item_carries_the_element_id_its_pointer_derives()
    {
        var state = EditorRail.FromGaps(new[] { new AuthoringGap(FieldPath.Owner, "Owner is required.") });

        Assert.Equal(FieldPath.ElementId(FieldPath.Owner), state.Items.Single().ElementId);
    }

    // Not every check names a field. Those items still appear; they simply are not
    // clickable, which is honest rather than scrolling nowhere.
    [Fact]
    public void A_check_with_no_pointer_produces_an_item_with_no_target()
    {
        var state = EditorRail.FromChecks(
            new[] { Check(CheckResultValue.Fail, Severity.Blocking, "Something global.", null) },
            OverallResult.Fail);

        Assert.Null(state.Items.Single().ElementId);
    }

    [Fact]
    public void Passing_checks_are_not_shown_and_the_build_is_enabled()
    {
        var state = EditorRail.FromChecks(
            new[]
            {
                Check(CheckResultValue.Pass, Severity.Blocking, "All good.", null),
                Check(CheckResultValue.Skipped, Severity.Warning, "No registry loaded.", null)
            },
            OverallResult.Pass);

        Assert.Empty(state.Items);
        Assert.True(state.BuildEnabled);
        Assert.Equal("", state.BuildBlockedReason);
    }

    [Fact]
    public void A_warning_is_shown_without_blocking_the_build()
    {
        var state = EditorRail.FromChecks(
            new[] { Check(CheckResultValue.Warn, Severity.Warning, "Unknown token.", FieldPath.PlaceholderMap) },
            OverallResult.PassWithWarnings);

        Assert.Equal(RailSeverity.Warning, state.Items.Single().Severity);
        Assert.True(state.BuildEnabled);
    }

    [Fact]
    public void A_failing_check_blocks_the_build_and_the_reason_names_the_check()
    {
        var state = EditorRail.FromChecks(
            new[] { Check(CheckResultValue.Fail, Severity.Blocking, "Prohibited character.", FieldPath.Owner) },
            OverallResult.Fail);

        Assert.False(state.BuildEnabled);
        Assert.Equal(RailSeverity.Failure, state.Items.Single().Severity);
        Assert.Contains("QA-SF-08", state.BuildBlockedReason, StringComparison.Ordinal);
    }

    // Warnings can exceed a configured threshold, making the overall result Fail while
    // leaving only Warning-severity items on the rail. The message must name the warnings,
    // not claim "failure" which would contradict the rail.
    [Fact]
    public void Warnings_exceeding_the_threshold_block_the_build_and_name_the_cause()
    {
        var state = EditorRail.FromChecks(
            new[]
            {
                Check(CheckResultValue.Warn, Severity.Warning, "Unknown token in field A.", FieldPath.PlaceholderMap),
                Check(CheckResultValue.Warn, Severity.Warning, "Unknown token in field B.", FieldPath.Client)
            },
            OverallResult.Fail);

        Assert.False(state.BuildEnabled);
        Assert.All(state.Items, i => Assert.Equal(RailSeverity.Warning, i.Severity));
        Assert.Contains("2 warnings", state.BuildBlockedReason, StringComparison.Ordinal);
        Assert.Contains("threshold", state.BuildBlockedReason, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual("The engine reported a failure.", state.BuildBlockedReason);
    }

    // A blank rail must never be ambiguous between "nothing is wrong" and "we did not
    // check". This is the same failure GUI-6 fixed on the Engagement screen.
    [Fact]
    public void An_engine_error_fills_the_rail_rather_than_leaving_it_empty()
    {
        var state = EditorRail.FromEngineError("Config failed to reload: access denied.");

        Assert.Equal(RailSeverity.EngineError, state.Items.Single().Severity);
        Assert.False(state.BuildEnabled);
        Assert.Contains("could not be checked", state.BuildBlockedReason, StringComparison.Ordinal);
    }
}
