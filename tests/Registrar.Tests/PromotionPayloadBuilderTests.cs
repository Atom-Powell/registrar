using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

public class PromotionPayloadBuilderTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 3, 12, 0, 0, TimeSpan.Zero);
    }

    [Fact]
    public void Build_assembles_payload_from_agr_registry_and_config()
    {
        var agr = SemanticTestSupport.Agr("agr-sample-ds-draft.json"); // documentId ORG-GOV-DS-016, DS, v1.0, Draft
        var record = new RegistryRecord("ORG-GOV-DS-016", "DS", "Client-Facing", "Draft", "v1.0");

        var payload = PromotionPayloadBuilder.Build(
            agr, record, SemanticTestSupport.Config(), new FixedClock(), "GOV-2026-07-03-001");

        Assert.Equal(EngineInfo.PayloadVersion, payload.PayloadVersion);
        Assert.Equal("GOV-2026-07-03-001", payload.ExecutionId);
        Assert.Equal("ORG-GOV-DS-016", payload.DocumentId);
        Assert.Equal("Draft", payload.RegistryUpdate.FromStatus);      // registry current
        Assert.Equal("Draft", payload.RegistryUpdate.ToStatus);        // AGR target (golden is Draft)
        Assert.Equal("Controlled Documents/Governance", payload.RepositoryTarget); // GOV
        Assert.Equal("QA-SPEC-18", payload.Attestation.CheckId);
        Assert.True(payload.Attestation.RequiresOperatorSignoff);
        Assert.Equal("2026-07-03T12:00:00.0000000+00:00", payload.GeneratedUtc);
    }
}
