using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Registrar.Core;
using Registrar.Pipeline;
using Registrar.Validation;
using Xunit;

namespace Registrar.App.Tests;

// MainWindow wires the real services and cannot be started by the suite. This proves the
// registrations themselves resolve, which is the failure a fake-based page test cannot see:
// a service the page injects that nobody registered fails only when the window opens.
public class AppServicesTests
{
    internal static string RepoRoot()
        => Directory.GetParent(Path.GetDirectoryName(
            ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

    internal static EngineContext LoadEngine() => EngineContext.Load(RepoRoot());

    [Fact]
    public void Every_service_the_page_injects_resolves()
    {
        var services = new ServiceCollection();
        AppServices.Register(services, LoadEngine(), RepoRoot());

        using var provider = services.BuildServiceProvider(validateScopes: true);

        foreach (var type in new[]
        {
            typeof(AppState), typeof(IAgrRunner), typeof(IOperatorHost),
            typeof(SettingsStore), typeof(IClock), typeof(EngineContext)
        })
            Assert.NotNull(provider.GetRequiredService(type));
    }

    [Fact]
    public void AppState_carries_the_repo_root()
    {
        var services = new ServiceCollection();
        AppServices.Register(services, LoadEngine(), RepoRoot());
        using var provider = services.BuildServiceProvider();

        Assert.Equal(RepoRoot(), provider.GetRequiredService<AppState>().RepoRoot);
    }
}
