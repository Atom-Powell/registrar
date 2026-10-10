using Registrar.Core;

namespace Registrar.Cli;

public static class TestCommand
{
    // Exit codes: 0 all matched (or updated), 1 one or more mismatches, 2 usage/IO error.
    public static int Run(string? fixtureName, bool all, bool update,
        TextWriter stdout, TextWriter stderr, string? repoRootOverride = null)
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

        List<string> fixtures;
        if (all)
        {
            fixtures = FixtureTestRunner.DiscoverFixtures(repoRoot).ToList();
            if (fixtures.Count == 0)
            {
                stderr.WriteLine($"error: no fixtures found under {Path.Combine(repoRoot, "fixtures")}");
                return 2;
            }
        }
        else if (!string.IsNullOrWhiteSpace(fixtureName))
        {
            fixtures = new List<string> { NormalizeFixtureName(fixtureName) };
        }
        else
        {
            stderr.WriteLine("error: specify --fixture <name> or --all.");
            return 2;
        }

        var expectedRoot = Path.Combine(repoRoot, "fixtures", "expected");
        var failed = 0;
        foreach (var name in fixtures)
        {
            var result = FixtureTestRunner.Run(name, repoRoot, expectedRoot, update);
            var tag = !result.Passed ? "FAIL" : (update ? "UPDATED" : "PASS");
            stdout.WriteLine($"{tag,-7} {name}");
            if (!result.Passed)
            {
                failed++;
                foreach (var m in result.Messages)
                    stdout.WriteLine($"        {m}");
            }
        }

        stdout.WriteLine();
        if (update)
            stdout.WriteLine($"updated {fixtures.Count - failed}/{fixtures.Count} fixture(s).");
        else
            stdout.WriteLine($"{fixtures.Count - failed}/{fixtures.Count} fixture(s) matched goldens.");

        return failed == 0 ? 0 : 1;
    }

    private static string NormalizeFixtureName(string name)
        => name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? Path.GetFileNameWithoutExtension(name)
            : name;
}
