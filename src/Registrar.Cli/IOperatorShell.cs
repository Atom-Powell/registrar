using System.Diagnostics;

namespace Registrar.Cli;

public interface IOperatorShell
{
    void OpenFile(string path);
    void CopyToClipboard(string text);
}

public sealed class OperatorShell : IOperatorShell
{
    public void OpenFile(string path)
        => Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });

    public void CopyToClipboard(string text)
    {
        // Pipe to clip.exe: robust from a console app, no WinForms/STA dependency.
        var psi = new ProcessStartInfo("clip.exe") { RedirectStandardInput = true, UseShellExecute = false };
        using var proc = Process.Start(psi)!;
        proc.StandardInput.Write(text);
        proc.StandardInput.Close();
        proc.WaitForExit();
    }
}
