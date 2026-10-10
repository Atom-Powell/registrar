using Registrar.Core;

namespace Registrar.Cli;

public static class BuildCommand
{
    // Exit codes: 0 built or cleanly not built, 1 validation fail, 2 usage/IO error.
    // The logic lives in Registrar.Pipeline.AgrFileRun so the operator window runs the same
    // code; this wrapper replays its recorded output so the console is unchanged.
    public static int Run(string agrPath, IClock clock, TextWriter stdout, TextWriter stderr,
        string? mode = null, string? registryPath = null)
    {
        var result = AgrFileRun.Build(agrPath, clock, mode, registryPath);
        result.Replay(stdout, stderr);
        return result.ExitCode;
    }
}
