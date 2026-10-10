# GUI-5: Design system generated from style-rules.json

Date: 2026-08-03
Status: Delivered 2026-08-03 on branch `feat/design-system-generation`
Roadmap unit: GUI-5 in `docs/ROADMAP.md`
Parent design: `docs/superpowers/specs/2026-08-01-operator-gui-design.md`

## Why this exists

The Registrar already produces branded DOCX deliverables from `style-rules.json`. The
operator GUI has to look like those documents, and the only way that stays true is if
both read the same config. A hand-maintained stylesheet would drift from the palette
the documents use, and the drift would be invisible until someone put a screen next to
a deliverable.

GUI-6 cannot start without this. It is the last unit before the application has a
window.

## What the parent spec got right, and one thing it did not

The parent spec calls for `scripts/build-design-tokens.ps1`. That is the wrong shape,
for the same reason GUI-3 found `BuildPipeline` could not live in Core.

A PowerShell script would be a **second reader of `style-rules.json`**, reimplementing
in PowerShell what `ConfigLoader` already does in C#. Two readers of one config file
drift, and rule 3 exists to prevent exactly that. It also could not be exercised by the
xUnit suite or by CI the way the rest of the engine is.

GUI-2 established a better pattern that is now proven in this repo: `registrar
emit-knowledge` generates files from config, the output is committed, and
`GeneratedKnowledgeIsCurrentTests` byte-compares the committed copy against a fresh
render so a config change without a regeneration fails the build. GUI-5 uses that
pattern exactly, as `registrar emit-tokens`.

The owner approved this deviation on 2026-08-03.

## The screen block

Three colors in `visualIdentity` are correct in print and wrong on screen. Measured
rather than assumed:

| Pair | Measured | Threshold | Verdict |
|---|---|---|---|
| `warnRedBorder` `#C0504D` on `warnRedFill` `#FBEEEC` | 4.12:1 | 4.5:1 text | fails |
| `borderNeutral` `#B8C1CC` on white | 1.82:1 | 3:1 control boundary | fails |
| `warnRedBorder` `#C0504D` on white | 4.67:1 | 4.5:1 text | passes AA, fails AAA |

The rest of the palette is fine on screen and is reused unchanged: `primaryBlue`
6.50:1, `headingNavy` 15.27:1, `accentBlue` 5.60:1, `secondaryGray` 6.01:1, all on
white.

So `style-rules.json` gains a `screen` block beside `visualIdentity`, carrying exactly
two new values:

```json
"screen": {
  "$comment": "Screen-only tokens. Documents must never reference these, and the renderer never reads this block: visualIdentity is the document palette. These exist because three visualIdentity colors are correct in print and fail WCAG as interactive UI.",
  "colors": {
    "danger": "#A3282B",
    "border": "#7E8B9C"
  }
}
```

**`danger` `#A3282B`** is a new brand value, approved by the owner on 2026-08-03 as
open item 2 of the parent spec. It is needed because warn red is reserved: `colorUsage`
restricts it to internal notes and remove-before-client callouts, and QA-SF-09 enforces
that meaning. Reusing warn red for a form error would give a technician one color with
two meanings, which is precisely what the reservation prevents. It measures 7.25:1 on
white and 6.40:1 on the existing `warnRedFill`, so it also resolves the warn-red-on-
warn-fill failure. One new value, two problems.

**`border` `#7E8B9C`** replaces `borderNeutral` for interactive control boundaries at
3.47:1. The obvious candidate `#8A96A6` computes to exactly 3.00:1 and was rejected:
landing precisely on a threshold means any rounding or rendering difference fails it.

No other new brand values. Everything else the stylesheet needs is derived from what
`visualIdentity` already carries.

## Generation

```
registrar emit-tokens [--out <file>]
    -> config/generated/org-tokens.css
```

`--out` names the file to write, not a directory. This differs from `emit-knowledge`,
whose `--out` is a directory because it writes several files. As delivered,
`emit-tokens` creates a file with that name and exits 0 when the given path does not
yet exist, indistinguishable from an ordinary filename; against an *existing*
directory it does error, but only by accident, because `File.WriteAllText` throws
`UnauthorizedAccessException` and the command reports it as a generic error rather
than naming the real problem. That gap is recorded as item 50 in `docs/tech-debt.md`.

