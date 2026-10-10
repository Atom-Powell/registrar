# Payload-generation test pack - design

Date: 2026-07-15

## Problem

We have a judgment eval for the Governor (`evals/governor-judgment/`, does it recommend
and refuse correctly, graded by an LLM). We do not have a Studio-driven eval of the other
half: given complete inputs, does the Governor produce a valid AGR **payload** the engine
accepts. The existing contract harness (`evals/prompts/` + `scripts/eval-governor.ps1`)
tests this, but only via a manual one-prompt-at-a-time paste loop.

## Decision

A Studio bulk-upload CSV that collects payloads, scored by the **engine**, not by Studio.

- **Grading: engine-scored.** A payload is JSON; only the engine can verify schema,
  character-exact section titles, clauses inserted by reference and proven by SHA-256, and
  canonical placeholders. Studio's LLM `result` column is ignored. The CSV is a bulk
  collection device: upload, the Governor emits AGRs into the export's `actualResponse`
  column, we extract each to a `.json` and run `registrar build`.
- **Coverage (first cut): 3 types x 2 modes.** TCL, DS, AUP - the three IDs already in
  `evals/registry-eval.csv` - each in draft and promotion. Six rows. No registry change:
  the promotion fixtures score against `Draft`-status registry entries, so promotion needs
  no status change; its extra strictness is payload completeness, which is the point.
- **Fully-specified prompts.** Each Question supplies every input (registered Document ID,
  client, version, tool, domain facts) so a failure is a payload-construction fault, not
  missing input. Single-line cells, matching the proven-uploadable dialect (UTF-8 BOM,
  CRLF, all fields quoted). Each prompt ends with "emit only the AGR JSON, no fence" to
  test output discipline directly.
- **Expected-response column** is a human-readable note stating the row's target and that
  the engine grades it, not the column.

## What it tests

Output discipline (raw JSON, else `MALFORMED-JSON`), schema validity, exact section
titles, clause-by-reference selection and hashing, placeholder canonicality, and
promotion-readiness (passes draft but fails promotion = renderable but not shippable).

## Out of scope (deliberate)

- No scoring script for the first cut; extraction + engine-scoring is done by hand until
  the loop proves repetitive, then a `score-payload-run.ps1` can follow.
- No trap rows (traps are judgment, covered by the other pack).
- Types beyond TCL/DS/AUP and the full 8-type matrix are a later expansion that requires
  extending `registry-eval.csv`.

## Files

- `evals/governor-payload/01-generation.csv` - the six-row pack.
- `evals/governor-payload/README.md` - the extract-and-engine-score workflow.
