namespace Registrar.Tests;

// Builds a throwaway config tree so a test can load a deliberately broken config release
// without touching the committed one. ConfigLocator walks up from the directory it is
// given looking for a sibling schemas/ and config/ pair, so the copy needs both, and it
// must live somewhere with no governed repo above it.
internal static class ConfigTestSupport
{
    public static string CopyConfigTree(Func<string, string> mutateStyleRules)
    {
        var root = TempDir.Create();
        Directory.CreateDirectory(Path.Combine(root, "schemas"));
        var configDir = Directory.CreateDirectory(Path.Combine(root, "config")).FullName;

        var source = Path.Combine(SemanticTestSupport.RepoRoot(), "config");
        foreach (var file in Directory.GetFiles(source, "*.json"))
            File.Copy(file, Path.Combine(configDir, Path.GetFileName(file)));

        var styleRulesPath = Path.Combine(configDir, "style-rules.json");
        File.WriteAllText(styleRulesPath, mutateStyleRules(File.ReadAllText(styleRulesPath)));

        return root;
    }
}
