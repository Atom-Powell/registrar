using Registrar.Core;

namespace Registrar.Cli;

public sealed record FixtureTestResult(string Fixture, bool Passed, IReadOnlyList<string> Messages);

/// <summary>
/// Deterministic golden-file harness. For a fixture it runs the real build pipeline
/// under a frozen clock, then compares the emitted validation-report.json and (when
/// produced) promotion-payload.json against stored expected files. The rendered DOCX is
/// not compared directly; it is covered by structural assertions in the xUnit suite.
///
/// artifactSha256 IS compared, exactly, like every other field. The renderer produces
/// byte-identical output for a given (AGR + config), so a golden hash is meaningful and
/// pins that guarantee: if a change makes the DOCX unreproducible again, these goldens
/// break. This harness previously normalized the value away because the package carried
/// random relationship IDs, a random core-properties GUID, and wall-clock zip timestamps.
/// See Registrar.Rendering.DeterministicPackage.
/// </summary>
public static class FixtureTestRunner
{
    // The frozen instant that makes fixture output deterministic. Matches the instant
    // the xUnit suite pins its clocks to, so goldens agree with existing expectations
    // (including review-date fixtures, which are evaluated relative to "today").
    public static readonly DateTimeOffset FrozenInstant = new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);

    private const string ReportFileName = "validation-report.json";
    private const string PayloadFileName = "promotion-payload.json";

    private sealed class FrozenClock : IClock
    {
        public DateTimeOffset UtcNow => FrozenInstant;
    }

    public static IReadOnlyList<string> DiscoverFixtures(string repoRoot)
    {
        var fixturesDir = Path.Combine(repoRoot, "fixtures");
        if (!Directory.Exists(fixturesDir))
            return Array.Empty<string>();

        return Directory.GetFiles(fixturesDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public static FixtureTestResult Run(string fixtureName, string repoRoot, string expectedRoot, bool update)
    {
        var fixturesDir = Path.Combine(repoRoot, "fixtures");
        var agrSource = Path.Combine(fixturesDir, fixtureName + ".json");
        if (!File.Exists(agrSource))
            return new FixtureTestResult(fixtureName, false, new[] { $"fixture not found: {agrSource}" });

        // Run the pipeline in an isolated working directory so the engine's outputs
        // (report, payload, DOCX) never land in /fixtures.
        var work = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var workAgr = Path.Combine(work, fixtureName + ".json");
            File.Copy(agrSource, workAgr);

            var registrySource = Path.Combine(fixturesDir, "registry-sample.csv");
            var registryPath = File.Exists(registrySource) ? registrySource : null;

            BuildCommand.Run(workAgr, new FrozenClock(), TextWriter.Null, TextWriter.Null, null, registryPath);

            var producedReport = Path.Combine(work, ReportFileName);
            if (!File.Exists(producedReport))
                return new FixtureTestResult(fixtureName, false,
                    new[] { "engine produced no validation-report.json" });

            var producedPayload = Path.Combine(work, PayloadFileName);
            var hasPayload = File.Exists(producedPayload);

            var expectedDir = Path.Combine(expectedRoot, fixtureName);
            var expectedReport = Path.Combine(expectedDir, ReportFileName);
            var expectedPayload = Path.Combine(expectedDir, PayloadFileName);

            if (update)
                return Update(fixtureName, expectedDir, producedReport, expectedReport,
                    hasPayload ? producedPayload : null, expectedPayload);

            return Verify(fixtureName, producedReport, expectedReport,
                hasPayload ? producedPayload : null, expectedPayload);
        }
        finally
        {
            try { Directory.Delete(work, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    private static FixtureTestResult Update(string fixtureName, string expectedDir,
        string producedReport, string expectedReport, string? producedPayload, string expectedPayload)
    {
        Directory.CreateDirectory(expectedDir);
        File.Copy(producedReport, expectedReport, overwrite: true);

        if (producedPayload is not null)
        {
            File.Copy(producedPayload, expectedPayload, overwrite: true);
        }
        else if (File.Exists(expectedPayload))
        {
            // The engine no longer emits a payload for this fixture; drop the stale golden.
            File.Delete(expectedPayload);
        }

        return new FixtureTestResult(fixtureName, true, new[] { $"updated goldens in {expectedDir}" });
    }

    private static FixtureTestResult Verify(string fixtureName,
        string producedReport, string expectedReport, string? producedPayload, string expectedPayload)
    {
        var messages = new List<string>();

        CompareArtifact(ReportFileName, producedReport, expectedReport, messages);

        var expectedHasPayload = File.Exists(expectedPayload);
        if (producedPayload is not null && !expectedHasPayload)
            messages.Add($"{PayloadFileName}: engine emitted a payload but no golden exists (run --update)");
        else if (producedPayload is null && expectedHasPayload)
            messages.Add($"{PayloadFileName}: golden expects a payload but engine emitted none");
        else if (producedPayload is not null && expectedHasPayload)
            CompareArtifact(PayloadFileName, producedPayload, expectedPayload, messages);

        return messages.Count == 0
            ? new FixtureTestResult(fixtureName, true, new[] { "matches goldens" })
            : new FixtureTestResult(fixtureName, false, messages);
    }

    private static void CompareArtifact(string label, string producedPath, string expectedPath, List<string> messages)
    {
        if (!File.Exists(expectedPath))
        {
            messages.Add($"{label}: no expected file (run --update to create the golden)");
            return;
        }

        var produced = Normalize(File.ReadAllText(producedPath));
        var expected = Normalize(File.ReadAllText(expectedPath));
        if (produced == expected)
            return;

        messages.Add($"{label}: output differs from golden. {FirstDifference(expected, produced)}");
    }

    // Line endings only. Every value in the report, artifactSha256 included, is compared
    // exactly: the DOCX is reproducible, so its hash is a fact the golden can pin.
    private static string Normalize(string s) =>
        s.Replace("\r\n", "\n").TrimEnd('\n');

    private static string FirstDifference(string expected, string actual)
    {
        var e = expected.Split('\n');
        var a = actual.Split('\n');
        var max = Math.Max(e.Length, a.Length);
        for (var i = 0; i < max; i++)
        {
            var el = i < e.Length ? e[i] : "<missing line>";
            var al = i < a.Length ? a[i] : "<missing line>";
            if (el != al)
                return $"first diff at line {i + 1}:\n  expected: {el.Trim()}\n  actual:   {al.Trim()}";
        }
        return "content differs only in trailing lines/length";
    }
}
