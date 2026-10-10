# Placeholder Coverage Rollup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `PlaceholderLayer` emits one informational check per run carrying a placeholder coverage summary (required, provided, missing, beyond-the-required-set), naming the tokens for the two counts a reviewer acts on.

**Architecture:** One new `Check` appended in `PlaceholderLayer.Validate`, `Result=pass` / `Severity=info`, riding in the existing `checks[]` array. No schema change, no `reportVersion` bump. The info/pass check is inert to `OverallResultPolicy` and to the CLI's failing/warning counts (both react only to `fail`+`blocking` and `warn`).

**Tech Stack:** .NET 8, C#, xUnit. The engine's own source is held to escapes-only for non-ASCII (SourceHygieneTests).

**Spec:** `docs/superpowers/specs/2026-07-14-placeholder-coverage-rollup-design.md`

## Global Constraints

- No change to `schemas/validation-report.schema.json`, `config/`, or any wire contract. This is a new emitted check within the existing shape.
- The coverage check is emitted on EVERY run, in BOTH modes, identically. It never fails, never warns.
- Reports "beyond the required set", never "unused": the layer runs pre-render and cannot prove a provided token is unreferenced.
- `beyondRequired` counts only provided keys that ARE canonical tokens and are NOT required. Drift-alias and unknown keys are excluded (each is already its own finding).
- US English, no emojis, no em/en dashes or decorative unicode in source or messages (QA-SF-08 scans rendered output; SourceHygieneTests holds the engine source to escapes-only).
- Token lists in the message are sorted `StringComparer.Ordinal` and read `none` when empty, so output is deterministic.

---

### Task 1: Emit the coverage check from PlaceholderLayer

**Files:**
- Modify: `src/Registrar.Validation/PlaceholderLayer.cs` (add the coverage computation before `return checks;` at line 58, and a `Coverage` helper)
- Test: `tests/Registrar.Tests/PlaceholderLayerTests.cs` (update one existing test, add four)

**Interfaces:**
- Consumes: `SemanticContext` (`context.Agr.Identity.DocumentId`, `context.Agr.Identity.ArtifactType`, `context.Agr.DataInputs.PlaceholderMap`, `context.Config.ArtifactTypesByCode`, `context.Config.CanonicalTokens`), all already used by the layer.
- Produces: an additional `Check` with `CheckId="QA-SPEC-07"`, `Result=CheckResultValue.Pass`, `SeverityLevel=Severity.Info`, appended last in the returned list.

**Important:** the existing test `QA_SPEC_07_passes_for_fully_resolved_canonical_placeholders` asserts `Assert.Empty(checks)`. Once the layer always emits the coverage check, that layer returns one check for a clean AGR, so this test MUST be updated (Step 1) or it will fail. That is expected, not a regression.

- [ ] **Step 1: Update the now-incorrect empty-assertion test to expect the coverage check**

In `tests/Registrar.Tests/PlaceholderLayerTests.cs`, replace the whole first test:

```csharp
    [Fact]
    public void QA_SPEC_07_passes_for_fully_resolved_canonical_placeholders()
    {
        var checks = new PlaceholderLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        Assert.Empty(checks);
    }
```

with:

```csharp
    [Fact]
    public void QA_SPEC_07_emits_only_the_coverage_check_for_fully_resolved_canonical_placeholders()
    {
        var checks = new PlaceholderLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        // A clean AGR produces no per-token findings, only the informational coverage rollup.
        var coverage = Assert.Single(checks);
        Assert.Equal("QA-SPEC-07", coverage.CheckId);
        Assert.Equal(CheckResultValue.Pass, coverage.Result);
        Assert.Equal(Severity.Info, coverage.SeverityLevel);
        Assert.Null(coverage.ErrorCategory);
        // DS requires 10 tokens; this fixture provides exactly those 10.
        Assert.Contains("10 required, 10 provided, 0 missing, 0 beyond the required set", coverage.Message);
        Assert.Contains("Missing: none", coverage.Message);
        Assert.Contains("Beyond required: none", coverage.Message);
    }
```

- [ ] **Step 2: Run it to confirm it fails**

Run: `dotnet test --nologo -v q --filter "FullyQualifiedName~QA_SPEC_07_emits_only_the_coverage_check"`
Expected: FAIL. `Assert.Single` sees an empty collection (the coverage check does not exist yet).

- [ ] **Step 3: Add the coverage computation and helper to PlaceholderLayer**

