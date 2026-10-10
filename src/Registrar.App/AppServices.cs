using Microsoft.Extensions.DependencyInjection;
using Registrar.Core;
using Registrar.Pipeline;
using Registrar.Validation;

namespace Registrar.App;

// The window's service registrations in one testable place. MainWindow calls this and adds
// the WebView services itself; AppServicesTests proves everything the page injects resolves.
public static class AppServices
{
    public static void Register(IServiceCollection services, EngineContext engine, string repoRoot)
    {
        services.AddSingleton(engine);
        services.AddSingleton<IClock>(new SystemClock());
        services.AddSingleton(new AppState(engine, repoRoot));
        services.AddSingleton<IAgrRunner>(sp => new AgrRunner(sp.GetRequiredService<IClock>()));
        services.AddSingleton<IOperatorHost, WpfOperatorHost>();
        services.AddSingleton(new SettingsStore(SettingsStore.DefaultPath()));
    }
}
