# Governor payload-generation eval

Tests whether the Governor, given complete inputs, produces a valid AGR **payload** the
engine accepts. This is the contract eval delivered as a Studio bulk upload: the Studio
run collects the payloads, and the **engine** grades them.

This is distinct from `evals/governor-judgment/`, which tests judgment (which artifacts
to recommend) and is graded by an LLM. A payload is JSON, and only the engine can verify
schema validity, character-exact section titles, clauses inserted by reference and proven
by SHA-256, and canonical placeholder tokens. An LLM judge cannot, so it is not used here.

## Files

- `01-generation.csv` - six fully-specified generation requests: TCL, DS, AUP, each in
  draft and promotion mode. Every request supplies all inputs (a registered Document ID,
  client, version, tool, and the domain facts) so the Governor has no reason to ask and
  must emit the AGR. The Document IDs are the ones in `evals/registry-eval.csv`
  (ORG-TRN-TCL-701, ORG-GOV-DS-702, ORG-GOV-AUP-703); an unregistered ID fails on the
  registry check regardless of JSON quality.

## Workflow

1. Upload `01-generation.csv` to Copilot Studio's evaluation feature. Studio runs the
   Governor on all six prompts and captures each reply in the `actualResponse` column of
   the exported results CSV.
2. **Ignore Studio's `result` column.** It is an LLM judge and cannot grade a payload.
   The export is only a transport for the AGRs.
3. Extract each `actualResponse` verbatim into a `.json` file. Do not clean it up. The
   export captures the reply with its code fence intact, which is what the output rule
   asks the Governor for; the scorer recovers the payload from inside it exactly as the
   engine does. `MALFORMED-JSON` means no recoverable payload at all. Cleaning a reply by
   hand hides whatever it was that needed cleaning.
4. Score each payload with the engine, in the row's mode:

       registrar build <agr>.json --mode draft      --registry evals/registry-eval.csv
       registrar build <agr>.json --mode promotion   --registry evals/registry-eval.csv

   Read the resulting `validation-report.json`: `overallResult` plus each failing
   `checkId`.

## What each mode tells you

- **Draft** answers "can the Governor produce a structurally valid payload at all":
  schema, section titles reproduced exactly, clauses selected and inserted by reference,
  canonical placeholders.
- **Promotion** is the stricter test: full placeholder completeness, no warn callouts, no
  internal-only notes. A payload that passes draft but fails promotion means the Governor
  produces something renderable but not yet shippable, which is a useful, specific signal.
