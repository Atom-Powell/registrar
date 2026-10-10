using Registrar.Pipeline;

namespace Registrar.App.Tests;

// For the shapes the real runner makes awkward to produce: a canned exit 2, or a runner
// that throws despite GuardedRun (the page must survive that too).
internal sealed class FakeAgrRunner : IAgrRunner
{
    public AgrFileRunResult? Canned { get; set; }
    public Exception? Throws { get; set; }
    public List<string> Calls { get; } = new();

    // When set, a run blocks here until the test releases it, so a test can look at the page mid-run.
    public ManualResetEventSlim? Gate { get; set; }

    public AgrFileRunResult Check(string agrPath, string? registryPath) => Respond("check", agrPath, registryPath);
    public AgrFileRunResult Build(string agrPath, string? registryPath) => Respond("build", agrPath, registryPath);

    private AgrFileRunResult Respond(string verb, string agrPath, string? registryPath)
    {
        Calls.Add(verb + ":" + agrPath + ":" + (registryPath ?? ""));
        Gate?.Wait();
        if (Throws is not null) throw Throws;
        return Canned ?? throw new InvalidOperationException("FakeAgrRunner has no canned result.");
    }

    public static AgrFileRunResult UsageError(string text, string dir) => new(
        2, new[] { new RunMessage(RunStream.Err, text) }, null, dir,
        Array.Empty<string>(), Array.Empty<string>(), null);
}
