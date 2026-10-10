# GovernorBrief and ImportParser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the operator GUI both directions of the Governor handoff: a per-type drafting brief to send into the chat, and a parser that recovers an `AgrDraft` from the AGR JSON pasted back out.

**Architecture:** Two additions to `Registrar.Core`, no new project and no project-reference changes. `GovernorBrief` is extracted from the per-type loop body already inside `DrafterProjection`, so the clipboard brief and the generated drafting knowledge are one code path. `ImportParser` recovers JSON from a chat reply, populates the nullable-everything `AgrDraft` tolerantly so a partial reply still opens, and scans every string in the payload for characters the engine prohibits. Repair is a separate, explicitly invoked function driven by a new config map.

**Tech Stack:** .NET 8, C#, `System.Text.Json` (`JsonNode` for tolerant reads), xUnit.

## Global Constraints

These bind every task. Copied from `CLAUDE.md` and the design spec.

- **US English, no emojis, no em dashes or en dashes, no decorative unicode** in any source, config, comment, test name, or commit message. Use a plain hyphen, straight quotes, and three periods. `SourceHygieneTests` enforces this on engine source: express any non-ASCII character in a test as an escape or a code point, never as a literal.
- **Zero LLM calls.** Nothing in this unit contacts a model.
- **Configuration over code.** The repair map lives in `config/style-rules.json`. No repair pair may be hard-coded in `src/`.
- **The engine never trusts the Governor.** Import reports; it never silently alters content, and it never guesses at a replacement or at JSON structure.
- **Fail closed on malformed config.** A malformed `transportRepairs` key throws at load rather than being skipped.
- **No new validation rules, no changes to `ConfigBundle.Hashes`, no changes to `agr.schema.json`.**
- **Do not duplicate anything `AuthoringGap` or the validation layers already report:** missing or empty fields, stray or misnamed sections, required clauses, identity, lifecycle, or review dates.
- Line endings: every newline written into generated knowledge text is a literal `\n`, never `Environment.NewLine`. `KnowledgeDocument` already does this; do not change it.
- Run the full suite with `dotnet test Registrar.sln` from the repo root. It must stay green and warning-free.

---

## File Structure

**Create:**
- `src/Registrar.Core/Governor/GovernorBrief.cs` - the per-type drafting brief, written into a `KnowledgeDocument` or returned standalone.
- `src/Registrar.Core/Import/ImportResult.cs` - `ImportResult`, `ImportFinding`, `ImportFindingKind`. Result types only, no logic.
- `src/Registrar.Core/Import/JsonCandidates.cs` - pulls candidate JSON spans out of a chat reply. No knowledge of AGRs.
- `src/Registrar.Core/Import/ImportParser.cs` - `Parse` and `Normalize`. Composes the two files above.
- `tests/Registrar.Tests/Governor/GovernorBriefTests.cs`
- `tests/Registrar.Tests/Import/JsonCandidatesTests.cs`
- `tests/Registrar.Tests/Import/ImportParserTests.cs`
- `tests/Registrar.Tests/Import/ImportRoundTripTests.cs`

**Modify:**
- `src/Registrar.Core/Governor/DrafterProjection.cs` - replace the per-type loop body with a call to `GovernorBrief.Write`.
- `src/Registrar.Core/Governor/KnowledgeDocument.cs` - add a headerless `Fragment()` factory.
- `src/Registrar.Core/Config/ContentLint.cs` - add `TransportRepairs` and its parser.
- `config/style-rules.json` - add `contentLint.transportRepairs`, and cross-reference it from the existing `prohibitedCharacters` comment.
- `tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs` - extract the fully-populated document builder so Task 7 can reuse it.

---

### Task 1: GovernorBrief extracted from DrafterProjection

**Files:**
- Create: `src/Registrar.Core/Governor/GovernorBrief.cs`
- Modify: `src/Registrar.Core/Governor/DrafterProjection.cs:43-79`
- Modify: `src/Registrar.Core/Governor/KnowledgeDocument.cs`
- Test: `tests/Registrar.Tests/Governor/GovernorBriefTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks.
- Produces: `GovernorBrief.For(ArtifactTypeConfig type, ConfigBundle config) -> string` and `GovernorBrief.Write(KnowledgeDocument doc, ArtifactTypeConfig type, ConfigBundle config) -> void`. No later task in this plan consumes them; GUI-8 does.

**Context you need:** `DrafterProjection.Render` currently loops over in-scope artifact types and writes a block per type directly into a `KnowledgeDocument`. That block is exactly the brief a technician needs for one type. The whole point of this task is that the two must be the same code, so they cannot drift. `docs/governor/drafter.md` is committed and byte-compared against fresh output by `GeneratedKnowledgeIsCurrentTests`, which means **that test is your regression guard: if your extraction changes the output by even one character, it fails.** Do not regenerate or hand-edit `docs/governor/drafter.md` in this task. If it fails, your extraction is wrong.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Governor/GovernorBriefTests.cs`:

