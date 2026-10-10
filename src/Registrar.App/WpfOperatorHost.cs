using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace Registrar.App;

// The WPF implementation of IOperatorHost. Glue only: it decides nothing, and the suite
// cannot reach it (a net8.0 test project cannot start a desktop), which is why every rule
// the window has lives in Registrar.Pipeline instead. Callers wrap each method in a catch,
// because the clipboard can be locked by another program and a missing file handler throws.
public sealed class WpfOperatorHost : IOperatorHost
{
    public string? PickFolder(string? startFolder)
    {
        var dialog = new OpenFolderDialog { Title = "Choose your working folder" };
        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
            dialog.InitialDirectory = startFolder;
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FolderName : null;
    }

    public string? PickAgrFile(string? startFolder)
        => PickFile("Add a file", "AGR files (*.json)|*.json|All files (*.*)|*.*", startFolder);

    public string? PickRegistryFile(string? startFolder)
        => PickFile("Choose the ID registry export", "Registry export (*.csv)|*.csv|All files (*.*)|*.*", startFolder);

    private static string? PickFile(string title, string filter, string? startFolder)
    {
        var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true };
        if (!string.IsNullOrWhiteSpace(startFolder) && Directory.Exists(startFolder))
            dialog.InitialDirectory = startFolder;
        return dialog.ShowDialog(Application.Current?.MainWindow) == true ? dialog.FileName : null;
    }

    public void OpenPath(string path)
        => Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    public void CopyText(string text) => Clipboard.SetText(text);
}
