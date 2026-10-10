# Governor Knowledge Generation Implementation Plan (GUI-2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate the Governor's knowledge files from config so its agents stop being taught stale facts, and delete the hand-maintained contract sheet that has been drifting since 2026-07-14.

**Architecture:** Four pure projections in `Registrar.Core.Governor` turn a `ConfigBundle` plus the AGR schema into one markdown file per Copilot Studio agent role. A CLI verb writes them; an xUnit test regenerates and byte-compares the committed copies so drift fails the build. Roughly half the facts they need are already in the repo and unbound, so Task 1 binds config before anything projects it.

**Tech Stack:** .NET 8, C#, `System.Text.Json.Nodes`, xUnit. No new dependencies.

## Global Constraints

- Configuration over code. No artifact type codes, layer names, clause IDs, section titles, or check IDs may be written as literals in `src/`. Every fact comes from `ConfigBundle` or the AGR schema at runtime. Test files are exempt.
- **Determinism is mandatory.** Projections must be pure functions of their input: no clock, no `Environment.NewLine`, no dictionary iteration without an explicit ordinal sort, no culture-sensitive formatting. The staleness guard rests entirely on this. Use `"\n"` literals for line endings.
- US English. No emojis, no em dashes, no en dashes, no smart quotes, no decorative unicode in source, config, generated output, or docs. A commit hook rejects them.
- Generated files carry a provenance header of config SHA-256 values from `ConfigBundle.Hashes` and **no timestamp**.
- Run `dotnet test` from the repo root. The suite is at **383 passing** before this plan starts.

---

## File Structure

**Create:**
- `src/Registrar.Core/Governor/KnowledgeSource.cs` - the input record. Pairs the config bundle with the AGR schema, because schema facts are not config facts.
- `src/Registrar.Core/Governor/IKnowledgeProjection.cs` - one method plus a filename.
- `src/Registrar.Core/Governor/KnowledgeDocument.cs` - shared markdown builder. Owns the provenance header and the line-ending discipline so four projections cannot each get it subtly wrong.
- `src/Registrar.Core/Governor/IntakeProjection.cs`
- `src/Registrar.Core/Governor/DrafterProjection.cs`
- `src/Registrar.Core/Governor/PayloadProjection.cs`
- `src/Registrar.Core/Governor/CorrectionProjection.cs`
- `src/Registrar.Cli/EmitKnowledgeCommand.cs`
- `tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs`
- `tests/Registrar.Tests/Governor/GeneratedKnowledgeIsCurrentTests.cs` - the staleness guard.
- `docs/governor/intake.md`, `drafter.md`, `payload.md`, `correction.md` - generated, committed.

**Modify:**
- `src/Registrar.Core/Config/ConfigModels.cs` - add `VocabularyConfig`; add `UsesRadioOptionPattern` to `ArtifactTypeConfig`.
- `src/Registrar.Core/Config/ConfigBundle.cs` - add `Vocabulary`.
- `src/Registrar.Core/Config/ConfigLoader.cs` - parse the vocabulary.
- `src/Registrar.Core/Config/QaCatalog.cs` - add `Rule`, `Layer`, `AppliesTo` to the entry; add enumeration.
- `src/Registrar.Cli/Program.cs` - register the verb.
- `scripts/build-travel-pack.ps1` - swap the contract sheet for the four generated files.
- `docs/governor-instructions.md`, `docs/operations-guide.md`, `docs/tech-debt.md` - drop contract-sheet references.
- `docs/ROADMAP.md` - two amendments.

**Delete:**
- `docs/governor-contract.md`

---

### Task 1: Bind the config that already exists

Three separate facts live in the repo and are never read. All three are needed by later tasks, and binding them is one reviewable idea: read the config we already ship.

**Files:**
- Modify: `src/Registrar.Core/Config/ConfigModels.cs`
- Modify: `src/Registrar.Core/Config/ConfigBundle.cs`
- Modify: `src/Registrar.Core/Config/ConfigLoader.cs`
- Modify: `src/Registrar.Core/Config/QaCatalog.cs`
- Test: `tests/Registrar.Tests/ConfigLoaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ConfigBundle.Vocabulary` of type `VocabularyConfig` with `DocumentIdPattern`, `VersionPattern`, `Classifications`, `FrameworkLayers`, `Categories`, `ProhibitedStatusAliases`, all non-null. `ArtifactTypeConfig.UsesRadioOptionPattern` as `bool`. `QaCatalogEntry` as `(string CheckId, string? RuleSource, string? ErrorCategory, string? Rule, string? Layer, string? AppliesTo)`. `QaCatalog.Entries` as `IReadOnlyList<QaCatalogEntry>` ordered by `CheckId` ordinal.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Registrar.Tests/ConfigLoaderTests.cs`:

```csharp
[Fact]
public void Vocabulary_is_bound_from_vocabularies_json()
{
    var v = SemanticTestSupport.Config().Vocabulary;

    Assert.StartsWith("^ORG-", v.DocumentIdPattern);
    Assert.Equal("^v[0-9]+\\.[0-9]+$", v.VersionPattern);
    Assert.Contains("Client-Facing", v.Classifications);
    Assert.Contains("Controlled Signoff", v.Classifications);
    Assert.Contains("Internal Working Document", v.Classifications);
    Assert.Equal(4, v.FrameworkLayers.Count);
    Assert.Contains("Governance", v.FrameworkLayers);
    Assert.Contains("GOV", v.Categories);
    Assert.Contains("Final", v.ProhibitedStatusAliases);
}

[Fact]
public void Radio_option_pattern_flag_is_bound_per_artifact_type()
{
    var types = SemanticTestSupport.Config().ArtifactTypesByCode;

    Assert.True(types["TCL"].UsesRadioOptionPattern);
    Assert.True(types["DQR"].UsesRadioOptionPattern);
    Assert.False(types["DS"].UsesRadioOptionPattern);
}

