using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Validation;

public static class PromotionPayloadBuilder
{
    private const string AttestationStatement =
        "Operator must apply this registry update to the PMC Registry sheet and confirm post-update state before final delivery.";

    public static PromotionPayload Build(
        AgrDocument agr, RegistryRecord record, ConfigBundle config, IClock clock, string executionId)
    {
        var category = agr.Identity.DocumentId.Split('-')[1];

        return new PromotionPayload
        {
            PayloadVersion = EngineInfo.PayloadVersion,
            ExecutionId = executionId,
            DocumentId = agr.Identity.DocumentId,
            ArtifactType = agr.Identity.ArtifactType,
            Version = agr.Identity.Version,
            GeneratedUtc = clock.UtcNow.ToString("o"),
            RegistryUpdate = new RegistryUpdate
            {
                DocumentId = agr.Identity.DocumentId,
                FromStatus = record.ApprovalStatus,
                ToStatus = agr.Identity.ApprovalStatus,
                Version = agr.Identity.Version
            },
            RepositoryTarget = config.RepositoryTargetsByCategory[category],
            Attestation = new Attestation
            {
                CheckId = "QA-SPEC-18",
                Statement = AttestationStatement,
                RequiresOperatorSignoff = true
            }
        };
    }
}
