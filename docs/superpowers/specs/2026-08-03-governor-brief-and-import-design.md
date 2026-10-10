# GUI-4: GovernorBrief and ImportParser

Date: 2026-08-03
Status: Approved, not yet planned
Roadmap unit: GUI-4 in `docs/ROADMAP.md`
Parent design: `docs/superpowers/specs/2026-08-01-operator-gui-design.md`
Predecessor: `docs/superpowers/specs/2026-08-02-core-authoring-foundation-design.md`

## Why this exists

GUI-4 completes the Core surface the operator GUI needs before any screen is built.
It covers the two directions of the Governor handoff: the text the operator sends into
the chat, and the reply they paste back out.

Neither half has a user interface. Both are plain functions, and both are consumed by
GUI-8 when the Import screen is built.

## Three corrections to the parent design

The parent spec was written on 2026-08-01, before GUI-2 and GUI-3 shipped. Both
delivered more than it anticipated, and its description of this unit is now wrong in
three places. All three were found while designing GUI-4.

**`GovernorBrief` is already built.** GUI-2's `DrafterProjection` derives exactly what
the parent spec specifies for `GovernorBrief`: exact ordered section titles, required
clause IDs with their canonical names, required placeholder tokens, and the
radio-option instruction gated on `usesRadioOptionPattern`. It renders all eight
in-scope types into `docs/governor/drafter.md`. What GUI-4 adds is the ability to
project a single type, which is a refactor of an existing loop body rather than a new
derivation. The GUI-3 spec anticipated this and recorded it as a note for GUI-4.

**`ImportParser` does not need to build a match report.** The parent spec has it return
"sections[] plus a structured result describing what matched, what is missing, and what
could not be read." GUI-3 delivered that. `AgrAuthoring.ToDocument` already emits an
`AuthoringGap` for a section whose title is not in config (a stray), for a config
section carrying no content, for an unknown artifact type, and for every required
context field left empty. `ClauseReferenceLayer` already enforces required clauses per
type. Rebuilding any of it here would give the operator two error surfaces that can
disagree about the same document.

**The Governor's reply is an AGR envelope, not prose.** The parent spec's wording
implies `ImportParser` reads drafted section text. The repo owner confirmed on
2026-08-03 that the chat returns a full AGR JSON blob, typically fenced and wrapped in
conversational text. This matches `CLAUDE.md`, which describes the operator as saving
"the AGR JSON from the Governor chat," and matches GUI-2's `payload.md`, which teaches
the payload-assembling agent to emit the envelope. Import is therefore a recovery
problem, not a parsing problem: get a valid AGR out of a chat paste and report what
transport did to it.

## Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | Both components live in `Registrar.Core`; no new project | Import needs only `ConfigBundle`, which Core already owns. Nothing here needs `Registrar.Validation`, so the GUI-3 boundary holds: Core stays pure, `Pipeline` composes. |
| 2 | Import reports only what no existing mechanism can see | Its three findings are transport facts. Everything structural or governing is already covered by `AuthoringGap` and the validation layers. |
| 3 | Import populates `AgrDraft` directly from `JsonNode`, never through `AgrDocument` | `AgrParser.Parse` throws on missing required members by design. Routing import through it would make a paste all-or-nothing. `AgrDraft` is nullable-everything precisely so a partial artifact can open, and absent fields then surface as existing `AuthoringGap`s. |
| 4 | Prohibited characters are reported, and repaired only on explicit operator action | Ratified by the repo owner on 2026-08-03. Silent repair would mean the engine altering Governor-supplied text without record, and the operator would never learn their transport is damaging content. Outright refusal costs a full regeneration round trip for one stray character. `CLAUDE.md` calls the operator checkpoint a governance control, not friction to be automated away. |
| 5 | `ImportFinding` is its own type, not a `Check` | Same reasoning GUI-3 applied to `AuthoringGap`: no `checkId`, `ruleSource`, or `errorCategory` applies to "this paste was truncated," and forcing one would dress a transport problem as a governance violation. |
| 6 | The repair map is config, not code | Rule 3. `CharacterPolicy` keeps its allowed ranges in code because they are fixed by ORG-MST-SPEC-001 at ADR level. A transport-repair table is a different kind of fact: when a chat surface introduces a new substitution, that should be a config release. |

## Architecture

```
Registrar.Core  + GovernorBrief
                + ImportParser, ImportResult, ImportFinding, ImportFindingKind
                ~ DrafterProjection  (per-type body extracted, output unchanged)
                ~ ContentLint        (gains TransportRepairs)

config          ~ style-rules.json   (gains contentLint.transportRepairs)
```

