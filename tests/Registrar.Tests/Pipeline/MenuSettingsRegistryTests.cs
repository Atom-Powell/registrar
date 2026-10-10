namespace Registrar.Tests.Pipeline;

public class MenuSettingsRegistryTests
{
    [Fact]
    public void WithRegistry_sets_and_clears_the_path()
    {
        var s = new MenuSettings().WithRegistry(@"C:\r\registry.csv");
        Assert.Equal(@"C:\r\registry.csv", s.RegistryPath);
        Assert.Null(s.WithRegistry(null).RegistryPath);
    }

    [Fact]
    public void The_registry_path_survives_a_save_and_load_beside_the_existing_fields()
    {
        var store = new SettingsStore(Path.Combine(TempDir.Create(), "settings.json"));
        store.Save(new MenuSettings().WithFolder(@"C:\work").WithRegistry(@"C:\r\registry.csv").WithRecent(@"C:\work\a.json"));

        var back = store.Load();

        Assert.Equal(@"C:\work", back.LastFolder);
        Assert.Equal(@"C:\r\registry.csv", back.RegistryPath);
        Assert.Equal(new[] { @"C:\work\a.json" }, back.RecentAgrs);
    }

    [Fact]
    public void A_settings_file_written_before_the_field_existed_still_loads()
    {
        var path = Path.Combine(TempDir.Create(), "settings.json");
        File.WriteAllText(path, "{ \"lastFolder\": \"C:\\\\work\", \"recentAgrs\": [] }");

        var back = new SettingsStore(path).Load();

        Assert.Equal(@"C:\work", back.LastFolder);
        Assert.Null(back.RegistryPath);
    }
}
