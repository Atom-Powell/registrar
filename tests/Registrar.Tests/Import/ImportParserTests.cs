using Registrar.Core;
using Registrar.Core.Import;
using Registrar.Core.Model;
using Xunit;

namespace Registrar.Tests.Import;

public class ImportParserTests
{
    private static string CleanAgrJson()
        => SemanticTestSupport.FixtureText("agr-sample-ds-draft.json");

    private static string AsChatReply(string json)
        => "Here is the AGR you asked for:\n\n```json\n" + json + "\n```\n\nLet me know if the review date needs adjusting.";

    [Fact]
    public void A_clean_fenced_reply_yields_a_draft_and_no_findings()
    {
        var result = ImportParser.Parse(AsChatReply(CleanAgrJson()), SemanticTestSupport.Config());

        Assert.Empty(result.Findings);
        Assert.NotNull(result.Draft);
        Assert.Equal("DS", result.Draft!.ArtifactType);
        Assert.NotEmpty(result.Draft.Sections);
    }

    [Fact]
    public void A_bare_json_paste_yields_a_draft()
    {
        var result = ImportParser.Parse(CleanAgrJson(), SemanticTestSupport.Config());

        Assert.NotNull(result.Draft);
        Assert.Equal("DS", result.Draft!.ArtifactType);
    }

    [Fact]
    public void A_reply_with_no_json_reports_NoJsonFound_and_no_draft()
    {
        var result = ImportParser.Parse(
            "I was not able to produce that artifact.", SemanticTestSupport.Config());

        Assert.Null(result.Draft);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(ImportFindingKind.NoJsonFound, finding.Kind);
    }

    [Fact]
    public void A_truncated_reply_reports_Truncated_and_no_draft()
    {
        var json = CleanAgrJson();
        var result = ImportParser.Parse(
            AsChatReply(json[..(json.Length / 2)]), SemanticTestSupport.Config());

        Assert.Null(result.Draft);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(ImportFindingKind.Truncated, finding.Kind);
    }

    [Fact]
    public void A_balanced_but_invalid_reply_reports_Malformed_and_no_draft()
    {
        var result = ImportParser.Parse("{\"a\": }", SemanticTestSupport.Config());

        Assert.Null(result.Draft);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(ImportFindingKind.Malformed, finding.Kind);
    }

