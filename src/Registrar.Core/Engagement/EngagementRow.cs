namespace Registrar.Core.Engagement;

// One artifact in an engagement folder. Detail is always populated so a row is
// actionable without opening the artifact.
public sealed record EngagementRow(
    string FileName,
    string? ArtifactType,
    string? DocumentId,
    string? Client,
    ArtifactState State,
    string Detail);
