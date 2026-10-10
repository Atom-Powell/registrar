using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core.Authoring;
using Registrar.Core.Config;
using Registrar.Core.Import;
using Registrar.Core.Model;

namespace Registrar.Core.Engagement;

// Classifies the artifacts in an engagement folder so the Engagement screen can answer
// "what does this client still owe".
//
// This lives in Core rather than in the app for a concrete reason: a net8.0 test
// project cannot reference a net8.0-windows one, so logic placed in Registrar.App
// would be unreachable by the suite. Keeping it here is also what lets the app enforce
// no rules of its own.
public static class EngagementScan
{
    // A built folder carries these beside the artifacts. They are not AGRs, and
    // AgrPicker's failure to exclude them is why the CLI menu offers them.
    private static readonly HashSet<string> OutputFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "validation-report.json",
        "artifact-manifest.json",
        "generation-log.json",
        "promotion-payload.json"
    };

    public static EngagementScanResult Run(
        string folder, ConfigBundle config, JsonNode agrSchema, IClock clock)
    {
        if (string.IsNullOrWhiteSpace(folder)) return EngagementScanResult.NoFolderChosen;
        if (!Directory.Exists(folder)) return EngagementScanResult.NotFound(folder);

        List<string> draftPaths;
        List<string> jsonPaths;
        try
        {
            // Materialized here, inside the guard, rather than enumerated lazily below.
            // Directory.EnumerateFiles defers its first directory read until the foreach
            // begins, so a folder removed between the Exists check above and that read
            // used to throw DirectoryNotFoundException out of Run - the same condition
            // as a folder that never existed, reported two contradictory ways. Both now
            // produce NotFound, and Run's never-throws property covers the race as well
            // as the plain case.
            draftPaths = Directory.EnumerateFiles(folder, "*" + DraftFile.Extension).ToList();
            jsonPaths = Directory.EnumerateFiles(folder, "*.json").ToList();
        }
        catch (DirectoryNotFoundException)
        {
            return EngagementScanResult.NotFound(folder);
        }

        var rows = new List<EngagementRow>();

        foreach (var path in draftPaths)
            rows.Add(Classify(path, DraftFile.Read(path), folder, config, agrSchema, clock));

        foreach (var path in jsonPaths)
        {
            if (OutputFileNames.Contains(Path.GetFileName(path))) continue;

            // Read exactly once. The structural check below and ImportParser.Parse used
            // to each read the file independently, so a failure that is a property of
            // the file itself (locked, permission denied, gone, a path too long) could
            // pass the first read's catch and then reproduce, uncaught, a few lines
            // later. Reading once and sharing the text closes that gap: any failure of
            // this single read produces a kept Unreadable row instead of an exception
            // out of Run, mirroring what DraftFile.Read already does for .agrdraft
            // files in one call.
            if (!TryRead(path, out var text))
            {
                rows.Add(Classify(path, null, folder, config, agrSchema, clock));
                continue;
            }

            if (!LooksLikeAgr(text)) continue;

            var draft = ImportParser.Parse(text, config).Draft;
            rows.Add(Classify(path, draft, folder, config, agrSchema, clock));
        }

        return EngagementScanResult.Scanned(
            folder, rows.OrderBy(r => r.FileName, StringComparer.OrdinalIgnoreCase).ToList());
    }

    // Mirrors DraftFile.Read's catch set exactly, including UnauthorizedAccessException,
    // which does not derive from IOException and is an ordinary condition in a synced
    // SharePoint folder, the deployment target.
    private static bool TryRead(string path, out string text)
    {
        try
        {
            text = File.ReadAllText(path);
            return true;
        }
        catch (IOException)
        {
            text = string.Empty;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            text = string.Empty;
            return false;
        }
    }

    // Either test excludes the file: the name check above (cheap, covers the common
    // case) or this structural one (still holds when an output file has been renamed).
    // A file too malformed for this structural test is KEPT, so it reaches
    // ImportParser.Parse and is reported as Unreadable rather than disappearing.
    private static bool LooksLikeAgr(string text)
    {
        try
        {
            return JsonNode.Parse(text) is not JsonObject root
                || root["artifactIdentity"] is not null;
        }
        catch (JsonException)
        {
            return true;
        }
    }

    private static EngagementRow Classify(
        string path,
        AgrDraft? draft,
        string folder,
        ConfigBundle config,
        JsonNode agrSchema,
        IClock clock)
    {
        var name = Path.GetFileName(path);

        if (draft is null)
        {
            return new EngagementRow(name, null, null, null, ArtifactState.Unreadable,
                "Could not be read. Open it to see what is wrong, or ask for it again.");
        }

        var result = AgrAuthoring.ToDocument(draft, config, agrSchema, clock);

        if (result.Document is null)
        {
            var first = result.Gaps.Count > 0 ? result.Gaps[0].Message : "Not yet complete.";
            return new EngagementRow(name, draft.ArtifactType, draft.DocumentId, draft.Client,
                ArtifactState.Draft,
                $"{result.Gaps.Count} field(s) still to fill. First: {first}");
        }

        var document = result.Document;
        var outputPath = Path.Combine(folder, document.RenderingOptions.OutputFileName);

        return File.Exists(outputPath)
            ? new EngagementRow(name, document.Identity.ArtifactType, document.Identity.DocumentId,
                document.Context.Client, ArtifactState.Built,
                $"Built as {document.RenderingOptions.OutputFileName}")
            : new EngagementRow(name, document.Identity.ArtifactType, document.Identity.DocumentId,
                document.Context.Client, ArtifactState.Ready,
                "Complete and ready to build.");
    }
}
