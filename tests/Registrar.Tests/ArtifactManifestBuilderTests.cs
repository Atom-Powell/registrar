using System.Collections.Generic;
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Tests;

public class ArtifactManifestBuilderTests
{
    private static ValidationReport Report(string sha = "abc") => new()
    {
        ReportVersion = EngineInfo.ReportVersion,
        ExecutionId = "GOV-2026-07-15-003",
        DocumentId = "ORG-GOV-DS-016",
        RequestedMode = "draft",
        OverallResult = "pass",
        EngineVersion = EngineInfo.Version,
        ConfigVersions = new Dictionary<string, string>(),
        TimestampUtc = "2026-07-15T12:00:00.0000000+00:00",
        ArtifactSha256 = sha,
        Checks = System.Array.Empty<Check>()
    };

    private static AgrDocument Agr(IReadOnlyList<AgrDependency>? deps = null) => new()
    {
        SchemaVersion = "1.0.0",
        Identity = new AgrIdentity
        {
            ArtifactType = "DS", DocumentId = "ORG-GOV-DS-016", Version = "v1.0",
            Classification = "Client-Facing", ApprovalStatus = "Draft", Layer = "Governance"
        },
        Context = new AgrContext
        {
            Owner = "Registry Owner", Audience = "Leadership", PreparedBy = "ORG",
            Client = "Talewind CPA", Engagement = "Pilot", ReviewDate = null, ReviewDateOverride = null
        },
        SourceWorkflowContext = new AgrSourceWorkflowContext
        {
            WorkflowName = "Copilot Readiness Assessment", Phase = "Reporting", ExecutionId = "GOV-2026-07-15-003"
        },
        DataInputs = new AgrDataInputs
        {
            PlaceholderMap = new Dictionary<string, string?>(),
            Sections = new[]
            {
                new AgrSection { Title = "Purpose", DescriptorLine = null, Blocks = new[]
                    { new AgrBlock { BlockType = "paragraph", Text = "x" } } },
                new AgrSection { Title = "Audit", DescriptorLine = null, Blocks = new[]
                    { new AgrBlock { BlockType = "paragraph", Text = "y" },
                      new AgrBlock { BlockType = "clauseInsertion", ClauseId = "CL-006" } } }
            }
        },
        ValidationPolicy = new AgrValidationPolicy { Mode = "draft", WarningThreshold = null },
        RenderingOptions = new AgrRenderingOptions { PackageName = "P", OutputFileName = "o.docx" },
        Dependencies = deps
    };

    private static PromotionPayload Payload() => new()
    {
        PayloadVersion = EngineInfo.PayloadVersion, ExecutionId = "GOV-2026-07-15-003",
        DocumentId = "ORG-GOV-DS-016", ArtifactType = "DS", Version = "v1.0",
        GeneratedUtc = "2026-07-15T12:00:00.0000000+00:00",
        RegistryUpdate = new RegistryUpdate
            { DocumentId = "ORG-GOV-DS-016", FromStatus = "Draft", ToStatus = "In Review", Version = "v1.0" },
        RepositoryTarget = "Controlled Documents/Governance",
        Attestation = new Attestation { CheckId = "QA-SPEC-18", Statement = "s", RequiresOperatorSignoff = true }
    };

    [Fact]
    public void Echoes_identity_context_and_header()
    {
        var m = ArtifactManifestBuilder.Build(Report(), Agr(), null);

        Assert.Equal(EngineInfo.ManifestVersion, m.ManifestVersion);
        Assert.Equal("GOV-2026-07-15-003", m.ExecutionId);
        Assert.Equal("2026-07-15T12:00:00.0000000+00:00", m.TimestampUtc);
        Assert.Equal("DS", m.Identity.ArtifactType);
        Assert.Equal("Registry Owner", m.Context.Owner);
        Assert.Equal("abc", m.ArtifactSha256);
    }

    [Fact]
    public void Builds_a_lean_section_inventory()
    {
        var m = ArtifactManifestBuilder.Build(Report(), Agr(), null);

        Assert.Equal(2, m.Sections.Count);
        Assert.Equal(1, m.Sections[0].Number);
        Assert.Equal("Purpose", m.Sections[0].Title);
        Assert.Equal(1, m.Sections[0].BlockCount);
        Assert.Empty(m.Sections[0].ClauseIds);
        Assert.Equal(2, m.Sections[1].Number);
        Assert.Equal(2, m.Sections[1].BlockCount);
        Assert.Equal(new[] { "CL-006" }, m.Sections[1].ClauseIds);
    }

    [Fact]
    public void Echoes_dependencies_or_empty_when_none()
    {
        var withDeps = ArtifactManifestBuilder.Build(Report(), Agr(new[]
            { new AgrDependency { DependencyId = "ORG-MST-SCL", Kind = "clause-library", Blocking = true } }), null);
        var dep = Assert.Single(withDeps.Dependencies);
        Assert.Equal("ORG-MST-SCL", dep.DependencyId);
        Assert.True(dep.Blocking);

        var noDeps = ArtifactManifestBuilder.Build(Report(), Agr(null), null);
        Assert.Empty(noDeps.Dependencies);
    }

    [Fact]
    public void RegistryReference_is_populated_from_payload_and_null_without_one()
    {
        var withPayload = ArtifactManifestBuilder.Build(Report(), Agr(), Payload());
        Assert.NotNull(withPayload.RegistryReference);
        Assert.Equal("Draft", withPayload.RegistryReference!.FromStatus);
        Assert.Equal("In Review", withPayload.RegistryReference.ToStatus);

        var noPayload = ArtifactManifestBuilder.Build(Report(), Agr(), null);
        Assert.Null(noPayload.RegistryReference);
    }
}
