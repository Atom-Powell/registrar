using Registrar.Core.Model;

namespace Registrar.Pipeline;

// What an AGR declares as its mode, read without running anything. The console menu used a
// private copy of TryRead to pre-fill its mode prompt; the window shows the same value
// read-only, because a mode that disagrees with the AGR is a usage error in the CLI and a
// control that can only produce an error is the confusion this window exists to remove.
public static class AgrMode
{
    // Null for a file that cannot be read or parsed. The caller shows Describe(null), and
    // the report from Check says what is wrong; this reader never judges validity.
    public static string? TryRead(string agrPath)
    {
        try { return AgrParser.Parse(File.ReadAllText(agrPath)).ValidationPolicy.Mode; }
        catch { return null; }
    }

    public static string Describe(string? mode) => mode switch
    {
        "draft" => "Draft: a working copy. Warnings are allowed.",
        "promotion" => "Promotion: the controlled copy. A clean pass is required.",
        _ => "Mode unknown. Press Check and the report will say what is wrong with this file."
    };
}
