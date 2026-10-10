using System.Text.Json;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Validation;

namespace Registrar.Pipeline;

public enum DoctorStatus { Ok, Info, Warn, Fail }

public sealed record DoctorCheck(string Name, DoctorStatus Status, string Detail);

public sealed record DoctorReport(IReadOnlyList<DoctorCheck> Checks)
{
    public bool Healthy => Checks.All(c => c.Status != DoctorStatus.Fail);
}

/// <summary>
/// Runtime self-diagnostics. Confirms the engine's config, schemas, registry, and
/// clause library are internally consistent without running a build - so an operator
/// (or a non-C# agent) can prove the installation is healthy before trusting output.
/// </summary>
public static class Doctor
{
    public static DoctorReport Run(string repoRoot, string? registryPath)
    {
        var checks = new List<DoctorCheck>();

        ConfigBundle? config = null;
        try
        {
            config = ConfigLoader.Load(repoRoot);
            checks.Add(new DoctorCheck("config load", DoctorStatus.Ok,
                $"loaded {config.Hashes.Count} config file(s) from {Path.Combine(repoRoot, "config")}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException
            or DirectoryNotFoundException or JsonException or InvalidOperationException)
        {
            checks.Add(new DoctorCheck("config load", DoctorStatus.Fail, $"cannot load config: {ex.Message}"));
        }

        checks.Add(Schemas(repoRoot));
        checks.Add(Registry(registryPath));

        if (config is not null)
        {
            checks.Add(PlaceholderAlignment(
                config.ArtifactTypesByCode.Values,
                config.CanonicalTokens.Select(t => t.Token).ToHashSet()));
            checks.Add(ClauseHashes(config.ClausesById.Values));
        }
        else
        {
            checks.Add(new DoctorCheck("placeholder alignment", DoctorStatus.Fail, "skipped: config did not load"));
            checks.Add(new DoctorCheck("clause hashes", DoctorStatus.Fail, "skipped: config did not load"));
        }

        checks.Add(WebViewUserData());

        return new DoctorReport(checks);
    }

    // The WebView2 user-data folder is where a read-only or network install fails, and
    // it fails as a blank window with no message. Checking it here means a technician
    // whose window will not open has a command that says why.
    //
    // Warn, not Fail. Before this check existed, a doctor Fail meant the engine could not
    // be trusted to produce output. This condition says only that one optional interface
    // will not open: a redirected or locked-down %LOCALAPPDATA% is an ordinary enterprise
    // profile configuration, and validate, build and promote are all unaffected by it.
    // Reporting it as Fail would print "doctor: FAILED" and exit nonzero on a healthy
    // engine.
    //
    // localAppDataRoot defaults to the real profile root, which is what Run passes. It is
    // a parameter so a test can point the check at a location it controls, including an
    // unwritable one, without touching the developer's own profile.
    public static DoctorCheck WebViewUserData(string? localAppDataRoot = null)
    {
        string folder;
        try
        {
            folder = WebViewEnvironment.UserDataFolder(
                localAppDataRoot
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        }
        catch (ArgumentException ex)
        {
            return new DoctorCheck("webview user data", DoctorStatus.Warn,
                $"cannot determine the user data folder: {ex.Message}");
        }

        string probe = Path.Combine(folder, ".registrar-write-probe");
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(probe, string.Empty);

            // Deleting the probe is best effort: a real-time antivirus scanner briefly
            // locking a just-written file is not the operator's problem, and must not
            // turn a demonstrably writable folder into a false "not writable" verdict.
            try
            {
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignored: the write above already proved the folder is writable.
            }

            return new DoctorCheck("webview user data", DoctorStatus.Ok, $"writable: {folder}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DoctorCheck("webview user data", DoctorStatus.Warn,
                $"not writable, so the app window will not open: {folder} ({ex.Message})");
        }
    }

    private static DoctorCheck Schemas(string repoRoot)
    {
        var names = new[] { "agr.schema.json", "validation-report.schema.json", "promotion-payload.schema.json" };
        var problems = new List<string>();
        var ok = 0;
        foreach (var name in names)
        {
            try
            {
                var path = ConfigLocator.FindSchema(repoRoot, name);
                IsolatedSchema.FromText(File.ReadAllText(path));
                ok++;
            }
            catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException
                or JsonException or InvalidOperationException)
            {
                problems.Add($"{name}: {ex.Message}");
            }
        }
        return problems.Count == 0
            ? new DoctorCheck("schemas", DoctorStatus.Ok, $"{ok} schema(s) parse and build")
            : new DoctorCheck("schemas", DoctorStatus.Fail, string.Join("; ", problems));
    }

    private static DoctorCheck Registry(string? registryPath)
    {
        if (registryPath is null)
            return new DoctorCheck("registry", DoctorStatus.Info,
                "no registry supplied (file-system mode); pass --registry <csv> to check an export");

        if (!File.Exists(registryPath))
            return new DoctorCheck("registry", DoctorStatus.Fail, $"registry file not found: {registryPath}");

        try
        {
            CsvIdRegistry.FromCsvText(File.ReadAllText(registryPath));
            return new DoctorCheck("registry", DoctorStatus.Ok, $"loaded and parsed {Path.GetFileName(registryPath)}");
        }
        catch (RegistryFormatException ex)
        {
            return new DoctorCheck("registry", DoctorStatus.Fail, $"registry export is malformed: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DoctorCheck("registry", DoctorStatus.Fail, $"cannot read registry: {ex.Message}");
        }
    }

    public static DoctorCheck ClauseHashes(IEnumerable<ClauseEntry> clauses)
    {
        var mismatches = new List<string>();
        var count = 0;
        foreach (var clause in clauses)
        {
            count++;
            var actual = Hashing.Sha256Hex(clause.CanonicalText);
            if (!string.Equals(actual, clause.CanonicalTextSha256, StringComparison.OrdinalIgnoreCase))
                mismatches.Add(clause.ClauseId);
        }
        return mismatches.Count == 0
            ? new DoctorCheck("clause hashes", DoctorStatus.Ok, $"{count} clause(s) verified (SHA-256)")
            : new DoctorCheck("clause hashes", DoctorStatus.Fail,
                $"SHA-256 mismatch for: {string.Join(", ", mismatches)}");
    }

    public static DoctorCheck PlaceholderAlignment(
        IEnumerable<ArtifactTypeConfig> types, IReadOnlySet<string> canonicalTokens)
    {
        var missing = new List<string>();
        var required = 0;
        foreach (var type in types)
        {
            if (type.RequiredPlaceholders is null) continue;
            foreach (var token in type.RequiredPlaceholders)
            {
                required++;
                if (!canonicalTokens.Contains(token))
                    missing.Add($"{token} (required by {type.Code})");
            }
        }
        return missing.Count == 0
            ? new DoctorCheck("placeholder alignment", DoctorStatus.Ok,
                $"{required} required placeholder(s) all present in placeholder-dictionary.json")
            : new DoctorCheck("placeholder alignment", DoctorStatus.Fail,
                $"required placeholder(s) missing from dictionary: {string.Join("; ", missing)}");
    }
}