```csharp
using Registrar.Core.Config;
using Registrar.Core.Governor;
using Xunit;

namespace Registrar.Tests.Governor;

public class GovernorBriefTests
{
    private static ConfigBundle Config() => SemanticTestSupport.Config();

    private static ArtifactTypeConfig Type(string code) => Config().ArtifactTypesByCode[code];

    [Theory]
    [InlineData("AUP")]
    [InlineData("DS")]
    [InlineData("TCL")]
    [InlineData("UQR")]
    [InlineData("PIG")]
    [InlineData("DCN")]
    [InlineData("TAK")]
    [InlineData("RAF")]
    public void A_brief_carries_every_ordered_section_title(string code)
    {
        var type = Type(code);
        var brief = GovernorBrief.For(type, Config());

        foreach (var title in type.SectionSchema!.OrderedSections!)
            Assert.Contains(title, brief, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("AUP")]
    [InlineData("TCL")]
    [InlineData("TAK")]
    [InlineData("RAF")]
    public void A_brief_carries_every_required_clause_id(string code)
    {
        var type = Type(code);
        var brief = GovernorBrief.For(type, Config());

        Assert.NotEmpty(type.RequiredClauses!);
        foreach (var id in type.RequiredClauses!)
            Assert.Contains(id, brief, StringComparison.Ordinal);
    }

    [Fact]
    public void A_brief_carries_every_required_placeholder_token()
    {
        var type = Type("AUP");
        var brief = GovernorBrief.For(type, Config());

        Assert.NotEmpty(type.RequiredPlaceholders!);
        foreach (var token in type.RequiredPlaceholders!)
            Assert.Contains(token, brief, StringComparison.Ordinal);
    }

    [Fact]
    public void The_radio_instruction_appears_only_for_types_that_use_the_pattern()
    {
        const string marker = "radio-option pattern";

        Assert.True(Type("TCL").UsesRadioOptionPattern);
        Assert.Contains(marker, GovernorBrief.For(Type("TCL"), Config()), StringComparison.Ordinal);

        Assert.False(Type("AUP").UsesRadioOptionPattern);
        Assert.DoesNotContain(marker, GovernorBrief.For(Type("AUP"), Config()), StringComparison.Ordinal);
    }

    [Fact]
    public void A_type_outside_governor_scope_has_no_brief()
    {
        var scl = Type("SCL");

        Assert.False(scl.GovernorScope);
        var ex = Assert.Throws<ArgumentException>(() => GovernorBrief.For(scl, Config()));
        Assert.Contains("SCL", ex.Message, StringComparison.Ordinal);
    }

    // The anti-drift guard. If the brief were ever re-derived rather than extracted, this
    // is the test that catches it: the standalone brief must appear verbatim inside the
    // knowledge document the drafting agent reads.
    [Fact]
    public void A_brief_appears_verbatim_inside_the_generated_drafter_knowledge()
    {
        var drafter = new DrafterProjection().Render(KnowledgeProjectionTests.Source());
        var brief = GovernorBrief.For(Type("AUP"), Config());

        Assert.Contains(brief.TrimEnd('\n'), drafter, StringComparison.Ordinal);
    }
}
```

`KnowledgeProjectionTests.Source()` already exists as an `internal static` helper in the same namespace (`Registrar.Tests.Governor`) and delegates to `EmitKnowledgeCommand.LoadSource`. Call it. Do not create a second way of building a `KnowledgeSource`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~GovernorBriefTests"`
Expected: FAIL to compile, "The name 'GovernorBrief' does not exist".

- [ ] **Step 3: Add the headerless factory to KnowledgeDocument**

In `src/Registrar.Core/Governor/KnowledgeDocument.cs`, add a private parameterless constructor and a factory immediately after the existing public constructor:

```csharp
    private KnowledgeDocument() { }

    // A document with no provenance header, for rendering one block on its own. The
    // generated knowledge files always carry the header because they are committed and
    // byte-compared; a brief a technician pastes into a chat does not need it.
    public static KnowledgeDocument Fragment() => new();
```

- [ ] **Step 4: Create GovernorBrief by moving the loop body verbatim**

Create `src/Registrar.Core/Governor/GovernorBrief.cs`. The body of `Write` is `DrafterProjection.cs:45-78` **moved without alteration**. Do not reword a single string: `GeneratedKnowledgeIsCurrentTests` compares bytes.

```csharp
using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// The per-type drafting brief. It lives here rather than inline in DrafterProjection so
// the GUI can hand a technician the brief for the one type they are drafting, using the
// same code that generates the drafting agent's knowledge. One code path means the
// clipboard brief and docs/governor/drafter.md cannot drift apart.
public static class GovernorBrief
{
    private const string DefinedStatus = "defined";

    // Writes one type's block into an existing document. DrafterProjection calls this in
    // its loop, having already filtered to in-scope types, so this method does not
    // re-check scope.
    public static void Write(KnowledgeDocument doc, ArtifactTypeConfig type, ConfigBundle config)
    {
        doc.Sub($"{type.Code} - {type.Name}");

        doc.Para($"Classification: {type.Classification ?? "not set"}. Layer: {type.Layer ?? "not set"}.");

        doc.Para("Sections, in this exact order:");
        var n = 1;
        foreach (var title in type.SectionSchema!.OrderedSections!)
            doc.Bullet($"{n++}. {title}");
        doc.EndBullets();

        if (type.RequiredClauses is { Count: > 0 })
        {
            doc.Para("Required clauses. Emit each as a `clauseInsertion` block carrying only "
                + "the clause ID, in the section where it belongs. Never write clause prose: "
                + "the engine inserts the canonical text and proves it with SHA-256.");
            foreach (var id in type.RequiredClauses)
                doc.Bullet(config.ClausesById.TryGetValue(id, out var clause)
                    ? $"`{id}` - {clause.Name}"
                    : $"`{id}`");
            doc.EndBullets();
        }

        if (type.RequiredPlaceholders is { Count: > 0 })
        {
            doc.Para("Required placeholder tokens:");
            foreach (var token in type.RequiredPlaceholders)
                doc.Bullet($"`{token}`");
            doc.EndBullets();
        }

        if (type.UsesRadioOptionPattern)
            doc.Para("This type uses the radio-option pattern. Selection items must be "
                + "`radioQuestion` blocks carrying at least four options. The renderer applies "
                + "the glyph, the indentation, and the descriptor line; supply labels only.");
    }

    // The same block on its own, for the clipboard. Rejects a type that has no brief
    // rather than returning an empty or misleading one: asking for a brief for SCL is a
    // caller error, not a runtime condition, and the GUI offers only in-scope types.
    public static string For(ArtifactTypeConfig type, ConfigBundle config)
    {
        if (!type.GovernorScope)
            throw new ArgumentException(
                $"{type.Code} is outside Governor scope, so it has no drafting brief.",
                nameof(type));

        if (type.SectionSchema?.Status != DefinedStatus || type.SectionSchema.OrderedSections is null)
            throw new ArgumentException(
                $"{type.Code} has no defined section schema, so it has no drafting brief.",
                nameof(type));

        var doc = KnowledgeDocument.Fragment();
        Write(doc, type, config);
        return doc.Build();
    }
}
```

- [ ] **Step 5: Replace the loop body in DrafterProjection**

In `src/Registrar.Core/Governor/DrafterProjection.cs`, replace lines 43 through 79 (the entire `foreach (var t in inScope) { ... }` block) with:

```csharp
        foreach (var t in inScope)
            GovernorBrief.Write(doc, t, config);
```

Leave everything before and after untouched, including the `inScope` filter and the "Block types" section that follows.

