using Registrar.Cli;

namespace Registrar.Tests.Cli;

public class MenuSettingsTests
{
    [Fact]
    public void WithRecent_puts_newest_first_dedups_and_caps_at_8()
    {
        var s = new MenuSettings();
        for (var i = 1; i <= 9; i++) s = s.WithRecent($@"C:\agrs\a{i}.json");
        s = s.WithRecent(@"C:\agrs\a5.json"); // re-add existing -> moves to front, no dup
        Assert.Equal(@"C:\agrs\a5.json", s.RecentAgrs[0]);
        Assert.Equal(8, s.RecentAgrs.Count);
        Assert.Single(s.RecentAgrs.Where(p => p.EndsWith("a5.json")));
        Assert.DoesNotContain(s.RecentAgrs, p => p.EndsWith("a1.json")); // oldest evicted
    }

    [Fact]
    public void WithRecent_dedups_ignoring_case()
    {
        var s = new MenuSettings()
            .WithRecent(@"C:\agrs\a5.json")
            .WithRecent(@"C:\AGRS\A5.JSON"); // same path, different case -> must collapse to one
        Assert.Single(s.RecentAgrs);
        Assert.Equal(@"C:\AGRS\A5.JSON", s.RecentAgrs[0]); // newest casing wins, prepended
    }

    [Fact]
    public void WithFolder_sets_last_folder()
    {
        var s = new MenuSettings().WithFolder(@"C:\ORG\AGRs");
        Assert.Equal(@"C:\ORG\AGRs", s.LastFolder);
    }
}
