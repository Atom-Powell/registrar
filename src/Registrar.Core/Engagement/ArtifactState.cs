namespace Registrar.Core.Engagement;

public enum ArtifactState
{
    // The draft is missing at least one required value.
    Draft,

    // Every required value is present, so the artifact converts to a document. This
    // does not claim validation passes: Build still runs the real layers and can still
    // refuse. It claims only that nothing is missing.
    Ready,

    // A document of the expected output file name is present in the folder.
    Built,

    // The file could not be read. Shown rather than omitted, because hiding a broken
    // artifact from the screen that reports what is outstanding is exactly the silent
    // degradation the design forbids.
    Unreadable
}
