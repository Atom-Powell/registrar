# Layer Mapping and QA-ENG-08 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `artifactIdentity.layer` a validated field by declaring the framework layer per artifact type in config and adding QA-ENG-08 to fail any AGR whose layer disagrees.

**Architecture:** `layer` is currently the one required identity field the engine accepts on trust. It flows from the AGR into `artifact-manifest.json` via `ArtifactManifestBuilder` and nothing else reads it, so a wrong value is recorded silently. This unit (GUI-1) adds `layer` to each generable artifact type in `config/artifact-types.json`, binds it on `ArtifactTypeConfig`, and adds a new semantic layer that compares the two. Config stays the source of truth; the check contains no artifact constants.

**Tech Stack:** .NET 8, C#, xUnit. No new dependencies.

## Global Constraints

- Fail closed. QA-ENG-08 is blocking in both draft and promotion.
- Configuration over code. No artifact type codes or layer names appear in `src/`.
- US English. No emojis, no em dashes, no en dashes, no decorative unicode in config strings, source, or docs.
- Every QA check ID gets at least one passing and one failing test, named after the checkId.
- `RuleSource` in code must match `ruleSource` in `config/qa-checklist.json` byte for byte. `QaCatalogConsistencyTests` asserts this at runtime across every fixture and will fail the build on any drift.
- The ratified layer mapping, which is an owner ruling dated 2026-08-01 and not lifted from ADR-002:

| Code | Category | Layer |
|---|---|---|
| DS | GOV | Governance |
| AUP | GOV | Governance |
| UQR | OPS | Delivery |
| DCN | OPS | Delivery |
| TCL | TRN | Delivery |
| TAK | TRN | Delivery |
| RAF | RSK | Operational |
| PIG | INT | Administration |
| SCL | MST | Administration |
| PDX | MST | Administration |

Only these ten get a `layer`. The other fourteen types in `artifact-types.json` have `sectionSchema.status: "not-defined-in-spec"` and are already refused by QA-ENG-02, so QA-ENG-08 skips them.

---

## File Structure

**Create:**
- `src/Registrar.Validation/LayerAgreementLayer.cs` - the QA-ENG-08 check. One responsibility: compare `artifactIdentity.layer` to the type's configured layer.
- `fixtures/agr-sample-ds-layer-mismatch.json` - failure fixture, mirrors the existing `agr-sample-ds-id-code-mismatch.json` convention.
- `tests/Registrar.Tests/LayerAgreementLayerTests.cs` - passing, failing, and skip-path tests.

**Modify:**
- `config/artifact-types.json` - add `"layer"` to the ten generable types.
- `src/Registrar.Core/Config/ConfigModels.cs:9-22` - add `Layer` to `ArtifactTypeConfig`.
- `config/qa-checklist.json` - append the QA-ENG-08 catalog entry.
- `src/Registrar.Validation/ValidationService.cs:64-74` - register the layer.
- Eleven fixtures in `fixtures/` - align `layer` to the ratified mapping.
- `docs/governor-contract.md:381` - state the per-type layer instead of listing the four values.
- `CLAUDE.md` - add the ruling to Known gaps.

**Naming note for the reviewer:** `LayerAgreementLayer` is awkward because "layer" means two things here, the framework layer and a validation layer. The alternative is folding the check into `MetadataLayer`, which already validates identity-versus-config agreement, but that would put two check IDs in one class and break the one-const-per-class pattern every other layer follows. Rename freely if you prefer something else; nothing outside the class and its registration depends on the name.

---

### Task 1: Bind the framework layer in config

**Files:**
- Modify: `config/artifact-types.json`
- Modify: `src/Registrar.Core/Config/ConfigModels.cs:9-22`
- Test: `tests/Registrar.Tests/ConfigLoaderTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ArtifactTypeConfig.Layer` as `string?`. Null for the fourteen non-generable types. Task 3 reads it.

- [ ] **Step 1: Write the failing test**

Append to `tests/Registrar.Tests/ConfigLoaderTests.cs`:

