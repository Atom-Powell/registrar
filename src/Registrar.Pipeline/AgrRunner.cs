using Registrar.Core;

namespace Registrar.Pipeline;

// The two things the window can do to an AGR, behind an interface so the page is tested
// against a fake for the failure shapes and against the real runner for the happy paths.
public interface IAgrRunner
{
    AgrFileRunResult Check(string agrPath, string? registryPath);
    AgrFileRunResult Build(string agrPath, string? registryPath);
}

public sealed class AgrRunner : IAgrRunner
{
    private readonly IClock _clock;

    public AgrRunner(IClock clock) => _clock = clock;

    public AgrFileRunResult Check(string agrPath, string? registryPath)
        => GuardedRun.Execute(agrPath, () => AgrFileRun.Validate(agrPath, _clock, registryPath: registryPath));

    // mode is passed as null on purpose: the window shows the AGR's own declared mode and
    // builds in it, so a disagreement cannot be expressed.
    public AgrFileRunResult Build(string agrPath, string? registryPath)
        => GuardedRun.Execute(agrPath, () => AgrFileRun.Build(agrPath, _clock, mode: null, registryPath: registryPath));
}
