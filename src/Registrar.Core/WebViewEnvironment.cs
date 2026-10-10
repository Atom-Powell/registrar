namespace Registrar.Core;

// WebView2 defaults its user-data folder to a directory beside the executable. When the
// application is extracted somewhere read-only, or run from a network path, that
// directory cannot be created and the window comes up blank with no error. Pointing it
// at the user's local application data instead is the documented fix.
//
// Only the path calculation lives here. Setting the environment variable is one line in
// Registrar.App, because that line cannot be exercised by a net8.0 test project and
// there is nothing in it left to get wrong once the path is proven.
public static class WebViewEnvironment
{
    public const string VariableName = "WEBVIEW2_USER_DATA_FOLDER";

    private const string FolderName = "Registrar";
    private const string SubFolderName = "WebView2";

    public static string UserDataFolder(string localAppDataRoot)
    {
        if (string.IsNullOrWhiteSpace(localAppDataRoot))
        {
            throw new ArgumentException(
                "A local application data root is required; an empty one would resolve to a "
                    + "relative path beside the executable, which is the failure this avoids.",
                nameof(localAppDataRoot));
        }

        return Path.Combine(localAppDataRoot, FolderName, SubFolderName);
    }
}