In `src/Registrar.Validation/PlaceholderLayer.cs`, insert this block immediately BEFORE `return checks;` (currently line 58):

```csharp
        // Coverage rollup (REG-TD-007): an informational summary of placeholder completeness,
        // always emitted in both modes. Inert to overall result (pass/info). Reports "beyond the
        // required set" rather than "unused" because this layer runs pre-render and cannot prove a
        // provided token is unreferenced (a non-required token may feed clause text at render).
        var requiredSet = context.Config.ArtifactTypesByCode.TryGetValue(context.Agr.Identity.ArtifactType, out var coverageType)
                          && coverageType.RequiredPlaceholders is not null
            ? coverageType.RequiredPlaceholders.ToHashSet()
            : new HashSet<string>();
        var providedKeys = context.Agr.DataInputs.PlaceholderMap.Keys;

        var missing = requiredSet.Where(t => !context.Agr.DataInputs.PlaceholderMap.ContainsKey(t))
            .OrderBy(t => t, StringComparer.Ordinal).ToList();
        var beyondRequired = providedKeys.Where(k => canonical.Contains(k) && !requiredSet.Contains(k))
            .OrderBy(t => t, StringComparer.Ordinal).ToList();

        checks.Add(Coverage(
            context.Agr.Identity.DocumentId,
            requiredSet.Count,
            context.Agr.DataInputs.PlaceholderMap.Count,
            missing,
            beyondRequired));
```

Then add this helper method to the class (for example after `Build`, before the closing brace):

```csharp
    private static Check Coverage(
        string documentId, int requiredCount, int providedCount,
        IReadOnlyList<string> missing, IReadOnlyList<string> beyondRequired)
    {
        static string List(IReadOnlyList<string> tokens)
            => tokens.Count == 0 ? "none" : string.Join(", ", tokens);

        var message =
            $"Placeholder coverage for {documentId}: {requiredCount} required, {providedCount} provided, " +
            $"{missing.Count} missing, {beyondRequired.Count} beyond the required set. " +
            $"Missing: {List(missing)}. Beyond required: {List(beyondRequired)}.";

        return new Check
        {
            CheckId = CheckId,
            LayerName = Layer.Content,
            Result = CheckResultValue.Pass,
            SeverityLevel = Severity.Info,
            Message = message,
            JsonPath = "/dataInputs/placeholderMap",
            RuleSource = RuleSourceValue
        };
    }
```

Note: `ErrorCategory`, `Expected`, `Actual`, and `Remediation` are left unset (null), matching `PromotionLayer.Pass`. `canonical` and `RuleSourceValue` already exist in the file.

- [ ] **Step 4: Run it to confirm it passes**

Run: `dotnet test --nologo -v q --filter "FullyQualifiedName~QA_SPEC_07_emits_only_the_coverage_check"`
Expected: PASS.

- [ ] **Step 5: Add the missing-required-token coverage test**

Append to `tests/Registrar.Tests/PlaceholderLayerTests.cs`:

```csharp
    [Fact]
    public void QA_SPEC_07_coverage_names_a_missing_required_token()
    {
        // Drop one DS required token; keep the map otherwise complete. The value string
        // below matches fixtures/agr-sample-ds-draft.json line 32 verbatim, including the
        // trailing comma, so the removal leaves valid JSON.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("      \"[OUT OF SCOPE USES]\": \"Processing regulated client financial data\",\n", "");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var checks = new PlaceholderLayer()
            .Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null));

        var coverage = Assert.Single(checks, c => c.Result == CheckResultValue.Pass && c.SeverityLevel == Severity.Info);
        Assert.Contains("10 required, 9 provided, 1 missing", coverage.Message);
        Assert.Contains("Missing: [OUT OF SCOPE USES]", coverage.Message);
        // The pre-existing per-token fail for the missing required token is still present.
        Assert.Contains(checks, c => c.Result == CheckResultValue.Fail && c.Message.Contains("[OUT OF SCOPE USES]") && c.Message.Contains("missing"));
    }
```

The `Replace` string matches `fixtures/agr-sample-ds-draft.json` as of this plan (the `[OUT OF SCOPE USES]` line with its leading six-space indent and trailing comma-newline). If the fixture has since changed and the removal produces invalid JSON, `AgrParser.Parse` throws; adjust the `Replace` to the fixture's real line and rerun.

