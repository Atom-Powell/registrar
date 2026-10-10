using System.Windows;
using System.Windows.Threading;
using Registrar.Core;

namespace Registrar.App;

public partial class App : Application
{
    // The last net, under the ErrorBoundary in Routes.razor rather than instead of it. The
    // boundary catches what Blazor raises inside the component tree; this catches what
    // reaches the WPF dispatcher outside it, which in this host includes an exception
    // rescheduled out of WpfDispatcher.InvokeAsync (a timer callback that throws off the
    // render path, for one) and anything WPF itself raises. The two do not double-report an
    // exception the boundary actually handles: it is never rethrown, so it never arrives here.
    // That is not an absolute guarantee, though: ErrorBoundaryBase itself rethrows when its
    // error content throws, and again once MaximumErrorCount is exceeded, and either rethrow
    // does reach this handler. The conclusion still holds in both cases, because a rethrown
    // exception was never actually handled by the boundary.
    //
    // Handled = true keeps the window alive, which is the point: an exception reaching the
    // dispatcher outside the boundary would otherwise end the window, so the handler reports
    // it and keeps the window open. Rule 7 sets the bar in both directions, so this neither shows a stack trace nor swallows in silence: it
    // names what failed and says what is and is not still true. It lives here rather than in
    // MainWindow because DispatcherUnhandledException is the Application's event, and
    // subscribing from the window would mean reaching back through Application.Current to
    // register on the same object.
    private void ReportAndContinue(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Something in the Registrar failed unexpectedly and the step was abandoned."
            + Environment.NewLine + Environment.NewLine +
            "Underlying error: " + e.Exception.Message + Environment.NewLine + Environment.NewLine +
            "The window is still open and anything already saved is still on disk. Nothing "
            + "further was written by the step that failed. If this repeats, send this message "
            + "along with the file you were working on.",
            "Registrar - unexpected failure",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        e.Handled = true;
    }

    // Must run before any BlazorWebView initializes. WebView2 otherwise puts its
    // user-data folder beside the executable, and a read-only or network install then
    // fails to start with a blank window and no message. The path itself is computed
    // and tested in Registrar.Core; this is the one line that cannot be.
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += ReportAndContinue;

        Environment.SetEnvironmentVariable(
            WebViewEnvironment.VariableName,
            WebViewEnvironment.UserDataFolder(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));

        base.OnStartup(e);
    }
}
