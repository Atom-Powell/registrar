namespace Registrar.Tests.Pipeline;

public class DoctorPresenterTests
{
    private static DoctorReport Report(params DoctorCheck[] checks) => new(checks);

    [Fact]
    public void A_healthy_report_ends_with_a_healthy_line_in_pass_style()
    {
        var lines = DoctorPresenter.Present(
            Report(new DoctorCheck("config load", DoctorStatus.Ok, "loaded 6 config file(s)")), registryChosen: false);

        Assert.Contains(lines, l => l.Text.StartsWith("OK", StringComparison.Ordinal) && l.Text.Contains("config load"));
        var last = lines[^1];
        Assert.Equal(LineStyle.Pass, last.Style);
        Assert.Equal("Healthy: everything the Registrar needs is in place.", last.Text);
    }

    [Fact]
    public void A_failing_check_makes_the_report_not_healthy_and_is_styled_as_a_failure()
    {
        var lines = DoctorPresenter.Present(
            Report(new DoctorCheck("schemas", DoctorStatus.Fail, "cannot find agr.schema.json")), registryChosen: true);

        Assert.Contains(lines, l => l.Style == LineStyle.Fail && l.Text.Contains("cannot find agr.schema.json"));
        Assert.Equal(LineStyle.Fail, lines[^1].Style);
        Assert.StartsWith("Not healthy", lines[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_command_line_registry_hint_is_replaced_with_window_wording()
    {
        var cliHint = new DoctorCheck("registry", DoctorStatus.Info,
            "no registry supplied (file-system mode); pass --registry <csv> to check an export");

        var lines = DoctorPresenter.Present(Report(cliHint), registryChosen: false);

        Assert.DoesNotContain(lines, l => l.Text.Contains("--registry", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Text.Contains("none chosen", StringComparison.Ordinal));
    }

    [Fact]
    public void Warnings_are_styled_as_warnings_and_do_not_fail_the_report()
    {
        var lines = DoctorPresenter.Present(
            Report(new DoctorCheck("webview user data", DoctorStatus.Warn, "folder is not writable")), registryChosen: false);

        Assert.Contains(lines, l => l.Style == LineStyle.Warn);
        Assert.Equal(LineStyle.Pass, lines[^1].Style);
    }
}