No project references change. No validation rule changes.

## GovernorBrief

`DrafterProjection.Render` loops over the in-scope types and writes a block per type:
heading, classification and layer, ordered section titles, required clauses, required
placeholder tokens, and the radio-option instruction where applicable. That loop body
becomes a reusable renderer.

```
GovernorBrief.For(ArtifactTypeConfig type, ConfigBundle config) -> string
```

`DrafterProjection` then composes its per-type blocks by calling it. The brief a
technician copies to the clipboard and the knowledge the drafting agent reads are the
same code path, so they cannot drift.

Types outside Governor scope are not brief-able. `For` throws
`ArgumentException` for a type whose `GovernorScope` is false or whose section schema
status is not `defined`, because a caller asking for a brief for SCL or PDX has made a
programming error rather than encountered a runtime condition. The GUI only offers
types from the same in-scope filter `DrafterProjection` uses.

Because `GeneratedKnowledgeIsCurrentTests` byte-compares the committed
`docs/governor/drafter.md` against freshly rendered output, it guards this refactor at
no additional cost: any behavioral change to the extracted renderer fails that test.

## ImportParser

```
ImportParser.Parse(string pastedText, ConfigBundle config)
    -> ImportResult

ImportResult
  Draft     AgrDraft?                     null only when no JSON could be recovered
  Findings  IReadOnlyList<ImportFinding>  empty when the paste was clean

ImportFinding
  JsonPath  string?    null for findings about the paste as a whole
  Message   string
  Kind      ImportFindingKind

ImportFindingKind
  NoJsonFound | Malformed | Truncated | ProhibitedCharacter | Unreadable
```

`Unreadable` was added at the merge gate. See "A member that will not deserialize"
below.

`Parse` never throws. Every failure is a finding.

### Extraction

The paste is a chat reply: conversational text, usually one fenced code block, sometimes
several, sometimes none.