```csharp
[Theory]
[InlineData("DS", "Governance")]
[InlineData("AUP", "Governance")]
[InlineData("UQR", "Delivery")]
[InlineData("DCN", "Delivery")]
[InlineData("TCL", "Delivery")]
[InlineData("TAK", "Delivery")]
[InlineData("RAF", "Operational")]
[InlineData("PIG", "Administration")]
[InlineData("SCL", "Administration")]
[InlineData("PDX", "Administration")]
public void Generable_types_declare_their_framework_layer(string code, string expected)
{
    var config = SemanticTestSupport.Config();

    Assert.Equal(expected, config.ArtifactTypesByCode[code].Layer);
}

[Fact]
public void Types_without_a_defined_section_schema_declare_no_layer()
{
    var config = SemanticTestSupport.Config();

    foreach (var type in config.ArtifactTypesByCode.Values)
        if (type.SectionSchema?.Status != "defined")
            Assert.Null(type.Layer);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: FAIL. `Generable_types_declare_their_framework_layer` fails because `ArtifactTypeConfig` has no `Layer` property, so the file will not compile.

- [ ] **Step 3: Add the property to the config model**

In `src/Registrar.Core/Config/ConfigModels.cs`, inside `ArtifactTypeConfig`, add below `Classification`:

```csharp
    // Framework layer per ADR-002's four-layer model, assigned per artifact type by
    // owner ruling 2026-08-01. Null for types with no defined section schema: they are
    // already refused by QA-ENG-02, so there is nothing to validate a layer against.
    public string? Layer { get; init; }
```

- [ ] **Step 4: Add the layer to the ten generable types**

In `config/artifact-types.json`, add a `"layer"` key immediately after `"classification"` in each of these ten entries. Do not add it to any other entry.

```
DS   "layer": "Governance",
AUP  "layer": "Governance",
UQR  "layer": "Delivery",
DCN  "layer": "Delivery",
TCL  "layer": "Delivery",
TAK  "layer": "Delivery",
RAF  "layer": "Operational",
PIG  "layer": "Administration",
SCL  "layer": "Administration",
PDX  "layer": "Administration",
```

For example, the DS entry becomes:

```json
    "classification": "Client-Facing",
    "layer": "Governance",
    "reviewIntervalMonths": 6,
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~ConfigLoaderTests"`
Expected: PASS, 12 new assertions green.

- [ ] **Step 6: Commit**

```bash
git add config/artifact-types.json src/Registrar.Core/Config/ConfigModels.cs tests/Registrar.Tests/ConfigLoaderTests.cs
git commit -m "feat: declare framework layer per artifact type in config"
```

---

### Task 2: Align the fixtures to the ratified mapping

Eleven fixtures carry a `layer` that predates the ruling and disagrees with it. They must be corrected before QA-ENG-08 is registered in Task 4, or the whole suite goes red.

`artifactIdentity.layer` does not appear in any golden file, so this task cannot move `artifactSha256` and no golden regeneration is needed. Verify that claim rather than trusting it; Step 3 does.

**Files:**
- Modify: 11 files in `fixtures/`

**Interfaces:**
- Consumes: the ratified mapping from Global Constraints.
- Produces: fixtures whose `layer` agrees with config, so Task 4's registration lands green.

- [ ] **Step 1: Confirm the current disagreement**

Run:

```bash
python -c "
import json,glob
r={'DS':'Governance','AUP':'Governance','RAF':'Operational','UQR':'Delivery',
   'DCN':'Delivery','TCL':'Delivery','TAK':'Delivery','PIG':'Administration',
   'SCL':'Administration','PDX':'Administration'}
for f in sorted(glob.glob('fixtures/agr-*.json')):
    try: d=json.load(open(f,encoding='utf-8'))
    except Exception: continue
    i=d.get('artifactIdentity') or {}
    e=r.get(i.get('artifactType'))
    if e and i.get('layer')!=e: print(f, i.get('artifactType'), i.get('layer'), '->', e)
"
```

Expected: exactly these eleven lines.

```
fixtures/agr-sample-dcn-draft.json                    DCN Operational    -> Delivery
fixtures/agr-sample-dcn-promotion.json                DCN Operational    -> Delivery
fixtures/agr-sample-raf-draft.json                    RAF Administration -> Operational
fixtures/agr-sample-raf-promotion.json                RAF Administration -> Operational
fixtures/agr-sample-raf-warn-callout-promotion.json   RAF Administration -> Operational
fixtures/agr-sample-tak-draft.json                    TAK Operational    -> Delivery
fixtures/agr-sample-tak-promotion.json                TAK Operational    -> Delivery
fixtures/agr-sample-tcl-draft.json                    TCL Operational    -> Delivery
fixtures/agr-sample-tcl-promotion.json                TCL Operational    -> Delivery
fixtures/agr-sample-tcl-radio-missing-question.json   TCL Operational    -> Delivery
fixtures/agr-sample-uqr-draft.json                    UQR Operational    -> Delivery
```