    // The reason tolerant population exists: a reply missing a required object must still
    // open, so the technician can fix it in the editor rather than lose everything else.
    [Fact]
    public void A_reply_missing_artifact_identity_still_yields_a_draft()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node.Remove("artifactIdentity");

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        Assert.NotNull(result.Draft);
        Assert.Null(result.Draft!.ArtifactType);
        Assert.Null(result.Draft.DocumentId);
        Assert.NotEmpty(result.Draft.Sections);
        Assert.Equal("Talewind CPA", result.Draft.Client);
    }

    // Import must not restate what AuthoringGap already reports. A missing field is not an
    // import finding; it is a gap, discovered later by AgrAuthoring.ToDocument.
    [Fact]
    public void A_missing_field_is_not_an_import_finding()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node.Remove("artifactIdentity");

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        Assert.Empty(result.Findings);
    }

    // The defect this guards. AgrSection.Title, AgrSection.Blocks and AgrBlock.BlockType are
    // required members, System.Text.Json throws for a missing one, and list deserialization
    // is atomic: one bad block anywhere collapsed the whole sections list to empty. The
    // operator was then shown an AuthoringGap per section reading "has no content yet",
    // which is false, and whose implied remediation is to redraft a body the Governor
    // already supplied and the engine discarded.
    [Fact]
    public void A_block_missing_its_block_type_reports_Unreadable_rather_than_vanishing()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        var block = node["dataInputs"]!["sections"]![0]!["blocks"]![0]!.AsObject();
        block.Remove("blockType");

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        var finding = Assert.Single(result.Findings);
        Assert.Equal(ImportFindingKind.Unreadable, finding.Kind);
        Assert.Equal("/dataInputs/sections", finding.JsonPath);
        Assert.Contains("blockType", finding.Message, StringComparison.Ordinal);

        // Recovering the good blocks and dropping only the bad one is recorded as
        // follow-up debt. What must never happen again is the loss happening in silence:
        // the sections are still empty, and now the operator is told why.
        Assert.NotNull(result.Draft);
        Assert.Empty(result.Draft!.Sections);
    }

    // The same class of defect on a member that is not a list, proving the fix is in Bind
    // rather than in a special case for sections.
    [Fact]
    public void A_malformed_source_workflow_context_reports_Unreadable_at_its_own_pointer()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["sourceWorkflowContext"]!.AsObject().Remove("executionId");

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        var finding = Assert.Single(result.Findings);
        Assert.Equal(ImportFindingKind.Unreadable, finding.Kind);
        Assert.Equal("/sourceWorkflowContext", finding.JsonPath);
        Assert.Contains("executionId", finding.Message, StringComparison.Ordinal);
        Assert.Null(result.Draft!.SourceWorkflow);
    }

    // Absent is not unreadable. AgrAuthoring.ToDocument already reports an absent member as
    // an AuthoringGap carrying the right jsonPath, and duplicating it here would give the
    // operator two surfaces reporting one fact.
    [Fact]
    public void An_absent_bound_member_stays_silent()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node.Remove("sourceWorkflowContext");
        node["dataInputs"]!.AsObject().Remove("sections");

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        Assert.Empty(result.Findings);
        Assert.Null(result.Draft!.SourceWorkflow);
        Assert.Empty(result.Draft.Sections);
    }

    [Fact]
    public void Mode_defaults_to_draft_when_the_policy_is_absent()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node.Remove("validationPolicy");

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        Assert.Equal(Mode.Draft, result.Draft!.Mode);
    }

    // Regression, found by the Task 3 review. JsonCandidates anchors its fallback span on
    // the first brace anywhere in the reply, so prose mentioning a brace yields a candidate
    // that LooksTruncated calls true. Reporting that as Truncated would tell the operator to
    // ask for a resend of an AGR the Governor never produced.
    [Fact]
    public void Prose_mentioning_a_brace_still_reports_NoJsonFound()
    {
        var result = ImportParser.Parse(
            "Some systems use { for grouping. I was not able to produce that artifact.",
            SemanticTestSupport.Config());

        Assert.Null(result.Draft);
        Assert.Equal(ImportFindingKind.NoJsonFound, Assert.Single(result.Findings).Kind);
    }

    // The one case the design says Normalize deliberately cannot fix. When a chat surface
    // smart-quotes the JSON delimiters there is no ASCII quotation mark left anywhere in
    // the candidate, so the quotation-mark gate that keeps prose out of the fallback span
    // used to empty the list and route this to NoJsonFound. That told an operator whose
    // paste was already inside a code fence to "copy the whole reply, including the code
    // block", an instruction they cannot follow. The gate belongs to the fallback only.
    [Fact]
    public void A_fenced_reply_whose_json_delimiters_are_smart_quotes_reports_Malformed()
    {
        var smartQuoted = CleanAgrJson().Replace("\"", LeftDoubleQuote, StringComparison.Ordinal);

        var result = ImportParser.Parse(AsChatReply(smartQuoted), SemanticTestSupport.Config());

        Assert.Null(result.Draft);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(ImportFindingKind.Malformed, finding.Kind);
        Assert.Contains("code fence", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_larger_of_two_fenced_blocks_wins()
    {
        var paste = "A fragment:\n\n```json\n{\"schemaVersion\": \"1.0.0\"}\n```\n\nThe envelope:\n\n"
            + "```json\n" + CleanAgrJson() + "\n```";

        var result = ImportParser.Parse(paste, SemanticTestSupport.Config());

        Assert.Equal("DS", result.Draft!.ArtifactType);
    }

    // Regression, found by the Task 4 review. A language model can easily emit a string
    // where an object belongs (for example "context": "See notes above"). JsonNode's
    // string indexer throws InvalidOperationException on a node that is not a JsonObject,
    // which is not a JsonException, so Bind's catch would not have helped; the container
    // must degrade to null before any indexer runs on it.
    [Fact]
    public void A_context_present_as_a_string_still_yields_a_draft_with_a_null_client()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"] = "oops, not an object";

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        Assert.NotNull(result.Draft);
        Assert.Null(result.Draft!.Client);
    }

    // Same defect, on the object indexed by Bind<T> rather than Str.
    [Fact]
    public void A_dataInputs_present_as_an_array_still_yields_a_draft_with_empty_sections()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["dataInputs"] = new System.Text.Json.Nodes.JsonArray(1, 2, 3);

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        Assert.NotNull(result.Draft);
        Assert.Empty(result.Draft!.Sections);
    }

    // A candidate is only ever handed to ToDraft after JsonCandidates finds a span
    // containing a brace, so an array or scalar reaches Parse only through a fenced block
    // whose body happens to contain one. Both must still resolve to "no draft", never an
    // exception.
    [Fact]
    public void A_paste_whose_top_level_json_is_an_array_does_not_throw()
    {
        // The string element deliberately carries a brace so the fenced body still
        // satisfies JsonCandidates' "contains a brace" gate, without nesting an actual
        // object that JsonCandidates' brace-matching fallback could mistake for its own
        // separate, independently valid candidate.
        var paste = "Here is what I have:\n\n```json\n[\"{a}\"]\n```\n\nLet me know.";

        var result = ImportParser.Parse(paste, SemanticTestSupport.Config());

        Assert.Null(result.Draft);
    }

    [Fact]
    public void A_paste_whose_top_level_json_is_a_scalar_does_not_throw()
    {
        var paste = "Here is what I have:\n\n```json\n\"a{b}\"\n```\n\nLet me know.";

        var result = ImportParser.Parse(paste, SemanticTestSupport.Config());

        Assert.Null(result.Draft);
    }

    // Built from code points, never written as literals: SourceHygieneTests holds engine
    // and test source to the same character rule the generated documents are held to, and
    // an editor or a hook can silently rewrite an escape sequence back into a literal.
    private static readonly string RightSingleQuote = char.ConvertFromUtf32(0x2019);
    private static readonly string Snowman = char.ConvertFromUtf32(0x2603);
    private static readonly string EmDash = char.ConvertFromUtf32(0x2014);

    [Fact]
    public void A_prohibited_character_in_section_block_text_is_found()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        var block = node["dataInputs"]!["sections"]![0]!["blocks"]![0]!.AsObject();
        block["text"] = "The client" + RightSingleQuote + "s environment.";

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        var finding = Assert.Single(result.Findings);
        Assert.Equal(ImportFindingKind.ProhibitedCharacter, finding.Kind);
        Assert.Equal("/dataInputs/sections/0/blocks/0/text", finding.JsonPath);
        Assert.Contains("U+2019", finding.Message, StringComparison.Ordinal);
        Assert.NotNull(result.Draft);
    }

    [Fact]
    public void A_prohibited_character_in_a_context_field_is_found()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance" + RightSingleQuote + " Lead";

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        var finding = Assert.Single(result.Findings);
        Assert.Equal("/context/owner", finding.JsonPath);
    }

    [Fact]
    public void A_placeholder_key_with_a_slash_is_escaped_in_the_json_path()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["dataInputs"]!["placeholderMap"]!.AsObject()["[TOOL / CONNECTOR NAME]"] =
            "Copilot" + RightSingleQuote + "s connector";

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        var finding = Assert.Single(result.Findings);
        Assert.Equal("/dataInputs/placeholderMap/[TOOL ~1 CONNECTOR NAME]", finding.JsonPath);
    }

    // Walk used an object key only to build the pointer and never scanned it, while
    // RepairNode repaired keys. A prohibited character in a placeholderMap key was
    // therefore invisible at paste time and surfaced much later as a QA-ENG-07 build
    // failure, which is the round trip this scan exists to eliminate. InputHygieneLayer
    // lints placeholder values only, so nothing else covered it either.
    [Fact]
    public void A_prohibited_character_in_a_placeholder_key_is_reported()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        var key = "[TOOL / " + RightSingleQuote + " NAME]";
        node["dataInputs"]!["placeholderMap"]!.AsObject()[key] = "Microsoft Copilot Chat";

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        var finding = Assert.Single(result.Findings);
        Assert.Equal(ImportFindingKind.ProhibitedCharacter, finding.Kind);
        Assert.Equal(
            "/dataInputs/placeholderMap/[TOOL ~1 " + RightSingleQuote + " NAME]",
            finding.JsonPath);
        Assert.Contains("U+2019", finding.Message, StringComparison.Ordinal);
    }

    // The compounding case. An operator could be told "Repair is available." for a
    // character in a value, click repair, and get their paste back unchanged because
    // Normalize refused over a collision between two keys the scan had never mentioned.
    // With keys scanned, the collision is foreseeable from the findings list.
    [Fact]
    public void A_coming_key_collision_is_visible_in_the_findings_before_repair_is_attempted()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        var placeholderMap = node["dataInputs"]!["placeholderMap"]!.AsObject();
        placeholderMap["[TOOL - NAME]"] = "Microsoft Copilot Chat";
        placeholderMap["[TOOL " + EmDash + " NAME]"] = "Microsoft Teams";
        var paste = AsChatReply(node.ToJsonString());
        var config = SemanticTestSupport.Config();

        var result = ImportParser.Parse(paste, config);

        Assert.Contains(result.Findings, f =>
            f.JsonPath == "/dataInputs/placeholderMap/[TOOL " + EmDash + " NAME]"
            && f.Kind == ImportFindingKind.ProhibitedCharacter);

        Assert.NotEmpty(ImportParser.Normalize(paste, config).Collisions);
    }

    [Fact]
    public void A_character_with_a_repair_says_repair_is_available()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance" + RightSingleQuote + " Lead";

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        Assert.Contains("Repair is available", Assert.Single(result.Findings).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_character_with_no_repair_says_so_and_is_not_guessed_at()
    {
        // U+2603 SNOWMAN: prohibited, and deliberately absent from transportRepairs.
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance " + Snowman + " Lead";

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        var finding = Assert.Single(result.Findings);
        Assert.Contains("U+2603", finding.Message, StringComparison.Ordinal);
        Assert.Contains("no repair", finding.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_committed_fixture_imports_with_no_character_findings()
    {
        foreach (var path in Directory.GetFiles(SemanticTestSupport.FixtureDirectory(), "*.json"))
        {
            var result = ImportParser.Parse(File.ReadAllText(path), SemanticTestSupport.Config());

            var offenders = result.Findings
                .Where(f => f.Kind == ImportFindingKind.ProhibitedCharacter)
                .ToList();

            // agr-sample-ds-prohibited-char.json is the only fixture carrying a prohibited
            // character. It holds U+2019 as a JSON escape to exercise QA-ENG-05, which is
            // why a raw byte scan of the fixtures finds nothing and this one still reports.
            if (Path.GetFileName(path) == "agr-sample-ds-prohibited-char.json")
                Assert.NotEmpty(offenders);
            else
                Assert.Empty(offenders);
        }
    }

    // Regression, found by the Task 5 review. The brief that shipped this scan had it emit
    // one ImportFinding per offending rune, uncapped, which would swamp the Import screen's
    // list view for a paragraph carrying more than one bad character and diverges from the
    // one-finding-per-field convention InputHygieneLayer and PostRenderScan already follow.
    [Fact]
    public void A_string_with_several_prohibited_characters_yields_one_finding_naming_more_than_one()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance" + RightSingleQuote + " Lead" + EmDash + " Team";

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        var finding = Assert.Single(result.Findings);
        Assert.Contains("U+2019", finding.Message, StringComparison.Ordinal);
        Assert.Contains("U+2014", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_string_with_more_than_five_offenses_reports_a_capped_message()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = string.Concat(Enumerable.Repeat(Snowman, 6));

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        var finding = Assert.Single(result.Findings);
        Assert.Contains("and", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_string_mixing_a_repairable_and_unrepairable_character_does_not_simply_say_repair_is_available()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance" + RightSingleQuote + " Lead " + Snowman;

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        var finding = Assert.Single(result.Findings);
        Assert.Contains("1 of 2", finding.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Repair is available.", finding.Message, StringComparison.Ordinal);
    }

    private static readonly string RightDoubleQuote = char.ConvertFromUtf32(0x201D);
    private static readonly string LeftDoubleQuote = char.ConvertFromUtf32(0x201C);

    [Fact]
    public void Normalize_repairs_mapped_characters_and_reports_the_count()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance" + RightSingleQuote + " Lead";

        var normalized = ImportParser.Normalize(
            AsChatReply(node.ToJsonString()), SemanticTestSupport.Config());

        Assert.Equal(1, normalized.Repaired);

        var result = ImportParser.Parse(normalized.Text, SemanticTestSupport.Config());
        Assert.Empty(result.Findings);
        Assert.Equal("Governance' Lead", result.Draft!.Owner);
    }

    // The defect this design was corrected for. A curly double quote inside drafted text
    // maps to a straight quote; repairing raw text would turn it into an unescaped JSON
    // delimiter and make a parseable paste unparseable.
    [Fact]
    public void Normalize_of_a_quote_inside_block_text_still_produces_valid_json()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        var block = node["dataInputs"]!["sections"]![0]!["blocks"]![0]!.AsObject();
        block["text"] = "The " + LeftDoubleQuote + "approved" + RightDoubleQuote + " tool list.";

        var normalized = ImportParser.Normalize(
            AsChatReply(node.ToJsonString()), SemanticTestSupport.Config());

        Assert.Equal(2, normalized.Repaired);

        var result = ImportParser.Parse(normalized.Text, SemanticTestSupport.Config());
        Assert.NotNull(result.Draft);
        Assert.Empty(result.Findings);
        Assert.Equal("The \"approved\" tool list.", result.Draft!.Sections[0].Blocks[0].Text);
    }

    [Fact]
    public void Normalize_leaves_an_unmapped_character_alone_and_parse_still_reports_it()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance " + Snowman + " Lead";

        var normalized = ImportParser.Normalize(
            AsChatReply(node.ToJsonString()), SemanticTestSupport.Config());

        Assert.Equal(0, normalized.Repaired);

        var result = ImportParser.Parse(normalized.Text, SemanticTestSupport.Config());
        Assert.Equal(ImportFindingKind.ProhibitedCharacter, Assert.Single(result.Findings).Kind);
    }

    [Fact]
    public void Normalize_is_idempotent()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance" + RightSingleQuote + " Lead";
        var config = SemanticTestSupport.Config();

        var once = ImportParser.Normalize(AsChatReply(node.ToJsonString()), config);
        var twice = ImportParser.Normalize(once.Text, config);

        Assert.Equal(once.Text, twice.Text);
        Assert.Equal(0, twice.Repaired);
    }

    [Fact]
    public void Normalize_returns_an_unreadable_paste_unchanged()
    {
        const string paste = "I was not able to produce that artifact.";

        var normalized = ImportParser.Normalize(paste, SemanticTestSupport.Config());

        Assert.Equal(paste, normalized.Text);
        Assert.Equal(0, normalized.Repaired);
    }

    // The import path must not be the place a malformed config release detonates. Once
    // ConfigLoader parses the repair map at load, the raw map riding on ContentLint is no
    // longer read by anything here, so even a broken one cannot break the "Parse never
    // throws" invariant.
    [Fact]
    public void A_malformed_raw_repair_map_does_not_break_parse_or_normalize()
    {
        var config = SemanticTestSupport.Config();
        var broken = config with
        {
            ContentLint = config.ContentLint with
            {
                TransportRepairs = new Dictionary<string, string> { ["2019"] = "'" }
            }
        };

        var result = ImportParser.Parse(AsChatReply(CleanAgrJson()), broken);

        Assert.NotNull(result.Draft);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Parse_never_repairs_on_its_own()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance" + RightSingleQuote + " Lead";

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        Assert.Contains(RightSingleQuote, result.Draft!.Owner, StringComparison.Ordinal);
    }

    // Regression, found in review. JsonObject's indexer is add-or-update, so repairing two
    // distinct keys to the same string would silently drop one entry with no signal that it
    // happened. Repair must be all-or-nothing: refuse rather than lose placeholder content.
    [Fact]
    public void Normalize_refuses_a_key_collision_and_returns_the_input_unchanged()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        var placeholderMap = node["dataInputs"]!["placeholderMap"]!.AsObject();
        placeholderMap["[TOOL - NAME]"] = "Microsoft Copilot Chat";
        placeholderMap["[TOOL " + EmDash + " NAME]"] = "Microsoft Teams";
        var paste = AsChatReply(node.ToJsonString());

        var normalized = ImportParser.Normalize(paste, SemanticTestSupport.Config());

        Assert.Equal(paste, normalized.Text);
        Assert.Equal(0, normalized.Repaired);
        Assert.Equal("[TOOL - NAME]", Assert.Single(normalized.Collisions));
    }

    // Proves key repair actually works: nothing else exercises a mangled character sitting
    // in a JSON key rather than a value.
    [Fact]
    public void Normalize_repairs_a_single_mangled_placeholder_key_with_no_collision()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        var placeholderMap = node["dataInputs"]!["placeholderMap"]!.AsObject();
        var mangledKey = "[TOOL " + EmDash + " NAME]";
        placeholderMap[mangledKey] = "Microsoft Copilot Chat";

        var normalized = ImportParser.Normalize(
            AsChatReply(node.ToJsonString()), SemanticTestSupport.Config());

        Assert.Empty(normalized.Collisions);

        var result = ImportParser.Parse(normalized.Text, SemanticTestSupport.Config());
        Assert.NotNull(result.Draft);
        Assert.True(result.Draft!.PlaceholderMap.ContainsKey("[TOOL - NAME]"));
        Assert.False(result.Draft.PlaceholderMap.ContainsKey(mangledKey));
    }

    // A collision below the top level must be caught too, not only at the payload root.
    [Fact]
    public void Normalize_refuses_a_key_collision_nested_below_the_top_level()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["renderingOptions"]!["optionalSectionToggles"] = new System.Text.Json.Nodes.JsonObject
        {
            ["Show - Appendix"] = true,
            ["Show " + EmDash + " Appendix"] = false
        };
        var paste = AsChatReply(node.ToJsonString());

        var normalized = ImportParser.Normalize(paste, SemanticTestSupport.Config());

        Assert.Equal(paste, normalized.Text);
        Assert.Equal(0, normalized.Repaired);
        Assert.Equal("Show - Appendix", Assert.Single(normalized.Collisions));
    }
}
