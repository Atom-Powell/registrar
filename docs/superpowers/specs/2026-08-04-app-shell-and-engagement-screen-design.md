# GUI-6: Registrar.App shell and Engagement screen

Date: 2026-08-04
Status: Approved, not yet planned
Roadmap unit: GUI-6 in `docs/ROADMAP.md`
Parent design: `docs/superpowers/specs/2026-08-01-operator-gui-design.md`

## Why this exists

This is the first unit that produces a window. Everything before it was headless
foundation: validation, config-generated agent knowledge, the draft model, the import
parser, the design tokens. GUI-6 proves the platform end to end on the smallest screen
that answers a real question, so that GUI-7, which is the large one, starts from a
shell that is known to work.

The screen answers "what does this client still owe."

## The constraint that shapes everything

**A `net8.0` project cannot reference a `net8.0-windows` project.** `Registrar.Tests`
targets `net8.0`. A WPF host must target `net8.0-windows`. So any logic placed in
`Registrar.App` is unreachable by the existing 555-test suite, which would make the
first screen the first untested thing in the repo.

The design follows from that: everything with behavior lives in `Registrar.Core`, and
`Registrar.App` is glue thin enough that there is nothing in it worth testing. This is
not a workaround. It is the parent spec's own rule, "`Registrar.App` enforces no rules
of its own," enforced by the project graph rather than by discipline.

## Decisions

| # | Decision | Rationale |
|---|---|---|
| 1 | `EngagementScan` lives in `Registrar.Core`, not in the app | The `net8.0` boundary above. It also means the folder-state logic is exercised by the same suite as everything else, against temp folders with known contents. |
| 2 | Ready means the draft converts with zero gaps, not that validation passes | Ratified by the owner on 2026-08-04. It is instant on a folder of any size, needs no registry, and claims only what it can prove: nothing is missing. Build still runs the real layers and can still refuse. |
| 3 | A fourth state, `Unreadable`, exists for a file that cannot be parsed | The parent spec's error-handling principle is that the app never silently degrades a governance control. A file that cannot be read must say so rather than vanish from the list or be silently counted as a Draft. |
| 4 | `ImportParser.Parse` is the reader for `.json` AGRs | Delivered by GUI-4 for chat pastes, but it is precisely a tolerant reader that never throws and returns a draft plus findings. Using `AgrParser.Parse` here would throw on the first malformed file and take the screen down. |
| 5 | One package, two executables | Ratified by the owner on 2026-08-04, closing open item 4 of the parent spec. `registrar.exe` provably cannot run without `config/`, `schemas/` and `assets/` as siblings; shipping the app into that same folder shares them, keeps one payload for `doctor`, and avoids two copies of config that can drift. |
| 6 | A minimal bUnit project ships in this unit | GUI-7 carries three block editors. Building the UI test harness and the editors in the same unit means neither is finished when the other is blocked. One passing test here means GUI-7 inherits infrastructure that works. |

## EngagementScan

```
EngagementScan.Run(
    string folder,
    ConfigBundle config,
    JsonNode agrSchema,
    IClock clock)
  -> IReadOnlyList<EngagementRow>

EngagementRow
  FileName      string
  ArtifactType  string?
  DocumentId    string?
  Client        string?
  State         ArtifactState
  Detail        string       one line, always populated

ArtifactState
  Built | Ready | Draft | Unreadable
```

Non-recursive, matching how the CLI treats a working folder.

### Discovery

Two globs: `*.agrdraft` and `*.json`.

**The `*.json` glob needs filtering that `AgrPicker` does not do.** A folder that has
been built also contains `validation-report.json`, `artifact-manifest.json`,
`generation-log.json` and `promotion-payload.json`. `AgrPicker.Candidates` globs
`*.json` with no filter and therefore already offers those as selectable AGRs in the
CLI menu; that is a pre-existing defect recorded as tech debt rather than fixed here.

The scan excludes a JSON file when either test says it is not an AGR: its name matches
one of those four output names, or its parsed root has no `artifactIdentity` member.
Two tests rather than one because the name check is cheap and obvious while the
structural check is the one that holds when an output file is renamed.

A file that is malformed enough that the structural test cannot run is **kept, not
excluded**, and reaches the reader where it becomes `Unreadable`. Excluding it would
hide a broken AGR from the one screen whose job is to say what is outstanding, and the
cost of being wrong the other way is one noisy row for a file that was never an AGR.

### Reading

- `.json` goes through `ImportParser.Parse`. It never throws. A null draft means
  `Unreadable`.
