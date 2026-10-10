using Registrar.Core;
using Registrar.Core.Model;
using Registrar.Validation;

namespace Registrar.Pipeline;

// What a build left on disk. Written is every file that landed, Withheld is every file the
// run deliberately did not produce with the reason, and Failure is the first IO failure, or
// null. Failure is a message rather than an exception because the caller has to report a
// PARTIAL package: a validation report without its document is evidence of something that
// did not happen, so "what landed" must survive the failure.
//
// The message is phrased so a caller can prefix it directly, which is how BuildCommand keeps
// its stderr text identical to what it printed before this type existed.
public sealed record OutputPackageResult(
    IReadOnlyList<string> Written,
    IReadOnlyList<string> Withheld,
    string? Failure);

// Which of the five files land, and when. CLAUDE.md's output-package section is the
// specification; this is the only implementation of it. It lived inside Registrar.Cli until
// GUI-8 needed a second caller, and a second copy of these rules would be a second engine.
public static class OutputPackage
{
    // The four JSON files this tool writes beside an AGR. Lets a picker tell a package file
    // from an input AGR. A drift test builds a package and fails if a file is missing here.
    public static IReadOnlyCollection<string> PackageFileNames { get; } = new HashSet<string>(
        new[] { "validation-report.json", "generation-log.json", "artifact-manifest.json", "promotion-payload.json" },
        StringComparer.OrdinalIgnoreCase);

    public static bool IsPackageFileName(string fileName)
        => PackageFileNames.Contains(Path.GetFileName(fileName));

    // Named for the artifact rather than dated, so a rebuild of the same artifact replaces
    // its own package instead of accumulating. The GUI writes each build into one of these;
    // the CLI does not use it and keeps its flat layout.
    //
    // Both arguments are gated the way DraftCreation.FileName gates a Document ID, and for
    // the same stated reason: this is the function that produces the path segment, and a
    // future caller that forgets the gate must fail loudly rather than write outside the
    // engagement folder. It is stronger here than there, because the caller does not merely
    // write into this folder, it deletes it recursively first. Version is gated as well as
    // documentId: both are concatenated into the one segment, so either can carry the
    // separator, and neither schema pattern admitting one today is a fact about config,
    // which a schema release can widen, rather than a property of this code.
    public static string DirectoryNameFor(string documentId, string version)
    {
        RefuseSeparator(documentId, nameof(documentId));
        RefuseSeparator(version, nameof(version));

        return $"{documentId} - {version}";
    }

    private static void RefuseSeparator(string value, string parameterName)
    {
        if (!string.Equals(Path.GetFileName(value), value, StringComparison.Ordinal))
            throw new ArgumentException(
                $"'{value}' names a path rather than an identifier, so no output package "
                + "folder name can be built from it.",
                parameterName);
    }

    // Report and payload. Called before rendering as well as after: the CLI writes a report
    // before AgrParser.Parse so a malformed AGR still produces one (rule 7), then again with
    // the merged post-render report.
    public static OutputPackageResult WriteValidation(ValidationOutcome outcome, string directory)
    {
        var written = new List<string>();

        var failure = TryWriteText(directory, "validation-report.json",
            ReportSerializer.ToJson(outcome.Report), "validation report", written);
        if (failure is not null) return new OutputPackageResult(written, Array.Empty<string>(), failure);

        if (outcome.Payload is not null)
        {
            failure = TryWriteText(directory, "promotion-payload.json",
                PromotionPayloadSerializer.ToJson(outcome.Payload), "promotion payload", written);
            if (failure is not null) return new OutputPackageResult(written, Array.Empty<string>(), failure);
        }

        return new OutputPackageResult(written, Array.Empty<string>(), null);
    }

    // Generation log, document, manifest.
    public static OutputPackageResult WriteArtifacts(BuildResult result, string directory, string outputFileName)
    {
        var written = new List<string>();
        var withheld = new List<string>();

        // A run that never reached rendering has nothing to log: pre-render failures are
        // fully described by the report alone.
        if (!result.ReachedRendering)
            return new OutputPackageResult(written, withheld, null);

        // Emitted before any outcome branch, so it records a run whose document a
        // post-render check withheld exactly as faithfully as one that succeeded.
        var failure = TryWriteText(directory, "generation-log.json",
            GenerationLogSerializer.ToJson(result.GenerationLog!), "generation log", written);
        if (failure is not null) return new OutputPackageResult(written, withheld, failure);

        // Rule 1, enforced here rather than assumed upstream. BuildPipeline cannot set
        // DocxEligible true alongside a Fail report today, so this branch is unreachable
        // through the pipeline, and it is kept for the same reason QA-ENG-06's block-type
        // fail path is kept: this method is public, static, and callable with a hand-built
        // BuildResult, it now has two callers, and the CLI's own independent Fail check
        // stopped standing in front of it when the writing moved in here. A controlled
        // document that a report calls Fail must never reach disk, whatever any other flag
        // says, so the report is consulted before eligibility rather than after it.
        if (result.Report.OverallResult == OverallResult.Fail)
        {
            withheld.Add($"{outputFileName}: validation failed, so no document is produced.");
            withheld.Add("artifact-manifest.json: validation failed, so no document is produced.");
            return new OutputPackageResult(written, withheld, null);
        }

        if (!result.DocxEligible)
        {
            withheld.Add($"{outputFileName}: no document was produced.");
            withheld.Add("artifact-manifest.json: no document was produced.");
            return new OutputPackageResult(written, withheld, null);
        }

        failure = TryWriteBytes(directory, outputFileName, result.DocxBytes!, "document", written);
        if (failure is not null) return new OutputPackageResult(written, withheld, failure);

        // The manifest describes the artifact just written, so it is emitted here and
        // nowhere else: no DOCX means no manifest.
        failure = TryWriteText(directory, "artifact-manifest.json",
            ArtifactManifestSerializer.ToJson(result.Manifest!), "artifact manifest", written);
        if (failure is not null) return new OutputPackageResult(written, withheld, failure);

        return new OutputPackageResult(written, withheld, null);
    }

    private static string? TryWriteText(string dir, string name, string content, string label, List<string> written)
    {
        try
        {
            File.WriteAllText(Path.Combine(dir, name), content);
            written.Add(name);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"cannot write {label}: {ex.Message}";
        }
    }

    private static string? TryWriteBytes(string dir, string name, byte[] content, string label, List<string> written)
    {
        try
        {
            File.WriteAllBytes(Path.Combine(dir, name), content);
            written.Add(name);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"cannot write {label}: {ex.Message}";
        }
    }
}
