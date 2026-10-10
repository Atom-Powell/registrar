namespace Registrar.Core.Engagement;

// What a scan established about the folder itself, as distinct from what it found
// inside it.
//
// An empty row list used to mean three different things at once: no folder had been
// chosen yet, the folder did not exist, or the folder existed and held nothing. The
// screen rendered all three identically, so a mistyped client name read as "this client
// owes nothing" - the silent degradation the design forbids, on the one screen whose job
// is to say what is outstanding.
//
// The distinction lives here rather than in the app for the same two reasons everything
// else does: the app enforces no rules of its own, and a net8.0 test project cannot
// reach a net8.0-windows one, so a condition held in the app would be untestable.
public enum EngagementFolderState
{
    // No folder has been supplied yet. The state the screen opens in.
    NotSpecified,

    // A folder was supplied and there is nothing at that path. Almost always a typo in a
    // client name, and the case that must never read as "nothing outstanding".
    NotFound,

    // The folder exists and holds no artifacts.
    Empty,

    // The folder exists and holds at least one artifact.
    Populated
}

// Message is always populated, on the same reasoning as EngagementRow.Detail: the
// sentence an operator reads is a statement about the engagement, so it is decided where
// it can be tested rather than assembled in markup.
public sealed record EngagementScanResult(
    string Folder,
    EngagementFolderState State,
    string Message,
    IReadOnlyList<EngagementRow> Rows)
{
    public static readonly EngagementScanResult NoFolderChosen = new(
        string.Empty,
        EngagementFolderState.NotSpecified,
        "Choose an engagement folder to see what this client still owes.",
        Array.Empty<EngagementRow>());

    // Names the path, because the operator's next move is to compare what they typed
    // against what the folder is actually called.
    public static EngagementScanResult NotFound(string folder) => new(
        folder,
        EngagementFolderState.NotFound,
        $"There is no folder at {folder}. Nothing was read, so this says nothing about "
            + "what the client owes. Check the path and open it again.",
        Array.Empty<EngagementRow>());

    public static EngagementScanResult Scanned(string folder, IReadOnlyList<EngagementRow> rows)
        => rows.Count == 0
            ? new EngagementScanResult(folder, EngagementFolderState.Empty,
                $"{folder} exists and holds no artifacts yet.", rows)
            : new EngagementScanResult(folder, EngagementFolderState.Populated,
                $"{rows.Count} artifact(s) in {folder}.", rows);
}
