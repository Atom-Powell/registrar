using Registrar.Cli;
using Registrar.Core;

namespace Registrar.Tests;

public class FixtureTestRunnerTests
{
    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    private static string TempExpectedRoot() => TempDir.Create();

    [Fact]
    public void Update_then_verify_round_trips_to_a_pass()
    {
        var repo = RepoRoot();
        var expected = TempExpectedRoot();

        var written = FixtureTestRunner.Run("agr-sample-ds-draft", repo, expected, update: true);
        Assert.True(written.Passed, string.Join("\n", written.Messages));

        var verified = FixtureTestRunner.Run("agr-sample-ds-draft", repo, expected, update: false);
        Assert.True(verified.Passed, string.Join("\n", verified.Messages));
    }

    [Fact]
    public void Verify_fails_when_the_golden_differs()
    {
        var repo = RepoRoot();
        var expected = TempExpectedRoot();
        FixtureTestRunner.Run("agr-sample-ds-draft", repo, expected, update: true);

        var golden = Path.Combine(expected, "agr-sample-ds-draft", "validation-report.json");
        File.WriteAllText(golden, File.ReadAllText(golden).Replace("ORG-GOV-DS-016", "ORG-GOV-DS-999"));

        var verified = FixtureTestRunner.Run("agr-sample-ds-draft", repo, expected, update: false);
        Assert.False(verified.Passed);
    }

    [Fact]
    public void Missing_golden_is_a_failure_not_a_silent_pass()
    {
        var repo = RepoRoot();
        var expected = TempExpectedRoot(); // empty: no goldens written

        var verified = FixtureTestRunner.Run("agr-sample-ds-draft", repo, expected, update: false);
        Assert.False(verified.Passed);
    }

    [Fact]
    public void Promotion_fixture_goldens_include_the_payload()
    {
        var repo = RepoRoot();
        var expected = TempExpectedRoot();
        FixtureTestRunner.Run("agr-sample-ds-promotion", repo, expected, update: true);

        Assert.True(File.Exists(Path.Combine(expected, "agr-sample-ds-promotion", "validation-report.json")));
        Assert.True(File.Exists(Path.Combine(expected, "agr-sample-ds-promotion", "promotion-payload.json")));
    }

    [Fact]
    public void Discovery_finds_every_agr_fixture()
    {
        var names = FixtureTestRunner.DiscoverFixtures(RepoRoot());

        Assert.Contains("agr-sample-ds-draft", names);
        Assert.Contains("agr-sample-aup-promotion", names);
        Assert.DoesNotContain("registry-sample", names); // not an AGR
    }
}
