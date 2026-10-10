# Governor Payload Correction Protocol

**Audience:** The operator, to paste into the Governor chat when a payload fails
validation; and anyone tuning the Governor's instructions.
**Purpose:** Turn a failed `validation-report.json` into a correct AGR in one or two
tries. This is the action sheet; `docs/governor-briefing.md` is the full orientation.

The engine never trusts the Governor and never improvises. A failed check is not a bug
to argue with - it is an instruction. Fix the exact field, do not restructure the
artifact, resubmit.

---

## The correction loop

1. The operator pastes the failing checks below into the Governor chat.
2. For each failure, the Governor fixes **only** the field named by its `jsonPath` and
   follows its `remediation` verbatim.
3. The Governor returns a corrected AGR. The operator re-runs the engine.
4. Repeat until the report reads `pass` (or `pass-with-warnings` in draft).

Do not regenerate the whole document to fix one field. Do not paraphrase a remediation.
Do not "improve" passing sections.

---

## How to read a failure

Every failure in the report carries five fields. Use them in this order:

| Field | Use it to |
|---|---|
| `jsonPath` | Locate the exact field to change (e.g. `/dataInputs/sections/0/blocks/0/text`). |
| `remediation` | Do exactly what it says. It is written as an executable instruction. |
| `expected` / `actual` | Confirm what the engine required vs. what you sent. |
| `checkId` | Cross-reference the rule (see the failure catalog below). |
| `ruleSource` | Traceability only; no action needed. |

---

## The two errors that block real payloads most often

These are not hypothetical. Both were observed on a live Governor payload
(`ORG-OPS-UQR-001`, the Copilot one-pager). Either one fails the request closed.

### Error 1 - Double-bracket placeholder tokens

**Wrong:** `[[TOOL / CONNECTOR NAME]]`
**Right:** `[TOOL / CONNECTOR NAME]`

Placeholder tokens use **single** brackets. The schema token pattern is
`^\[[A-Z0-9][A-Z0-9 /\-]*\]$` - one `[`, an uppercase/digit body (spaces, `/`, and `-`
allowed), one `]`. Double brackets fail `QA-SCHEMA-01` at the input gate before anything
else runs. Fix every token: in `placeholderMap` **keys** and anywhere a token appears.

### Error 2 - Placeholder tokens left in body prose

**Wrong** (paragraph text): `[TOOL / CONNECTOR NAME] operates within existing permissions.`
**Right** (paragraph text): `Microsoft Copilot operates within existing permissions.`

This is the important one - it reflects a wrong mental model. **The engine does not
substitute placeholders in body prose.** Paragraphs, bullets, headings, and table cells
render exactly as written. The `placeholderMap` exists only to resolve tokens inside
**clause text** (which the engine inserts by reference). So:

- Write the **resolved literal value** directly into body prose. If the tool is Microsoft
  Copilot, the paragraph says "Microsoft Copilot", not `[TOOL / CONNECTOR NAME]`.
- Reserve tokens for the `placeholderMap` and for clause content only.

Any raw `[TOKEN]` left in body text fails `QA-ENG-07` (token residue) in both draft and
promotion.

---

## Failure catalog: what each check means and how to fix it

Ordered by how often it bites. Full rule text is in `config/qa-checklist.json`.

| checkId | What failed | How the Governor fixes it |
|---|---|---|
| `QA-SCHEMA-01` | The AGR shape violates `agr.schema.json` (e.g. double-bracket token, wrong type, missing required field). | Correct the value at the `jsonPath` to satisfy the stated pattern/type. See Error 1. |
| `QA-ENG-07` | A bracketed `[TOKEN]` was left in body prose. | Replace the token in the body text with its resolved literal value. See Error 2. |
| `QA-ENG-05` / `QA-SF-08` | Prohibited characters in free text (em dash, en dash, smart quotes, ellipsis char, emoji, decorative unicode). | Use a plain hyphen `-`, straight quotes `"` `'`, and three periods `...`. Accented letters are fine. |
| `QA-ENG-04` / `QA-SF-11` | Clause prose was written out, or a required clause is missing. | Emit a `clauseInsertion` block with only the `clauseId`. The engine inserts the canonical text. Never paraphrase a clause. |
| `QA-ENG-02` | Section titles do not match the artifact type's schema exactly and in order. | Use the exact section titles, in the exact order, from the type's section schema. No additions, omissions, or reordering. |
| `QA-SF-07` | Empty H1 (a section with only headings and no content) or a heading-level skip (`h3` with no preceding `h2`). | Give every section at least one content block; introduce an `h2` before any `h3`. |
| `QA-SF-09` | A warn (red) callout in client-facing output at promotion. | Remove the warn callout. (Note: `warnCallout` is also not a renderable block type yet - do not use it.) |
| `QA-SF-03` / `QA-ENG-03` | `documentId` not in the ID Registry, wrong status, or category/code does not match `artifactType`. | Use a `documentId` that already exists in the registry in the expected status, with a category/code that matches the type. The engine never mints IDs. |
| `QA-SF-04` | Illegal `approvalStatus` transition. | Set an `approvalStatus` whose transition from the registry's current status is legal per the lifecycle map. |
| `QA-ENG-06` | A block type the engine cannot render yet (`warnCallout`, `radioQuestion`, `aggregationTable`, `copyReadyMessage`). | Remove it. Use only: `paragraph`, `h2`, `h3`, `bulletList`, `table`, `clauseInsertion`. |

---

## Draft vs promotion

- **Draft** tolerates warnings and still renders, so use it to surface the full set of
  issues in one pass. Unknown placeholder tokens warn here rather than block.
- **Promotion** is strict: unknown tokens and other draft-warnings become blocking, the
  DOCX is produced only on a clean pass, and a `promotion-payload.json` is emitted.

Get a payload clean in **draft first**, then promote. Structural and safety violations
(bracket/schema errors, token residue, character hygiene, heading structure, unregistered
ID, illegal transition, unsupported block) fail closed in **both** modes regardless.

---

## Artifact type scope (current engine)

- **Rendered and accepted for v2:** `DS` (Deployment Summary), `AUP` (Acceptable Use
  Policy).
- **Renderable, but outside the certified v2 acceptance scope:** `UQR`, `TCL` (their
  section schemas are defined; TCL also needs the radio-option pattern). The engine will
  process these, but they are not part of the accepted v2 milestone.
- **Refused (no section schema):** `TAK`, `RAF`, `DCN` - the engine returns a
  `ConfigurationError` because it will not invent structure. Do not request these.

---

## Paste-ready block for the Governor chat

> The Registrar rejected the payload. Below are the failing checks from
> `validation-report.json`. For each one, change only the field at its `jsonPath` and
> follow its `remediation` exactly - do not restructure the document or edit passing
> sections. Two rules that catch most payloads: (1) placeholder tokens use **single**
> brackets `[LIKE THIS]`, never double `[[LIKE THIS]]`; (2) the engine does **not**
> substitute placeholders in body text - write the resolved literal value directly into
> paragraphs, bullets, headings, and tables, and reserve tokens for the placeholderMap
> and clause content only. Return the corrected AGR JSON in full.
>
> [paste the failing `checks[]` entries here]