- [ ] **Step 2: Edit the eleven `layer` values**

In each of the eleven files, change only the `"layer"` value inside `artifactIdentity`. Change nothing else. Do not reformat the JSON; these files are compared byte for byte in other tests.

- [ ] **Step 3: Verify no golden moved**

Run: `dotnet test`
Expected: PASS, 366 tests, no golden failures. If a golden fails here, stop: it means `layer` reaches rendered output somewhere this plan did not account for, and the assumption behind this task is wrong.

- [ ] **Step 4: Commit**

```bash
git add fixtures
git commit -m "fix: align fixture framework layers to the ratified mapping"
```

---

### Task 3: The QA-ENG-08 check

**Files:**
- Create: `src/Registrar.Validation/LayerAgreementLayer.cs`
- Create: `fixtures/agr-sample-ds-layer-mismatch.json`
- Test: `tests/Registrar.Tests/LayerAgreementLayerTests.cs`

**Interfaces:**
- Consumes: `ArtifactTypeConfig.Layer` from Task 1. `SemanticContext(AgrDocument Agr, ConfigBundle Config, string Mode, IIdRegistry? Registry)` and `ISemanticLayer.Validate`.
- Produces: `LayerAgreementLayer` with `public const string CheckId = "QA-ENG-08"`. Task 4 registers it and reuses the exact `RuleSourceValue` string.

- [ ] **Step 1: Create the failure fixture**

Copy `fixtures/agr-sample-ds-draft.json` to `fixtures/agr-sample-ds-layer-mismatch.json`, then make exactly two edits:

1. In `artifactIdentity`, change `"layer": "Governance"` to `"layer": "Delivery"`.
2. In `renderingOptions`, change `"outputFileName"` so the fixture does not collide with another fixture's output. Set it to `"ORG - Deployment Summary - Talewind CPA - v1.0.docx"` if it is not already that value.

This mirrors `fixtures/agr-sample-ds-id-code-mismatch.json`, the existing convention for a single-field failure fixture.

- [ ] **Step 2: Write the failing tests**

Create `tests/Registrar.Tests/LayerAgreementLayerTests.cs`:

```csharp
using Registrar.Core;
using Registrar.Validation;

namespace Registrar.Tests;

public class LayerAgreementLayerTests
{
    [Fact]
    public void QA_ENG_08_passes_when_layer_matches_the_configured_layer()
    {
        var checks = new LayerAgreementLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }

    [Fact]
    public void QA_ENG_08_fails_when_layer_disagrees_with_config()
    {
        var checks = new LayerAgreementLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-layer-mismatch.json", Mode.Draft));

        var check = Assert.Single(checks);
        Assert.Equal("QA-ENG-08", check.CheckId);
        Assert.Equal(Layer.Input, check.LayerName);
        Assert.Equal(CheckResultValue.Fail, check.Result);
        Assert.Equal(Severity.Blocking, check.SeverityLevel);
        Assert.Equal(ErrorCategory.MetadataViolation, check.ErrorCategory);
        Assert.Equal("/artifactIdentity/layer", check.JsonPath);
        Assert.Equal("Governance", check.Expected);
        Assert.Equal("Delivery", check.Actual);
    }

    [Fact]
    public void QA_ENG_08_blocks_in_promotion_as_well_as_draft()
    {
        var checks = new LayerAgreementLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-layer-mismatch.json", Mode.Promotion));

        Assert.Single(checks);
    }

    // A type with no defined section schema carries no layer in config, so there is
    // nothing to compare. QA-ENG-02 already refuses it; this layer must not double-report.
    [Fact]
    public void QA_ENG_08_skips_a_type_that_declares_no_layer()
    {
        var context = SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft);
        var tvr = context.Agr with
        {
            Identity = context.Agr.Identity with { ArtifactType = "TVR" }
        };

        var checks = new LayerAgreementLayer().Validate(context with { Agr = tvr });

        Assert.Empty(checks);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LayerAgreementLayerTests"`
Expected: FAIL, will not compile, `LayerAgreementLayer` does not exist.

- [ ] **Step 4: Write the implementation**

Create `src/Registrar.Validation/LayerAgreementLayer.cs`:

