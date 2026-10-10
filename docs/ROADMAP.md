# The Registrar - Roadmap

**This file contains only work that is not yet done.** Shipped work lives in
`docs/DELIVERED.md`. Deferred non-blocking follow-ups live in `docs/tech-debt.md`.
Nothing is recorded in two places.

**This is the second roadmap.** The first one (endpoint adopted 2026-08-01) was closed by
the owner on 2026-10-09, because the operator window it assumed was replaced by a thinner
one and two of its endpoint bullets could no longer be met as written. Everything it
delivered is in `docs/DELIVERED.md`; its text is in git history (`docs/ROADMAP.md` at
commit `d4e046b`). Its two unfinished units, GOV-1 and GOV-2, carry over below with their
IDs unchanged.

## How this file works

It is a plan of record, not a running commentary. Three rules keep it that way:

1. **The endpoint below is fixed.** Reaching it completes this roadmap. A new
   roadmap is written after, not appended to this one.
2. **Unit IDs are permanent.** `GOV-1` means the same thing forever. Units are never
   renumbered, reordered, or reused, because plans, commits, and PRs cite them.
3. **Discovered work is an amendment, not an edit.** Append it to the Amendments
   section with a date and the reason it appeared. Do not insert rows into the table
   above. Adding scope is an owner decision, and the amendment log is the record of
   who decided what and when.

The reason for this discipline: before 2026-08-01 the delivered history and the
remaining work shared the same sections here and in `CLAUDE.md`, so neither list was
readable as a plan, and work units were being invented mid-conversation.

---

## Endpoint

**An ORG technician who has never used the Registrar produces a correct governed
deliverable, unaided.**

Done means all of the following are true:

- They never have to read or edit JSON. They copy the Governor's reply with the code
  block's copy button, save it, and use the window; everything the tool tells them is in
  plain words.
- They cannot produce an invalid controlled deliverable. The engine refuses to write one,
  says in plain words what is wrong and what to fix, and hands them text to send back to
  the Governor.
- The AI agent they consult gives them correct guidance for the artifact they are
  making, because its knowledge is generated from config rather than hand-maintained.
- They can go from "this client needs a Training Checklist" to a built DOCX without
  asking anyone how, shown by someone who has never used the tool doing it while the
  owner watches.

---

## Units

| ID | Unit | Depends on | Status |
|---|---|---|---|
| GOV-1 | Rebuild the Studio Governor on the generated per-role knowledge | none | Not started |
| GOV-2 | Run the eval harness against the Studio Governor and score it | GOV-1 | Not started |
| ACC-1 | Observed first run: a technician who has never used the tool completes the walkthrough while the owner watches, with nothing explained, and what they stumble on is fixed | GOV-1 | Not started |

Each unit gets its own plan in `docs/superpowers/plans/` and ships working, tested
software or a recorded result.

### Notes on specific units

**GOV-1 is a rebuild, not a stand-up.** The Governor already runs in Copilot Studio,
its children split by function, not by artifact type: intake, drafter, payload
assembler, correction handler, and archivist. What changes is where its knowledge
comes from: the retired hand-derived contract sheet was written "as of 2026-07-14"
and had gone stale, so a technician could be confidently told the wrong section
titles. GUI-2 generates one knowledge file per agent role from config, at
`docs/governor/`; GOV-1 is adopting them. This also mitigates the 2026-07-28
paste-corruption failure, since a shorter generated file can be regenerated rather
than repaired.

**GOV-2 has been open since 2026-07-15** and the prior roadmap called it the
highest-value open item. The engine side is done and green; it is gated on real
Copilot Studio replies. Three replies per prompt (DS, AUP, TCL, plus the refusal
trap) into `evals/runs/<model-slug>/`, scored with `scripts/eval-governor.ps1`.

The run folder is `evals/runs/claude-sonnet-4.6/`. That is the model the Governor
has run on since it was stood up and the model it was published on; the earlier
`claude-sonnet-5` scaffold named a model not available in the tenant and has been
removed. Its collection notes also now carry the transcription rule the scaffold
lacked: copy the AGR with the code block's copy button, never by mouse-selecting
the reply, because a mouse selection yields rendered markdown and rewrites the
bytes. That is the same class of failure as the 2026-07-28 paste corruption, and
an eval that measures it would be measuring the operator rather than the model.

