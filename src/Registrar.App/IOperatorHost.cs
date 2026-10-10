namespace Registrar.App;

// Everything the page needs from the machine it runs on: native pickers, opening a file or
// folder, and the clipboard. An interface so the page is tested without a desktop, and so
// the WPF specifics sit in one thin class that holds no rules.
public interface IOperatorHost
{
    // Each picker returns null when the operator cancels.
    string? PickFolder(string? startFolder);
    string? PickAgrFile(string? startFolder);
    string? PickRegistryFile(string? startFolder);

    // Open a document, report or folder in the shell's default handler.
    void OpenPath(string path);

    void CopyText(string text);
}
