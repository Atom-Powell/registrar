using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

public sealed class ValidationService
{
    private readonly IClock _clock;
    private readonly IValidationLayer _schemaLayer;

    public ValidationService(IClock clock)
    {
        _clock = clock;
        _schemaLayer = new SchemaValidationLayer();
    }

    public ValidationOutcome Validate(string agrText, string schemaPath, string? registryCsvText = null)
    {
        var repoRoot = Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
        return Validate(agrText, EngineContext.Load(repoRoot), registryCsvText);
    }

    public ValidationOutcome Validate(string agrText, EngineContext engine, string? registryCsvText = null)
    {
        var configVersions = new Dictionary<string, string>
        {
            ["agr.schema.json"] = engine.SchemaHash
        };
        foreach (var (name, hash) in engine.Config.Hashes) configVersions[name] = hash;

        var schemaChecks = _schemaLayer.Validate(
            new SchemaValidationInput(agrText, engine.Schema, engine.SchemaRoot));
        var (executionId, documentId, mode) = AgrFacts.Extract(agrText);

        var schemaBlocked = schemaChecks.Any(c =>
            c.Result == CheckResultValue.Fail && c.SeverityLevel == Severity.Blocking);

        IReadOnlyList<Check> checks;
        string overall;
        PromotionPayload? payload = null;

        if (schemaBlocked)
        {
            checks = schemaChecks;
            overall = OverallResult.Fail;
        }
        else
        {
            var agr = AgrParser.Parse(agrText);

            IIdRegistry? registry = null;
            string? registryError = null;
            if (registryCsvText is not null)
            {
                try { registry = CsvIdRegistry.FromCsvText(registryCsvText); }
                catch (RegistryFormatException ex) { registryError = ex.Message; }
            }

            var context = new SemanticContext(agr, engine.Config, mode, registry);
            var layers = new List<ISemanticLayer>
            {
                new InputHygieneLayer(),
                new SectionSchemaLayer(),
                new BlockTypeSupportLayer(),
                new PlaceholderLayer(),
                new ReviewDateLayer(_clock),
                new MetadataLayer(),
                new LayerAgreementLayer(),
                new ClauseReferenceLayer(),
                new RadioQuestionLayer(),
                new HeadingStructureLayer(),
                new WarnCalloutLayer(),
                new TokenResidueLayer()
            };
            if (registryError is null)
            {
                layers.Add(new RegistryLayer());
                layers.Add(new LifecycleLayer());
                layers.Add(new PromotionLayer());
            }

            var all = new List<Check>(schemaChecks);
            foreach (var layer in layers) all.AddRange(layer.Validate(context));
            if (registryError is not null) all.Add(MalformedRegistryCheck(registryError));

            checks = all;
            overall = OverallResultPolicy.Compute(all, agr.ValidationPolicy.WarningThreshold);

            // Emit the payload on any non-fail promotion run with a registry
            // (pass or pass-with-warnings; warnings do not block promotion).
            // A non-fail result in promotion guarantees the ID exists and the
            // repository target resolves (QA-SF-03 / QA-SPEC-18 would have
            // failed otherwise).
            if (mode == Mode.Promotion && overall != OverallResult.Fail && registry is not null)
            {
                var record = registry.Lookup(documentId);
                if (record is not null)
                    payload = PromotionPayloadBuilder.Build(agr, record, engine.Config, _clock, executionId);
            }
        }

        // Stamp ruleSource and errorCategory from qa-checklist.json (the authoritative
        // catalog) onto every check before the report is built. Covers both the
        // schema-blocked and full-pipeline branches.
        checks = engine.Config.Qa.StampAll(checks);

        var report = new ValidationReport
        {
            ReportVersion = EngineInfo.ReportVersion,
            ExecutionId = executionId,
            DocumentId = documentId,
            RequestedMode = mode,
            OverallResult = overall,
            EngineVersion = EngineInfo.Version,
            ConfigVersions = configVersions,
            TimestampUtc = _clock.UtcNow.ToString("o"),
            ArtifactSha256 = null,
            Checks = checks
        };

        return new ValidationOutcome(report, payload);
    }

    private static Check MalformedRegistryCheck(string reason) => new()
    {
        CheckId = "QA-SF-03",
        LayerName = Layer.Release,
        Result = CheckResultValue.Fail,
        SeverityLevel = Severity.Blocking,
        ErrorCategory = ErrorCategory.RegistrySyncError,
        Message = $"The ID Registry export could not be read: {reason}",
        Expected = "a well-formed registry CSV export with the required columns",
        Remediation = "Re-export the PMC Registry sheet to CSV with the required columns and no duplicate Document IDs.",
        RuleSource = "ORG-MST-WFE-001 4.5 item 3"
    };
}
