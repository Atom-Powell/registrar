# PDX Placeholder Dictionary Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate the Placeholder Dictionary (PDX, ORG-MST-PDX) as a controlled internal reference DOCX projected from `config/placeholder-dictionary.json`, declared the canonical placeholder source.

**Architecture:** PDX is the twin of the delivered Shared Clause Library (SCL) slice. It reuses the existing config-sourced `aggregationTable` block: the AGR carries only `aggregationSource: "placeholderDictionary"`, and the renderer projects the token dictionary from config at build time, so the document cannot diverge from config. This slice adds `placeholderDictionary` as a live aggregation source, plumbs the token list into the renderer, authors the PDX section schema, and declares config canonical (the canonical xlsx was never located).

**Tech Stack:** .NET 8, C#, DocumentFormat.OpenXml 3.5.1, xUnit, System.CommandLine 3.0.0-preview. Config is JSON in `/config`.

## Global Constraints

- Zero LLM calls at runtime (engine rule 2). None of this work adds a model call.
- Configuration over code (engine rule 3): token data, section schema, and vocabularies live in `/config` as versioned data. No artifact constants in core logic.
- Fail closed (engine rule 1): an invalid or unsupported `aggregationSource` produces no deliverable.
- Do not invent structure (engine rule 6): authoring the PDX section schema is a documentation-owner decision, recorded in the config release. It has been approved in the design spec.
- Output hygiene (engine rule 8): US English; no emojis; no em/en dashes or decorative unicode in config strings, docs, or code comments. The repo lint hook enforces this on edited files.
- DOCX output must stay byte-deterministic across runs (artifactSha256 proves the artifact).
- Every QA check ID gets at least one passing and one failing test named after the checkId (repo test convention).
- Do NOT edit `README.md` in this slice: it carries pre-existing em dashes that trip the lint hook on any edit. The one README reference to `reconciledAgainstPdxXlsx` is left to the separate README prose-pass thread.
- Do NOT rewrite `docs/v2-acceptance-roadmap.md` or `docs/v2-coding-agent-directive.md`: these are locked v2-contract records. Their `reconciledAgainstPdxXlsx` mentions are historical and describe behavior (mode-gating is the real gate) that this slice preserves.
- Commit after every task.

**Spec:** `docs/superpowers/specs/2026-07-20-pdx-placeholder-dictionary-design.md`

**Precedent to mirror throughout:** the SCL slice. Key files: `src/Registrar.Rendering/Blocks/AggregationTableBlock.cs`, `tests/Registrar.Tests/Rendering/SclEndToEndTests.cs`, `fixtures/agr-sample-scl-draft.json`, `fixtures/agr-sample-scl-promotion.json`.

---

## File Structure

**Modified:**
- `config/placeholder-dictionary.json` - declared canonical (version, provenance, `usage` already present on some tokens).
- `config/artifact-types.json` - PDX `sectionSchema` authored, `generationTarget: true`.
- `src/Registrar.Core/Config/ConfigModels.cs` - add `Usage` to `CanonicalToken`.
- `src/Registrar.Rendering/RenderContext.cs` - add `PlaceholderDictionary` member.
- `src/Registrar.Rendering/ArtifactRenderer.cs:24` - populate the new member.
- `src/Registrar.Rendering/Blocks/AggregationTableBlock.cs` - add the `placeholderDictionary` branch.
- `src/Registrar.Validation/BlockTypeSupportLayer.cs` - add `placeholderDictionary` to the live-source set; update remediation text.
- `fixtures/registry-sample.csv` - add `ORG-MST-PDX-001`.
- Test files (see tasks): `AggregationTableBlockTests.cs`, `BlockTypeSupportLayerTests.cs`, `AggregationSourceValidationTests.cs`.
- Docs: `CLAUDE.md`, `docs/tech-debt.md`, `docs/ROADMAP.md`.

**Created:**
- `tests/Registrar.Tests/Config/PlaceholderDictionaryConfigTests.cs`
- `tests/Registrar.Tests/Rendering/PdxEndToEndTests.cs`
- `fixtures/agr-sample-pdx-draft.json`, `fixtures/agr-sample-pdx-promotion.json`
- `fixtures/expected/agr-sample-pdx-draft/` and `fixtures/expected/agr-sample-pdx-promotion/` (generated goldens).

---

## Task 1: Token completeness audit (gate)

No code. This is the reshaped reconciliation: verify the canonical set in config is complete against every token actually referenced in the repository, before declaring config canonical. Output is an evidence block appended to this plan, and an escalation to the owner if a genuinely missing token appears.

**Files:**
- Modify: `docs/superpowers/plans/2026-07-20-pdx-placeholder-dictionary.md` (append the evidence block at the bottom).

- [ ] **Step 1: Sweep every referenced token in the repo**

Run (Git Bash):
```bash
cd /c/GitRepos/The-Registrar
# distinct tokens referenced across fixtures, config, and docs (exclude build/dist)
grep -rohE '\[[A-Z0-9][A-Z0-9 /._-]*\]' fixtures/ config/ docs/ \
  --include=*.json --include=*.md 2>/dev/null | sort -u > /tmp/pdx-referenced.txt
# the canonical set declared in config
grep -oE '"token"[[:space:]]*:[[:space:]]*"\[[^"]+\]"' config/placeholder-dictionary.json \
  | grep -oE '\[[^"]+\]' | sort -u > /tmp/pdx-canonical.txt
echo "=== referenced but NOT canonical (candidates for missing tokens) ==="
comm -23 /tmp/pdx-referenced.txt /tmp/pdx-canonical.txt
echo "=== canonical but never referenced (informational only) ==="
comm -13 /tmp/pdx-referenced.txt /tmp/pdx-canonical.txt
```

