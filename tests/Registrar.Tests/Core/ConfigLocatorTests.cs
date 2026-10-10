using Registrar.Core;

namespace Registrar.Tests.Core;

public class ConfigLocatorTests
{
    [Fact]
    public void FindSchema_walks_up_to_directory_containing_schemas_and_config()
    {
        // Arrange: temp/root/{schemas,config} and a nested start dir.
        var root = TempDir.Create();
        Directory.CreateDirectory(Path.Combine(root, "schemas"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "schemas", "agr.schema.json"), "{}");
        var nested = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;

        var found = ConfigLocator.FindSchema(nested, "agr.schema.json");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "schemas", "agr.schema.json")),
            Path.GetFullPath(found));
    }

    [Fact]
    public void FindSchema_throws_when_no_repo_root_found()
    {
        var isolated = TempDir.Create();
        Assert.Throws<FileNotFoundException>(
            () => ConfigLocator.FindSchema(isolated, "agr.schema.json"));
    }

    [Fact]
    public void FindConfig_walks_up_to_directory_containing_schemas_and_config()
    {
        var root = TempDir.Create();
        Directory.CreateDirectory(Path.Combine(root, "schemas"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        File.WriteAllText(Path.Combine(root, "config", "vocabularies.json"), "{}");
        var nested = Directory.CreateDirectory(Path.Combine(root, "a", "b")).FullName;

        var found = ConfigLocator.FindConfig(nested, "vocabularies.json");

        Assert.Equal(
            Path.GetFullPath(Path.Combine(root, "config", "vocabularies.json")),
            Path.GetFullPath(found));
    }

    [Fact]
    public void FindConfig_throws_when_no_repo_root_found()
    {
        var isolated = TempDir.Create();
        Assert.Throws<FileNotFoundException>(
            () => ConfigLocator.FindConfig(isolated, "vocabularies.json"));
    }

    [Fact]
    public void Find_resolves_when_base_dir_itself_holds_config_and_schemas()
    {
        var root = TempDir.Create();
        Directory.CreateDirectory(Path.Combine(root, "schemas"));
        Directory.CreateDirectory(Path.Combine(root, "config"));
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "schemas", "agr.schema.json"), "{}");
        File.WriteAllText(Path.Combine(root, "config", "style-rules.json"), "{}");
        File.WriteAllText(Path.Combine(root, "assets", "org-logo.png"), "x");

        // baseDir == the directory that holds config/schemas/assets (packaged layout)
        Assert.EndsWith(Path.Combine("schemas", "agr.schema.json"),
            Registrar.Core.ConfigLocator.FindSchema(root, "agr.schema.json"));
        Assert.EndsWith(Path.Combine("config", "style-rules.json"),
            Registrar.Core.ConfigLocator.FindConfig(root, "style-rules.json"));
        Assert.EndsWith(Path.Combine("assets", "org-logo.png"),
            Registrar.Core.ConfigLocator.FindAsset(root, "org-logo.png"));
    }
}