- `.agrdraft` is deserialized as `AgrDraft` inside a try, because it is the app's own
  format and a corrupt one is a real condition. A failure means `Unreadable`.

### State

For a draft that was read, call `AgrAuthoring.ToDocument`.

- Gaps: `Draft`. `Detail` names how many fields are still empty and the first one, so
  the row is actionable without opening the artifact.
- No gaps: the document is complete, so its `renderingOptions.outputFileName` is known.
  If a file of that name exists in the folder, `Built`; otherwise `Ready`.

Deriving the expected filename rather than pattern-matching the DOCX means the
correlation uses the same rule the renderer uses, so a naming change in config cannot
put the screen and the output out of step.

An incomplete draft cannot be `Built`, because a document that never converted was
never rendered. That falls out of the ordering rather than needing a special case.

## The WebView2 user-data folder

The parent spec records the trap: WebView2 defaults its user-data folder beside the
exe, so an app extracted somewhere read-only or run from a network path fails to start
with a blank window.

The fix splits by testability. `WebViewEnvironment.UserDataFolder(string appDataRoot)`
in `Registrar.Core` computes the path under `%LOCALAPPDATA%` and is unit tested. The
single line that sets `WEBVIEW2_USER_DATA_FOLDER` before the view initializes lives in
`Registrar.App`, where there is nothing left to get wrong.

## Registrar.App

```
src/Registrar.App/            net8.0-windows, WPF, UseWPF
  App.xaml, App.xaml.cs       sets the WebView2 env var, then starts
  MainWindow.xaml             a Window containing one BlazorWebView
  Components/Routes.razor     one route
  Components/Engagement.razor renders EngagementScan output
  wwwroot/index.html          references the generated stylesheet
```

References `Registrar.Core` and `Registrar.Validation`. Uses
`Microsoft.AspNetCore.Components.WebView.Wpf`. **Not MAUI, and the MAUI workload must
not be pulled in**, per the parent spec.

`wwwroot/index.html` references `config/generated/org-tokens.css`, produced by GUI-5,
so the screen is on brand by construction rather than by a hand-copied palette.

The Engagement screen offers a folder picker, lists the rows, and does nothing else.
No creation, no editing, no build. Those are GUI-7 and GUI-8.

## doctor

`doctor` gains one check: the WebView2 runtime is present, and the user-data folder
resolves to a writable location. It reports through the existing status vocabulary and
lives in `Registrar.Cli`, which is `net8.0` and therefore testable now.

This pays off the parent spec's "also surfaced by `doctor`" and means a technician whose
window comes up blank has a command that says why.

## Packaging

`scripts/publish.ps1` emits one folder carrying both executables beside the shared
`config/`, `schemas/` and `assets/`. Neither binary changes: both already resolve their
inputs by walking up from their own location, which is what makes a single shared
payload work.

## Testing

**In the existing suite, which is where the value is:**

- `EngagementScan` over temp folders: an empty folder, a folder of drafts at each
  state, a folder containing all four output-file names and nothing else, a corrupt
  `.agrdraft`, a `.json` that is valid JSON but not an AGR, and a complete draft whose
  DOCX is present versus absent. Each asserts the row set, not just a count.
- The output-file exclusion gets its own test naming all four files, because that is
  the rule `AgrPicker` gets wrong and the one most likely to regress.
- `WebViewEnvironment.UserDataFolder` for a normal root and for one with a trailing
  separator.
- The `doctor` check with the runtime present, and with the user-data folder pointed at
  an unwritable location.

**One bUnit test**, proving the harness rather than the screen: given a fixed list of
rows, `Engagement.razor` renders one row per artifact with its state visible. It lives
in a new `Registrar.App.Tests` targeting `net8.0-windows`, which is also what makes it
able to reference the app at all.

## Out of scope

- Creating, editing, or building an artifact. GUI-7 and GUI-8.
- The `CLAUDE.md` draft-inversion amendment. GUI-6 reads `.agrdraft` files but never
  writes one, so the inversion the parent spec describes is not yet real. It belongs to
  whichever unit first authors a draft.
- Fixing `AgrPicker`'s missing output-file filter. Adjacent and cheap, but it is the
  CLI's behavior and changing it is not this unit's business.
- Any styling beyond consuming the generated tokens. The screen should look like the
  brand because it reads the brand, not because it was decorated.

## Records to fold in

The roadmap still lists GUI-4 and GUI-5 as not started and `DELIVERED.md` records
neither, because both merged while this unit was being designed. This branch corrects
both, so the record is accurate before a sixth unit lands on top of it.
