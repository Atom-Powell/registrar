using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Validation;

namespace Registrar.Tests;

public class QaCatalogConsistencyTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    // Runtime proof that qa-checklist.json is authoritative: for every check emitted across
    // every fixture, ruleSource comes from the catalog, and (where the catalog declares one)
    // errorCategory agrees. A layer const that drifts from the catalog fails this test.
    [Fact]
    public void Every_emitted_check_sources_its_metadata_from_the_catalog()
    {
        var repo = SemanticTestSupport.RepoRoot();
        var catalog = QaCatalog.Parse(File.ReadAllText(Path.Combine(repo, "config", "qa-checklist.json")));
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var registryCsv = File.ReadAllText(Path.Combine(repo, "fixtures", "registry-sample.csv"));

        var emittedCheckIds = new HashSet<string>();
        foreach (var fixture in Directory.GetFiles(Path.Combine(repo, "fixtures"), "agr-*.json"))
        {
            var outcome = new ValidationService(new FixedClock())
                .Validate(File.ReadAllText(fixture), schemaPath, registryCsv);

            foreach (var check in outcome.Report.Checks)
            {
                emittedCheckIds.Add(check.CheckId);
                var entry = catalog.Lookup(check.CheckId);
                Assert.True(entry is not null, $"{check.CheckId} (from {Path.GetFileName(fixture)}) is not in qa-checklist.json");
                Assert.Equal(entry!.RuleSource, check.RuleSource);
                if (check.ErrorCategory is not null && entry.ErrorCategory is not null)
                    Assert.Equal(entry.ErrorCategory, check.ErrorCategory);
            }
        }

        Assert.NotEmpty(emittedCheckIds); // guard against a no-op pass
    }
}