```csharp
using Registrar.Core;
using Registrar.Core.Model;

namespace Registrar.Validation;

// Validates artifactIdentity.layer against the artifact type's configured framework
// layer. Before this check existed, layer was the one required identity field the
// engine accepted on trust: it flows from the AGR into artifact-manifest.json via
// ArtifactManifestBuilder and nothing else reads it, so a wrong value was recorded
// silently and shipped in the manifest.
public sealed class LayerAgreementLayer : ISemanticLayer
{
    public const string CheckId = "QA-ENG-08";
    private const string RuleSourceValue = "ADR-002 AI Governance Program Structure";

    public IReadOnlyList<Check> Validate(SemanticContext context)
    {
        var artifactType = context.Agr.Identity.ArtifactType;
        var actual = context.Agr.Identity.Layer;

        // Deliberately fail-open on an unknown type or a type with no configured layer.
        // QA-ENG-02 (SectionSchemaLayer) already refuses a type absent from
        // artifact-types.json or without a defined section schema, so this layer does
        // not double-report it. Only generable types carry a layer in config.
        if (!context.Config.ArtifactTypesByCode.TryGetValue(artifactType, out var type)
            || string.IsNullOrEmpty(type.Layer))
            return Array.Empty<Check>();

        if (string.Equals(type.Layer, actual, StringComparison.Ordinal))
            return Array.Empty<Check>();

        return new[]
        {
            new Check
            {
                CheckId = CheckId,
                LayerName = Layer.Input,
                Result = CheckResultValue.Fail,
                SeverityLevel = Severity.Blocking,
                ErrorCategory = ErrorCategory.MetadataViolation,
                Message = $"Framework layer '{actual}' does not match the layer configured "
                    + $"for artifact type '{artifactType}'.",
                JsonPath = "/artifactIdentity/layer",
                Expected = type.Layer,
                Actual = actual,
                Remediation = $"Set artifactIdentity.layer to '{type.Layer}'.",
                RuleSource = RuleSourceValue
            }
        };
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LayerAgreementLayerTests"`
Expected: PASS, 4 tests.

- [ ] **Step 6: Commit**

```bash
git add src/Registrar.Validation/LayerAgreementLayer.cs tests/Registrar.Tests/LayerAgreementLayerTests.cs fixtures/agr-sample-ds-layer-mismatch.json
git commit -m "feat: QA-ENG-08 validates framework layer against config"
```

---

### Task 4: Catalog the check and wire it into the pipeline

Registering the layer makes it run over every fixture through `ValidationService`. `QaCatalogConsistencyTests` then asserts the emitted `ruleSource` matches the catalog exactly, so the catalog entry and the const in Task 3 must agree byte for byte.

**Files:**
- Modify: `config/qa-checklist.json`
- Modify: `src/Registrar.Validation/ValidationService.cs:64-74`
- Modify: `fixtures/expected/` via the golden harness

**Interfaces:**
- Consumes: `LayerAgreementLayer` and its `RuleSourceValue` from Task 3.
- Produces: QA-ENG-08 emitted through the full pipeline.

- [ ] **Step 1: Add the catalog entry**

In `config/qa-checklist.json`, append to the `checks` array immediately after the `QA-ENG-07` entry. The `ruleSource` must be exactly `ADR-002 AI Governance Program Structure`, matching `RuleSourceValue` in Task 3.

```json
    { "checkId": "QA-ENG-08", "layer": "input", "automation": "full", "appliesTo": "all", "rule": "artifactIdentity.layer matches the framework layer configured for the artifact type in artifact-types.json. Before this check, layer was the one required identity field the engine accepted on trust: it flows into artifact-manifest.json and nothing else validated it. Types with no configured layer are skipped because QA-ENG-02 already refuses them. Blocks in draft and promotion.", "errorCategory": "MetadataViolation", "ruleSource": "ADR-002 AI Governance Program Structure" }
```

Remember the comma on the preceding entry.

- [ ] **Step 2: Register the layer**

In `src/Registrar.Validation/ValidationService.cs`, add to the layer list immediately after `new MetadataLayer(),`:

```csharp
                new LayerAgreementLayer(),
```

- [ ] **Step 3: Run the full suite to see the new fixture's golden gap**

Run: `dotnet test`
Expected: FAIL. `TestCommandTests.All_committed_fixtures_match_their_goldens` reports no expected file for `agr-sample-ds-layer-mismatch`. Every other test passes. If any other fixture now emits QA-ENG-08, stop: Task 2 missed one.

- [ ] **Step 4: Generate the golden for the new fixture**

Run: `dotnet run --project src/Registrar.Cli -- test --all --update`
Expected: `updated 37/37 fixture(s).`

- [ ] **Step 5: Confirm the new golden records the QA-ENG-08 failure**

