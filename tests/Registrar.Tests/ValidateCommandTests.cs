using System.Text.Json.Nodes;
using Registrar.Cli;
using Registrar.Core;

namespace Registrar.Tests;

public class ValidateCommandTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    private static string CopyToTemp(string repoRelative)
    {
        var src = Path.Combine(RepoRoot(), repoRelative);
        var dir = TempDir.Create();
        var dest = Path.Combine(dir, Path.GetFileName(src));
        File.Copy(src, dest);
        return dest;
    }

    [Fact]
    public void Valid_agr_exits_0_and_writes_pass_report()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(0, code);
        var reportPath = Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json");
        Assert.True(File.Exists(reportPath));
        var report = JsonNode.Parse(File.ReadAllText(reportPath))!.AsObject();
        Assert.Equal("pass", (string?)report["overallResult"]);
    }

    [Fact]
    public void Invalid_agr_exits_1_and_writes_fail_report_with_qa_schema_01()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-invalid-schema.json");
        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var reportPath = Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json");
        var report = JsonNode.Parse(File.ReadAllText(reportPath))!.AsObject();
        Assert.Equal("fail", (string?)report["overallResult"]);
        Assert.Contains(report["checks"]!.AsArray(),
            c => (string?)c!["checkId"] == "QA-SCHEMA-01");
    }

    [Fact]
    public void Semantic_invalid_agr_exits_1_with_semantic_check()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-section-mismatch.json");
        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var reportPath = Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json");
        var report = JsonNode.Parse(File.ReadAllText(reportPath))!.AsObject();
        Assert.Equal("fail", (string?)report["overallResult"]);
        Assert.Contains(report["checks"]!.AsArray(), c => (string?)c!["checkId"] == "QA-ENG-02");
    }

    [Fact]
    public void Missing_file_exits_2_and_writes_no_report()
    {
        var dir = TempDir.Create();
        var missing = Path.Combine(dir, "nope.json");
        var code = ValidateCommand.Run(missing, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(2, code);
        Assert.False(File.Exists(Path.Combine(dir, "validation-report.json")));
    }

    [Fact]
    public void Locked_file_exits_2_and_writes_no_report()
    {
        var dir = TempDir.Create();
        var agrPath = Path.Combine(dir, "locked.json");
        File.WriteAllText(agrPath, "{}");

        using (var _ = new FileStream(agrPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

            Assert.Equal(2, code);
            Assert.False(File.Exists(Path.Combine(dir, "validation-report.json")));
        }
    }

    [Fact]
    public void Acl_denied_file_exits_2_and_writes_no_report()
    {
        var dir = TempDir.Create();
        var agrPath = Path.Combine(dir, "denied.json");
        File.WriteAllText(agrPath, "{}");
        var user = Environment.UserName;

        var setupExitCode = RunIcacls($"\"{agrPath}\" /deny \"{user}:(R)\"");
        Assert.Equal(0, setupExitCode);
        try
        {
            var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

            Assert.Equal(2, code);
            Assert.False(File.Exists(Path.Combine(dir, "validation-report.json")));
        }
        finally
        {
            // Restore access so the temp directory can be cleaned up.
            RunIcacls($"\"{agrPath}\" /remove:d \"{user}\"");
        }
    }

    [Fact]
    public void Valid_agr_with_registry_flag_exits_0()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, registryPath);

        Assert.Equal(0, code);
    }

    [Fact]
    public void Unregistered_agr_with_registry_flag_exits_1_with_qa_sf_03()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-unregistered.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, registryPath);

        Assert.Equal(1, code);
        var report = JsonNode.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json")))!.AsObject();
        Assert.Contains(report["checks"]!.AsArray(), c => (string?)c!["checkId"] == "QA-SF-03");
    }

    [Fact]
    public void Illegal_transition_with_registry_fails_with_qa_sf_04_lifecycle()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-illegal-transition.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, registryPath);

        Assert.Equal(1, code);
        var report = JsonNode.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(agrPath)!, "validation-report.json")))!.AsObject();
        var check = Assert.Single(report["checks"]!.AsArray(), c => (string?)c!["checkId"] == "QA-SF-04");
        Assert.Equal("fail", (string?)check!["result"]);
        Assert.Equal("blocking", (string?)check!["severity"]);
        Assert.Equal("LifecycleTransitionError", (string?)check!["errorCategory"]);
    }

    [Fact]
    public void Missing_registry_path_exits_2()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var missing = Path.Combine(Path.GetDirectoryName(agrPath)!, "no-registry.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, missing);

        Assert.Equal(2, code);
    }

    [Fact]
    public void Passing_promotion_writes_promotion_payload()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-promotion.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, registryPath);

        Assert.Equal(0, code);
        var payloadPath = Path.Combine(Path.GetDirectoryName(agrPath)!, "promotion-payload.json");
        Assert.True(File.Exists(payloadPath));
        var payload = JsonNode.Parse(File.ReadAllText(payloadPath))!.AsObject();
        Assert.Equal("In Review", (string?)payload["registryUpdate"]!["toStatus"]);
        Assert.Equal("QA-SPEC-18", (string?)payload["attestation"]!["checkId"]);
    }

    [Fact]
    public void Draft_run_writes_no_promotion_payload()
    {
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-draft.json");
        var registryPath = Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, registryPath);

        Assert.Equal(0, code);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(agrPath)!, "promotion-payload.json")));
    }

    [Fact]
    public void Failing_promotion_writes_no_promotion_payload()
    {
        // promotion fixture with no registry -> QA-SF-03 fails, no payload
        var agrPath = CopyToTemp("fixtures/agr-sample-ds-promotion.json");

        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, null);

        Assert.Equal(1, code);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(agrPath)!, "promotion-payload.json")));
    }

    private static int RunIcacls(string arguments)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("icacls", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    [Fact]
    public void Malformed_json_exits_1_with_single_fail_check()
    {
        var dir = TempDir.Create();
        var agrPath = Path.Combine(dir, "bad.json");
        File.WriteAllText(agrPath, "{ not json ");
        var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

        Assert.Equal(1, code);
        var report = JsonNode.Parse(
            File.ReadAllText(Path.Combine(dir, "validation-report.json")))!.AsObject();
        Assert.Equal("fail", (string?)report["overallResult"]);
        var checks = report["checks"]!.AsArray();
        Assert.Single(checks);
        Assert.Equal("QA-SCHEMA-01", (string?)checks[0]!["checkId"]);
    }

    [Fact]
    public void Unwritable_report_directory_exits_2_and_writes_no_report()
    {
        var dir = TempDir.Create();
        var agrPath = Path.Combine(dir, "agr.json");
        File.Copy(Path.Combine(RepoRoot(), "fixtures", "agr-sample-ds-draft.json"), agrPath);
        var user = Environment.UserName;

        // Deny write on the directory: the AGR still reads, but the report cannot be created.
        var setupExitCode = RunIcacls($"\"{dir}\" /deny \"{user}:(W)\"");
        Assert.Equal(0, setupExitCode);
        try
        {
            var code = ValidateCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null);

            Assert.Equal(2, code);
            Assert.False(File.Exists(Path.Combine(dir, "validation-report.json")));
        }
        finally
        {
            // Restore access so the temp directory can be cleaned up.
            RunIcacls($"\"{dir}\" /remove:d \"{user}\"");
        }
    }
}
