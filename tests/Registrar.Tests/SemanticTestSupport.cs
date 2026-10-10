using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Tests;

internal static class SemanticTestSupport
{
    public static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    public static ConfigBundle Config() => ConfigLoader.Load(RepoRoot());

    public static string FixtureText(string name)
        => File.ReadAllText(Path.Combine(RepoRoot(), "fixtures", name));

    public static string FixtureDirectory() => Path.Combine(RepoRoot(), "fixtures");

    public static AgrDocument Agr(string fixtureName) => AgrParser.Parse(FixtureText(fixtureName));

    public static SemanticContext Context(string fixtureName, string mode)
        => new(Agr(fixtureName), Config(), mode, Registry: null);

    public static SemanticContext ContextWithRegistry(string fixtureName, string mode, string registryCsv)
        => new(Agr(fixtureName), Config(), mode, CsvIdRegistry.FromCsvText(registryCsv));

    public static SemanticContext ContextNoRegistry(string fixtureName, string mode)
        => new(Agr(fixtureName), Config(), mode, null);

    public const string SampleRegistryCsv =
        "documentId,artifactType,classification,approvalStatus,version\n" +
        "ORG-GOV-DS-016,DS,Client-Facing,Draft,v1.0\n" +
        "ORG-GOV-DS-017,DS,Client-Facing,Retired,v2.0\n";

    // Every optional and nullable member of AgrDocument set to a distinctive non-null
    // value. Shared by the authoring round trip and the import round trip: both are
    // hand-written mappings with the same failure mode, a silently dropped field, and a
    // second copy of this builder would drift out of step with the model.
    //
    // ValidationPolicy.Mode is required, not optional, but is deliberately overridden to
    // promotion rather than left at the base fixture's draft value: AgrDraft.Mode falls
    // back to a compiled default of Registrar.Core.Mode.Draft when a mapping omits it, so
    // a document that stayed on draft would let a dropped Mode line in ImportParser.ToDraft
    // pass unnoticed, both sides landing on "draft" by coincidence rather than by mapping.
    public static AgrDocument FullyPopulatedAgr()
    {
        var original = Agr("agr-sample-ds-draft.json");
        return original with
        {
            Context = original.Context with
            {
                ReviewDate = "2026-09-01",
                ReviewDateOverride = new AgrReviewOverride
                {
                    ApprovedBy = "Documentation Owner",
                    ApprovalDate = "2026-08-02",
                    Justification = "Deferred pending client sign-off."
                }
            },
            Dependencies = new List<AgrDependency>
            {
                new() { DependencyId = "ORG-MST-SCL", Kind = "clause-library", Blocking = true }
            },
            EvidenceReferences = new AgrEvidenceReferences
            {
                ReadinessWorkbookRefs = new[] { "REF-001" },
                RiskRegisterRefs = new[] { "RISK-001" },
                AssetInventoryRefs = new[] { "ASSET-001" },
                ApprovedNarrativeInputs = new[] { "NARR-001" }
            },
            DataInputs = original.DataInputs with
            {
                NormalizedFindings = new List<AgrEvidenceItem>
                {
                    new() { Id = "FIND-001", Summary = "Finding summary", SourceRef = "SRC-1", Severity = "High" }
                },
                NormalizedRisks = new List<AgrEvidenceItem>
                {
                    new() { Id = "RISK-001", Summary = "Risk summary", SourceRef = "SRC-2", Severity = "Medium" }
                },
                NormalizedInventoryEntities = new List<AgrEvidenceItem>
                {
                    new() { Id = "INV-001", Summary = "Inventory summary", SourceRef = "SRC-3", Severity = "Low" }
                }
            },
            ValidationPolicy = original.ValidationPolicy with
            {
                Mode = Registrar.Core.Mode.Promotion,
                WarningThreshold = 5,
                AdditionalRequiredCheckIds = new[] { "QA-CUSTOM-01" }
            },
            RenderingOptions = original.RenderingOptions with
            {
                OptionalSectionToggles = new Dictionary<string, bool> { ["ShowAppendix"] = true }
            }
        };
    }
}