**ACC-1 is the test of the word "unaided".** Every earlier check of this tool was done by
the people who built it or by tests. The endpoint is about a person who has not, so the
unit is an observed run, not more documentation. It needs a real technician, the
packaged travel pack on a real workstation, and the Governor from GOV-1 in the loop; the
output is a short list of where they hesitated, and the fixes to it. The walkthrough
(`docs/first-artifact-walkthrough.md`) is the only instruction they get. One step in it,
the Governor handoff, is written from the interface contract rather than an observed run
and says so in the text; GOV-1 removes that caveat.

**Only GOV-1 and GOV-2 remain, and both are gated on Copilot Studio** rather than on
this repo. `docs/copilot-studio-setup.md` is the configuration procedure for GOV-1.

---

## Decisions this endpoint waits on

Only decisions that block a unit. Everything else is in the out-of-scope list.

No decision currently blocks a unit.

### Resolved

| Decision | Outcome |
|---|---|
| A destructive-action color (`--org-danger`) | Ratified `#A3282B` by the owner on 2026-08-03. Warn red is reserved for remove-before-client callouts, so screens needed a different color. Ships in `screen.colors` in `config/style-rules.json`. |
| The operator window's shape | On 2026-10-08 the owner replaced the authoring editor with a one-page window over the same engine (a GUI version of the CLI). Delivered in PRs #78 to #82. |
| The first roadmap's endpoint | On 2026-10-09 the owner closed the first roadmap and adopted the endpoint above. The bullets "never see or edit JSON" and "every rule validates as they type" assumed the authoring editor and were reworded to what the window delivers. |
| Two new screen colors (`surface`, `dangerHover`) | Approved by the owner by merging PR #82. |

### Confirmations wanted, not blocking

- **SCL and PDX framework layers.** The owner ratified eight rows on 2026-08-01; SCL
  and PDX were extended to `Administration` during GUI-1 because they are also
  generable and already carried that value in their fixtures. Nothing was overridden,
  but both are now enforced by a blocking check. Recorded in `CLAUDE.md`.

---

## Explicitly out of scope for this endpoint

Real work, deliberately excluded. Each becomes a candidate for the next roadmap.

| Item | Why it is out |
|---|---|
| **GovLib library normalization tool** | A parallel workstream with its own owner, in flight as of 2026-08-01 (PR #51). Named here so it is visible in the plan of record; it does not share this finish line and its rows are not tracked above. |
| SharePoint ID Registry migration | Retires the interim workbook behind the existing `IIdRegistry` interface. External dependencies outside this endpoint's control. |
| Deliverable inventory reconciliation report | Deferred 2026-07-21 and coupled to the SharePoint migration; a list view covers half of it. |
| DCH custom properties | Owner decision on which Document Control Header fields become DOCX custom properties so a SharePoint library can drive columns. Open since PR #26. |
| Validation history store | The Governor asked for repeat-failure detection and cross-run dataset export. Both need state the engine deliberately lacks. Architecture call, tech-debt item 30. |
| Universal closing elements | "Distribution and Follow-up" and the prepared-by closing line, blocked on ORG-MST-SPEC-001 Section 4.1 content and a documentation-owner ruling. tech-debt item 10. |
| **Paste from Governor** | A button that takes the Governor's reply from the clipboard, recovers the fenced JSON block, and saves it, removing the hand-save step. It changes what the operator sees at the checkpoint, so it needs an owner decision after the plain window has been used. |
| Drag and drop onto the window | Deferred in SW-3: the embedded browser swallows file drops and a path needs a bridge that cannot be verified without a person at the window. **Add a file...** covers it. |
| Delete the dead Core authoring code | The GUI-3 and GUI-4 authoring and import code is unused since the window was replaced. Tech-debt item 78, its own PR. |
| New artifact types VRA and EX | Asked for by the owner on 2026-08-02. Each needs a category and ordered section schemas (rule 6), which are the documentation owner's. |

---

## Amendments

- **2026-10-09** | The first roadmap was closed and this one opened with a reworded
  endpoint (see Resolved). ACC-1 was added because "unaided" had never been tested by
  someone who did not build the tool. GOV-1 and GOV-2 carry over with their IDs | The
  window the first endpoint assumed was replaced on 2026-10-08, and rule 1 of the first
  roadmap says a changed endpoint means a new roadmap | Repo owner (draft for the owner's
  ruling; merging this change is the ruling)
- **2026-10-09** | Removed the out-of-scope item "Line-ending-independent config hashes":
  it was done, by pinning the config and schema files to LF in `.gitattributes` and
  re-pinning the hashes once, rather than by changing how hashing works. See
  `docs/DELIVERED.md` | The fix was small and the roadmap lists only work not yet done |
  Engine
