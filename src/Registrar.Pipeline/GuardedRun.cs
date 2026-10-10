namespace Registrar.Pipeline;

// AgrFileRun is not total: a broken config or schema tree throws out of EngineContext.Load,
// exactly as it does in the CLI, where the exception ends the process with a message. In the
// window an exception that escapes a Blazor handler takes the whole process down, so this is
// the boundary that turns any failure into the same shape every other failure has: exit 2,
// no report, an "error:" line the operator can read.
//
// Catching Exception here is deliberate and narrow in purpose: it is the last frame before
// the UI, it records the message rather than swallowing it, and it reports no files as
// written; a step that threw midway may have left partial files.
public static class GuardedRun
{
    public static AgrFileRunResult Execute(string agrPath, Func<AgrFileRunResult> run)
    {
        try
        {
            return run();
        }
        catch (Exception ex)
        {
            return new AgrFileRunResult(
                2,
                new[] { new RunMessage(RunStream.Err, $"error: the run could not complete: {ex.Message}") },
                null,
                DirectoryOf(agrPath),
                Array.Empty<string>(),
                Array.Empty<string>(),
                null);
        }
    }

    private static string DirectoryOf(string agrPath)
    {
        try { return Path.GetDirectoryName(Path.GetFullPath(agrPath)) ?? ""; }
        catch { return ""; }
    }
}
