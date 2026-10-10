namespace Registrar.Pipeline;

// How the health check reads in the window. The console prints Doctor's own text, which
// tells the reader to pass a command-line option; a window operator cannot act on that, so
// the one hint that names a CLI flag is replaced here. The CLI output is untouched because
// this class is only called by the window.
public static class DoctorPresenter
{
    private const string CliRegistryHint = "--registry";

    public static IReadOnlyList<PresenterLine> Present(DoctorReport report, bool registryChosen)
    {
        var lines = new List<PresenterLine>();

        foreach (var check in report.Checks)
        {
            var (tag, style) = check.Status switch
            {
                DoctorStatus.Ok => ("OK", LineStyle.Pass),
                DoctorStatus.Info => ("INFO", LineStyle.Normal),
                DoctorStatus.Warn => ("WARN", LineStyle.Warn),
                _ => ("FAIL", LineStyle.Fail)
            };

            var detail = check.Status == DoctorStatus.Info
                && !registryChosen
                && check.Detail.Contains(CliRegistryHint, StringComparison.Ordinal)
                    ? "none chosen (optional). Choose a registry file above to check it."
                    : check.Detail;

            lines.Add(new PresenterLine($"{tag,-5} {check.Name,-22} {detail}", style));
        }

        lines.Add(new PresenterLine("", LineStyle.Normal));
        lines.Add(report.Healthy
            ? new PresenterLine("Healthy: everything the Registrar needs is in place.", LineStyle.Pass)
            : new PresenterLine("Not healthy: one or more checks failed. Send this list to whoever supports you.", LineStyle.Fail));

        return lines;
    }
}