1. Collect candidates: every fenced block (```` ```json ```` preferred, then any fence),
   then the outermost balanced-brace span in the raw text.
2. Attempt each candidate as JSON, largest first.
3. The first candidate that parses wins.
4. If no candidate parses, return `Draft = null` with one finding. `Kind` is
   `NoJsonFound` when no candidate contained a brace at all, `Malformed` when a
   candidate existed but would not parse, and `Truncated` when a candidate has
   unbalanced braces or brackets, which is the signature of a chat reply cut off by a
   length limit.

Largest-first matters because a Governor reply sometimes fences a short illustrative
fragment alongside the full envelope, and the envelope is the larger one.

### Tolerant population

Read the recovered `JsonNode` into `AgrDraft` property by property. A missing or
wrong-typed node leaves that property null rather than throwing. `Sections`,
`PlaceholderMap`, and the normalized arrays default to empty rather than null, matching
how `AgrDraft` is constructed elsewhere.

Absent fields produce no import finding. They surface through
`AgrAuthoring.ToDocument`, which already reports each one as an `AuthoringGap` carrying
the correct jsonPath. That is the mechanism the editor's validation rail is built on,
and import feeding it is the point.

### A member that will not deserialize

Corrected at the merge gate. "A missing or wrong-typed node leaves that property null
rather than throwing" is right, but leaving it null in silence is not. `AgrSection.Title`,
`AgrSection.Blocks`, `AgrBlock.BlockType` and every member of `AgrQuestion` are `required`,
`System.Text.Json` throws for a missing required member, and list deserialization is
atomic. One block missing a `blockType` therefore discarded every drafted section, and the
operator was then shown an `AuthoringGap` per section reading "has no content yet", which
is false and whose implied remediation is to redraft a body the Governor had supplied.

A member that was present and could not be read now records an `Unreadable` finding at
that member's own pointer, carrying the deserializer's message, which names the property
at fault. Absent members stay silent, unchanged, because `AuthoringGap` reports those.
`Unreadable` is its own kind rather than a reuse of `Malformed`, which means the whole
paste could not be read and would be ambiguous on a result that also carries a draft.

Recovering the readable elements of a list and dropping only the bad one is better and is
tracked as follow-up debt. It is not needed for the result to be honest.

Population takes no schema argument. `AgrDraft` has fixed properties, so the schema's
required list drives nothing here, and a parameter that exists without a use is a
dependency the next reader has to disprove. Envelope completeness is reported by
`AuthoringGap`, which derives it from config rather than from the schema.

### Character scan

Walk every string in the recovered payload, object keys as well as values, in document
order, and scan each with
`CharacterLint.Scan`, the shared helper `InputHygieneLayer` (QA-ENG-05) and
`PostRenderScan` (QA-SF-08) already use for this policy. Emit one `ProhibitedCharacter`
finding per offending string, carrying the RFC 6901 jsonPath of that string and a message
built by `CharacterLint.FormatActual`, which names each code point with its friendly
label and position, caps the list at five, and appends "and N more".

One finding per string rather than per character: a drafted paragraph carrying fifteen
curly quotes is one problem an operator fixes once, not fifteen list entries, and the two
sibling layers enforcing the same policy already report one finding per field. The
message also states whether repair is available for all, none, or some of the offenses,
so an operator is never told repair will clear a problem it will not clear.

That statement is about repair-mapping availability, which is what the scan can decide
from a single string. `Normalize` applies one further gate the scan cannot foresee: it
refuses the whole document when two repaired keys would collide, because performing a
lossy repair is worse than performing none. So a finding can truthfully report that a
mapping exists and repair can still be refused. Detecting a collision at scan time would
need cross-key analysis the scan does not do; the gap is recorded in `docs/tech-debt.md`
rather than papered over here.

This is the one scan no existing layer performs. `InputHygieneLayer` (QA-ENG-05) covers
`context.owner`, `audience`, `preparedBy`, `client`, `engagement`,
`renderingOptions.packageName`, and every `placeholderMap` value. It does not cover
section block text, which is caught only post-render by QA-SF-08. A smart quote inside
a drafted paragraph therefore reaches the operator as a failed build long after the
paste, when the cheapest moment to fix it was the paste itself.

Keys are scanned for the same reason, corrected at the merge gate. `Normalize` has always
repaired keys, and `InputHygieneLayer` lints `placeholderMap` values only, so a prohibited
character in a `placeholderMap` key was covered by nothing and surfaced as a QA-ENG-07
build failure. It also made the collision refusal below unforeseeable: an operator could
be told repair was available for a character in a value and get their paste back unchanged
because two keys nothing had reported would have collided. A key offense carries the
pointer the value would use, since that pointer already names the key, and the message
says which of the two is at fault.

The scan detects. It never modifies.

### Normalization

```
ImportParser.Normalize(string pastedText, ConfigBundle config) -> NormalizeResult

NormalizeResult
  Text     string   repaired AGR JSON, ready to hand back to Parse
  Repaired int      how many characters were substituted
```

Pure, and separate from `Parse` so that repair is always a deliberate act. GUI-8 calls
it when the operator chooses to; nothing calls it automatically.

Substitutions come from `contentLint.transportRepairs` in `style-rules.json`, a map
from prohibited codepoint to replacement string. A prohibited character with no entry
is left exactly as it is and continues to be reported. The engine never guesses at a
replacement.

**`Normalize` repairs string values inside the recovered payload, never the raw paste
text.** It runs the same extraction `Parse` does, walks the `JsonNode`, substitutes
inside each string value, and re-serializes with `AgrSerializer.Options`. Repairing raw
text would corrupt the document: the map sends U+201D to `"`, and applying that to raw
JSON turns a curly quote inside a drafted sentence into an unescaped delimiter, making
a parseable paste unparseable. Re-serializing from the node escapes the replacement
correctly, so the output is always valid JSON.

The returned `Text` is therefore the AGR envelope alone, with the conversational
wrapper dropped. That is what GUI-8 wants: the operator clicks repair, the paste box
holds clean JSON, and a second `Parse` reports zero `ProhibitedCharacter` findings for
every offense that was mapped.

When extraction finds nothing to parse, `Normalize` returns the input unchanged with
`Repaired = 0`. It cannot repair what it cannot read.

### The one case Normalize deliberately cannot fix

If a chat surface smart-quotes the JSON *delimiters* rather than the prose inside them,
the paste will not parse at all and there is no node to walk. Rather than attempt a
raw-text repair that would have to distinguish a delimiter from an apostrophe inside a
sentence, the `Malformed` finding says so directly, and its message tells the operator
to ask the Governor to resend inside a code fence. Guessing at JSON structure is
exactly the kind of inference an engine that never trusts the Governor should refuse to
make.

Reaching that finding requires `JsonCandidates.Collect` to say where a candidate came
from. `Parse` gates out a candidate carrying no ASCII quotation mark, because
`JsonCandidates` anchors its fallback span on the first brace anywhere in the reply and
prose that merely mentions one would otherwise report as a truncated AGR. A fenced
envelope whose delimiters were smart-quoted has no ASCII quotation mark either, so the
gate swallowed exactly the case this section describes and reported `NoJsonFound` with an
instruction to include a code block the paste already had. The gate now applies to the
raw-text fallback alone: a fence is by construction an attempt at JSON.

## Config change

`style-rules.json` gains one live key under `contentLint`:

```json
"transportRepairs": {
  "$comment": "READ BY THE ENGINE, unlike the documentary prohibitedCharacters block above. Maps a codepoint that chat surfaces substitute into its approved replacement. ImportParser.Normalize applies these only when the operator explicitly asks. A prohibited codepoint absent from this map is reported and never replaced.",
  "U+2018": "'",
  "U+2019": "'",
  "U+201C": "\"",
  "U+201D": "\"",
  "U+2013": "-",
  "U+2014": "-",
  "U+2026": "...",
  "U+00A0": " ",
  "U+00AD": ""
}
```

Keys are `U+XXXX` strings rather than raw characters so the file stays readable and so a
repair entry cannot itself be mangled by an editor. `ConfigLoader.Load` parses them to
integers at load and fails closed on a malformed key, and `ConfigBundle` carries the
parsed map. That is why `Normalize` takes `ConfigBundle` rather than `ContentLint`: both
entry points read an already-parsed map, so neither can throw for a config defect that
should have failed the release. The literal `$comment` is the one key skipped; a key that
merely starts with `$` is a broken entry, not an annotation.

The `$comment` is load-bearing. `contentLint.prohibitedCharacters` immediately above it
is explicitly documentary, with the live policy in `CharacterPolicy`. A live key next to
a documentary one is exactly the ambiguity that misleads someone later, so the
distinction is stated in both places: a matching note is added to the
`prohibitedCharacters` comment pointing at `transportRepairs` as the live sibling.

## Error handling

`Parse` returns findings and never throws. `Normalize` returns a result and never
throws. `GovernorBrief.For` throws only for a caller error, as described above.

A paste that recovers a draft but carries findings still returns the draft. Importing
with known problems is allowed by the parent design, because the editor keeps showing
them and Build stays blocked until they are resolved.

## Testing

**GovernorBrief.** One test per in-scope type asserting the brief contains that type's
exact ordered section titles, its required clause IDs, and its required placeholder
tokens. One test asserting the radio-option instruction appears for TCL and not for
AUP. One asserting `For` throws for SCL. `GeneratedKnowledgeIsCurrentTests` continues
to pass unchanged, which is the guard that the extraction was faithful.

**Extraction.** Six cases: a bare JSON paste, a single fenced block, a fenced block
wrapped in conversational text, two fenced blocks where the envelope is the larger, a
truncated reply with unbalanced braces, and a reply with no JSON at all. Each asserts
both the recovered draft and the expected `ImportFindingKind`.

**Tolerant population.** A paste missing `artifactIdentity` entirely still returns a
draft, with the remaining fields populated. A companion test runs
`AgrAuthoring.ToDocument` on that draft and asserts the missing fields come back as
`AuthoringGap`s, proving the two halves compose rather than each half in isolation.

**Character scan.** A paste carrying U+2019 inside a section block's text yields exactly
one `ProhibitedCharacter` finding with the jsonPath of that block's text. This is the
case no existing layer catches, and it is the reason this component exists. A second
test asserts a clean fixture yields zero findings.

**Normalize.** Repairs every mapped codepoint; leaves an unmapped prohibited codepoint
untouched while `Parse` still reports it; is idempotent, so normalizing twice equals
normalizing once. One test asserts `Normalize` output re-parsed by `Parse` yields zero
`ProhibitedCharacter` findings for a paste whose only offenses were mapped. One test
guards the defect this design was corrected for: a paste carrying U+201D *inside* a
section block's drafted text normalizes to output that is still valid JSON and still
parses, proving the repair happens at the node level and not on raw text. One asserts
an unreadable paste returns its input unchanged with `Repaired = 0`.

**Fixture round trip.** For every committed fixture: serialize it with `AgrSerializer`,
wrap the result in a synthetic chat reply (prose, fence, prose), `Parse` it, and assert
the resulting draft equals `AgrAuthoring.FromDocument` of the original. This reuses the
whole fixture corpus to prove extraction and population against every shape the schema
allows, for almost no effort, and mirrors the round-trip test that caught four data-loss
defects in GUI-3.

## Out of scope

- Any UI. `Registrar.App` does not exist until GUI-6, and the Import screen is GUI-8.
- Clipboard access. `GovernorBrief.For` returns a string; who copies it is GUI-8's
  problem.
- Changing any validation rule, `ConfigBundle.Hashes`, or the AGR schema.
- Re-deriving anything `AuthoringGap` or the validation layers already report.