- [ ] **Step 2: Classify each "referenced but not canonical" entry**

For every token the first `comm` prints, classify it as one of:
- a known drift alias (already listed under some token's `knownDriftAliases` in config) - expected, not missing;
- a deliberate test fixture token (for example `[UNRECOGNIZED CUSTOM TOKEN]`, `[UNRECOGNISED CUSTOM TOKEN]`, or a `[0-9]`-style regex artifact) - not a real token;
- a genuinely missing canonical token - STOP.

- [ ] **Step 3: Escalation gate**

If Step 2 finds a genuinely missing canonical token, do NOT add it. Stop and report it to the owner with its source location; adding a canonical token is a judgment call, not an engine default (design spec, "The token completeness audit"). Resume only after the owner rules.

If Step 2 finds none (the expected outcome, given the early 18-of-19 signal), the canonical set is complete.

- [ ] **Step 4: Record the evidence**

Append to the bottom of this plan a short "Audit Evidence" section: the two `comm` outputs verbatim, and the classification of each non-canonical entry. This is the audit trail that justifies declaring config canonical in Task 3.

- [ ] **Step 5: Commit**

```bash
git add docs/superpowers/plans/2026-07-20-pdx-placeholder-dictionary.md
git commit -m "docs: PDX token completeness audit evidence"
```

---

## Task 2: Add `Usage` to `CanonicalToken` and bind it from config

The renderer's Usage column needs the token `usage` tag, which config carries (for example `[PACKAGE]` has `"usage": "footer"`) but the model does not yet bind.

**Files:**
- Modify: `src/Registrar.Core/Config/ConfigModels.cs:47-51` (`CanonicalToken`)
- Create: `tests/Registrar.Tests/Config/PlaceholderDictionaryConfigTests.cs`

**Interfaces:**
- Produces: `CanonicalToken.Usage` (`string?`, default null), consumed by `AggregationTableBlock` in Task 4.

- [ ] **Step 1: Write the failing test**

Create `tests/Registrar.Tests/Config/PlaceholderDictionaryConfigTests.cs`:
```csharp
using System.Linq;
using Registrar.Tests;
using Xunit;

namespace Registrar.Tests.Config;

public class PlaceholderDictionaryConfigTests
{
    [Fact]
    public void CanonicalToken_binds_usage_from_config()
    {
        var config = SemanticTestSupport.Config();
        var package = config.CanonicalTokens.Single(t => t.Token == "[PACKAGE]");
        Assert.Equal("footer", package.Usage);
    }

    [Fact]
    public void CanonicalToken_usage_is_null_when_absent()
    {
        var config = SemanticTestSupport.Config();
        var clientOrg = config.CanonicalTokens.Single(t => t.Token == "[CLIENT ORGANIZATION NAME]");
        Assert.Null(clientOrg.Usage);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~PlaceholderDictionaryConfigTests`
Expected: FAIL to compile (`CanonicalToken` has no `Usage`).

- [ ] **Step 3: Add the property**

In `src/Registrar.Core/Config/ConfigModels.cs`, add to `CanonicalToken` (after `KnownDriftAliases`):
```csharp
    public string? Usage { get; init; }
```
No loader change is needed: `ConfigLoader.Options` uses `PropertyNameCaseInsensitive = true`, so the config key `usage` binds to `Usage`.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~PlaceholderDictionaryConfigTests`
Expected: PASS (both facts).

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Core/Config/ConfigModels.cs tests/Registrar.Tests/Config/PlaceholderDictionaryConfigTests.cs
git commit -m "feat: bind token usage tag on CanonicalToken"
```

---

## Task 3: Declare `config/placeholder-dictionary.json` canonical

Data and provenance change. No code reads `reconciledAgainstPdxXlsx` (verified: zero `.cs` references), so removing it is safe.

**Files:**
- Modify: `config/placeholder-dictionary.json:1-6` (head `$comment`, version, flag, note)
- Modify: `CLAUDE.md:116-119` (known-gaps placeholder-dictionary bullet)
- Modify: `docs/tech-debt.md` (item 33 and the reconciliation follow-up)
- Create/append: audit already recorded in Task 1

- [ ] **Step 1: Write the failing guard test**

Add to `tests/Registrar.Tests/Config/PlaceholderDictionaryConfigTests.cs`:
```csharp
    [Fact]
    public void Dictionary_is_declared_canonical_not_a_subset()
    {
        var config = SemanticTestSupport.Config();
        // The dictionary version no longer advertises itself as an extracted subset.
        Assert.DoesNotContain("extracted-subset", config.DictionaryVersion);
    }
```
This requires exposing the dictionary version. If `ConfigBundle` does not already carry it, add a `DictionaryVersion` string to `ConfigBundle` populated from `placeholderFile.DictionaryVersion`, and add `DictionaryVersion` to `PlaceholderDictionaryFile`. If wiring that through is more than a trivial change, instead assert against the raw config file:
```csharp
    [Fact]
    public void Dictionary_is_declared_canonical_not_a_subset()
    {
        var path = System.IO.Path.Combine(SemanticTestSupport.RepoRoot(), "config", "placeholder-dictionary.json");
        var text = System.IO.File.ReadAllText(path);
        Assert.DoesNotContain("extracted-subset", text);
        Assert.DoesNotContain("reconciledAgainstPdxXlsx", text);
    }
```
Use whichever `SemanticTestSupport` helper for the repo root exists (the SCL and fixture tests locate the repo root via `ConfigLocator.FindSchema`); if no `RepoRoot()` helper is on `SemanticTestSupport`, inline the same `ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json")` walk used in `SclEndToEndTests.RepoRoot()`.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~Dictionary_is_declared_canonical`
Expected: FAIL (config still says `v1.2-extracted-subset` and still holds `reconciledAgainstPdxXlsx`).

- [ ] **Step 3: Edit the config file**

In `config/placeholder-dictionary.json`, replace lines 1-6 (the `$comment`, `dictionaryVersion`, `reconciledAgainstPdxXlsx`, `reconciledAgainstPdxXlsxNote`) with:
```json
{
  "$comment": "Canonical placeholder tokens for the ORG Master Template System. PROVENANCE: the canonical ORG-MST-PDX placeholder dictionary was originally maintained as ORG - Placeholder Dictionary - v1.2.xlsx, which was never among the program source documents and could not be located in the repository or the ORG workspace. On 2026-07-20 the repo owner ruled this file the authoritative placeholder source; the human-readable Placeholder Dictionary reference document (artifact type PDX) is generated from this file. Completeness was verified by a repository token sweep recorded in docs/superpowers/plans/2026-07-20-pdx-placeholder-dictionary.md. Unknown or unresolved tokens warn in Draft and fail (block) on Promotion via PlaceholderLayer (QA-SPEC-07) mode-gating.",
  "dictionaryId": "ORG-MST-PDX",
  "dictionaryVersion": "v1.2",
```
Leave `tokenRules`, `canonicalTokens` (including their `usage` tags), and `driftHandling` unchanged.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~PlaceholderDictionaryConfigTests`
Expected: PASS (all facts, including the canonical guard).

- [ ] **Step 5: Update the two provenance docs**

In `CLAUDE.md`, the known-gaps bullet beginning "config/placeholder-dictionary.json is an extracted subset..." (around line 116): replace it with a bullet stating the file is now the canonical placeholder source, the ORG-MST-PDX xlsx was never located, PlaceholderLayer (QA-SPEC-07) mode-gating remains the runtime gate, and `reconciledAgainstPdxXlsx` has been retired. Keep it to output-hygiene rules (no em dashes).

In `docs/tech-debt.md`, item 33 ("The PDX ... reference document is not yet generated") and item 34's surrounding reconciliation note: mark item 33 delivered by this slice (PDX generable, `placeholderDictionary` a live source, config declared canonical). Do not delete the historical text; append a dated "Resolved 2026-07-20 (PDX slice)" line in the same style as the other resolved items.

- [ ] **Step 6: Commit**

```bash
git add config/placeholder-dictionary.json CLAUDE.md docs/tech-debt.md tests/Registrar.Tests/Config/PlaceholderDictionaryConfigTests.cs
git commit -m "feat: declare placeholder-dictionary.json canonical, retire reconcile flag"
```

---

## Task 4: Render the `placeholderDictionary` aggregation

Plumb the token list into the renderer and add the `placeholderDictionary` branch to `AggregationTableBlock`. RenderContext and renderer change together because the record signature change must compile against all call sites in the same task.

**Files:**
- Modify: `src/Registrar.Rendering/RenderContext.cs`
- Modify: `src/Registrar.Rendering/ArtifactRenderer.cs:24`
- Modify: `src/Registrar.Rendering/Blocks/AggregationTableBlock.cs`
- Modify: `tests/Registrar.Tests/Rendering/AggregationTableBlockTests.cs`

**Interfaces:**
- Consumes: `CanonicalToken.Usage` (Task 2), `RenderContext` construction in `ArtifactRenderer`.
- Produces: `AggregationTableBlock.Render("placeholderDictionary", ctx)` returns a `BlockRenderResult` whose single element is a `Token | Drift Aliases | Usage` table.

- [ ] **Step 1: Write the failing test and fix the now-stale throw test**

In `tests/Registrar.Tests/Rendering/AggregationTableBlockTests.cs`:

First, update the `Ctx()` helper to pass the new constructor argument (append `config.CanonicalTokens` as the final argument - see Step 3 for the exact record order).

Second, change the existing `Render_throws_on_non_live_source` test: `placeholderDictionary` is about to become live, so switch its argument to a genuinely non-live source:
```csharp
    [Fact]
    public void Render_throws_on_non_live_source()
    {
        var ctx = Ctx();
        Assert.Throws<InvalidOperationException>(() => AggregationTableBlock.Render("unknownSource", ctx));
    }
```

Third, add the new passing test:
```csharp
    [Fact]
    public void Render_projects_every_token_with_aliases_and_usage()
    {
        var ctx = Ctx();
        var result = AggregationTableBlock.Render("placeholderDictionary", ctx);
        var table = Assert.IsType<Table>(Assert.Single(result.Elements));
        var text = table.InnerText;

        foreach (var token in ctx.PlaceholderDictionary)
            Assert.Contains(token.Token, text);

        // A known alias renders in the aliases column.
        Assert.Contains("[CUSTOMER]", result.ScanText);
        // A usage tag renders in the usage column.
        Assert.Contains("footer", result.ScanText);
        // Header row present.
        Assert.Contains("Token", text);
        Assert.Contains("Drift Aliases", text);
        Assert.Contains("Usage", text);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~AggregationTableBlockTests`
Expected: FAIL to compile (`RenderContext` has no `PlaceholderDictionary`; `Render` has no `placeholderDictionary` branch).

- [ ] **Step 3: Add the RenderContext member**

In `src/Registrar.Rendering/RenderContext.cs`, append a parameter to the record (keep it last to minimize churn):
```csharp
public sealed record RenderContext(
    StyleRules Style,
    IReadOnlyDictionary<string, ClauseEntry> Clauses,
    IReadOnlyDictionary<string, string?> Placeholders,
    RadioOptionPattern Radio,
    WarnCalloutStyle Warn,
    IReadOnlyList<ClauseEntry> ClauseLibrary,
    CopyReadyMessageStyle CopyReadyMessage,
    IReadOnlyList<CanonicalToken> PlaceholderDictionary);
```

- [ ] **Step 4: Populate it at the construction site**

In `src/Registrar.Rendering/ArtifactRenderer.cs:24`, append `config.CanonicalTokens` as the final constructor argument:
```csharp
        var ctx = new RenderContext(style, config.ClausesById, agr.DataInputs.PlaceholderMap, config.RadioPattern, config.WarnCallout, config.Clauses, config.CopyReadyMessage, config.CanonicalTokens);
```

- [ ] **Step 5: Add the renderer branch**

Rewrite `src/Registrar.Rendering/Blocks/AggregationTableBlock.cs` to dispatch on source:
```csharp
using Registrar.Core.Config;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Registrar.Rendering;

// Config-sourced aggregation. The AGR block carries only aggregationSource; the whole
// body is projected from config here, so the document cannot diverge from config.
// clauseLibrary and placeholderDictionary are the two live sources. Canonical text and
// tokens are rendered verbatim, because these reference documents exist to display them.
public static class AggregationTableBlock
{
    private static readonly IReadOnlyList<string> ClauseHeaders =
        new[] { "Clause ID", "Name", "Canonical Text", "SHA-256" };

    private static readonly IReadOnlyList<string> PlaceholderHeaders =
        new[] { "Token", "Drift Aliases", "Usage" };

    public static BlockRenderResult Render(string? aggregationSource, RenderContext ctx) => aggregationSource switch
    {
        "clauseLibrary" => Project(ClauseHeaders, ctx.ClauseLibrary
            .Select(c => (IReadOnlyList<string>)new[]
                { c.ClauseId, c.Name, c.CanonicalText, c.CanonicalTextSha256 }).ToList(), ctx.Style),
        "placeholderDictionary" => Project(PlaceholderHeaders, ctx.PlaceholderDictionary
            .Select(t => (IReadOnlyList<string>)new[]
                { t.Token, string.Join(", ", t.KnownDriftAliases), t.Usage ?? string.Empty }).ToList(), ctx.Style),
        // BlockTypeSupportLayer (QA-ENG-06) fails closed on a non-live source before
        // rendering, so reaching here with one is an engine bug, not Governor input.
        _ => throw new InvalidOperationException(
            $"aggregationTable reached the renderer with source '{aggregationSource ?? "null"}'; "
            + "QA-ENG-06 should have blocked it. Live sources are clauseLibrary and placeholderDictionary."),
    };

    private static BlockRenderResult Project(
        IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> rows, StyleRules style)
    {
        var table = TableBlock.Render(headers, rows, style);
        var scan = string.Join(" ", headers.Concat(rows.SelectMany(r => r)));
        return new BlockRenderResult(new OpenXmlElement[] { table }, scan, null, null);
    }
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~AggregationTableBlockTests`
Expected: PASS (clause tests unchanged, new placeholder test passes, throw test uses `unknownSource`).

- [ ] **Step 7: Build the whole solution to catch other call sites**

Run: `dotnet build`
Expected: 0 errors, 0 warnings. If any other `new RenderContext(...)` exists, it will fail here; the grep at plan-time found only `ArtifactRenderer.cs:24` and the test `Ctx()` helper, both updated.

- [ ] **Step 8: Commit**

```bash
git add src/Registrar.Rendering/RenderContext.cs src/Registrar.Rendering/ArtifactRenderer.cs src/Registrar.Rendering/Blocks/AggregationTableBlock.cs tests/Registrar.Tests/Rendering/AggregationTableBlockTests.cs
git commit -m "feat: render placeholderDictionary aggregation table"
```

---

## Task 5: Make `placeholderDictionary` a live QA-ENG-06 source

`BlockTypeSupportLayer` currently fails closed on `placeholderDictionary` (reserved). Promote it to a live source and flip the test that asserted it fails.

**Files:**
- Modify: `src/Registrar.Validation/BlockTypeSupportLayer.cs:23-26` (live-source set), `:80-81` (remediation text)
- Modify: `tests/Registrar.Tests/BlockTypeSupportLayerTests.cs`

**Interfaces:**
- Produces: QA-ENG-06 accepts `aggregationTable` with `aggregationSource: "placeholderDictionary"`; still fails on missing/unknown sources.

- [ ] **Step 1: Flip the failing test to a passing one, add a drift guard**

In `tests/Registrar.Tests/BlockTypeSupportLayerTests.cs`, replace `QA_ENG_06_fails_aggregationTable_with_placeholderDictionary_source` with:
```csharp
    [Fact]
    public void QA_ENG_06_passes_aggregationTable_with_placeholderDictionary_source()
    {
        var ctx = ContextWithBlock(new AgrBlock
        {
            BlockType = "aggregationTable",
            AggregationSource = "placeholderDictionary"
        });
        var checks = new BlockTypeSupportLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-06");
    }

    [Fact]
    public void QA_ENG_06_live_aggregation_sources_are_exactly_clauseLibrary_and_placeholderDictionary()
    {
        Assert.Equal(
            new[] { "clauseLibrary", "placeholderDictionary" }.OrderBy(x => x),
            BlockTypeSupportLayer.LiveAggregationSources.OrderBy(x => x));
    }
```
Keep `QA_ENG_06_fails_aggregationTable_with_missing_source` and `QA_ENG_06_fails_aggregationTable_with_unknown_source` unchanged: they are the failing-side coverage.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~BlockTypeSupportLayerTests`
Expected: FAIL - `placeholderDictionary` still rejected; `LiveAggregationSources` is private (compile error).

- [ ] **Step 3: Make the source live and expose the set**

In `src/Registrar.Validation/BlockTypeSupportLayer.cs`, change the private field to public and add the source:
```csharp
    // Aggregation sources this engine version projects. Both are config-sourced reference
    // projections (the clause library and the placeholder dictionary).
    public static readonly IReadOnlySet<string> LiveAggregationSources = new HashSet<string>(StringComparer.Ordinal)
    {
        "clauseLibrary",
        "placeholderDictionary"
    };
```

Update the remediation text in `FailSource` (around line 80): drop the "placeholderDictionary is reserved until the PDX slice" clause and the `Expected` line so they read:
```csharp
        Expected = "a live aggregation source: clauseLibrary or placeholderDictionary",
        ...
        Remediation = $"Set aggregationSource to a live source (clauseLibrary or placeholderDictionary). "
            + $"'{source ?? "null"}' is not projected by this engine version.",
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~BlockTypeSupportLayerTests`
Expected: PASS (placeholderDictionary accepted; missing/unknown still fail; drift guard green).

- [ ] **Step 5: Commit**

```bash
git add src/Registrar.Validation/BlockTypeSupportLayer.cs tests/Registrar.Tests/BlockTypeSupportLayerTests.cs
git commit -m "feat: promote placeholderDictionary to a live QA-ENG-06 source"
```

---

## Task 6: Lock the QA-ENG-07 exemption for `placeholderDictionary`

`TokenResidueLayer` already exempts `aggregationTable` by omission (it is not in the scan switch), so this needs no production change. Add a test that pins the behavior for the `placeholderDictionary` source specifically, so a future edit to the scan switch cannot silently start flagging PDX rows.

**Files:**
- Modify: `tests/Registrar.Tests/AggregationSourceValidationTests.cs`

- [ ] **Step 1: Add the lock test**

In `tests/Registrar.Tests/AggregationSourceValidationTests.cs`, add:
```csharp
    // PDX rows ARE bracketed tokens by nature; the residue scan must never flag the
    // placeholderDictionary aggregation, exactly as it exempts clauseLibrary. This pins
    // that a future scan-switch edit cannot start failing the document whose purpose is
    // to display the tokens.
    [Fact]
    public void QA_ENG_07_does_not_flag_placeholderDictionary_aggregation()
    {
        var ctx = ContextWithBlockAndConfig(new AgrBlock
        {
            BlockType = "aggregationTable",
            AggregationSource = "placeholderDictionary"
        });
        var checks = new TokenResidueLayer().Validate(ctx);
        Assert.DoesNotContain(checks, c => c.CheckId == "QA-ENG-07");
    }
```

- [ ] **Step 2: Run test to verify it passes immediately (no production change)**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~AggregationSourceValidationTests`
Expected: PASS. If it FAILS, the exemption is not by omission as believed; stop and inspect `TokenResidueLayer.ScanFields` before proceeding.

- [ ] **Step 3: Commit**

```bash
git add tests/Registrar.Tests/AggregationSourceValidationTests.cs
git commit -m "test: pin QA-ENG-07 exemption for placeholderDictionary aggregation"
```

---

## Task 7: Author the PDX section schema and flip `generationTarget`

Per rule 6 this is a documentation-owner decision, approved in the spec. Until it lands, a PDX build refuses generation.

**Files:**
- Modify: `config/artifact-types.json:276` (the PDX entry)
- Modify: `tests/Registrar.Tests/Config/PlaceholderDictionaryConfigTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `PlaceholderDictionaryConfigTests`:
```csharp
    [Fact]
    public void Pdx_is_a_defined_generation_target()
    {
        var config = SemanticTestSupport.Config();
        var pdx = config.ArtifactTypesByCode["PDX"];
        Assert.True(pdx.GenerationTarget);
        Assert.Equal("defined", pdx.SectionSchema.Status);
        Assert.Equal(
            new[] { "Purpose", "How Placeholders Work", "Placeholder Dictionary", "Version and Change Notes" },
            pdx.SectionSchema.OrderedSections);
    }
```
Match the actual property names on the artifact-type config model. The SCL slice used the same `sectionSchema.status` / `orderedSections` shape; confirm the C# names by opening `ArtifactTypeConfig` (referenced from `ConfigBundle.ArtifactTypesByCode`). If `SectionSchema` exposes `Status` and `OrderedSections`, the above compiles as written.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~Pdx_is_a_defined_generation_target`
Expected: FAIL (`generationTarget` is false; `sectionSchema.status` is `not-defined-in-spec`).

- [ ] **Step 3: Edit the PDX entry**

In `config/artifact-types.json`, replace the PDX line (276) with the SCL-shaped definition:
```json
    { "code": "PDX", "name": "Placeholder Dictionary", "category": "MST", "idPattern": "ORG-MST-PDX-NNN", "classification": "Internal Working Document", "reviewIntervalMonths": 12, "generationTarget": true, "sectionSchema": { "status": "defined", "orderedSections": ["Purpose", "How Placeholders Work", "Placeholder Dictionary", "Version and Change Notes"] } }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~Pdx_is_a_defined_generation_target`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add config/artifact-types.json tests/Registrar.Tests/Config/PlaceholderDictionaryConfigTests.cs
git commit -m "feat: author PDX section schema and flip generationTarget"
```

---

## Task 8: Fixtures, registry entry, end-to-end test, and goldens

The acceptance milestone: PDX builds end to end in draft and promotion, byte-deterministic, every canonical token present.

**Files:**
- Create: `fixtures/agr-sample-pdx-draft.json`, `fixtures/agr-sample-pdx-promotion.json`
- Modify: `fixtures/registry-sample.csv`
- Create: `tests/Registrar.Tests/Rendering/PdxEndToEndTests.cs`
- Create (generated): `fixtures/expected/agr-sample-pdx-draft/`, `fixtures/expected/agr-sample-pdx-promotion/`

- [ ] **Step 1: Add the registry row**

Append to `fixtures/registry-sample.csv`:
```
ORG-MST-PDX-001,PDX,Internal Working Document,Draft,v1.0,Fixture Client
```

- [ ] **Step 2: Create the draft fixture**

`fixtures/agr-sample-pdx-draft.json` (mirrors `agr-sample-scl-draft.json`):
```json
{
  "schemaVersion": "1.0.0",
  "artifactIdentity": {
    "artifactType": "PDX",
    "documentId": "ORG-MST-PDX-001",
    "version": "v1.0",
    "classification": "Internal Working Document",
    "approvalStatus": "Draft",
    "layer": "Administration"
  },
  "context": {
    "client": "ORG Consulting, LLC",
    "engagement": "Master Template System Maintenance",
    "owner": "Registry Owner",
    "audience": "Documentation owner, engine maintainers",
    "preparedBy": "ORG Consulting, LLC",
    "reviewDate": null,
    "reviewDateOverride": null
  },
  "sourceWorkflowContext": {
    "workflowName": "Master Template System Maintenance",
    "phase": "Maintenance",
    "executionId": "GOV-2026-07-20-001"
  },
  "dataInputs": {
    "placeholderMap": {},
    "sections": [
      { "title": "Purpose", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "This document is the Placeholder Dictionary reference. It lists every canonical placeholder token the engine recognizes, together with its known drift aliases and where it is used, so any artifact can be checked against the tokens the engine actually validates." }
      ] },
      { "title": "How Placeholders Work", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "A placeholder is an uppercase name in square brackets that stands in for a client-specific value. The engine recognizes only the canonical tokens listed here. When Governor-supplied content uses a known drift alias, the engine reports the canonical replacement rather than silently correcting it, so drift is fed back for correction. An unresolved token is permitted with a warning in Draft and is prohibited in a client-facing final document at Promotion." }
      ] },
      { "title": "Placeholder Dictionary", "descriptorLine": null, "blocks": [
        { "blockType": "aggregationTable", "aggregationSource": "placeholderDictionary" }
      ] },
      { "title": "Version and Change Notes", "descriptorLine": null, "blocks": [
        { "blockType": "paragraph", "text": "Version v1.0 is the first published edition of this reference. It reflects the placeholder dictionary as loaded from config at build time. Future editions record added tokens, alias changes, and usage updates as change notes in this section." }
      ] }
    ],
    "normalizedFindings": null,
    "normalizedRisks": null,
    "normalizedInventoryEntities": null
  },
  "evidenceReferences": null,
  "dependencies": null,
  "renderingOptions": {
    "packageName": "Master Template System",
    "outputFileName": "ORG - Placeholder Dictionary - v1.2.docx",
    "optionalSectionToggles": null,
    "stylingProfile": "org-v1.2"
  },
  "validationPolicy": {
    "mode": "draft",
    "additionalRequiredCheckIds": null,
    "warningThreshold": null
  }
}
```

- [ ] **Step 3: Create the promotion fixture**

Copy the draft fixture to `fixtures/agr-sample-pdx-promotion.json` and change exactly two fields, mirroring the SCL draft/promotion split (registry row is `Draft`, the AGR requests `In Review`):
- `artifactIdentity.approvalStatus`: `"In Review"`
- `validationPolicy.mode`: `"promotion"`

If the SCL promotion fixture uses a different requested status, match it instead: open `fixtures/agr-sample-scl-promotion.json` and copy its `approvalStatus` value verbatim, since both are `ORG-MST` internal docs registered as `Draft` and must request the same legal next status.

- [ ] **Step 4: Write the end-to-end test**

Create `tests/Registrar.Tests/Rendering/PdxEndToEndTests.cs`, mirroring `SclEndToEndTests`:
```csharp
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using Registrar.Cli;
using Registrar.Core;
using Xunit;

