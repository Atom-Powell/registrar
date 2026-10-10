namespace Registrar.Core.Model;

public sealed record RegistryRecord(
    string DocumentId,
    string ArtifactType,
    string Classification,
    string ApprovalStatus,
    string Version);
