using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Validation;

namespace Registrar.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        var services = new ServiceCollection();
        services.AddWpfBlazorWebView();

        // Config and the compiled schema load once for the process lifetime, so every run
        // in the window sees the same rules.
        //
        // A missing or misplaced config or schemas tree is a real field-deployment failure,
        // not a developer-only concern: this runs inside an MSP network off a build the
        // operator did not create. The parent design's error-handling table requires a
        // Doctor-style diagnostic here rather than a stack trace, so this load is guarded;
        // a failure shows that diagnostic and ends the process before any window opens.
        EngineContext engine;
        string repoRoot;
        try
        {
            repoRoot = Directory.GetParent(Path.GetDirectoryName(
                ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;

            engine = EngineContext.Load(repoRoot);
        }
        catch (Exception ex)
        {
            ShowStartupFailure(ex);
            Environment.Exit(1);
            return;
        }

        AppServices.Register(services, engine, repoRoot);

        Resources.Add("services", services.BuildServiceProvider());

        InitializeComponent();
    }

    // Matches the voice registrar doctor already uses for its own config-resolution
    // failures (Doctor.cs: "cannot load config: {message}", "{schema name}: {message}")
    // rather than inventing a new one: name what was being looked for, where the search
    // started, and the underlying error, not a stack trace. This text is presentation, so
    // it lives in the app rather than in config.
    private static void ShowStartupFailure(Exception ex)
    {
        MessageBox.Show(
            "The Registrar cannot start: config or schema failed to load." + Environment.NewLine +
            Environment.NewLine +
            "Looked for schemas/agr.schema.json and a matching config folder, walking up from:" +
            Environment.NewLine + AppContext.BaseDirectory + Environment.NewLine +
            Environment.NewLine +
            "Underlying error: " + ex.Message,
            "Registrar - startup diagnostic",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