[Fact]
public void Qa_catalog_binds_rule_text_and_enumerates()
{
    var qa = SemanticTestSupport.Config().Qa;

    Assert.NotEmpty(qa.Entries);
    Assert.Equal(qa.Entries.Select(e => e.CheckId).OrderBy(id => id, StringComparer.Ordinal),
        qa.Entries.Select(e => e.CheckId));

    var eng08 = qa.Entries.Single(e => e.CheckId == "QA-ENG-08");
    Assert.Equal("input", eng08.Layer);
    Assert.Equal("MetadataViolation", eng08.ErrorCategory);
    Assert.False(string.IsNullOrWhiteSpace(eng08.Rule));
    Assert.Contains("artifactIdentity.layer", eng08.Rule);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: FAIL, will not compile. `Vocabulary`, `UsesRadioOptionPattern`, `Entries`, and `Rule` do not exist.

- [ ] **Step 3: Add the config models**

In `src/Registrar.Core/Config/ConfigModels.cs`, inside `ArtifactTypeConfig`, add below `Layer`:

```csharp
    // Set on TCL and DQR. Drives the four-option radio rule (QA-SF-10) and the
    // drafter's per-type guidance. Config that nothing read until GUI-2.
    public bool UsesRadioOptionPattern { get; init; }
```

Then add a new record at the end of the file:

```csharp
// vocabularies.json beyond the approval-status and review-interval slices ConfigLoader
// already parsed. These are closed sets and patterns the Governor must be told about
// exactly, so they are bound rather than restated anywhere.
public sealed record VocabularyConfig
{
    public required string DocumentIdPattern { get; init; }
    public required string VersionPattern { get; init; }
    public required IReadOnlyList<string> Classifications { get; init; }
    public required IReadOnlyList<string> FrameworkLayers { get; init; }
    public required IReadOnlyList<string> Categories { get; init; }
    public required IReadOnlyList<string> ProhibitedStatusAliases { get; init; }
}
```

- [ ] **Step 4: Add the bundle field**

In `src/Registrar.Core/Config/ConfigBundle.cs`, add below `Transitions`:

```csharp
    public required VocabularyConfig Vocabulary { get; init; }
```

- [ ] **Step 5: Parse the vocabulary**

In `src/Registrar.Core/Config/ConfigLoader.cs`, add this method beside `ParseApprovalStatus`:

```csharp
    private static VocabularyConfig ParseVocabulary(string vocabulariesText)
    {
        var root = JsonNode.Parse(vocabulariesText)!;
        static IReadOnlyList<string> Strings(JsonNode? node) =>
            node is null ? Array.Empty<string>() : node.AsArray().Select(n => (string)n!).ToList();

        return new VocabularyConfig
        {
            DocumentIdPattern = (string)root["documentId"]!["pattern"]!,
            VersionPattern = (string)root["version"]!["pattern"]!,
            Classifications = Strings(root["classification"]),
            FrameworkLayers = Strings(root["frameworkLayer"]!["values"]),
            Categories = Strings(root["category"]!["values"]),
            ProhibitedStatusAliases = Strings(root["approvalStatus"]!["prohibitedAliases"])
        };
    }
```

Then in `Load`, add to the returned `ConfigBundle` initializer, beside `Transitions`:

```csharp
            Vocabulary = ParseVocabulary(vocabulariesText),
```

- [ ] **Step 6: Widen the QA catalog entry and add enumeration**

In `src/Registrar.Core/Config/QaCatalog.cs`, replace the entry record on line 6:

```csharp
public sealed record QaCatalogEntry(
    string CheckId,
    string? RuleSource,
    string? ErrorCategory,
    string? Rule = null,
    string? Layer = null,
    string? AppliesTo = null);
```

Add this property to the `QaCatalog` class, beside `Contains`:

```csharp
    // Ordered so the generated correction knowledge is byte-stable across runs.
    public IReadOnlyList<QaCatalogEntry> Entries =>
        _byId.Values.OrderBy(e => e.CheckId, StringComparer.Ordinal).ToList();
```

And in `Parse`, replace the entry construction:

```csharp
            byId[checkId] = new QaCatalogEntry(
                checkId,
                (string?)node["ruleSource"],
                (string?)node["errorCategory"],
                (string?)node["rule"],
                (string?)node["layer"],
                (string?)node["appliesTo"]);
```

The three new parameters are optional with defaults, so every existing `new QaCatalogEntry(...)` call site keeps compiling unchanged.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test`
Expected: PASS, 386 tests (383 plus the three added here).

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Core/Config tests/Registrar.Tests/ConfigLoaderTests.cs
git commit -m "feat: bind the vocabulary, radio flag, and QA rule text from config"
```

---

### Task 2: The projection contract and the intake file

Establishes the pattern the other three follow, using the simplest projection.

**Files:**
- Create: `src/Registrar.Core/Governor/KnowledgeSource.cs`
- Create: `src/Registrar.Core/Governor/IKnowledgeProjection.cs`
- Create: `src/Registrar.Core/Governor/KnowledgeDocument.cs`
- Create: `src/Registrar.Core/Governor/IntakeProjection.cs`
- Test: `tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs`

**Interfaces:**
- Consumes: `ConfigBundle.Vocabulary`, `ArtifactTypeConfig.UsesRadioOptionPattern` from Task 1.
- Produces: `KnowledgeSource(ConfigBundle Config, JsonNode AgrSchema)`; `IKnowledgeProjection` with `string FileName { get; }` and `string Render(KnowledgeSource source)`; `KnowledgeDocument` with a constructor `(string title, string audience, string purpose, ConfigBundle config)` and the chainable methods `Heading`, `Sub`, `Para`, `Bullet`, `Table`, `Build`; `IntakeProjection` whose `FileName` is `"intake.md"`. Tasks 3, 4, and 5 use all of these.

- [ ] **Step 1: Write the failing tests**

Create `tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Governor;

namespace Registrar.Tests.Governor;

public class KnowledgeProjectionTests
{
    internal static KnowledgeSource Source()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        var schema = JsonNode.Parse(File.ReadAllText(schemaPath))!;
        return new KnowledgeSource(SemanticTestSupport.Config(), schema);
    }

    [Fact]
    public void Intake_lists_every_artifact_type_including_non_generable_ones()
    {
        var text = new IntakeProjection().Render(Source());
        var config = SemanticTestSupport.Config();

        foreach (var code in config.ArtifactTypesByCode.Keys)
            Assert.Contains(code, text);

        // The Steward must be able to say "that one cannot be generated yet",
        // so non-generable types appear and are marked.
        Assert.Contains("TVR", text);
        Assert.Contains("SOP", text);
    }

    [Fact]
    public void Intake_marks_generable_and_non_generable_types_differently()
    {
        var text = new IntakeProjection().Render(Source());

        var ds = text.Split('\n').Single(l => l.StartsWith("| DS |"));
        var tvr = text.Split('\n').Single(l => l.StartsWith("| TVR |"));

        Assert.Contains("| yes |", ds);
        Assert.Contains("| no |", tvr);
    }

    [Fact]
    public void Every_projection_carries_the_config_provenance_header()
    {
        var text = new IntakeProjection().Render(Source());

        Assert.Contains("artifact-types.json", text);
        Assert.Contains(SemanticTestSupport.Config().Hashes["artifact-types.json"], text);
    }

    [Fact]
    public void Projections_are_deterministic()
    {
        var a = new IntakeProjection().Render(Source());
        var b = new IntakeProjection().Render(Source());

        Assert.Equal(a, b);
    }

    [Fact]
    public void Generated_knowledge_contains_no_prohibited_characters()
    {
        var text = new IntakeProjection().Render(Source());

        // Code points, never literal characters. The repo holds its own source to an
        // escapes-only rule (SourceHygieneTests), so a test that spells an em dash
        // literally would itself be the violation it is checking for.
        int[] prohibited = { 0x2014, 0x2013, 0x2018, 0x2019, 0x201C, 0x201D, 0x2026 };
        foreach (var cp in prohibited)
            Assert.DoesNotContain((char)cp, text);
        Assert.DoesNotContain('\r', text);       // CR: line endings must be "\n" only
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~KnowledgeProjectionTests"`
Expected: FAIL, will not compile. Nothing in `Registrar.Core.Governor` exists.

- [ ] **Step 3: Create the input record and the interface**

`src/Registrar.Core/Governor/KnowledgeSource.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// The AGR schema travels alongside the config bundle rather than inside it: required
// fields, the outputFileName pattern, and the blockType enum are schema facts, not
// config facts, and folding them into ConfigBundle would blur that line.
public sealed record KnowledgeSource(ConfigBundle Config, JsonNode AgrSchema);
```

`src/Registrar.Core/Governor/IKnowledgeProjection.cs`:

```csharp
namespace Registrar.Core.Governor;

// One generated knowledge file for one Copilot Studio agent role. Implementations must
// be pure: no IO, no clock, no ambient state. The staleness guard in the test suite
// regenerates every projection and byte-compares it against the committed file, so any
// nondeterminism fails the build.
public interface IKnowledgeProjection
{
    string FileName { get; }
    string Render(KnowledgeSource source);
}
```

- [ ] **Step 4: Create the shared document builder**

`src/Registrar.Core/Governor/KnowledgeDocument.cs`:

```csharp
using System.Text;
using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// Shared markdown builder for the generated knowledge files. It owns two things that
// four separate projections would otherwise each have to get right: the provenance
// header, and the line-ending discipline. Every newline here is a literal "\n", never
// Environment.NewLine, because the output is byte-compared on a Windows dev machine and
// a Linux CI runner.
public sealed class KnowledgeDocument
{
    private readonly StringBuilder _sb = new();

    public KnowledgeDocument(string title, string audience, string purpose, ConfigBundle config)
    {
        _sb.Append("# ").Append(title).Append("\n\n");
        _sb.Append("**Audience:** ").Append(audience).Append("\n\n");
        _sb.Append("**Purpose:** ").Append(purpose).Append("\n\n");
        _sb.Append("Generated by `registrar emit-knowledge` from the config below. Do not edit ")
           .Append("by hand: edits are overwritten on the next run, and a committed copy that ")
           .Append("disagrees with config fails the build.\n\n");
        _sb.Append("| Config source | SHA-256 |\n|---|---|\n");
        foreach (var (name, hash) in config.Hashes.OrderBy(h => h.Key, StringComparer.Ordinal))
            _sb.Append("| `").Append(name).Append("` | `").Append(hash).Append("` |\n");
        _sb.Append('\n');
    }

    public KnowledgeDocument Heading(string text)
    {
        _sb.Append("## ").Append(text).Append("\n\n");
        return this;
    }

    public KnowledgeDocument Sub(string text)
    {
        _sb.Append("### ").Append(text).Append("\n\n");
        return this;
    }

    public KnowledgeDocument Para(string text)
    {
        _sb.Append(text).Append("\n\n");
        return this;
    }

    public KnowledgeDocument Bullet(string text)
    {
        _sb.Append("- ").Append(text).Append('\n');
        return this;
    }

    public KnowledgeDocument EndBullets()
    {
        _sb.Append('\n');
        return this;
    }

    public KnowledgeDocument Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        _sb.Append("| ").Append(string.Join(" | ", headers)).Append(" |\n");
        _sb.Append('|');
        foreach (var _ in headers) _sb.Append("---|");
        _sb.Append('\n');
        foreach (var row in rows)
            _sb.Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
        _sb.Append('\n');
        return this;
    }

    public string Build() => _sb.ToString();
}
```

- [ ] **Step 5: Write the intake projection**

`src/Registrar.Core/Governor/IntakeProjection.cs`:

```csharp
using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// Knowledge for the intake agent (the Steward), whose job is deciding which artifact an
// engagement needs. It deliberately carries no section schemas, clause IDs, or block
// types: an intake agent holding structural detail is an intake agent that starts
// drafting.
public sealed class IntakeProjection : IKnowledgeProjection
{
    private const string DefinedStatus = "defined";

    public string FileName => "intake.md";

    public string Render(KnowledgeSource source)
    {
        var config = source.Config;

        var doc = new KnowledgeDocument(
            "Artifact Catalogue",
            "The intake agent, which works out which artifact an engagement needs.",
            "Name every artifact type, say what it is for, and say whether the engine can "
                + "generate it yet.",
            config);

        doc.Para("This catalogue is the complete set. If a request does not match one of these, "
            + "say so rather than inventing a type. If the type exists but is not generable, say "
            + "that too: the engine refuses to generate a type whose structure has not been "
            + "authored, and it will not improvise one.");

        var rows = config.ArtifactTypesByCode.Values
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .Select(t => (IReadOnlyList<string>)new[]
            {
                t.Code,
                t.Name ?? "",
                t.Category,
                t.Classification ?? "not set",
                t.SectionSchema?.Status == DefinedStatus ? "yes" : "no",
                config.ReviewIntervalMonthsByCategory.TryGetValue(t.Category, out var m)
                    ? m.ToString(System.Globalization.CultureInfo.InvariantCulture) + " months"
                    : "",
                t.Description ?? ""
            });

        doc.Heading("Every artifact type");
        doc.Table(
            new[] { "Code", "Name", "Category", "Classification", "Generable", "Review interval", "Description" },
            rows);

        doc.Heading("Default audiences");
        doc.Para("Use these unless the engagement gives you a reason to narrow them.");
        foreach (var t in config.ArtifactTypesByCode.Values
                     .Where(t => !string.IsNullOrWhiteSpace(t.AudienceDefault))
                     .OrderBy(t => t.Code, StringComparer.Ordinal))
            doc.Bullet($"**{t.Code}**: {t.AudienceDefault}");
        doc.EndBullets();

        doc.Heading("What you do not decide");
        doc.Bullet("Document IDs. Registry Owner issues them; the engine never mints one. "
            + "If no ID exists, say so and stop.");
        doc.Bullet("Section structure, clause selection, or placeholder values. Those belong to "
            + "the drafting agent.");
        doc.EndBullets();

        return doc.Build();
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~KnowledgeProjectionTests"`
Expected: PASS, 5 tests.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Core/Governor tests/Registrar.Tests/Governor
git commit -m "feat: knowledge projection contract and the intake catalogue"
```

---

### Task 3: The drafter projection

The heaviest file and the one whose errors are most expensive, because a wrong section title becomes a wrong document.

**Files:**
- Create: `src/Registrar.Core/Governor/DrafterProjection.cs`
- Test: `tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs`

**Interfaces:**
- Consumes: `KnowledgeSource`, `KnowledgeDocument`, `IKnowledgeProjection` from Task 2. `ArtifactTypeConfig.UsesRadioOptionPattern` from Task 1.
- Produces: `DrafterProjection` whose `FileName` is `"drafter.md"`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs`:

```csharp
[Fact]
public void Drafter_carries_every_TCL_section_title_in_order()
{
    var text = new DrafterProjection().Render(Source());
    var titles = SemanticTestSupport.Config()
        .ArtifactTypesByCode["TCL"].SectionSchema!.OrderedSections!;

    var lastIndex = -1;
    foreach (var title in titles)
    {
        var i = text.IndexOf(title, StringComparison.Ordinal);
        Assert.True(i > lastIndex, $"'{title}' is missing or out of order in drafter.md");
        lastIndex = i;
    }
}

[Fact]
public void Drafter_names_required_clauses_with_their_canonical_names()
{
    var text = new DrafterProjection().Render(Source());
    var config = SemanticTestSupport.Config();

    Assert.Contains("CL-003", text);
    Assert.Contains(config.ClausesById["CL-003"].Name, text);
    Assert.Contains("CL-004", text);
    Assert.Contains(config.ClausesById["CL-004"].Name, text);
}

// usesRadioOptionPattern is set on TCL and DQR, but DQR has no defined section schema
// and must not appear at all. Iterate the generable set and read the flag within it;
// do not iterate the flag.
[Fact]
public void Drafter_covers_generable_types_only_and_flags_the_radio_pattern_correctly()
{
    var text = new DrafterProjection().Render(Source());

    Assert.Contains("four", text, StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("DQR", text);
}

// The converse invariant. Without it, a future generable type is silently absent from
// the drafter's knowledge and the suite stays green.
[Fact]
public void Drafter_covers_every_type_with_a_defined_section_schema()
{
    var text = new DrafterProjection().Render(Source());

    foreach (var t in SemanticTestSupport.Config().ArtifactTypesByCode.Values)
        if (t.SectionSchema?.Status == "defined")
            Assert.True(text.Contains(t.Code, StringComparison.Ordinal),
                $"{t.Code} is generable but absent from drafter.md");
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~KnowledgeProjectionTests"`
Expected: FAIL, `DrafterProjection` does not exist.

- [ ] **Step 3: Write the implementation**

`src/Registrar.Core/Governor/DrafterProjection.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// Knowledge for the drafting agent: the structure and required content of every artifact
// the engine can actually generate. Types with no defined section schema are excluded
// entirely, because the engine refuses them (QA-ENG-02) and listing them would invite
// the agent to attempt one.
public sealed class DrafterProjection : IKnowledgeProjection
{
    private const string DefinedStatus = "defined";

    public string FileName => "drafter.md";

    public string Render(KnowledgeSource source)
    {
        var config = source.Config;

        var doc = new KnowledgeDocument(
            "Artifact Structure and Required Content",
            "The drafting agent, which produces artifact content.",
            "Give the exact structure and required content of every artifact the engine can "
                + "generate.",
            config);

        doc.Para("Section titles below are exact. The engine compares them character for "
            + "character and in order, and rejects any addition, omission, reordering, or "
            + "rewording. Do not improve them.");

        var generable = config.ArtifactTypesByCode.Values
            .Where(t => t.SectionSchema?.Status == DefinedStatus && t.SectionSchema.OrderedSections is not null)
            .OrderBy(t => t.Code, StringComparer.Ordinal)
            .ToList();

        foreach (var t in generable)
        {
            doc.Sub($"{t.Code} - {t.Name}");

            doc.Para($"Classification: {t.Classification ?? "not set"}. Layer: {t.Layer ?? "not set"}.");

            doc.Para("Sections, in this exact order:");
            var n = 1;
            foreach (var title in t.SectionSchema!.OrderedSections!)
                doc.Bullet($"{n++}. {title}");
            doc.EndBullets();

            if (t.RequiredClauses is { Count: > 0 })
            {
                doc.Para("Required clauses. Emit each as a `clauseInsertion` block carrying only "
                    + "the clause ID, in the section where it belongs. Never write clause prose: "
                    + "the engine inserts the canonical text and proves it with SHA-256.");
                foreach (var id in t.RequiredClauses)
                    doc.Bullet(config.ClausesById.TryGetValue(id, out var clause)
                        ? $"`{id}` - {clause.Name}"
                        : $"`{id}`");
                doc.EndBullets();
            }

            if (t.RequiredPlaceholders is { Count: > 0 })
            {
                doc.Para("Required placeholder tokens:");
                foreach (var token in t.RequiredPlaceholders)
                    doc.Bullet($"`{token}`");
                doc.EndBullets();
            }

            if (t.UsesRadioOptionPattern)
                doc.Para("This type uses the radio-option pattern. Selection items must be "
                    + "`radioQuestion` blocks carrying at least four options. The renderer applies "
                    + "the glyph, the indentation, and the descriptor line; supply labels only.");
        }

        doc.Heading("Block types");
        doc.Para("These are the only block types the engine renders. Any other value is rejected "
            + "before rendering is attempted.");
        foreach (var bt in BlockTypes(source.AgrSchema))
            doc.Bullet($"`{bt}`");
        doc.EndBullets();

        doc.Heading("Character policy");
        doc.Para("Free text and placeholder values may contain only the characters below. A "
            + "prohibited character fails the run in both draft and promotion. Use a plain "
            + "hyphen, straight quotes, and three periods.");
        doc.Bullet("Printable Basic Latin.");
        doc.Bullet("Printable Latin-1 Supplement, excluding the non-breaking space and the soft "
            + "hyphen, so accented names are fine.");
        if (config.ContentLint.AllowedGlyphs is { Count: > 0 })
            doc.Bullet("Approved glyphs: "
                + string.Join(", ", config.ContentLint.AllowedGlyphs.Select(g => $"`{g}`")));
        doc.EndBullets();

        return doc.Build();
    }

    private static IReadOnlyList<string> BlockTypes(JsonNode schema)
    {
        var node = schema["$defs"]!["contentBlock"]!["properties"]!["blockType"]!["enum"]!;
        return node.AsArray().Select(n => (string)n!).ToList();
    }
}
```

If `ContentLint` has no `AllowedGlyphs` member, read the actual member name from `src/Registrar.Core/Config/ContentLint.cs` and use it; the intent is to list the approved glyph allowlist, and if the type does not expose one, drop that bullet rather than inventing a name.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~KnowledgeProjectionTests"`
Expected: PASS, 9 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Governor/DrafterProjection.cs tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs
git commit -m "feat: drafter knowledge projection"
```

---

### Task 4: The payload projection

**Files:**
- Create: `src/Registrar.Core/Governor/PayloadProjection.cs`
- Test: `tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs`

**Interfaces:**
- Consumes: `ConfigBundle.Vocabulary` from Task 1; `KnowledgeSource`, `KnowledgeDocument` from Task 2.
- Produces: `PayloadProjection` whose `FileName` is `"payload.md"`.

- [ ] **Step 1: Write the failing tests**

Append to `tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs`:

```csharp
[Fact]
public void Payload_carries_the_identity_patterns_from_config()
{
    var text = new PayloadProjection().Render(Source());
    var v = SemanticTestSupport.Config().Vocabulary;

    Assert.Contains(v.DocumentIdPattern, text);
    Assert.Contains(v.VersionPattern, text);
}

[Fact]
public void Payload_carries_every_legal_lifecycle_transition()
{
    var text = new PayloadProjection().Render(Source());
    var config = SemanticTestSupport.Config();

    foreach (var (from, tos) in config.Transitions)
    {
        Assert.Contains(from, text);
        foreach (var to in tos)
            Assert.Contains(to, text);
    }
}

[Fact]
public void Payload_names_the_prohibited_status_aliases()
{
    var text = new PayloadProjection().Render(Source());

    foreach (var alias in SemanticTestSupport.Config().Vocabulary.ProhibitedStatusAliases)
        Assert.Contains(alias, text);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~KnowledgeProjectionTests"`
Expected: FAIL, `PayloadProjection` does not exist.

- [ ] **Step 3: Write the implementation**

`src/Registrar.Core/Governor/PayloadProjection.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core.Config;

namespace Registrar.Core.Governor;

// Knowledge for the payload-assembling agent: the envelope, the identity rules, and the
// closed vocabularies. Everything here is a hard gate; none of it is a matter of taste.
public sealed class PayloadProjection : IKnowledgeProjection
{
    public string FileName => "payload.md";

    public string Render(KnowledgeSource source)
    {
        var config = source.Config;
        var v = config.Vocabulary;

        var doc = new KnowledgeDocument(
            "AGR Envelope and Identity Rules",
            "The payload-assembling agent, which turns content into a valid request.",
            "Give the exact shape of an Artifact Generation Request and the rules its identity "
                + "fields must satisfy.",
            config);

        doc.Heading("Required top-level fields");
        foreach (var field in RequiredFields(source.AgrSchema))
            doc.Bullet($"`{field}`");
        doc.EndBullets();

        doc.Heading("Identity");
        doc.Bullet($"`documentId` must match `{v.DocumentIdPattern}`. The pattern encodes which "
            + "category and code may pair, so a mismatched pairing fails at the schema, not "
            + "later. The embedded code must also equal `artifactType`.");
        doc.Bullet("**Never mint a Document ID.** Registry Owner issues them and the ID must "
            + "already exist in the registry. If none exists, ask for one.");
        doc.Bullet($"`version` must match `{v.VersionPattern}`.");
        doc.Bullet($"`classification` is one of: {Join(v.Classifications)}.");
        doc.Bullet($"`layer` is one of: {Join(v.FrameworkLayers)}. It is determined by the "
            + "artifact type, not chosen. A disagreement with config fails the run.");
        doc.Bullet($"`approvalStatus` is one of: {Join(config.ApprovalStatuses.OrderBy(s => s, StringComparer.Ordinal))}. "
            + "A new artifact is always the initial state.");
        doc.EndBullets();

        doc.Heading("Prohibited status aliases");
        doc.Para($"Never use: {Join(v.ProhibitedStatusAliases)}. They are not controlled values.");

        doc.Heading("Legal lifecycle transitions");
        doc.Table(
            new[] { "From", "May become" },
            config.Transitions
                .OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => (IReadOnlyList<string>)new[]
                {
                    t.Key,
                    t.Value.Count == 0 ? "nothing, this state is terminal" : Join(t.Value)
                }));

        doc.Heading("Review dates");
        doc.Para("A review date must fall within the category interval measured from today, "
            + "unless a documentation-owner override is attached. Do not fabricate an override "
            + "to dodge the interval.");
        doc.Table(
            new[] { "Category", "Interval" },
            config.ReviewIntervalMonthsByCategory
                .OrderBy(k => k.Key, StringComparer.Ordinal)
                .Select(k => (IReadOnlyList<string>)new[]
                {
                    k.Key,
                    k.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + " months"
                }));

        doc.Heading("Rendering options");
        var pattern = (string?)source.AgrSchema["properties"]!["renderingOptions"]!
            ["properties"]!["outputFileName"]!["pattern"];
        if (pattern is not null)
            doc.Bullet($"`outputFileName` must match `{pattern}`.");
        doc.Bullet("`packageName` is the footer package value and must not be empty.");
        doc.EndBullets();

        return doc.Build();
    }

    private static string Join(IEnumerable<string> values) =>
        string.Join(", ", values.Select(v => $"`{v}`"));

    private static IReadOnlyList<string> RequiredFields(JsonNode schema) =>
        schema["required"]!.AsArray().Select(n => (string)n!).ToList();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~KnowledgeProjectionTests"`
Expected: PASS, 12 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Governor/PayloadProjection.cs tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs
git commit -m "feat: payload knowledge projection"
```

---

### Task 5: The correction projection

**Files:**
- Create: `src/Registrar.Core/Governor/CorrectionProjection.cs`
- Test: `tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs`

**Interfaces:**
- Consumes: `QaCatalog.Entries` from Task 1; `KnowledgeSource`, `KnowledgeDocument` from Task 2.
- Produces: `CorrectionProjection` whose `FileName` is `"correction.md"`.

- [ ] **Step 1: Write the failing test**

Append to `tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs`:

```csharp
[Fact]
public void Correction_lists_every_check_in_the_catalogue()
{
    var text = new CorrectionProjection().Render(Source());
    var entries = SemanticTestSupport.Config().Qa.Entries;

    Assert.NotEmpty(entries);
    foreach (var e in entries)
        Assert.True(text.Contains(e.CheckId, StringComparison.Ordinal),
            $"{e.CheckId} is in the catalogue but absent from correction.md");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~KnowledgeProjectionTests"`
Expected: FAIL, `CorrectionProjection` does not exist.

- [ ] **Step 3: Write the implementation**

`src/Registrar.Core/Governor/CorrectionProjection.cs`:

```csharp
namespace Registrar.Core.Governor;

// Knowledge for the correction agent, which turns a failed validation report into a
// corrected request. It is the check catalogue and nothing else: the protocol for how to
// respond to a failure is hand-written prose and lives in governor-payload-correction.md,
// because it is judgment rather than data.
public sealed class CorrectionProjection : IKnowledgeProjection
{
    public string FileName => "correction.md";

    public string Render(KnowledgeSource source)
    {
        var config = source.Config;

        var doc = new KnowledgeDocument(
            "Validation Check Catalogue",
            "The correction agent, which repairs a request the engine rejected.",
            "Say what every check means, so a failed report can be acted on rather than guessed at.",
            config);

        doc.Para("Every entry in a validation report carries a `checkId`. Find it here. The "
            + "report's own `remediation` field is phrased as an instruction you can execute; "
            + "this catalogue tells you what rule produced it and where that rule comes from.");

        doc.Para("A failed check is not a bug to argue with. Fix the exact field the report "
            + "names, change nothing else, and resubmit.");

        doc.Heading("Checks");
        doc.Table(
            new[] { "Check", "Stage", "Fails as", "Rule" },
            config.Qa.Entries.Select(e => (IReadOnlyList<string>)new[]
            {
                $"`{e.CheckId}`",
                e.Layer ?? "",
                e.ErrorCategory ?? "",
                (e.Rule ?? "").Replace("|", "\\|")
            }));

        doc.Heading("Rule sources");
        doc.Para("Where each rule comes from, when you need to check the governing document.");
        doc.Table(
            new[] { "Check", "Source" },
            config.Qa.Entries
                .Where(e => !string.IsNullOrWhiteSpace(e.RuleSource))
                .Select(e => (IReadOnlyList<string>)new[] { $"`{e.CheckId}`", e.RuleSource! }));

        return doc.Build();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~KnowledgeProjectionTests"`
Expected: PASS, 13 tests.

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Governor/CorrectionProjection.cs tests/Registrar.Tests/Governor/KnowledgeProjectionTests.cs
git commit -m "feat: correction knowledge projection"
```

---

### Task 6: The CLI verb, the generated files, and the staleness guard

**Files:**
- Create: `src/Registrar.Cli/EmitKnowledgeCommand.cs`
- Modify: `src/Registrar.Cli/Program.cs`
- Create: `docs/governor/intake.md`, `drafter.md`, `payload.md`, `correction.md`
- Test: `tests/Registrar.Tests/Governor/GeneratedKnowledgeIsCurrentTests.cs`

**Interfaces:**
- Consumes: all four projections from Tasks 2 through 5.
- Produces: `registrar emit-knowledge [--out <dir>]`; four committed files under `docs/governor/`.

- [ ] **Step 1: Write the command**

`src/Registrar.Cli/EmitKnowledgeCommand.cs`:

```csharp
using System.Text.Json.Nodes;
using Registrar.Core;
using Registrar.Core.Config;
using Registrar.Core.Governor;

namespace Registrar.Cli;

public static class EmitKnowledgeCommand
{
    // The single list of projections. Adding one here is all it takes for both the CLI
    // and the staleness guard to pick it up.
    public static IReadOnlyList<IKnowledgeProjection> Projections { get; } = new IKnowledgeProjection[]
    {
        new IntakeProjection(),
        new DrafterProjection(),
        new PayloadProjection(),
        new CorrectionProjection()
    };

    public static KnowledgeSource LoadSource(string repoRoot)
    {
        var schemaPath = ConfigLocator.FindSchema(repoRoot, "agr.schema.json");
        var schema = JsonNode.Parse(File.ReadAllText(schemaPath))!;
        return new KnowledgeSource(ConfigLoader.Load(repoRoot), schema);
    }

    public static int Run(string? outDir, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            var repoRoot = Directory.GetParent(
                Path.GetDirectoryName(ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json"))!)!.FullName;
            var target = outDir ?? Path.Combine(repoRoot, "docs", "governor");
            Directory.CreateDirectory(target);

            var source = LoadSource(repoRoot);
            foreach (var projection in Projections)
            {
                var path = Path.Combine(target, projection.FileName);
                File.WriteAllText(path, projection.Render(source));
                stdout.WriteLine($"wrote {projection.FileName}");
            }
            stdout.WriteLine($"{Projections.Count} knowledge file(s) written to {target}");
            return 0;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"error: {ex.Message}");
            return 2;
        }
    }
}
```

- [ ] **Step 2: Register the verb**

In `src/Registrar.Cli/Program.cs`, add above the `var root = new RootCommand(...)` line:

```csharp
var emitOutOption = new Option<string?>("--out")
{
    Description = "Directory to write the knowledge files to. Defaults to docs/governor."
};

var emitKnowledge = new Command("emit-knowledge",
    "Generate the Governor's per-role knowledge files from config.");
emitKnowledge.Options.Add(emitOutOption);
emitKnowledge.SetAction(parseResult =>
    EmitKnowledgeCommand.Run(parseResult.GetValue(emitOutOption), Console.Out, Console.Error));
```

Then add `emitKnowledge` to the root command's list:

```csharp
var root = new RootCommand("The Registrar - governed artifact compiler")
    { validate, build, menu, test, doctor, emitKnowledge };
```

- [ ] **Step 3: Generate the files**

Run: `dotnet run --project src/Registrar.Cli -- emit-knowledge`
Expected: four `wrote <name>.md` lines and a summary.

Then read `docs/governor/drafter.md` and confirm by eye that the TCL section titles are present, in order, and identical to `config/artifact-types.json`. This is the one manual check in the plan and it is worth doing once: everything downstream trusts these files.

- [ ] **Step 4: Write the staleness guard**

Create `tests/Registrar.Tests/Governor/GeneratedKnowledgeIsCurrentTests.cs`:

```csharp
using Registrar.Cli;

namespace Registrar.Tests.Governor;

// The generated knowledge files are committed so they can be attached to Copilot Studio
// agents and reviewed in a PR. That only stays honest if a config change without a
// regeneration fails the build. This is that gate.
public class GeneratedKnowledgeIsCurrentTests
{
    [Fact]
    public void Committed_knowledge_files_match_a_fresh_render()
    {
        var repoRoot = SemanticTestSupport.RepoRoot();
        var source = EmitKnowledgeCommand.LoadSource(repoRoot);

        foreach (var projection in EmitKnowledgeCommand.Projections)
        {
            var path = Path.Combine(repoRoot, "docs", "governor", projection.FileName);
            Assert.True(File.Exists(path), $"{projection.FileName} is missing. Run: registrar emit-knowledge");

            var committed = File.ReadAllText(path);
            var fresh = projection.Render(source);

            Assert.True(committed == fresh,
                $"docs/governor/{projection.FileName} is out of date with config. "
                + "Run: dotnet run --project src/Registrar.Cli -- emit-knowledge");
        }
    }
}
```

This test lives in the test project, which already references `Registrar.Cli` (see `BuildCommandTests` and `DoctorCommandTests`). If the reference is missing, add it to `tests/Registrar.Tests/Registrar.Tests.csproj` rather than duplicating the projection list.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test`
Expected: PASS, 400 tests.

- [ ] **Step 6: Prove the guard actually fires**

Temporarily append a space to the end of `docs/governor/intake.md`, then run:

`dotnet test --filter "FullyQualifiedName~GeneratedKnowledgeIsCurrentTests"`

Expected: FAIL, naming `intake.md` and telling you to run `emit-knowledge`. Then restore the file with `git checkout docs/governor/intake.md` and re-run to confirm it passes. A guard nobody has watched fail is a guard nobody knows works.

- [ ] **Step 7: Commit**

```bash
git add src/Registrar.Cli docs/governor tests/Registrar.Tests/Governor
git commit -m "feat: emit-knowledge verb, generated knowledge files, and their staleness guard"
```

---

### Task 7: Retire the contract sheet and correct the docs

**Files:**
- Delete: `docs/governor-contract.md`
- Modify: `docs/governor-instructions.md`
- Modify: `docs/operations-guide.md`
- Modify: `docs/tech-debt.md`
- Modify: `scripts/build-travel-pack.ps1`
- Modify: `docs/ROADMAP.md`

**Interfaces:**
- Consumes: the four files generated in Task 6.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Find every live reference**

Run: `git grep -l "governor-contract" -- '*.md' '*.ps1'`

Expected: five files. `docs/superpowers/specs/2026-07-17-copy-ready-message-design.md` is a historical design record and must be left exactly as written. The other four are updated below.

- [ ] **Step 2: Delete the contract sheet**

```bash
git rm docs/governor-contract.md
```

- [ ] **Step 3: Correct the instructions doc**

In `docs/governor-instructions.md`, find the paragraph containing "generated from config and this is not". That claim is false and is the reason the drift went unnoticed. Replace the paragraph that references `docs/governor-contract.md` as the knowledge attachment so that it instead names the four generated files, and state plainly that they are generated from config by `registrar emit-knowledge` and that config wins on any disagreement.

Also update the earlier paragraph that says the full contract "lives in `docs/governor-contract.md`, which must be attached to the agent as knowledge" to name `docs/governor/` instead.

Do not change the paste-ready instruction block itself. It is under an 8000-character Studio cap and per-agent instruction text is a separate unit.

- [ ] **Step 4: Correct the operations guide and tech-debt**

In `docs/operations-guide.md` and `docs/tech-debt.md`, replace each `docs/governor-contract.md` reference with the appropriate file under `docs/governor/`, or with `docs/governor/` where the reference is to the contract as a whole. Read the surrounding sentence in each case; do not blind-replace.

- [ ] **Step 5: Update the travel pack**

In `scripts/build-travel-pack.ps1`, find the document list that copies the Governor docs into the pack's `governor/` directory (near lines 16 and 66). Remove `governor-contract.md` and add the four files from `docs/governor/`.

Then update the generated `UPLOAD-TO-GOVERNOR.md` text in that script so it describes the five agents and which files each receives:

- intake agent: `intake.md`
- drafting agent: `drafter.md`
- payload agent: `payload.md`
- correction agent: `correction.md` plus the hand-written `governor-payload-correction.md`
- archivist: no generated knowledge yet

`governor-briefing.md` remains orientation for all of them.

- [ ] **Step 6: Amend the roadmap**

In `docs/ROADMAP.md`, make two corrections and log them.

Change the GUI-2 row's description from "Generate Governor knowledge per artifact type from config" to "Generate Governor knowledge per agent role from config", and set its Status to **Done**.

In the "Notes on specific units" section, the GOV-1 note says the Governor runs "as a parent with per-type children". Correct it: the children are split by function, as intake, drafter, payload assembler, correction handler, and archivist.

Append to the Amendments section:

```
- **2026-08-02** | GUI-2 redefined from per-artifact-type to per-agent-role knowledge
  generation, and an Archivist projection split out as its own future unit | The Studio
  Governor's children are split by function, not by artifact type, so every child needs
  all ten types and a per-type split would not match how knowledge is attached. The
  Archivist's sources are the config files the GovLib workstream is actively changing |
  Repo owner
```

- [ ] **Step 7: Verify nothing dangles**

Run: `git grep -n "governor-contract" -- '*.md' '*.ps1'`
Expected: exactly one hit, in `docs/superpowers/specs/2026-07-17-copy-ready-message-design.md`.

Run: `dotnet test`
Expected: PASS, 400 tests.

- [ ] **Step 8: Commit**

```bash
git add -A docs scripts
git commit -m "docs: retire the hand-maintained contract sheet for generated knowledge"
```

---

## Self-Review

**Spec coverage.** Decision 1 (per-role) is Tasks 2 through 5. Decision 2 (four projections) is Tasks 2 through 5. Decision 3 (Archivist deferred) appears in the spec's out-of-scope list and the Task 7 amendment; no task implements it, correctly. Decision 4 (Core plus CLI verb) is Tasks 2 and 6. Decision 5 (delete the contract sheet) is Task 7. Decision 6 (committed with a guard) is Task 6. Decision 7 (instructions out of scope) is honored by Task 7 Step 3 leaving the paste block alone. The prerequisite config binding is Task 1. The travel pack is Task 7 Step 5. Every test named in the spec's Testing section appears in Tasks 2 through 6.

**One thing the spec did not anticipate.** The plan adds a manual eyeball check in Task 6 Step 3 and a deliberate guard-failure drill in Task 6 Step 6. Neither is in the spec. Both are cheap and both protect the thing everything downstream trusts.

**Placeholder scan.** No TBD or TODO. Every code step carries the code. Two steps (Task 7 Steps 3 and 4) describe edits to prose rather than quoting exact replacement text, because the surrounding sentences must be read to edit them correctly; each names the exact file, the exact phrase to find, and what the result must say.

**One known unknown, flagged rather than guessed.** Task 3 references `config.ContentLint.AllowedGlyphs`. That member name is not verified. The step says to read `ContentLint.cs` and use the real name, or drop the bullet if no such member exists, rather than inventing one.

**Type consistency.** `KnowledgeSource(ConfigBundle, JsonNode)` is defined in Task 2 and used identically in Tasks 3, 4, 5, and 6. `IKnowledgeProjection` has `FileName` and `Render` throughout. `QaCatalogEntry`'s new members are `Rule`, `Layer`, `AppliesTo` in Task 1 and used under those names in Task 5. `VocabularyConfig`'s six members are defined in Task 1 and used under those names in Task 4. `EmitKnowledgeCommand.Projections` and `LoadSource` are defined in Task 6 Step 1 and consumed by the guard in Step 4.

**Test count.** 383 before. Task 1 adds 3, Task 2 adds 5, Task 3 adds 4, Task 4 adds 3, Task 5 adds 1, Task 6 adds 1, reaching 400. The step-level expectations say 386, then 391, 395, 398, 399, 400. Adjust if the suite has moved.