`TokenProjection` lives in `Registrar.Core` beside the Governor projections, takes the
loaded `ConfigBundle`, and emits CSS custom properties. `EmitTokensCommand` in the CLI
mirrors `EmitKnowledgeCommand`: it loads config, renders, writes.

The output is one `:root` block. No preprocessor, no build step, no framework. A
Blazor component at GUI-6 references `var(--org-primary-blue)` and gets whatever
config says today.

The tokens inside that block are emitted as two labelled groups, screen first and
document second, each sorted ordinally so the render stays byte-stable for the
staleness guard. A single alphabetical run put `--org-border` (3.47:1 on white, the
safe one) and `--org-border-neutral` (1.82:1, the reason the screen block exists) on
consecutive lines under equally official names, with nothing in the file saying which
one a control boundary wants. The group comments carry that guidance. This is
presentational only: both config blocks still feed one flat custom-property namespace
and the collision guard below is unchanged.

```css
/* Generated by registrar emit-tokens. Do not edit by hand: edits are
   overwritten on the next run, and a committed copy that disagrees with
   config fails the build. Source: config/style-rules.json (sha256 ...) */
:root {
  /* Screen tokens. These are the tokens chosen for interactive UI. ... */
  --org-border: #7E8B9C;
  --org-danger: #A3282B;

  /* Document tokens. This is the document palette ... some of these
     fail WCAG as interactive foreground ... */
  --org-accent-blue: #1766C6;
  ...
  --org-primary-blue: #005CB9;
}
```

Token names are derived from the config keys by a single documented rule: camelCase
becomes kebab-case, prefixed `--org-`. `visualIdentity.colors.primaryBlue` becomes
`--org-primary-blue`; `screen.colors.danger` becomes `--org-danger`. Deriving rather
than mapping means a new config color appears in the stylesheet automatically, and no
lookup table can fall out of step.

Generated output lives in `config/generated/` rather than an application folder,
because `Registrar.App` does not exist until GUI-6 and inventing its layout early would
be guessing. GUI-6 references it from there.

The provenance comment carries the SHA-256 of `style-rules.json`, matching how the
generated knowledge documents record their sources.

## Testing

**The regeneration guard.** `GeneratedTokensAreCurrentTests` byte-compares
`config/generated/org-tokens.css` against a fresh render, exactly as
`GeneratedKnowledgeIsCurrentTests` does. A palette change without a regeneration fails
the build, and the failure message names the command to run.

The generated file must be pinned to LF in `.gitattributes` alongside
`docs/governor/*.md`. This is not hypothetical: a byte-compare guard on a generated
file already broke a CI run on this repo, because `core.autocrlf` rewrites the working
copy on a fresh Windows clone and the guard fails there while passing locally.

**The contrast assertions, which are the ones that earn their keep.** A test walks
every screen token against the background it is used on and asserts its WCAG ratio
meets the threshold for its role: 4.5:1 for text, 3:1 for a control boundary. The three
failures above were found by hand, once. A test means the next palette edit cannot
silently ship an inaccessible token.

This mirrors `Every_repaired_code_point_is_actually_prohibited`, which caught real dead
config this week by asserting a property of config rather than of code.

The ratio calculation is the WCAG 2.1 relative-luminance formula and belongs in the
engine, not the test, so that the check and any future tooling share one implementation.

**Token derivation.** One test per naming case: a single word, a camelCase pair, and
the `screen` block reaching the same namespace as `visualIdentity` without collision.

**A collision guard.** `visualIdentity.colors` and `screen.colors` both feed one flat
namespace, so a key added to both would silently produce a duplicate custom property
where the last one wins. The generator fails closed on a duplicate token name rather
than emitting it.

## Out of scope

- Any UI. `Registrar.App` does not exist until GUI-6.
- Typography, spacing, and component styles. This unit ships color tokens, which is
  what GUI-6 needs to render the Engagement screen on brand. Extending the block later
  is a config release, which is the point of generating it.
- Changing `visualIdentity`, any renderer behavior, or any validation rule. The
  document palette is not touched.
- A dark theme. Nothing has asked for one.

## Consequences worth recording

Adding a key to `style-rules.json` re-pins its SHA-256 in every fixture golden and in
all four generated knowledge documents, roughly 42 files. That is expected and was
already paid twice this week. Regenerate in the same commit as the config change so the
churn is paid once.

`screen` is deliberately a sibling of `visualIdentity` rather than nested inside it.
The renderer must never read it, and a reviewer must be able to see that at a glance.
