namespace Registrar.Core;

// Wire-format string values from schemas/validation-report.schema.json.
// Centralized so no engine code carries magic strings.
public static class Layer
{
    public const string Input = "input";
    public const string Content = "content";
    public const string Structural = "structural";
    public const string Release = "release";
}

public static class CheckResultValue
{
    public const string Pass = "pass";
    public const string Warn = "warn";
    public const string Fail = "fail";
    public const string Skipped = "skipped";
}

public static class Severity
{
    public const string Blocking = "blocking";
    public const string Warning = "warning";
    public const string Info = "info";
}

public static class ErrorCategory
{
    // Emitted categories. ClauseResolutionError covers both clause-reference resolution
    // (missing/undefined clauseId) and clause-integrity failures (SHA-256 mismatch): the
    // governing spec classifies clause paraphrase/integrity as ClauseResolutionError
    // (qa-checklist.json QA-SF-11), so there is deliberately no separate ClauseIntegrityError.
    public const string ConfigurationError = "ConfigurationError";
    public const string SchemaValidationError = "SchemaValidationError";
    public const string MetadataViolation = "MetadataViolation";
    public const string ClauseResolutionError = "ClauseResolutionError";
    public const string RegistrySyncError = "RegistrySyncError";
    public const string LifecycleTransitionError = "LifecycleTransitionError";

    // Reserved - defined in the wire contract but not emitted in v2. RenderingError is
    // unreachable by design (unsupported blocks are rejected pre-render as ConfigurationError
    // via QA-ENG-06, so the renderer never reaches a rendering-failure state); the other three
    // back roadmapped features (dependency resolution, SharePoint integration, promotion policy).
    // Kept, not pruned, per the Slice 9 decision - see docs/failure-path-coverage.md.
    public const string DependencyResolutionError = "DependencyResolutionError";
    public const string RenderingError = "RenderingError";
    public const string SharePointIntegrationError = "SharePointIntegrationError";
    public const string PromotionPolicyError = "PromotionPolicyError";
}

public static class OverallResult
{
    public const string Pass = "pass";
    public const string PassWithWarnings = "pass-with-warnings";
    public const string Fail = "fail";
}

public static class Mode
{
    public const string Draft = "draft";
    public const string Promotion = "promotion";
}
