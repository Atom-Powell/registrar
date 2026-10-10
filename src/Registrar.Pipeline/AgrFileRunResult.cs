using Registrar.Core.Model;

namespace Registrar.Pipeline;

public enum RunStream { Out, Err }

// One line the CLI would have printed, in the order it would have printed it. Kept as an
// ordered list rather than two strings because the CLI interleaves the streams on one path
// (a manifest-write failure prints a stdout line and then a stderr line), and a wrapper
// that replays them in order reproduces that exactly.
public sealed record RunMessage(RunStream Stream, string Text);

// What one file-based validate or build left behind, for any caller: the console wrappers
// replay Messages and return ExitCode, the operator window reads the structured fields.
//
// Report is the report written to disk THIS run. It is null when the run ended before any
// report was written (missing AGR, unreadable registry, mode disagreement), and a caller
// must never fall back to an older validation-report.json sitting in the same folder.
//
// Written is the set of file names written at any point in this run (the validation report
// is written twice on a build, so a name appears once). Withheld holds sentences of the
// form "name: reason" from OutputPackage for files the run deliberately did not produce.
public sealed record AgrFileRunResult(
    int ExitCode,
    IReadOnlyList<RunMessage> Messages,
    ValidationReport? Report,
    string OutputDirectory,
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Withheld,
    string? DocumentFileName)
{
    // Everything the run sent to stderr, joined and trimmed. This is the text an operator
    // reads when ExitCode is 2.
    public string ErrorText => string.Join(
        Environment.NewLine,
        Messages.Where(m => m.Stream == RunStream.Err).Select(m => m.Text)).Trim();

    public void Replay(TextWriter stdout, TextWriter stderr)
    {
        foreach (var m in Messages)
            (m.Stream == RunStream.Out ? stdout : stderr).WriteLine(m.Text);
    }
}
