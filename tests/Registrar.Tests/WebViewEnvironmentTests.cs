using Registrar.Core;
using Xunit;

namespace Registrar.Tests;

public class WebViewEnvironmentTests
{
    [Fact]
    public void The_folder_sits_under_the_given_root()
    {
        var path = WebViewEnvironment.UserDataFolder(Path.Combine("C:", "Users", "t", "AppData", "Local"));

        Assert.StartsWith(Path.Combine("C:", "Users", "t", "AppData", "Local"), path, StringComparison.Ordinal);
    }

    [Fact]
    public void The_folder_is_named_for_this_application()
    {
        var path = WebViewEnvironment.UserDataFolder(Path.Combine("C:", "root"));

        Assert.Contains("Registrar", path, StringComparison.Ordinal);
    }

    // A trailing separator on the root is a real shape: Environment.GetFolderPath can
    // return one, and naive concatenation would produce a doubled separator.
    [Fact]
    public void A_trailing_separator_on_the_root_does_not_double_up()
    {
        var withSlash = WebViewEnvironment.UserDataFolder(Path.Combine("C:", "root") + Path.DirectorySeparatorChar);
        var without = WebViewEnvironment.UserDataFolder(Path.Combine("C:", "root"));

        Assert.Equal(without, withSlash);
    }

    [Fact]
    public void An_empty_root_is_rejected_rather_than_producing_a_relative_path()
    {
        Assert.Throws<ArgumentException>(() => WebViewEnvironment.UserDataFolder(""));
    }
}
