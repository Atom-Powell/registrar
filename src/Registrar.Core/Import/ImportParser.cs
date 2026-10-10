using System.Text.Json;
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Model;

namespace Registrar.Core.Import;

// Recovers an AgrDraft from what an operator pasted out of a Governor chat.
//
// It reports only what nothing else can see: whether JSON could be recovered at all, and
// whether transport mangled any characters. Everything structural or governing is already
// covered. AgrAuthoring.ToDocument reports missing, stray, and empty sections and every
// unfilled required field as an AuthoringGap; ClauseReferenceLayer enforces required
// clauses; the validation layers own identity, lifecycle, and review dates. Restating any
// of it here would give the operator two error surfaces that can disagree.
public static class ImportParser
{
    public static ImportResult Parse(string pastedText, ConfigBundle config)
    {
        var candidates = JsonCandidates.Collect(pastedText);

        if (candidates.Count == 0) return new ImportResult(null, new[] { NoJsonFound() });

        foreach (var candidate in candidates)
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(candidate.Text);
            }
            catch (JsonException)
            {
                continue;
            }

            if (node is not JsonObject payload) continue;

            var findings = new List<ImportFinding>();
            ScanCharacters(payload, string.Empty, config, findings);
            var draft = ToDraft(payload, findings);
            return new ImportResult(draft, findings);
        }

        // An unfenced candidate carrying no quotation mark was never an attempt at JSON:
        // every JSON object with content has a quoted key. This gate matters because
        // JsonCandidates anchors its fallback span on the first brace anywhere in the reply,
        // so ordinary prose that merely mentions a brace still produces a candidate. Without
        // it, a Governor reply that produced no AGR at all reports as truncated, and the
        // operator is told to ask for a resend of something that was never sent.
        //
        // A fenced candidate is exempt. A fence is by construction an attempt at JSON, and
        // the case the design singles out is exactly a fenced envelope whose delimiters a
        // chat surface replaced with curly quotes: it has no ASCII quotation mark at all.
        // Gating it away reported NoJsonFound and told the operator to include the code
        // block their paste already had, an instruction nobody can act on.
        var attempts = candidates.Where(c => c.Fenced || c.Text.Contains('"')).ToList();

        if (attempts.Count == 0) return new ImportResult(null, new[] { NoJsonFound() });

        return new ImportResult(null, new[]
        {
            attempts.Any(a => JsonCandidates.LooksTruncated(a.Text))
                ? new ImportFinding(
                    null,
                    "The reply appears to have been cut off before the AGR finished. Ask the "
                        + "Governor to resend it, or to send it in smaller parts.",
                    ImportFindingKind.Truncated)
                : new ImportFinding(
                    null,
                    "JSON was found but could not be read. If the chat replaced the quotation "
                        + "marks with curly ones, ask the Governor to resend the AGR inside a "
                        + "code fence.",
                    ImportFindingKind.Malformed)
        });
    }

    // Repairs the characters the config map names, and only when a caller asks. Parse never
    // calls this: silently altering Governor-supplied text would hide from the operator
    // that their transport is damaging content, and CLAUDE.md treats that operator
    // checkpoint as a governance control rather than friction to remove.
    //
    // Repair happens on the parsed payload, never on raw text. The map sends U+201D to a
    // straight quote; applying that to raw JSON would turn a curly quote inside a drafted
    // sentence into an unescaped delimiter and break a document that parsed a moment ago.
    // Re-serializing from the node escapes the replacement correctly.
    //
    // Takes the whole ConfigBundle rather than ContentLint, matching Parse. The repair map
    // is parsed by ConfigLoader at load, so reading it here is a lookup rather than a parse
    // that could throw.
    public static NormalizeResult Normalize(string pastedText, ConfigBundle config)
    {
        var repairs = config.TransportRepairs;
        if (repairs.Count == 0) return new NormalizeResult(pastedText, 0, Array.Empty<string>());

        foreach (var candidate in JsonCandidates.Collect(pastedText))
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(candidate.Text);
            }
            catch (JsonException)
            {
                continue;
            }

            if (node is not JsonObject payload) continue;

            var repaired = 0;
            var collisions = new HashSet<string>();
            var result = RepairNode(payload, repairs, ref repaired, collisions);

            // Repair is all-or-nothing. Two source keys repairing to the same string would
            // otherwise overwrite one another silently through JsonObject's add-or-update
            // indexer, and a partial repair that dropped a placeholder is worse than no
            // repair: the operator would believe the document had been cleaned when content
            // was actually lost.
            if (collisions.Count > 0)
                return new NormalizeResult(pastedText, 0, collisions.ToList());

            return new NormalizeResult(
                result!.ToJsonString(AgrSerializer.Options),
                repaired,
                Array.Empty<string>());
        }

        // Nothing readable to repair. Returning the input unchanged is the honest answer:
        // repairing what cannot be parsed would mean guessing at JSON structure.
        return new NormalizeResult(pastedText, 0, Array.Empty<string>());
    }

    // Does not clone child before recursing. RepairNode never returns its own argument for
    // the JsonObject, JsonArray, or string-JsonValue cases below: each builds and returns a
    // brand-new node, so the original (still parented to the source tree) is only ever read,
    // never reattached. The one case that can return its own argument, the default branch
    // below, already clones it there before returning, which is the only point a clone is
    // needed to avoid attaching an already-parented node to the new tree.
    private static JsonNode? RepairNode(
        JsonNode? node,
        IReadOnlyDictionary<int, string> repairs,
        ref int repaired,
        HashSet<string> collisions)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var (key, child) in obj.ToList())
                {
                    var repairedKey = RepairText(key, repairs, ref repaired);
                    var repairedChild = RepairNode(child, repairs, ref repaired, collisions);

                    // A collision is only possible between two distinct source keys: JSON
                    // does not permit an object to carry the same key twice to begin with.
                    if (result.ContainsKey(repairedKey))
                        collisions.Add(repairedKey);
                    else
                        result[repairedKey] = repairedChild;
                }
                return result;
            }

            case JsonArray array:
            {
                var result = new JsonArray();
                foreach (var child in array.ToList())
                    result.Add(RepairNode(child, repairs, ref repaired, collisions));
                return result;
            }

            case JsonValue value when value.TryGetValue<string>(out var text):
                return JsonValue.Create(RepairText(text, repairs, ref repaired));

            default:
                return node?.DeepClone();
        }
    }

    private static string RepairText(
        string text, IReadOnlyDictionary<int, string> repairs, ref int repaired)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        var changed = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (repairs.TryGetValue(rune.Value, out var replacement))
            {
                builder.Append(replacement);
                changed++;
            }
            else
            {
                builder.Append(rune);
            }
        }

        repaired += changed;
        return builder.ToString();
    }

    private static ImportFinding NoJsonFound()
        => new(
            null,
            "No JSON was found in this paste. Copy the whole reply, including the code block "
                + "that holds the AGR.",
            ImportFindingKind.NoJsonFound);

    // Mirrors AgrAuthoring.FromDocument property for property, at the node level so a
    // missing member leaves a null rather than throwing. SchemaVersion and
    // renderingOptions.stylingProfile are absent on purpose: both are schema constants
    // that ToDocument re-derives, so AgrDraft does not carry them.
    private static AgrDraft ToDraft(JsonObject payload, List<ImportFinding> findings)
    {
        var identity = payload["artifactIdentity"] as JsonObject;
        var context = payload["context"] as JsonObject;
        var data = payload["dataInputs"] as JsonObject;
        var policy = payload["validationPolicy"] as JsonObject;
        var rendering = payload["renderingOptions"] as JsonObject;

        return new AgrDraft
        {
            ArtifactType = Str(identity, "artifactType"),
            DocumentId = Str(identity, "documentId"),
            Client = Str(context, "client"),
            Engagement = Str(context, "engagement"),
            Owner = Str(context, "owner"),
            ReviewDate = Str(context, "reviewDate"),
            ReviewDateOverride = Bind<AgrReviewOverride>(context, "/context", "reviewDateOverride", findings),
            Mode = Str(policy, "mode") ?? Registrar.Core.Mode.Draft,
            Sections = Bind<List<AgrSection>>(data, "/dataInputs", "sections", findings)
                ?? new List<AgrSection>(),
            PlaceholderMap = Bind<Dictionary<string, string?>>(data, "/dataInputs", "placeholderMap", findings)
                ?? new Dictionary<string, string?>(),
            SourceWorkflow = Bind<AgrSourceWorkflowContext>(payload, "", "sourceWorkflowContext", findings),
            Dependencies = Bind<List<AgrDependency>>(payload, "", "dependencies", findings),
            EvidenceReferences = Bind<AgrEvidenceReferences>(payload, "", "evidenceReferences", findings),
            NormalizedFindings = Bind<List<AgrEvidenceItem>>(data, "/dataInputs", "normalizedFindings", findings),
            NormalizedRisks = Bind<List<AgrEvidenceItem>>(data, "/dataInputs", "normalizedRisks", findings),
            NormalizedInventoryEntities = Bind<List<AgrEvidenceItem>>(
                data, "/dataInputs", "normalizedInventoryEntities", findings),
            WarningThreshold = Bind<int?>(policy, "/validationPolicy", "warningThreshold", findings),
            AdditionalRequiredCheckIds = Bind<List<string>>(
                policy, "/validationPolicy", "additionalRequiredCheckIds", findings),
            OptionalSectionToggles = Bind<Dictionary<string, bool>>(
                rendering, "/renderingOptions", "optionalSectionToggles", findings),
            Overrides = new AgrDraftOverrides
            {
                Version = Str(identity, "version"),
                Classification = Str(identity, "classification"),
                ApprovalStatus = Str(identity, "approvalStatus"),
                Layer = Str(identity, "layer"),
                Audience = Str(context, "audience"),
                PreparedBy = Str(context, "preparedBy"),
                PackageName = Str(rendering, "packageName"),
                OutputFileName = Str(rendering, "outputFileName")
            }
        };
    }

    // Takes JsonObject?, not JsonNode?, on purpose. JsonNode's string indexer throws
    // InvalidOperationException when the node it is called on is not actually a JsonObject
    // (for example the Governor emitted "context": "See notes above" instead of an object),
    // and that exception is not a JsonException, so a catch around Deserialize would not
    // help here. ToDraft casts each container with `as JsonObject` before it ever reaches
    // Str or Bind, so a wrong-typed container is already null by the time the indexer runs.
    private static string? Str(JsonObject? parent, string name)
        => parent?[name] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    // A member of the wrong shape degrades to null rather than throwing: the Governor got
    // it wrong, and the operator needs the rest of the document more than they need an
    // exception. Degrading in silence, however, is a lie. Deserialization of a list is
    // atomic, so one block missing a required property discards every section the Governor
    // drafted, and with no finding the operator is shown an AuthoringGap per section saying
    // the section has no content yet. That is false, and the remediation it implies is to
    // redraft a body that was supplied and thrown away.
    //
    // So a member that was present and could not be read is reported at its own pointer,
    // carrying the deserializer's message, which names the property at fault. An absent
    // member stays silent: AgrAuthoring.ToDocument already reports it as an AuthoringGap,
    // and two surfaces reporting one fact is what this component exists to avoid.
    //
    // Recovering the readable elements of a list and dropping only the bad one would be
    // better still. It is a larger change with its own risks and is tracked as follow-up
    // debt; it is not needed for the result to be honest.
    private static T? Bind<T>(
        JsonObject? parent, string parentPath, string name, List<ImportFinding> findings)
    {
        var child = parent?[name];
        if (child is null) return default;

        try
        {
            return child.Deserialize<T>();
        }
        catch (JsonException ex)
        {
            findings.Add(new ImportFinding(
                parentPath + "/" + EscapePointer(name),
                $"The Governor supplied '{name}' but the engine could not read it, so its "
                    + "content was discarded. Correct it and resend the AGR. Reader message: "
                    + ex.Message,
                ImportFindingKind.Unreadable));
            return default;
        }
    }

    // Walks every string in the payload. This is the one scan no existing layer performs:
    // InputHygieneLayer (QA-ENG-05) covers the context fields and placeholder values but
    // not section block text, which is caught only after rendering by QA-SF-08. Finding it
    // at the moment of paste is the cheapest possible moment.
    //
    // It detects. It never modifies: repair is Normalize, and only when the operator asks.
    private static void ScanCharacters(
        JsonNode? node, string path, ConfigBundle config, List<ImportFinding> findings)
    {
        var policy = CharacterPolicy.FromConfig(config.ContentLint);
        Walk(node, path, policy, config.TransportRepairs, findings);
    }

    private static void Walk(
        JsonNode? node,
        string path,
        CharacterPolicy policy,
        IReadOnlyDictionary<int, string> repairs,
        List<ImportFinding> findings)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj)
                {
                    // The key is scanned, not just used to build the pointer. RepairNode
                    // repairs keys, so leaving them out of the scan made the two halves
                    // disagree about which strings they cover: a prohibited character in a
                    // placeholderMap key was invisible at paste time and surfaced later as
                    // a QA-ENG-07 build failure, which is the round trip this scan exists
                    // to eliminate. It also made a repair collision arrive as a surprise
                    // rather than something the findings list had already shown.
                    //
                    // A key offense uses the pointer the value would use, because that
                    // pointer already names the key. The message says which of the two is
                    // at fault.
                    var childPath = path + "/" + EscapePointer(key);

                    var keyOffenses = CharacterLint.Scan(key, policy);
                    if (keyOffenses.Count > 0)
                        findings.Add(new ImportFinding(
                            childPath,
                            Describe("This property name contains", keyOffenses, repairs),
                            ImportFindingKind.ProhibitedCharacter));

                    Walk(child, childPath, policy, repairs, findings);
                }
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    Walk(array[i], path + "/" + i, policy, repairs, findings);
                break;

            case JsonValue value when value.TryGetValue<string>(out var text):
                var offenses = CharacterLint.Scan(text, policy);
                if (offenses.Count == 0) break;

                findings.Add(new ImportFinding(
                    path,
                    Describe("Contains", offenses, repairs),
                    ImportFindingKind.ProhibitedCharacter));
                break;
        }
    }

    // One finding per offending string, not per offending character: CharacterLint.Scan
    // already caps how many offenses FormatActual will print, so a paragraph carrying a
    // dozen curly quotes still reads as one line on the Import screen. The repair sentence
    // has to account for a string whose offenses are a mix of repairable and not, or an
    // operator who repairs would be told the repair clears a problem it does not.
    //
    // The subject is passed in because a key and its value share one pointer, so the
    // message is the only thing that tells the operator which of the two to fix.
    private static string Describe(
        string subject, IReadOnlyList<Offense> offenses, IReadOnlyDictionary<int, string> repairs)
    {
        var actual = CharacterLint.FormatActual(offenses);
        var repairableCount = offenses.Count(o => repairs.ContainsKey(o.Codepoint.Value));

        var repairSentence = repairableCount == offenses.Count
            ? "Repair is available."
            : repairableCount == 0
                ? "No repair mapping is available. Ask the Governor to resend this value without it."
                : $"Repair is available for {repairableCount} of {offenses.Count} of these "
                    + "characters; the rest have no repair mapping.";

        return $"{subject} a prohibited character: {actual}. {repairSentence}";
    }

    // RFC 6901. The "~" substitution runs first, or an escaped "/" would be corrupted by
    // the second pass. Placeholder keys carry both brackets and slashes, so this is load
    // bearing rather than defensive.
    private static string EscapePointer(string key)
        => key.Replace("~", "~0", StringComparison.Ordinal)
              .Replace("/", "~1", StringComparison.Ordinal);
}
