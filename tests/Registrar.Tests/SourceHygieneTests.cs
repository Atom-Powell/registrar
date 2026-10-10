namespace Registrar.Tests;

// The engine forbids decorative unicode in generated documents (QA-SF-08 and the
// QA-ENG-05 input check). This test holds the engine's own source and governed
// config to the same rule: prohibited characters must be written as unicode
// escapes, so no literal non-ASCII byte is ever committed. When it fails, run
// scripts/normalize-hygiene.ps1 on the offending file(s).
public class SourceHygieneTests
{
    [Fact]
    public void No_literal_non_ascii_bytes_in_source_or_governed_config()
    {
        var root = SemanticTestSupport.RepoRoot();
        var offenders = new SortedSet<string>();

        foreach (var file in HygieneFiles(root))
        {
            var bytes = File.ReadAllBytes(file);
            var line = 1;
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\n') { line++; continue; }
                if (bytes[i] >= 0x80)
                {
                    var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                    offenders.Add($"{rel}:{line} (byte 0x{bytes[i]:X2})");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Literal non-ASCII byte(s) found. Write prohibited characters as unicode "
            + "escapes instead (run scripts/normalize-hygiene.ps1). Offenders:\n"
            + string.Join("\n", offenders));
    }

    // Scans the governed source and data trees only: engine C# and markup, and the
    // versioned JSON config/fixtures/schemas. Project files (.csproj) carry a benign
    // UTF-8 BOM and are build plumbing, not governed content, so they are excluded.
    //
    // The markup patterns matter as much as the C# one. Rule 6's only automated gate is
    // this test, and the application's user-facing strings live in .razor and .html
    // rather than in .cs; a decorative glyph typed into a component would otherwise
    // reach a technician's screen unchallenged. .xaml is included on the same reasoning:
    // a Window Title is user-facing text.
    private static IEnumerable<string> HygieneFiles(string root)
    {
        (string Dir, string Pattern)[] scopes =
        {
            ("src", "*.cs"),
            ("src", "*.razor"),
            ("src", "*.xaml"),
            ("src", "*.html"),
            ("src", "*.css"),
            ("tests", "*.cs"),
            ("fixtures", "*.json"),
            ("config", "*.json"),
            ("schemas", "*.json")
        };

        foreach (var (dir, pattern) in scopes)
        {
            var full = Path.Combine(root, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var file in Directory.EnumerateFiles(full, pattern, SearchOption.AllDirectories))
            {
                var norm = file.Replace('\\', '/');
                if (norm.Contains("/bin/") || norm.Contains("/obj/")) continue;
                yield return file;
            }
        }
    }
}