Run: `grep -c "QA-ENG-08" fixtures/expected/agr-sample-ds-layer-mismatch/validation-report.json`
Expected: at least `1`. This proves the check reaches the report through the real pipeline, not just the unit test.

- [ ] **Step 6: Run the full suite**

Run: `dotnet test`
Expected: PASS, 370 tests, 0 failures, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add config/qa-checklist.json src/Registrar.Validation/ValidationService.cs fixtures/expected
git commit -m "feat: register QA-ENG-08 and catalog its rule"
```

---

### Task 5: Update the documentation the Governor reads

**Files:**
- Modify: `docs/governor-contract.md:381`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: the ratified mapping.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Replace the layer line in the contract sheet**

In `docs/governor-contract.md`, find this line in section 9:

```
- `layer`: `Governance`, `Operational`, `Delivery`, or `Administration`.
```

Replace it with:

```
- `layer`: determined by the artifact type, not chosen. DS and AUP are `Governance`.
  UQR, DCN, TCL, and TAK are `Delivery`. RAF is `Operational`. PIG is
  `Administration`. A mismatch fails QA-ENG-08 with a `MetadataViolation`.
```

- [ ] **Step 2: Record the ruling in the constitution**

In `CLAUDE.md`, add to the Known gaps list:

```
- (Closed 2026-08-01.) artifactIdentity.layer was the one required identity field
  the engine accepted on trust: it flowed into artifact-manifest.json and nothing
  validated it, because artifact-types.json carried category per type and no layer.
  ADR-002 defines the four layers over program artifacts and names none of the
  generation targets, so the per-type assignment was a new owner ruling rather than
  a mapping lifted from the ADR. layer is now declared per type in
  artifact-types.json and enforced by QA-ENG-08 (LayerAgreementLayer), blocking in
  both modes. It is per-type rather than per-category because ADR-002 places
  Operational Runbooks in the Governance layer while UQR and DCN, in the same OPS
  category, are Delivery.
```

- [ ] **Step 3: Verify no prohibited characters entered the docs**

Run: `dotnet test --filter "FullyQualifiedName~SourceHygieneTests"`
Expected: PASS.

- [ ] **Step 4: Run the full suite one last time**

Run: `dotnet test`
Expected: PASS, 370 tests.

- [ ] **Step 5: Commit**

```bash
git add docs/governor-contract.md CLAUDE.md
git commit -m "docs: record the per-type framework layer ruling"
```

---

## Self-Review

**Spec coverage.** The GUI-1 scope in `docs/superpowers/specs/2026-08-01-operator-gui-design.md` lists four items: add `layer` to `ArtifactTypeConfig` and `artifact-types.json` (Task 1), add QA-ENG-08 failing any AGR whose layer disagrees (Task 3), add the `qa-checklist.json` entry (Task 4), update `governor-contract.md` (Task 5). All covered. Task 2 is additional and was discovered during planning: eleven fixtures disagreed with the ratified mapping, and without correcting them Task 4 turns the suite red.

**Two spec corrections this plan makes.** The spec's mapping table lists eight types. SCL and PDX are also generable and have fixtures, so the table is extended to ten here. The spec also does not mention that PIG has no fixture, so its `Administration` assignment ships covered only by the Task 1 config test, not by an end-to-end fixture. Authoring a PIG fixture is a reasonable follow-up and is deliberately out of scope for GUI-1.

**Placeholder scan.** No TBD, TODO, or "handle appropriately". Every code step carries the actual code. Every run step carries the actual command and the expected result.

**Type consistency.** `ArtifactTypeConfig.Layer` is `string?` in Task 1 and null-checked with `string.IsNullOrEmpty` in Task 3. `LayerAgreementLayer.CheckId` is `"QA-ENG-08"` in Task 3 and `checkId` is `"QA-ENG-08"` in Task 4. `RuleSourceValue` is `"ADR-002 AI Governance Program Structure"` in Task 3 and `ruleSource` is the identical string in Task 4, which `QaCatalogConsistencyTests` enforces. `Layer.Input` is the validation-stage constant from `Registrar.Core.Vocabulary` and is distinct from `ArtifactTypeConfig.Layer`; C# resolves them unambiguously because one is a static class and the other is member access.

**Test count.** 366 today. Task 1 adds 11 (a 10-case `[Theory]` plus one `[Fact]`), Task 3 adds 4, and the new fixture adds 1 golden case, reaching 370 at Task 4. Adjust the expected counts if the suite has moved.
