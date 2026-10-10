using Registrar.Core;

namespace Registrar.Tests.Pipeline;

public class OutputFileNamesTests
{
    private static readonly string[] Names =
        { "validation-report.json", "generation-log.json", "artifact-manifest.json", "promotion-payload.json" };

    private static string Root() => Directory.GetParent(Path.GetDirectoryName(
        ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    [Fact]
    public void IsPackageFileName_matches_the_four_names_in_any_case_and_nothing_else()
    {
        foreach (var name in Names)
        {
            Assert.True(OutputPackage.IsPackageFileName(name));
            Assert.True(OutputPackage.IsPackageFileName(name.ToUpperInvariant()));
        }
        Assert.False(OutputPackage.IsPackageFileName("agr-sample-ds-draft.json"));
        Assert.False(OutputPackage.IsPackageFileName("ORG - Deployment Summary.docx"));
    }

    // Builds in draft mode, so promotion-payload.json (promotion only) is not exercised here;
    // it is covered only by the PackageFileNames list itself.
    [Fact]
    public void Every_json_a_build_writes_is_in_PackageFileNames()
    {
        var dest = Path.Combine(TempDir.Create(), "agr-sample-ds-draft.json");
        File.Copy(Path.Combine(Root(), "fixtures", "agr-sample-ds-draft.json"), dest);

        var result = new AgrRunner(new SystemClock()).Build(dest, null);

        Assert.NotEmpty(result.Written);
        foreach (var name in result.Written.Where(n => n != result.DocumentFileName))
            Assert.Contains(name, OutputPackage.PackageFileNames);
        Assert.Equal(4, OutputPackage.PackageFileNames.Count);
    }

    [Fact]
    public void Candidates_drops_package_files_only_when_asked()
    {
        var dir = TempDir.Create();
        File.WriteAllText(Path.Combine(dir, "agr.json"), "{}");
        foreach (var n in Names) File.WriteAllText(Path.Combine(dir, n), "{}");
        var recent = new List<string> { Path.Combine(dir, "Validation-Report.json") };

        var excluded = AgrPicker.Candidates(dir, recent, excludeOutputs: true);
        Assert.Equal(new[] { Path.Combine(dir, "agr.json") }, excluded);

        Assert.Equal(5, AgrPicker.Candidates(dir, recent).Count);
    }
}