- [ ] **Step 6: Add the beyond-required test, including that a drift alias does not count**

Append:

```csharp
    [Fact]
    public void QA_SPEC_07_coverage_counts_extra_canonical_token_as_beyond_required_but_not_a_drift_alias()
    {
        // [SUPPORT CONTACT] is canonical and NOT a DS required token; add it as an extra.
        var text = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[VERSION]\": \"v1.0\"", "\"[VERSION]\": \"v1.0\", \"[SUPPORT CONTACT]\": \"ORG Service Desk\"");
        var agr = Registrar.Core.Model.AgrParser.Parse(text);

        var coverage = Assert.Single(
            new PlaceholderLayer().Validate(new SemanticContext(agr, SemanticTestSupport.Config(), Mode.Draft, null)),
            c => c.Result == CheckResultValue.Pass && c.SeverityLevel == Severity.Info);
        Assert.Contains("10 required, 11 provided, 0 missing, 1 beyond the required set", coverage.Message);
        Assert.Contains("Beyond required: [SUPPORT CONTACT]", coverage.Message);

        // A drift alias added instead is a fail, and must NOT be counted as beyond-required.
        var aliasText = SemanticTestSupport.FixtureText("agr-sample-ds-draft.json")
            .Replace("\"[VERSION]\": \"v1.0\"", "\"[VERSION]\": \"v1.0\", \"[CLIENT]\": \"x\"");
        var aliasAgr = Registrar.Core.Model.AgrParser.Parse(aliasText);
        var aliasCoverage = Assert.Single(
            new PlaceholderLayer().Validate(new SemanticContext(aliasAgr, SemanticTestSupport.Config(), Mode.Draft, null)),
            c => c.Result == CheckResultValue.Pass && c.SeverityLevel == Severity.Info);
        Assert.Contains("0 beyond the required set", aliasCoverage.Message);
        Assert.Contains("Beyond required: none", aliasCoverage.Message);
    }
```

- [ ] **Step 7: Add the null-required (UQR) test**

Append:

```csharp
    [Fact]
    public void QA_SPEC_07_coverage_handles_a_type_with_no_required_placeholders()
    {
        // UQR has requiredPlaceholders: null. This fixture provides 6 canonical tokens.
        var coverage = Assert.Single(
            new PlaceholderLayer().Validate(SemanticTestSupport.Context("agr-sample-uqr-draft.json", Mode.Draft)),
            c => c.Result == CheckResultValue.Pass && c.SeverityLevel == Severity.Info);
        Assert.Contains("0 required, 6 provided, 0 missing, 6 beyond the required set", coverage.Message);
        Assert.Contains("Missing: none", coverage.Message);
    }
```

- [ ] **Step 8: Add the overall-result-inertness test**

Append:

```csharp
    [Fact]
    public void QA_SPEC_07_coverage_check_does_not_change_overall_result()
    {
        var checks = new PlaceholderLayer()
            .Validate(SemanticTestSupport.Context("agr-sample-ds-draft.json", Mode.Draft));

        // The only check for a clean AGR is the info/pass coverage line; overall result stays pass.
        Assert.Equal(OverallResult.Pass, OverallResultPolicy.Compute(checks, null));
    }
```

- [ ] **Step 9: Run the full PlaceholderLayer test class**

Run: `dotnet test --nologo -v q --filter "FullyQualifiedName~PlaceholderLayerTests"`
Expected: PASS, all tests in the class green. If Step 5's JSON edit failed to parse, fix the `Replace` to match the fixture's real line and rerun.

- [ ] **Step 10: Commit**

```bash
git add src/Registrar.Validation/PlaceholderLayer.cs tests/Registrar.Tests/PlaceholderLayerTests.cs
git commit -m "feat: emit a placeholder coverage rollup from QA-SPEC-07"
```

---

### Task 2: Regenerate goldens and close the tech-debt item

**Files:**
- Modify: every `fixtures/expected/*/validation-report.json` (regenerated, mechanical)
- Modify: `docs/tech-debt.md` (mark item 29 resolved)

**Interfaces:**
- Consumes: the coverage check from Task 1.
- Produces: nothing code-facing.

The coverage check now appears in every run's report, so every stored golden gains it. Regenerate once and confirm idempotence.

- [ ] **Step 1: Confirm the goldens are currently stale (the guard is working)**

