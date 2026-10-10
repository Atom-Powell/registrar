using Registrar.Cli;

namespace Registrar.Tests.Cli;

public class SettingsStoreTests
{
    private static string TempFile() =>
        TempDir.File("settings.json");

    [Fact]
    public void Save_then_load_round_trips()
    {
        var path = TempFile();
        var store = new SettingsStore(path);
        store.Save(new MenuSettings { LastFolder = @"C:\ORG\AGRs" }.WithRecent(@"C:\a.json"));
        var loaded = store.Load();
        Assert.Equal(@"C:\ORG\AGRs", loaded.LastFolder);
        Assert.Contains(@"C:\a.json", loaded.RecentAgrs);
    }

    [Fact]
    public void Load_missing_file_returns_defaults()
    {
        var loaded = new SettingsStore(TempFile()).Load();
        Assert.Null(loaded.LastFolder);
        Assert.Empty(loaded.RecentAgrs);
    }

    [Fact]
    public void Load_corrupt_file_returns_defaults_without_throwing()
    {
        var path = TempFile();
        File.WriteAllText(path, "{ not valid json ");
        var loaded = new SettingsStore(path).Load();
        Assert.Null(loaded.LastFolder);
    }
}
