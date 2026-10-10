using Registrar.Cli;
using Registrar.Core;
using Registrar.Core.Config;

namespace Registrar.Tests;

public class DoctorTests
{
    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    [Fact]
    public void Healthy_repo_passes_every_diagnostic()
    {
        var report = Doctor.Run(RepoRoot(), registryPath: null);
        Assert.True(report.Healthy,
            string.Join("\n", report.Checks.Select(c => $"{c.Status} {c.Name}: {c.Detail}")));
        Assert.DoesNotContain(report.Checks, c => c.Status == DoctorStatus.Fail);
    }

    [Fact]
    public void Healthy_repo_reports_the_expected_diagnostics()
    {
        var names = Doctor.Run(RepoRoot(), registryPath: null).Checks.Select(c => c.Name).ToList();
        Assert.Contains("config load", names);
        Assert.Contains("schemas", names);
        Assert.Contains("registry", names);
        Assert.Contains("placeholder alignment", names);
        Assert.Contains("clause hashes", names);
    }

    [Fact]
    public void Clause_hash_mismatch_is_a_failure()
    {
        var tampered = new[]
        {
            new ClauseEntry { ClauseId = "CL-001", CanonicalText = "Hello.", CanonicalTextSha256 = "deadbeef" }
        };
        var check = Doctor.ClauseHashes(tampered);
        Assert.Equal(DoctorStatus.Fail, check.Status);
        Assert.Contains("CL-001", check.Detail);
    }

    [Fact]
    public void Matching_clause_hash_is_ok()
    {
        const string text = "Hello.";
        var good = new[]
        {
            new ClauseEntry { ClauseId = "CL-001", CanonicalText = text, CanonicalTextSha256 = Hashing.Sha256Hex(text) }
        };
        Assert.Equal(DoctorStatus.Ok, Doctor.ClauseHashes(good).Status);
    }

    [Fact]
    public void Placeholder_alignment_flags_a_required_token_absent_from_the_dictionary()
    {
        var types = new[]
        {
            new ArtifactTypeConfig { Code = "XX", Category = "GOV", RequiredPlaceholders = new[] { "[GHOST TOKEN]" } }
        };
        var check = Doctor.PlaceholderAlignment(types, new HashSet<string> { "[DATE]" });
        Assert.Equal(DoctorStatus.Fail, check.Status);
        Assert.Contains("[GHOST TOKEN]", check.Detail);
    }

    [Fact]
    public void Placeholder_alignment_ok_when_every_required_token_is_canonical()
    {
        var types = new[]
        {
            new ArtifactTypeConfig { Code = "XX", Category = "GOV", RequiredPlaceholders = new[] { "[DATE]" } }
        };
        var check = Doctor.PlaceholderAlignment(types, new HashSet<string> { "[DATE]", "[VERSION]" });
        Assert.Equal(DoctorStatus.Ok, check.Status);
    }

    [Fact]
    public void Bad_registry_path_is_a_failure()
    {
        var missing = Path.Combine(Path.GetTempPath(), "registrar-doctor-no-such-registry.csv");
        var report = Doctor.Run(RepoRoot(), registryPath: missing);
        Assert.Contains(report.Checks, c => c.Name == "registry" && c.Status == DoctorStatus.Fail);
        Assert.False(report.Healthy);
    }

    [Fact]
    public void Valid_registry_export_is_ok()
    {
        var registry = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");
        var report = Doctor.Run(RepoRoot(), registryPath: registry);
        Assert.Contains(report.Checks, c => c.Name == "registry" && c.Status == DoctorStatus.Ok);
    }

    [Fact]
    public void Doctor_runs_the_webview_user_data_check()
    {
        var report = Doctor.Run(RepoRoot(), registryPath: null);

        Assert.Single(report.Checks, c => c.Name == "webview user data");
    }

    // Against a root the test owns, not the developer's real profile. Both of these used
    // to resolve %LOCALAPPDATA% and write into it, which is why the check gained an
    // injectable root.
    [Fact]
    public void A_writable_user_data_root_is_ok()
    {
        var root = TempDir.Create();
        try
        {
            var check = Doctor.WebViewUserData(root);

            Assert.Equal(DoctorStatus.Ok, check.Status);
            Assert.Contains(WebViewEnvironment.UserDataFolder(root), check.Detail, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Warn, not Fail: a redirected or locked-down %LOCALAPPDATA% stops the optional
    // window from opening and nothing else, so doctor must still exit 0 on a machine
    // where validate, build and promote are healthy.
    [Fact]
    public void An_unwritable_user_data_root_warns_rather_than_failing()
    {
        var root = TempDir.Create();
        try
        {
            // A file occupying the directory name the check must create. Deterministic
            // and needs no ACL manipulation: Directory.CreateDirectory throws IOException
            // when a file of that name already exists.
            var blocker = Path.GetDirectoryName(WebViewEnvironment.UserDataFolder(root))!;
            File.WriteAllText(blocker, string.Empty);

            var check = Doctor.WebViewUserData(root);

            Assert.Equal(DoctorStatus.Warn, check.Status);
            Assert.Contains("not writable", check.Detail, StringComparison.Ordinal);

            var report = new DoctorReport(new[] { check });
            Assert.True(report.Healthy);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void An_undeterminable_user_data_root_warns_rather_than_failing()
    {
        var check = Doctor.WebViewUserData("   ");

        Assert.Equal(DoctorStatus.Warn, check.Status);
        Assert.True(new DoctorReport(new[] { check }).Healthy);
    }

    [Fact]
    public void A_probe_cleanup_failure_does_not_flip_the_verdict_to_not_writable()
    {
        var root = TempDir.Create();
        try
        {
            var folder = WebViewEnvironment.UserDataFolder(root);
            Directory.CreateDirectory(folder);
            var probe = Path.Combine(folder, ".registrar-write-probe");

            using var handle = new FileStream(
                probe, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite);

            Assert.Equal(DoctorStatus.Ok, Doctor.WebViewUserData(root).Status);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
