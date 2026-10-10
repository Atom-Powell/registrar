using Registrar.Core.Model;

namespace Registrar.Core;

// Pure assembly of the artifact manifest from the post-render merged report, the
// parsed AGR, and the optional promotion payload. No clock, no I/O, no RenderResult:
// the artifact hash is already stamped on the report, and sections render 1:1 from
// the AGR, so every field is a pure function of these three inputs.
public static class ArtifactManifestBuilder
{
    public static ArtifactManifest Build(
        ValidationReport mergedReport, AgrDocument agr, PromotionPayload? payload)
        => new()
        {
            ManifestVersion = EngineInfo.ManifestVersion,
            ExecutionId = mergedReport.ExecutionId,
            DocumentId = mergedReport.DocumentId,
            EngineVersion = mergedReport.EngineVersion,
            TimestampUtc = mergedReport.TimestampUtc,
            Identity = new ManifestIdentity
            {
                ArtifactType = agr.Identity.ArtifactType,
                DocumentId = agr.Identity.DocumentId,
                Version = agr.Identity.Version,
                Classification = agr.Identity.Classification,
                ApprovalStatus = agr.Identity.ApprovalStatus,
                Layer = agr.Identity.Layer
            },
            Context = new ManifestContext
            {
                Owner = agr.Context.Owner,
                Audience = agr.Context.Audience,
                PreparedBy = agr.Context.PreparedBy,
                Client = agr.Context.Client,
                Engagement = agr.Context.Engagement,
                ReviewDate = agr.Context.ReviewDate
            },
            Sections = agr.DataInputs.Sections.Select((section, index) => new ManifestSection
            {
                Number = index + 1,
                Title = section.Title,
                // Counts AGR-declared blocks, which equals rendered elements: unsafe or
                // excluded blocks fail closed before a DOCX exists, so any manifest we
                // write here describes a section where every declared block rendered.
                BlockCount = section.Blocks.Count,
                ClauseIds = section.Blocks
                    .Where(b => b.BlockType == "clauseInsertion" && b.ClauseId is not null)
                    .Select(b => b.ClauseId!)
                    .ToList()
            }).ToList(),
            Dependencies = (agr.Dependencies ?? Array.Empty<AgrDependency>())
                .Select(d => new ManifestDependency
                {
                    DependencyId = d.DependencyId, Kind = d.Kind, Blocking = d.Blocking
                }).ToList(),
            RegistryReference = payload is null ? null : new ManifestRegistryReference
            {
                DocumentId = payload.RegistryUpdate.DocumentId,
                FromStatus = payload.RegistryUpdate.FromStatus,
                ToStatus = payload.RegistryUpdate.ToStatus,
                Version = payload.RegistryUpdate.Version
            },
            // Guaranteed non-null: the manifest is built only after a DOCX was written,
            // which is exactly when PostRenderMerge stamped artifactSha256 on the report.
            ArtifactSha256 = mergedReport.ArtifactSha256
                ?? throw new InvalidOperationException(
                    "artifact manifest requires a stamped artifactSha256; build it only when a DOCX was produced.")
        };
}
