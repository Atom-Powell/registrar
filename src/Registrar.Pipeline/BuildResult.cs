using Registrar.Core.Model;

namespace Registrar.Pipeline;

// Everything a build produces, in memory. The caller decides where any of it goes:
// the CLI writes files and maps to exit codes, a GUI keeps the bytes and shows the
// report. Nothing here touches the file system.
public sealed record BuildResult(
    ValidationReport Report,
    PromotionPayload? Payload,
    byte[]? DocxBytes,
    GenerationLog? GenerationLog,
    ArtifactManifest? Manifest,
    bool DocxEligible,
    // True once the pipeline reached the render stage, whether or not a post-render check
    // ultimately withheld the DOCX. This is the explicit signal for "did we reach
    // rendering": callers must not infer it from GenerationLog being non-null, which was
    // an undocumented contract between this type and its callers.
    bool ReachedRendering);
