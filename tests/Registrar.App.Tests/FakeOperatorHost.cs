namespace Registrar.App.Tests;

// Records what the page asked the shell to do and returns whatever a test queued. The WPF
// implementation is thin enough to be glue; the page's behavior around it is what is tested.
internal sealed class FakeOperatorHost : IOperatorHost
{
    public string? NextFolder { get; set; }
    public string? NextAgrFile { get; set; }
    public string? NextRegistryFile { get; set; }

    public List<string> Opened { get; } = new();
    public string? CopiedText { get; private set; }

    // Set to make the matching call throw, as a locked clipboard or a missing handler does.
    public Exception? OpenFails { get; set; }
    public Exception? CopyFails { get; set; }

    public string? PickFolder(string? startFolder) => NextFolder;
    public string? PickAgrFile(string? startFolder) => NextAgrFile;
    public string? PickRegistryFile(string? startFolder) => NextRegistryFile;

    public void OpenPath(string path)
    {
        if (OpenFails is not null) throw OpenFails;
        Opened.Add(path);
    }

    public void CopyText(string text)
    {
        if (CopyFails is not null) throw CopyFails;
        CopiedText = text;
    }
}