Run: `dotnet run --project src/Registrar.Cli -- test --all`
Expected: fixtures FAIL to match goldens, each diff showing the added QA-SPEC-07 coverage check line. This proves the golden comparison sees the new check.

- [ ] **Step 2: Regenerate all goldens**

Run: `dotnet run --project src/Registrar.Cli -- test --all --update`
Expected: `updated 30/30 fixture(s).`

- [ ] **Step 3: Confirm idempotence (the real proof determinism holds)**

Run: `dotnet run --project src/Registrar.Cli -- test --all`
Expected: `30/30 fixture(s) matched goldens.` Run it a second time; it must still be 30/30. If a golden drifts between runs, STOP: the coverage message is nondeterministic (most likely an unsorted token list) and must be fixed before committing.

- [ ] **Step 4: Inspect one regenerated golden to confirm the coverage line is correct**

Run: `git diff fixtures/expected/agr-sample-ds-draft/validation-report.json`
Expected: the diff adds one QA-SPEC-07 check with `"result": "pass"`, `"severity": "info"`, and a message reading `Placeholder coverage for ORG-GOV-DS-016: 10 required, 10 provided, 0 missing, 0 beyond the required set. Missing: none. Beyond required: none.` Read it and confirm it says what it should.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test --nologo -v q`
Expected: PASS, 0 failed. The count grows by the tests added in Task 1.

- [ ] **Step 6: Mark tech-debt item 29 resolved**

In `docs/tech-debt.md`, change the opening of item 29 from:

```markdown
29. **Placeholder coverage rollup (Governor-requested, real gap).** `PlaceholderLayer`
```

to:

```markdown
29. ~~**Placeholder coverage rollup (Governor-requested, real gap).**~~ RESOLVED
    2026-07-14. `PlaceholderLayer` (QA-SPEC-07) now emits an informational coverage
    check on every run (required / provided / missing / beyond-the-required-set,
    naming the tokens), riding in the existing `checks[]` array with no schema change.
    Original entry: `PlaceholderLayer`
```

- [ ] **Step 7: Verify no prohibited characters entered the changed files**

Run:

```bash
python3 -c "
for p in ['src/Registrar.Validation/PlaceholderLayer.cs','docs/tech-debt.md']:
    s=open(p,encoding='utf-8').read()
    bad=[(hex(ord(c)),c) for c in s if not(0x20<=ord(c)<=0x7E or c in '\n\r\t')]
    print(p, bad if bad else 'clean')
"
```

Expected: both `clean`. (The regenerated goldens may legitimately contain U+25CB in unrelated TCL fixtures; do not scan those.)

- [ ] **Step 8: Commit**

```bash
git add fixtures/expected docs/tech-debt.md
git commit -m "test: regenerate goldens for the coverage check; close tech-debt item 29"
```

---

## Self-review

**Spec coverage.** The check shape (info/pass, QA-SPEC-07, null ErrorCategory, `/dataInputs/placeholderMap`, existing rule source) is in Task 1 Step 3. The four counts and their definitions are in Task 1 Step 3 (`requiredSet`, `PlaceholderMap.Count`, `missing`, `beyondRequired` restricted to canonical non-required). The message format with `none` fallback and ordinal sort is in the `Coverage` helper. Emitted in both modes, always: the block is unconditional before `return`. Inertness to overall result: Task 1 Step 8. The five spec test cases map to Task 1 Steps 1 (complete DS), 5 (missing), 6 (beyond-required plus the alias exclusion), 7 (null-required UQR), 8 (overall-result inertness). Golden regeneration and idempotence: Task 2 Steps 1 to 4. Tech-debt item 29 resolved: Task 2 Step 6. All covered.

**Placeholder scan.** No TBD/TODO. Step 5 carries an explicit "confirm the real value string" instruction because the `Replace` depends on a fixture line the plan cannot guarantee byte-for-byte; this is a real verification step, not a vague hand-wave, and it names exactly what to check and how to recover.

**Type consistency.** `Coverage(string, int, int, IReadOnlyList<string>, IReadOnlyList<string>)` is defined and called with matching argument types in Step 3. `canonical`, `RuleSourceValue`, and `CheckId` are pre-existing members of `PlaceholderLayer` (confirmed against the current file). Tests use `SemanticTestSupport.Context`, `.FixtureText`, `.Config`, and `new SemanticContext(agr, config, mode, null)`, all matching the existing test file's patterns. `OverallResultPolicy.Compute(checks, null)` matches its real signature.
