namespace Registrar.Tests.Pipeline;

public class GuardedRunTests
{
    [Fact]
    public void A_normal_result_passes_through_untouched()
    {
        var ok = new AgrFileRunResult(0, Array.Empty<RunMessage>(), null, "d",
            Array.Empty<string>(), Array.Empty<string>(), null);

        Assert.Same(ok, GuardedRun.Execute("x.json", () => ok));
    }

    [Fact]
    public void An_exception_becomes_an_exit_2_result_with_a_readable_error_and_no_report()
    {
        var path = Path.Combine(TempDir.Create(), "a.json");

        var r = GuardedRun.Execute(path, () => throw new InvalidOperationException("config tree is broken"));

        Assert.Equal(2, r.ExitCode);
        Assert.Null(r.Report);
        Assert.Contains("config tree is broken", r.ErrorText, StringComparison.Ordinal);
        Assert.StartsWith("error: ", r.ErrorText, StringComparison.Ordinal);
        Assert.Equal(Path.GetDirectoryName(path), r.OutputDirectory);
        Assert.Empty(r.Written);
    }
}