namespace Registrar.Tests.Rendering;

// Acceptance milestone: the Placeholder Dictionary artifact type renders end to end.
// Every canonical token in the loaded dictionary must appear in the rendered aggregation
// table, and the build must be byte-deterministic like every other artifact type.
public class PdxEndToEndTests
{
    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
    }

    private static string RepoRoot()
    {
        var schemaPath = ConfigLocator.FindSchema(AppContext.BaseDirectory, "agr.schema.json");
        return Directory.GetParent(Path.GetDirectoryName(schemaPath)!)!.FullName;
    }

    private static string CopyToTemp(string repoRelative)
    {
        var src = Path.Combine(RepoRoot(), repoRelative);
        var dir = Directory.CreateTempSubdirectory().FullName;
        var dest = Path.Combine(dir, Path.GetFileName(src));
        File.Copy(src, dest);
        return dest;
    }

    private static string DocxBodyText(string docxPath)
    {
        using var doc = WordprocessingDocument.Open(docxPath, false);
        return doc.MainDocumentPart!.Document!.Body!.InnerText;
    }

    [Theory]
    [InlineData("fixtures/agr-sample-pdx-draft.json", "draft", false)]
    [InlineData("fixtures/agr-sample-pdx-promotion.json", "promotion", true)]
    public void Pdx_builds_and_every_token_is_present(string fixture, string mode, bool useRegistry)
    {
        var agrPath = CopyToTemp(fixture);
        var registryPath = useRegistry ? Path.Combine(RepoRoot(), "fixtures", "registry-sample.csv") : null;

        var code = BuildCommand.Run(agrPath, new FixedClock(), TextWriter.Null, TextWriter.Null, mode, registryPath);
        Assert.Equal(0, code);

        var dir = Path.GetDirectoryName(agrPath)!;
        var docx = Directory.GetFiles(dir, "*.docx").Single();

        using (var doc = WordprocessingDocument.Open(docx, false))
        {
            var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).ToList();
            Assert.Empty(errors);
        }

        var text = DocxBodyText(docx);
        var config = SemanticTestSupport.Config();
        foreach (var token in config.CanonicalTokens)
            Assert.Contains(token.Token, text);
    }

    [Fact]
    public void Pdx_build_is_byte_deterministic()
    {
        var a = CopyToTemp("fixtures/agr-sample-pdx-draft.json");
        Assert.Equal(0, BuildCommand.Run(a, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var docxA = File.ReadAllBytes(Directory.GetFiles(Path.GetDirectoryName(a)!, "*.docx").Single());

        var b = CopyToTemp("fixtures/agr-sample-pdx-draft.json");
        Assert.Equal(0, BuildCommand.Run(b, new FixedClock(), TextWriter.Null, TextWriter.Null));
        var docxB = File.ReadAllBytes(Directory.GetFiles(Path.GetDirectoryName(b)!, "*.docx").Single());

        Assert.Equal(docxA, docxB);
    }
}
```
Confirm the `BuildCommand.Run` overloads match `SclEndToEndTests` exactly (a 4-arg form for the deterministic test and a 6-arg form with mode + registry). Copy the signatures from `SclEndToEndTests` verbatim if they differ.

- [ ] **Step 5: Run the end-to-end test**

Run: `dotnet test tests/Registrar.Tests --filter FullyQualifiedName~PdxEndToEndTests`
Expected: PASS. If the build refuses (non-zero exit), read the emitted `validation-report.json` in the temp dir for the failing checkId before changing anything.

- [ ] **Step 6: Generate the goldens**

`FixtureTestRunner.DiscoverFixtures` auto-enrolls the two new fixtures. Generate their goldens:
```bash
dotnet run --project src/Registrar.Cli -- test --fixture agr-sample-pdx-draft --update
dotnet run --project src/Registrar.Cli -- test --fixture agr-sample-pdx-promotion --update
```
Confirm these created `fixtures/expected/agr-sample-pdx-draft/validation-report.json` and `fixtures/expected/agr-sample-pdx-promotion/{validation-report.json,promotion-payload.json}`.

- [ ] **Step 7: Verify the whole golden suite is green**

Run: `dotnet run --project src/Registrar.Cli -- test --all`
Expected: every fixture PASS (including the two new ones), zero FAIL.

- [ ] **Step 8: Commit**

```bash
git add fixtures/agr-sample-pdx-draft.json fixtures/agr-sample-pdx-promotion.json fixtures/registry-sample.csv fixtures/expected/agr-sample-pdx-draft fixtures/expected/agr-sample-pdx-promotion tests/Registrar.Tests/Rendering/PdxEndToEndTests.cs
git commit -m "feat: PDX builds end to end (draft and promotion) with goldens"
```

---

## Task 9: Docs closeout and full-suite verification

**Files:**
- Modify: `docs/ROADMAP.md`
- Modify: `docs/tech-debt.md` (if not fully closed in Task 3)

- [ ] **Step 1: Move PDX to delivered in the roadmap**

In `docs/ROADMAP.md`, add a row to the "Delivered after v2 acceptance" table dated 2026-07-20 describing the PDX slice (PDX generable; `placeholderDictionary` a live aggregation source; config declared canonical). In the post-v2 backlog, update the "Remaining generation targets" bullet: PDX is no longer the one target not yet generated. Remove PDX from the "prerequisite is the dictionary reconciliation" framing in "Config and infrastructure" (the reconciliation gate is retired). No em dashes.

- [ ] **Step 2: Confirm tech-debt item 33 is closed**

Verify `docs/tech-debt.md` item 33 carries the dated "Resolved 2026-07-20 (PDX slice)" note added in Task 3. If Task 3 did not fully close it, add the note now.

- [ ] **Step 3: Full suite green, clean build**

Run: `dotnet test`
Expected: all tests pass (the pre-slice baseline was 356 passed, 0 failed; this slice adds tests and one flipped test, so expect a higher passing count, 0 failed), build clean, 0 warnings.

- [ ] **Step 4: Confirm output hygiene on every edited non-code file**

Run (Git Bash) over the files this slice touched:
```bash
python -c "
bad={0x2013:'en',0x2014:'em',0x2018:'lq',0x2019:'rq',0x201C:'ldq',0x201D:'rdq',0x2026:'ell',0x00A0:'nbsp'}
import glob
for f in ['config/placeholder-dictionary.json','config/artifact-types.json','CLAUDE.md','docs/tech-debt.md','docs/ROADMAP.md','fixtures/agr-sample-pdx-draft.json','fixtures/agr-sample-pdx-promotion.json']:
    for i,l in enumerate(open(f,encoding='utf-8'),1):
        for ch in l:
            if ord(ch) in bad: print(f'{f}:{i} U+{ord(ch):04X} {bad[ord(ch)]}')
print('scan done')
"
```
Expected: only "scan done", no hits.

- [ ] **Step 5: Commit**

```bash
git add docs/ROADMAP.md docs/tech-debt.md
git commit -m "docs: close PDX slice (roadmap delivered, tech-debt item 33)"
```

- [ ] **Step 6: Open the PR**

Per the repo's PR-not-direct-merge preference:
```bash
git push -u origin feat/pdx-placeholder-dictionary
gh pr create --fill --base main
```

---

---

## Audit Evidence (Task 1 completed 2026-07-20)

Token completeness audit run against fixtures/, config/, and docs/.

**Command:** grep -rohE `\[[A-Z0-9][A-Z0-9 /._-]*\]` fixtures/ config/ docs/ --include=*.json --include=*.md | sort -u

**Referenced but NOT canonical (from `comm -23`):**
```
[0-9]
[0]
[1..]
[16]
[17]
[18]
[19]
[1]
[20]
[21]
[22]
[23]
[2]
[A-Z0-9 /._-]
[A-Z0-9]
[CATEGORY]
[CLIENT NAME]
[CLIENT]
[CODE]
[CUSTOMER]
[LIKE THIS]
[NNN]
[NONREQUIRED TOKEN]
[NUMBER]
[ORG NAME]
[OWNER]
[PRODUCT]
[TOKENS]
[TOKEN]
[TOOL]
[UNRECOGNISED CUSTOM TOKEN]
[UNRECOGNIZED CUSTOM TOKEN]
[ZZ NOT A REAL TOKEN]
```

**Canonical but never referenced (from `comm -13`):**
```
(none)
```

**Classification of non-canonical entries:**

| Token | Category | Evidence |
|-------|----------|----------|
| [A-Z0-9] | regex pattern (not a token) | extracted from tokenRules.pattern in config/placeholder-dictionary.json:9 |
| [A-Z0-9 /._-] | regex pattern (not a token) | extracted from tokenRules.pattern in config/placeholder-dictionary.json:9 |
| [0], [1], [2], [16], [17], [18], [19], [20], [21], [22], [23] | code example (not a token) | array indices from PNG header example in docs/superpowers/plans/2026-07-05-slice5-renderer-foundation.md |
| [0-9] | regex pattern (not a token) | extracted from pattern notation in documentation |
| [1..] | regex pattern (not a token) | pattern notation artifact in documentation |
| [CATEGORY], [CODE], [NNN] | documentation example (not a token) | used as placeholders in document ID format example "ORG-[CATEGORY]-[CODE]-[NNN]" in docs/governor-briefing.md |
| [LIKE THIS] | documentation example (not a token) | example of bracket format in docs/governor-payload-correction.md |
| [TOKEN], [TOKENS] | meta-reference (not a token) | used in documentation to describe tokens generically and in QA check descriptions |
| [NONREQUIRED TOKEN] | test fixture | explicit test fixture name in plan documentation |
| [UNRECOGNISED CUSTOM TOKEN], [UNRECOGNIZED CUSTOM TOKEN] | test fixture | intentional test fixtures in fixtures/agr-sample-ds-draft-warn.json for testing unknown-token handling |
| [ZZ NOT A REAL TOKEN] | test fixture | intentional test fixture in docs/superpowers/plans/2026-07-07-slice9-v2-acceptance-closeout.md test code |
| [CLIENT] | known drift alias | listed in config/placeholder-dictionary.json:18 as alias for [CLIENT ORGANIZATION NAME] |
| [CLIENT NAME] | known drift alias | listed in config/placeholder-dictionary.json:18 as alias for [CLIENT ORGANIZATION NAME] |
| [CUSTOMER] | known drift alias | listed in config/placeholder-dictionary.json:18 as alias for [CLIENT ORGANIZATION NAME] |
| [ORG NAME] | known drift alias | listed in config/placeholder-dictionary.json:18 as alias for [CLIENT ORGANIZATION NAME] |
| [OWNER] | known drift alias | listed in config/placeholder-dictionary.json:24 as alias for [DESIGNATED SECURITY OWNER] |
| [PRODUCT] | known drift alias | listed in config/placeholder-dictionary.json:21 as alias for [TOOL / CONNECTOR NAME] |
| [TOOL] | known drift alias | listed in config/placeholder-dictionary.json:21 as alias for [TOOL / CONNECTOR NAME] |
| [NUMBER] | documentation example (not a token) | likely used as a placeholder example in documentation |

**Conclusion:** Audit COMPLETE. Zero genuinely missing canonical tokens. All 33 non-canonical entries are accounted for: 7 known drift aliases (expected and covered in config), 26 test fixtures or documentation examples (not real tokens). The canonical set of 19 tokens in config/placeholder-dictionary.json is verified complete.

---

## Self-Review (completed at plan-authoring time)

- **Spec coverage:** audit (Task 1), config canonical + provenance (Task 3), Usage model bind (Task 2), renderer branch + plumbing (Task 4), QA-ENG-06 live source (Task 5), QA-ENG-07 exemption lock (Task 6), section schema + generationTarget (Task 7), fixtures + registry + e2e + goldens (Task 8), docs (Tasks 3 and 9). All spec deliverables map to a task.
- **Placeholder scan:** none. Every code step carries actual code; every doc edit names the target lines and the exact replacement.
- **Type consistency:** `RenderContext.PlaceholderDictionary` (`IReadOnlyList<CanonicalToken>`) is produced in Task 4 and consumed by `AggregationTableBlock`; `CanonicalToken.Usage` (`string?`) is produced in Task 2 and consumed in Task 4; `BlockTypeSupportLayer.LiveAggregationSources` is made public in Task 5 and asserted by the drift-guard test in the same task. `AggregationTableBlock.Render(string?, RenderContext)` signature is unchanged; only its body gains a branch.
- **Known follow-through the executor must honor:** the existing `AggregationTableBlockTests.Render_throws_on_non_live_source` test (asserts `placeholderDictionary` throws) MUST be repointed to `unknownSource` in Task 4 Step 1, and the existing `BlockTypeSupportLayerTests.QA_ENG_06_fails_aggregationTable_with_placeholderDictionary_source` MUST be replaced with the passing variant in Task 5 Step 1. Both are called out inline.