- [ ] **Step 6: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS, all tests including `GeneratedKnowledgeIsCurrentTests`. If `GeneratedKnowledgeIsCurrentTests` fails, your extraction altered the output. Diff it and fix the extraction. **Do not regenerate the committed file to make the test pass.**

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/Governor/GovernorBrief.cs src/Registrar.Core/Governor/DrafterProjection.cs src/Registrar.Core/Governor/KnowledgeDocument.cs tests/Registrar.Tests/Governor/GovernorBriefTests.cs
git commit -m "feat: extract GovernorBrief from DrafterProjection"
```

---

### Task 2: The transportRepairs config map

**Files:**
- Modify: `config/style-rules.json`
- Modify: `src/Registrar.Core/Config/ContentLint.cs`
- Test: `tests/Registrar.Tests/ConfigLoaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ContentLint.TransportRepairs` (`IReadOnlyDictionary<string, string>?`) and `ContentLint.ParseTransportRepairs() -> IReadOnlyDictionary<int, string>`. Task 5 and Task 6 both consume `ParseTransportRepairs()`.

**Context you need:** `config/style-rules.json` already has a `contentLint.prohibitedCharacters` object whose `$comment` says it is documentary only, because the live character policy is `Registrar.Core.CharacterPolicy`. You are adding a sibling that **is** read by the engine, so both comments must say plainly which is which. Keys are `U+XXXX` strings rather than raw characters so the file stays readable and a repair entry cannot itself be mangled by an editor.

- [ ] **Step 1: Write the failing test**

Append to `tests/Registrar.Tests/ConfigLoaderTests.cs` (inside the existing class):

```csharp
    [Fact]
    public void Transport_repairs_parse_to_code_points()
    {
        var repairs = SemanticTestSupport.Config().ContentLint.ParseTransportRepairs();

        Assert.Equal("'", repairs[0x2019]);   // right single quotation mark
        Assert.Equal("-", repairs[0x2014]);   // em dash
        Assert.Equal("...", repairs[0x2026]); // horizontal ellipsis
        Assert.Equal(" ", repairs[0x00A0]);   // no-break space
        Assert.Equal("", repairs[0x00AD]);    // soft hyphen
    }

    [Fact]
    public void A_transport_repair_comment_key_is_not_a_repair()
    {
        var repairs = SemanticTestSupport.Config().ContentLint.ParseTransportRepairs();

        Assert.All(repairs.Keys, cp => Assert.True(cp > 0));
        Assert.DoesNotContain(repairs, r => r.Value.Contains("READ BY THE ENGINE", StringComparison.Ordinal));
    }

    [Fact]
    public void A_malformed_transport_repair_key_fails_closed()
    {
        var lint = new ContentLint
        {
            ApprovedGlyphAllowlist = Array.Empty<string>(),
            TransportRepairs = new Dictionary<string, string> { ["2019"] = "'" }
        };

        var ex = Assert.Throws<InvalidOperationException>(() => lint.ParseTransportRepairs());
        Assert.Contains("2019", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_repaired_code_point_is_actually_prohibited()
    {
        var config = SemanticTestSupport.Config();
        var policy = CharacterPolicy.FromConfig(config.ContentLint);

        foreach (var codePoint in config.ContentLint.ParseTransportRepairs().Keys)
            Assert.False(policy.IsAllowed(new System.Text.Rune(codePoint)),
                $"U+{codePoint:X4} has a repair entry but is already allowed, so the entry is dead config.");
    }
```

Add `using Registrar.Core;` and `using Registrar.Core.Config;` at the top of the file if they are not already there.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: FAIL to compile, "'ContentLint' does not contain a definition for 'ParseTransportRepairs'".

- [ ] **Step 3: Add the config block**

In `config/style-rules.json`, inside `contentLint`, add `transportRepairs` immediately after the `prohibitedCharacters` object:

```json
    "transportRepairs": {
      "$comment": "READ BY THE ENGINE, unlike the documentary prohibitedCharacters block above. Maps a code point that chat surfaces substitute into its approved replacement. ImportParser.Normalize applies these only when the operator explicitly asks for repair. A prohibited code point absent from this map is reported and never replaced: the engine does not guess.",
      "U+2018": "'",
      "U+2019": "'",
      "U+201C": "\"",
      "U+201D": "\"",
      "U+2013": "-",
      "U+2014": "-",
      "U+2026": "...",
      "U+00A0": " ",
      "U+00AD": ""
    },
```

Then amend the existing `prohibitedCharacters.$comment` so the distinction is stated from both sides. Its current text begins `"Documentary only. The engine's authoritative character policy lives in code (Registrar.Validation.CharacterPolicy, ...`. Append this sentence to it, preserving whatever the rest of the existing sentence says:

```
 The transportRepairs map below, unlike this block, IS read by the engine.
```

- [ ] **Step 4: Add the model and parser**

Replace `src/Registrar.Core/Config/ContentLint.cs` with:

```csharp
using System.Globalization;

namespace Registrar.Core.Config;

public sealed record ContentLint
{
    public required IReadOnlyList<string> ApprovedGlyphAllowlist { get; init; }

    // Nullable, not required: ContentLint is constructed directly in some unit tests
    // (CharacterLintTests) that only need the allowlist. TokenResidueLayer (QA-ENG-07)
    // fails closed with a ConfigurationError check when this is absent.
    public TokenResidueConfig? TokenResidue { get; init; }

    // Repairs for characters a chat surface substitutes in transit, keyed "U+XXXX".
    // Read by ImportParser.Normalize and only when the operator explicitly asks. Held as
    // the raw string map the config file declares; ParseTransportRepairs turns it into
    // code points. Nullable for the same reason TokenResidue is.
    public IReadOnlyDictionary<string, string>? TransportRepairs { get; init; }

    // Fails closed on a malformed key rather than skipping it. A skipped entry would mean
    // a character silently stops being repairable, with nothing to notice it: the config
    // would look complete and behave as though the entry were absent.
    //
    // Keys beginning with "$" are the file's own comments, matching the convention the
    // rest of style-rules.json uses, and are the one thing skipped deliberately.
    public IReadOnlyDictionary<int, string> ParseTransportRepairs()
    {
        var result = new Dictionary<int, string>();
        if (TransportRepairs is null) return result;

        foreach (var (key, value) in TransportRepairs)
        {
            if (key.StartsWith('$')) continue;

            if (!key.StartsWith("U+", StringComparison.Ordinal)
                || !int.TryParse(
                        key.AsSpan(2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture,
                        out var codePoint))
            {
                throw new InvalidOperationException(
                    $"contentLint.transportRepairs key '{key}' is not a U+XXXX code point.");
            }

            result[codePoint] = value;
        }

        return result;
    }
}

public sealed record TokenResidueConfig
{
    public required string Pattern { get; init; }
}
```

No `ConfigLoader` change is needed: `contentLint` is already deserialized whole, so the new property binds automatically.

- [ ] **Step 5: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS. If `Every_repaired_code_point_is_actually_prohibited` fails, you have added a repair for a character the engine already permits, which would be dead config. Remove it.

- [ ] **Step 6: Commit**

```bash
git add config/style-rules.json src/Registrar.Core/Config/ContentLint.cs tests/Registrar.Tests/ConfigLoaderTests.cs
git commit -m "feat: add transportRepairs to contentLint config"
```

---

### Task 3: Recovering candidate JSON from a chat reply

**Files:**
- Create: `src/Registrar.Core/Import/JsonCandidates.cs`
- Test: `tests/Registrar.Tests/Import/JsonCandidatesTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `JsonCandidates.Collect(string pastedText) -> IReadOnlyList<string>`, ordered longest first, and `JsonCandidates.LooksTruncated(string candidate) -> bool`. Task 4 consumes both.

**Context you need:** A Governor reply is conversational text that usually contains one fenced code block holding the AGR, sometimes several blocks, sometimes none. Longest-first ordering matters because a reply sometimes fences a short illustrative fragment beside the full envelope, and the envelope is the larger one. This file knows nothing about AGRs: it finds JSON-shaped spans and nothing more.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Import/JsonCandidatesTests.cs`:

```csharp
using Registrar.Core.Import;
using Xunit;

namespace Registrar.Tests.Import;

public class JsonCandidatesTests
{
    [Fact]
    public void A_bare_json_paste_is_a_candidate()
    {
        var candidates = JsonCandidates.Collect("{\"a\": 1}");

        Assert.Contains("{\"a\": 1}", candidates);
    }

    [Fact]
    public void A_fenced_block_is_a_candidate_without_its_fence()
    {
        var paste = "Here you go:\n\n```json\n{\"a\": 1}\n```\n\nLet me know.";

        var candidates = JsonCandidates.Collect(paste);

        Assert.Contains(candidates, c => c.Trim() == "{\"a\": 1}");
        Assert.DoesNotContain(candidates, c => c.Contains("```", StringComparison.Ordinal));
        Assert.DoesNotContain(candidates, c => c.Contains("Let me know", StringComparison.Ordinal));
    }

    [Fact]
    public void An_untagged_fence_is_still_a_candidate()
    {
        var paste = "```\n{\"a\": 1}\n```";

        var candidates = JsonCandidates.Collect(paste);

        Assert.Contains(candidates, c => c.Trim() == "{\"a\": 1}");
    }

    [Fact]
    public void Candidates_are_ordered_longest_first()
    {
        var paste = "A fragment:\n\n```json\n{\"a\": 1}\n```\n\nThe envelope:\n\n```json\n{\"aaaaaaaaaaaaaaaa\": 1}\n```";

        var candidates = JsonCandidates.Collect(paste);

        Assert.True(candidates.Count >= 2);
        Assert.Contains("aaaaaaaaaaaaaaaa", candidates[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_paste_with_no_braces_yields_no_candidates()
    {
        var candidates = JsonCandidates.Collect("I could not generate that artifact.");

        Assert.Empty(candidates);
    }

    [Fact]
    public void An_unbalanced_span_is_collected_and_reported_as_truncated()
    {
        var paste = "```json\n{\"a\": 1, \"b\": {\"c\": 2}";

        var candidates = JsonCandidates.Collect(paste);

        Assert.NotEmpty(candidates);
        Assert.True(JsonCandidates.LooksTruncated(candidates[0]));
    }

    [Fact]
    public void A_balanced_span_is_not_truncated()
    {
        Assert.False(JsonCandidates.LooksTruncated("{\"a\": 1}"));
    }

    [Fact]
    public void A_brace_inside_a_string_does_not_affect_balance()
    {
        Assert.False(JsonCandidates.LooksTruncated("{\"a\": \"a { brace and a \\\" quote\"}"));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~JsonCandidatesTests"`
Expected: FAIL to compile, "The type or namespace name 'Import' does not exist".

- [ ] **Step 3: Implement**

Create `src/Registrar.Core/Import/JsonCandidates.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Registrar.Core.Import;

// Finds JSON-shaped spans in a chat reply. It knows nothing about AGRs: its whole job is
// to undo what a chat surface wraps around a payload. Kept separate from ImportParser so
// the recovery heuristics can be tested against raw text alone.
public static class JsonCandidates
{
    // Non-greedy so consecutive fences are separate matches rather than one span running
    // from the first opening fence to the last closing one.
    private static readonly Regex Fenced = new(
        "```[a-zA-Z0-9]*[ \\t]*\\r?\\n(.*?)```",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // Ordered longest first. A reply sometimes fences a short illustrative fragment beside
    // the full envelope, and the envelope is the larger one, so length is the tiebreak
    // rather than document order or fence tag.
    public static IReadOnlyList<string> Collect(string pastedText)
    {
        var candidates = new List<string>();

        foreach (Match match in Fenced.Matches(pastedText))
        {
            var body = match.Groups[1].Value;
            if (body.Contains('{')) candidates.Add(body);
        }

        var span = BraceSpan(pastedText);
        if (span is not null) candidates.Add(span);

        return candidates
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(c => c.Length)
            .ToList();
    }

    // True when braces or brackets never close, which is the signature of a reply cut off
    // by a length limit rather than one that is merely malformed.
    public static bool LooksTruncated(string candidate)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        foreach (var ch in candidate)
        {
            if (escaped) { escaped = false; continue; }
            if (inString)
            {
                if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }

            switch (ch)
            {
                case '"': inString = true; break;
                case '{':
                case '[': depth++; break;
                case '}':
                case ']': depth--; break;
            }
        }

        return depth > 0 || inString;
    }

    // The outermost brace span of the raw text: from the first '{' to its matching '}', or
    // to the end when it never closes, so a truncated reply is still a candidate that can
    // be reported as truncated rather than vanishing.
    private static string? BraceSpan(string text)
    {
        var start = text.IndexOf('{');
        if (start < 0) return null;

        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (escaped) { escaped = false; continue; }
            if (inString)
            {
                if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }

            switch (ch)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return text[start..(i + 1)];
                    break;
            }
        }

        return text[start..];
    }
}
```

- [ ] **Step 4: Run the tests**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~JsonCandidatesTests"`
Expected: PASS, 8 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Import/JsonCandidates.cs tests/Registrar.Tests/Import/JsonCandidatesTests.cs
git commit -m "feat: recover candidate JSON spans from a chat reply"
```

---

### Task 4: ImportParser.Parse and tolerant population

**Files:**
- Create: `src/Registrar.Core/Import/ImportResult.cs`
- Create: `src/Registrar.Core/Import/ImportParser.cs`
- Test: `tests/Registrar.Tests/Import/ImportParserTests.cs`

**Interfaces:**
- Consumes: `JsonCandidates.Collect`, `JsonCandidates.LooksTruncated` from Task 3.
- Produces: `ImportParser.Parse(string pastedText, ConfigBundle config) -> ImportResult`; `ImportResult { AgrDraft? Draft, IReadOnlyList<ImportFinding> Findings }`; `ImportFinding(string? JsonPath, string Message, ImportFindingKind Kind)`; `ImportFindingKind { NoJsonFound, Malformed, Truncated, ProhibitedCharacter }`. Tasks 5, 6, and 7 all consume these.

**Context you need, and the failure mode to fear:** `AgrParser.Parse` deliberately throws when a required member is missing, because it is documented as callable only after schema validation. Import cannot use it: the entire point is that a partial reply still opens. So population reads `JsonNode` property by property, leaving absent fields null.

**That hand-written mapping is exactly where GUI-3 lost four fields**, one at a time, across four review rounds. `AgrAuthoring.FromDocument` at `src/Registrar.Core/Authoring/AgrAuthoring.cs:205-237` is the authoritative list of what an `AgrDraft` carries. **Open it and mirror it property for property.** Two `AgrDocument` members are deliberately absent from `AgrDraft` and must stay absent: `SchemaVersion` and `RenderingOptions.StylingProfile` are both schema constants that `ToDocument` re-derives. Task 7 adds the test that proves nothing else was dropped.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Import/ImportParserTests.cs`:

```csharp
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

    [Fact]
    public void The_larger_of_two_fenced_blocks_wins()
    {
        var paste = "A fragment:\n\n```json\n{\"schemaVersion\": \"1.0.0\"}\n```\n\nThe envelope:\n\n"
            + "```json\n" + CleanAgrJson() + "\n```";

        var result = ImportParser.Parse(paste, SemanticTestSupport.Config());

        Assert.Equal("DS", result.Draft!.ArtifactType);
    }
}
```

`SemanticTestSupport.FixtureText(name)` already exists and reads a fixture by name; use it rather than adding a second fixture-path helper. The client value asserted above (`Talewind CPA`) has been confirmed against `fixtures/agr-sample-ds-draft.json`.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~ImportParserTests"`
Expected: FAIL to compile, "The name 'ImportParser' does not exist".

- [ ] **Step 3: Create the result types**

Create `src/Registrar.Core/Import/ImportResult.cs`:

```csharp
using Registrar.Core.Model;

namespace Registrar.Core.Import;

// What a paste produced. Draft is null only when no JSON could be recovered at all; a
// paste that recovers a draft returns it even when findings accompany it, because the
// editor keeps showing the problems and Build stays blocked until they are resolved.
public sealed record ImportResult(
    AgrDraft? Draft,
    IReadOnlyList<ImportFinding> Findings);

// Deliberately not a Check. A Check carries checkId, ruleSource, and errorCategory, none
// of which mean anything for "this paste was truncated", and dressing a transport problem
// as a governance violation would mislead both readers of a validation report. This
// mirrors the reasoning behind AuthoringGap.
public sealed record ImportFinding(
    string? JsonPath,
    string Message,
    ImportFindingKind Kind);

public enum ImportFindingKind
{
    // No brace appeared anywhere in the paste.
    NoJsonFound,

    // A JSON-shaped span existed but would not parse.
    Malformed,

    // Braces or brackets never closed, the signature of a reply cut off by a length limit.
    Truncated,

    // A character the engine prohibits, found in the recovered payload.
    ProhibitedCharacter
}
```

- [ ] **Step 4: Implement Parse**

Create `src/Registrar.Core/Import/ImportParser.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
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
                node = JsonNode.Parse(candidate);
            }
            catch (JsonException)
            {
                continue;
            }

            if (node is not JsonObject payload) continue;

            // Task 5 adds the character scan here. Until then a recovered payload has no
            // findings, which is what the tests for this task assert.
            return new ImportResult(ToDraft(payload), Array.Empty<ImportFinding>());
        }

        // A candidate carrying no quotation mark was never an attempt at JSON: every JSON
        // object with content has a quoted key. This gate matters because JsonCandidates
        // anchors its fallback span on the first brace anywhere in the reply, so ordinary
        // prose that merely mentions a brace still produces a candidate. Without it, a
        // Governor reply that produced no AGR at all reports as truncated, and the operator
        // is told to ask for a resend of something that was never sent.
        var attempts = candidates.Where(c => c.Contains('"')).ToList();

        if (attempts.Count == 0) return new ImportResult(null, new[] { NoJsonFound() });

        return new ImportResult(null, new[]
        {
            attempts.Any(JsonCandidates.LooksTruncated)
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
    private static AgrDraft ToDraft(JsonObject payload)
    {
        var identity = payload["artifactIdentity"];
        var context = payload["context"];
        var data = payload["dataInputs"];
        var policy = payload["validationPolicy"];
        var rendering = payload["renderingOptions"];

        return new AgrDraft
        {
            ArtifactType = Str(identity, "artifactType"),
            DocumentId = Str(identity, "documentId"),
            Client = Str(context, "client"),
            Engagement = Str(context, "engagement"),
            Owner = Str(context, "owner"),
            ReviewDate = Str(context, "reviewDate"),
            ReviewDateOverride = Bind<AgrReviewOverride>(context, "reviewDateOverride"),
            Mode = Str(policy, "mode") ?? Registrar.Core.Mode.Draft,
            Sections = Bind<List<AgrSection>>(data, "sections") ?? new List<AgrSection>(),
            PlaceholderMap = Bind<Dictionary<string, string?>>(data, "placeholderMap")
                ?? new Dictionary<string, string?>(),
            SourceWorkflow = Bind<AgrSourceWorkflowContext>(payload, "sourceWorkflowContext"),
            Dependencies = Bind<List<AgrDependency>>(payload, "dependencies"),
            EvidenceReferences = Bind<AgrEvidenceReferences>(payload, "evidenceReferences"),
            NormalizedFindings = Bind<List<AgrEvidenceItem>>(data, "normalizedFindings"),
            NormalizedRisks = Bind<List<AgrEvidenceItem>>(data, "normalizedRisks"),
            NormalizedInventoryEntities = Bind<List<AgrEvidenceItem>>(data, "normalizedInventoryEntities"),
            WarningThreshold = Bind<int?>(policy, "warningThreshold"),
            AdditionalRequiredCheckIds = Bind<List<string>>(policy, "additionalRequiredCheckIds"),
            OptionalSectionToggles = Bind<Dictionary<string, bool>>(rendering, "optionalSectionToggles"),
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

    private static string? Str(JsonNode? parent, string name)
        => parent?[name] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

    // A member of the wrong shape degrades to null rather than throwing: the Governor got
    // it wrong, and the operator needs the rest of the document more than they need an
    // exception.
    private static T? Bind<T>(JsonNode? parent, string name)
    {
        var child = parent?[name];
        if (child is null) return default;

        try
        {
            return child.Deserialize<T>();
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
```

The `config` parameter is unused in this task and that is expected: Task 5 introduces the character scan that consumes it. Do not remove it from the signature, and do not add a placeholder method for the scan.

- [ ] **Step 5: Run the tests**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~ImportParserTests"`
Expected: PASS, 9 tests.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test Registrar.sln`
Expected: PASS, everything green with no new warnings.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/Import/ImportResult.cs src/Registrar.Core/Import/ImportParser.cs tests/Registrar.Tests/Import/ImportParserTests.cs
git commit -m "feat: recover an AgrDraft from a pasted Governor reply"
```

---

### Task 5: The character scan

**Files:**
- Modify: `src/Registrar.Core/Import/ImportParser.cs`
- Test: `tests/Registrar.Tests/Import/ImportParserTests.cs`

**Interfaces:**
- Consumes: `ImportFinding`, `ImportFindingKind` from Task 4; `ContentLint.ParseTransportRepairs()` from Task 2; `CharacterPolicy.FromConfig(ContentLint)` which already exists at `src/Registrar.Core/CharacterPolicy.cs`.
- Produces: no new public surface. `Parse` now returns `ProhibitedCharacter` findings.

**Context you need, and why this task is the point of the unit:** `InputHygieneLayer` (QA-ENG-05) scans `context.owner`, `audience`, `preparedBy`, `client`, `engagement`, `renderingOptions.packageName`, and every `placeholderMap` value. **It does not scan section block text.** A curly quote inside a drafted paragraph is caught only after rendering, by QA-SF-08, which is the most expensive moment to discover it. This scan closes that gap at the moment of paste.

Use the existing `CharacterPolicy`. Do not write a second character policy.

JSON Pointer escaping per RFC 6901: `~` becomes `~0` and `/` becomes `~1`, in that order. Placeholder keys contain `[`, `]`, and `/`, so this matters.

- [ ] **Step 1: Write the failing test**

Append to `tests/Registrar.Tests/Import/ImportParserTests.cs`, inside the existing class:

```csharp
    // Built from code points, never written as literals: SourceHygieneTests holds engine
    // and test source to the same character rule the generated documents are held to, and
    // an editor or a hook can silently rewrite an escape sequence back into a literal.
    private static readonly string RightSingleQuote = char.ConvertFromUtf32(0x2019);
    private static readonly string Snowman = char.ConvertFromUtf32(0x2603);

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
```

`SemanticTestSupport` has no `FixtureDirectory()` yet. Add one beside the existing `FixtureText`, reusing `RepoRoot()` rather than recomputing the path:

```csharp
    public static string FixtureDirectory() => Path.Combine(RepoRoot(), "fixtures");
```

The prohibited-character fixture has been confirmed as `agr-sample-ds-prohibited-char.json`, the only one of the 38 fixtures carrying a character outside the allowed set.

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~ImportParserTests"`
Expected: FAIL. The new tests find zero findings, because `ScanCharacters` is still the empty stub from Task 4.

- [ ] **Step 3: Implement the scan**

In `src/Registrar.Core/Import/ImportParser.cs`, first wire the scan into `Parse`. Replace the success branch Task 4 left:

```csharp
            // Task 5 adds the character scan here. Until then a recovered payload has no
            // findings, which is what the tests for this task assert.
            return new ImportResult(ToDraft(payload), Array.Empty<ImportFinding>());
```

with:

```csharp
            var findings = new List<ImportFinding>();
            ScanCharacters(payload, string.Empty, config, findings);
            return new ImportResult(ToDraft(payload), findings);
```

Then add the scan itself:

```csharp
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
        var repairs = config.ContentLint.ParseTransportRepairs();
        Walk(node, path, policy, repairs, findings);
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
                    Walk(child, path + "/" + EscapePointer(key), policy, repairs, findings);
                break;

            case JsonArray array:
                for (var i = 0; i < array.Count; i++)
                    Walk(array[i], path + "/" + i, policy, repairs, findings);
                break;

            case JsonValue value when value.TryGetValue<string>(out var text):
                foreach (var rune in text.EnumerateRunes())
                {
                    if (policy.IsAllowed(rune)) continue;

                    findings.Add(new ImportFinding(
                        path,
                        Describe(rune, repairs),
                        ImportFindingKind.ProhibitedCharacter));
                }
                break;
        }
    }

    private static string Describe(System.Text.Rune rune, IReadOnlyDictionary<int, string> repairs)
    {
        var code = $"U+{rune.Value:X4}";

        return repairs.ContainsKey(rune.Value)
            ? $"{code} is not an allowed character. Repair is available."
            : $"{code} is not an allowed character and has no repair mapping. Ask the Governor "
                + "to resend this value without it.";
    }

    // RFC 6901. The "~" substitution runs first, or an escaped "/" would be corrupted by
    // the second pass. Placeholder keys carry both brackets and slashes, so this is load
    // bearing rather than defensive.
    private static string EscapePointer(string key)
        => key.Replace("~", "~0", StringComparison.Ordinal)
              .Replace("/", "~1", StringComparison.Ordinal);
```

Add `using Registrar.Core;` at the top of the file if `CharacterPolicy` does not resolve.

- [ ] **Step 4: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS, everything green.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Import/ImportParser.cs tests/Registrar.Tests/Import/ImportParserTests.cs
git commit -m "feat: scan an imported payload for prohibited characters"
```

---

### Task 6: Normalize

**Files:**
- Modify: `src/Registrar.Core/Import/ImportParser.cs`
- Modify: `src/Registrar.Core/Import/ImportResult.cs`
- Test: `tests/Registrar.Tests/Import/ImportParserTests.cs`

**Interfaces:**
- Consumes: `JsonCandidates.Collect` from Task 3; `ContentLint.ParseTransportRepairs()` from Task 2.
- Produces: `ImportParser.Normalize(string pastedText, ContentLint lint) -> NormalizeResult`; `NormalizeResult(string Text, int Repaired)`.

**Context you need, and the defect this design was corrected for:** repair must happen on the **parsed payload**, never on raw text. The map sends U+201D to `"`. Applied to raw JSON, a curly quote inside a drafted sentence becomes an unescaped delimiter, turning a parseable paste into an unparseable one. Walking the `JsonNode` and re-serializing escapes the replacement correctly, so the output is always valid JSON.

Consequently `Normalize` returns the AGR envelope alone, with the conversational wrapper dropped. That is what the Import screen wants: the operator clicks repair and the box then holds clean JSON.

Repair is never automatic. `Parse` must not call `Normalize`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Registrar.Tests/Import/ImportParserTests.cs`, inside the existing class:

```csharp
    private static readonly string RightDoubleQuote = char.ConvertFromUtf32(0x201D);
    private static readonly string LeftDoubleQuote = char.ConvertFromUtf32(0x201C);

    [Fact]
    public void Normalize_repairs_mapped_characters_and_reports_the_count()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance" + RightSingleQuote + " Lead";

        var normalized = ImportParser.Normalize(
            AsChatReply(node.ToJsonString()), SemanticTestSupport.Config().ContentLint);

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
            AsChatReply(node.ToJsonString()), SemanticTestSupport.Config().ContentLint);

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
            AsChatReply(node.ToJsonString()), SemanticTestSupport.Config().ContentLint);

        Assert.Equal(0, normalized.Repaired);

        var result = ImportParser.Parse(normalized.Text, SemanticTestSupport.Config());
        Assert.Equal(ImportFindingKind.ProhibitedCharacter, Assert.Single(result.Findings).Kind);
    }

    [Fact]
    public void Normalize_is_idempotent()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance" + RightSingleQuote + " Lead";
        var lint = SemanticTestSupport.Config().ContentLint;

        var once = ImportParser.Normalize(AsChatReply(node.ToJsonString()), lint);
        var twice = ImportParser.Normalize(once.Text, lint);

        Assert.Equal(once.Text, twice.Text);
        Assert.Equal(0, twice.Repaired);
    }

    [Fact]
    public void Normalize_returns_an_unreadable_paste_unchanged()
    {
        const string paste = "I was not able to produce that artifact.";

        var normalized = ImportParser.Normalize(paste, SemanticTestSupport.Config().ContentLint);

        Assert.Equal(paste, normalized.Text);
        Assert.Equal(0, normalized.Repaired);
    }

    [Fact]
    public void Parse_never_repairs_on_its_own()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(CleanAgrJson())!.AsObject();
        node["context"]!["owner"] = "Governance" + RightSingleQuote + " Lead";

        var result = ImportParser.Parse(node.ToJsonString(), SemanticTestSupport.Config());

        Assert.Contains(RightSingleQuote, result.Draft!.Owner, StringComparison.Ordinal);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~ImportParserTests"`
Expected: FAIL to compile, "'ImportParser' does not contain a definition for 'Normalize'".

- [ ] **Step 3: Add the result type**

Append to `src/Registrar.Core/Import/ImportResult.cs`:

```csharp
// Text is the repaired AGR envelope, ready to hand back to Parse. The conversational
// wrapper is dropped, because repair works on the parsed payload and re-serializes it.
public sealed record NormalizeResult(string Text, int Repaired);
```

- [ ] **Step 4: Implement Normalize**

Add to `ImportParser`, after `Parse`:

```csharp
    // Repairs the characters the config map names, and only when a caller asks. Parse never
    // calls this: silently altering Governor-supplied text would hide from the operator
    // that their transport is damaging content, and CLAUDE.md treats that operator
    // checkpoint as a governance control rather than friction to remove.
    //
    // Repair happens on the parsed payload, never on raw text. The map sends U+201D to a
    // straight quote; applying that to raw JSON would turn a curly quote inside a drafted
    // sentence into an unescaped delimiter and break a document that parsed a moment ago.
    // Re-serializing from the node escapes the replacement correctly.
    public static NormalizeResult Normalize(string pastedText, ContentLint lint)
    {
        var repairs = lint.ParseTransportRepairs();
        if (repairs.Count == 0) return new NormalizeResult(pastedText, 0);

        foreach (var candidate in JsonCandidates.Collect(pastedText))
        {
            JsonNode? node;
            try
            {
                node = JsonNode.Parse(candidate);
            }
            catch (JsonException)
            {
                continue;
            }

            if (node is not JsonObject payload) continue;

            var repaired = 0;
            var result = RepairNode(payload, repairs, ref repaired);

            return new NormalizeResult(
                result!.ToJsonString(AgrSerializer.Options),
                repaired);
        }

        // Nothing readable to repair. Returning the input unchanged is the honest answer:
        // repairing what cannot be parsed would mean guessing at JSON structure.
        return new NormalizeResult(pastedText, 0);
    }

    private static JsonNode? RepairNode(
        JsonNode? node, IReadOnlyDictionary<int, string> repairs, ref int repaired)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var result = new JsonObject();
                foreach (var (key, child) in obj.ToList())
                    result[RepairText(key, repairs, ref repaired)] =
                        RepairNode(child?.DeepClone(), repairs, ref repaired);
                return result;
            }

            case JsonArray array:
            {
                var result = new JsonArray();
                foreach (var child in array.ToList())
                    result.Add(RepairNode(child?.DeepClone(), repairs, ref repaired));
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
```

Add `using Registrar.Core;` if `AgrSerializer` does not resolve.

- [ ] **Step 5: Run the tests**

Run: `dotnet test Registrar.sln`
Expected: PASS, everything green.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Core/Import/ImportParser.cs src/Registrar.Core/Import/ImportResult.cs tests/Registrar.Tests/Import/ImportParserTests.cs
git commit -m "feat: repair transport-mangled characters on explicit request"
```

---

### Task 7: Round-trip proof that no field is dropped

**Files:**
- Create: `tests/Registrar.Tests/Import/ImportRoundTripTests.cs`
- Modify: `tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs:533-583`

**Interfaces:**
- Consumes: `ImportParser.Parse` from Task 4; `AgrSerializer.ToJson`, `AgrAuthoring.FromDocument`, both existing.
- Produces: nothing consumed by later tasks. This is the guard task.

**Context you need, and why this task exists:** GUI-3 lost four fields from `AgrDraft` one at a time across four review rounds, because each fix was driven by a hand-written assertion list, and a list can always omit the field no fixture exercises. The converging move was a single test that populates every optional member and asserts the whole document survives. `ImportParser.ToDraft` is a hand-written mapping with exactly the same failure mode, so it gets the same guard.

`AgrAuthoringTests.A_fully_populated_document_round_trips_losslessly` already builds that fully-populated document. Extract the builder rather than writing a second one: two copies would drift, and the drifted one would be the one that stops catching drops.

- [ ] **Step 1: Extract the fully-populated document builder**

In `tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs`, take the `var populated = original with { ... };` initializer out of `A_fully_populated_document_round_trips_losslessly` and put it in a shared helper. Add to `tests/Registrar.Tests/SemanticTestSupport.cs`:

```csharp
    // Every optional and nullable member of AgrDocument set to a distinctive non-null
    // value. Shared by the authoring round trip and the import round trip: both are
    // hand-written mappings with the same failure mode, a silently dropped field, and a
    // second copy of this builder would drift out of step with the model.
    public static AgrDocument FullyPopulatedAgr()
    {
        var original = Agr("agr-sample-ds-draft.json");
        return original with
        {
            // Cut the existing initializer from AgrAuthoringTests.cs and paste it here.
            // Do not retype it and do not change a single value: the specific values are
            // what make a dropped field visible, and a retyped copy is how the two halves
            // start to disagree.
        };
    }
```

Then have the existing test call `SemanticTestSupport.FullyPopulatedAgr()`. Do not change any of its assertions.

- [ ] **Step 2: Run the tests to confirm the extraction changed nothing**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~AgrAuthoringTests"`
Expected: PASS, exactly as before.

- [ ] **Step 3: Write the failing test**

Create `tests/Registrar.Tests/Import/ImportRoundTripTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Authoring;
using Registrar.Core.Import;
using Xunit;

namespace Registrar.Tests.Import;

public class ImportRoundTripTests
{
    private static string AsChatReply(string json)
        => "Here is the AGR:\n\n```json\n" + json + "\n```\n\nAnything else?";

    // The guard against a silently dropped field. ImportParser.ToDraft is a hand-written
    // mapping, the same shape of code that lost four fields during GUI-3, so it is held to
    // the same standard: every optional member populated, and the whole draft compared.
    [Fact]
    public void A_fully_populated_document_survives_serialize_then_import()
    {
        var populated = SemanticTestSupport.FullyPopulatedAgr();
        var expected = AgrAuthoring.FromDocument(populated);

        var result = ImportParser.Parse(
            AsChatReply(AgrSerializer.ToJson(populated)), SemanticTestSupport.Config());

        Assert.Empty(result.Findings);
        Assert.Equal(expected, result.Draft);
    }

    // Broad shape coverage for nearly no effort: every committed fixture is a different
    // arrangement of the schema, and all of them must survive the wrapper and come back
    // identical to what FromDocument produces from the same document.
    [Fact]
    public void Every_committed_fixture_survives_serialize_then_import()
    {
        var config = SemanticTestSupport.Config();
        var checkedCount = 0;

        foreach (var path in Directory.GetFiles(SemanticTestSupport.FixtureDirectory(), "*.json"))
        {
            AgrDocument document;
            try
            {
                document = AgrParser.Parse(File.ReadAllText(path));
            }
            catch (System.Text.Json.JsonException)
            {
                // Fixtures that deliberately violate the schema cannot be parsed into a
                // document, so there is nothing to round trip. They are covered by the
                // malformed-paste tests instead.
                continue;
            }

            var expected = AgrAuthoring.FromDocument(document);
            var result = ImportParser.Parse(AsChatReply(AgrSerializer.ToJson(document)), config);

            Assert.Equal(expected, result.Draft);
            checkedCount++;
        }

        Assert.True(checkedCount > 20, $"only {checkedCount} fixtures round tripped; expected most of them");
    }
}
```

Add `using Registrar.Core.Model;` if `AgrDocument` or `AgrParser` do not resolve.

- [ ] **Step 4: Run the test**

Run: `dotnet test Registrar.sln --filter "FullyQualifiedName~ImportRoundTripTests"`

Expected: this either passes or reveals a genuine drop. **If `Assert.Equal(expected, result.Draft)` fails, the mapping in `ImportParser.ToDraft` is missing a property.** Compare the failure output against `AgrAuthoring.FromDocument` at `src/Registrar.Core/Authoring/AgrAuthoring.cs:205-237` property by property and add whatever is absent. Do not weaken the assertion, and do not compare field subsets.

One legitimate difference is possible and must be handled in the mapping rather than the assertion: `AgrSerializer.Options` sets `DefaultIgnoreCondition = WhenWritingNull`, so a property whose value is null is omitted from the JSON entirely, and reading it back must produce null rather than an empty collection. If `Sections` or `PlaceholderMap` come back as empty collections where the original was null, that is a real mismatch to resolve; note that `AgrDraft` declares both as non-nullable with empty defaults, so a document with null sections is not representable and that specific case cannot arise.

- [ ] **Step 5: Report the count**

Report in your task report how many fixtures round tripped and how many were skipped as unparseable. Do not adjust the `checkedCount > 20` threshold to match; if it fails, say so and stop, because a sharp drop in the number means extraction quietly stopped working.

- [ ] **Step 6: Run the whole suite**

Run: `dotnet test Registrar.sln`
Expected: PASS, everything green with no new warnings.

- [ ] **Step 7: Commit**

```bash
git add tests/Registrar.Tests/Import/ImportRoundTripTests.cs tests/Registrar.Tests/Authoring/AgrAuthoringTests.cs tests/Registrar.Tests/SemanticTestSupport.cs
git commit -m "test: prove import drops no draft field across every fixture"
```

---

## Definition of done

- `dotnet test Registrar.sln` green, no new warnings.
- `GeneratedKnowledgeIsCurrentTests` green without `docs/governor/drafter.md` having been regenerated or edited.
- No repair pair appears anywhere in `src/`.
- No literal non-ASCII character appears in engine source or tests; `SourceHygieneTests` green.
- `ImportParser.Parse` never throws for any input, and never calls `Normalize`.
