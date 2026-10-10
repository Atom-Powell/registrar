using Registrar.Core;

namespace Registrar.Cli;

public static class DoctorCommand
{
    // Exit codes: 0 healthy, 1 one or more failing diagnostics, 2 usage/IO error.
    public static int Run(TextWriter stdout, TextWriter stderr, string? registryPath = null, string? repoRootOverride = null)
    {
        string repoRoot;
        if (repoRootOverride is not null)
        {
            repoRoot = repoRootOverride;
        }
        else
        {
            try
            {
                var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
                repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
            }
            catch (FileNotFoundException ex)
            {
                stderr.WriteLine($"error: cannot locate the repository root (schemas/agr.schema.json): {ex.Message}");
                return 2;
            }
        }

        var report = Doctor.Run(repoRoot, registryPath);
        foreach (var c in report.Checks)
        {
            var tag = c.Status switch
            {
                DoctorStatus.Ok => "OK",
                DoctorStatus.Info => "INFO",
                DoctorStatus.Warn => "WARN",
                _ => "FAIL"
            };
            stdout.WriteLine($"{tag,-5} {c.Name,-22} {c.Detail}");
        }

        stdout.WriteLine();
        stdout.WriteLine(report.Healthy
            ? "doctor: healthy"
            : "doctor: FAILED - one or more diagnostics failed");
        return report.Healthy ? 0 : 1;
    }
}
