# GOV-1 session checklist: rebuild the Governor in Copilot Studio

A single sitting, in order, with the decisions already made so you only have to tick boxes.
Budget about two hours. This is the short route; `docs/copilot-studio-setup.md` is the
reference behind every step, with the links and the reasons.

**What this unit does.** Stands up the Governor on the generated per-role knowledge, pastes
the current instructions, and proves the whole hand-off once: Governor reply, saved file,
the Registrar window. It does not score the Governor (that is GOV-2) and it does not
publish it beyond you.

**Have open:** Copilot Studio, the Registrar window (`runtime\RegistrarApp.cmd` from the
travel pack, or the dev build), and a notes file.

**Where the files are.** The steps below name the repo paths. In the extracted travel pack
the same files are in these places:

| What | In the repo | In the travel pack |
|---|---|---|
| Instructions to paste | `docs\governor-instructions-paste.txt` | `governor\governor-instructions-paste.txt` |
| The four knowledge files | `docs\governor\intake.md`, `drafter.md`, `payload.md`, `correction.md` | `governor\intake.md`, `drafter.md`, `payload.md`, `correction.md` |
| The three AGRs to upload | `fixtures\agr-sample-tcl-draft.json`, `agr-sample-ds-draft.json`, `agr-sample-aup-draft.json` | `governor\sample-agrs\` (the same three files) |
| The hand-off test prompt | `evals\prompts\01-tcl.md` | `eval-harness\evals\prompts\01-tcl.md` |
| The eval registry | `evals\registry-eval.csv` | `eval-harness\evals\registry-eval.csv` |

On a machine with only the travel pack, skip "Before you start": it checks the repo, which
you do not have there, and the pack was built from files that already pass those checks.

---

## Before you start (on this machine, 5 minutes)

- [ ] `git checkout main` and `git pull`. The only untracked file should be
      `fixtures\ORG-GOV-AUP-004.agrdraft`.
- [ ] Run `dotnet run --project src/Registrar.Cli --configuration Release -- emit-knowledge`
      and then `git status`. Nothing under `docs\governor\` may show as changed. If it does,
      the committed copies are stale: stop and tell me.
- [ ] The instructions to paste are `docs\governor-instructions-paste.txt`: 7,768
      characters (7,796 if Studio counts line breaks as two) against Studio's 8,000 limit.
      There is no room to add to it. If you want a new rule, it goes in the knowledge files,
      through config, not here.
- [ ] The four knowledge files in `docs\governor\` were restamped when the ORG branding
      merged (one provenance hash line each). Re-upload all four even if you uploaded them
      before, so Studio serves what the repo says.

## Decisions (defaults are the recommendation; change only with a reason)

| Decision | Default |
|---|---|
| Model | **Claude Sonnet 4.6.** It is the baseline GOV-2 is set up to score, and scores only compare within one model. Needs external models enabled in two admin places (step A). If your tenant cannot use it, pick GPT-4.1 (the Default) and write down that GOV-2 is then a new baseline. |
| One agent or child agents | **One agent.** The four knowledge files are structured so you can split later. |
| Allow ungrounded responses | **On.** The engine re-validates and fails closed, so an ungrounded slip becomes a validation failure, not a document. |
| Web search | **Off.** |
| Content moderation | **Default (High).** |
| Tools, connectors, flows | **None, ever.** Connecting anything that reaches the engine removes the human checkpoint. |
| If a test goes wrong | **Record it, do not edit the instructions yet.** GOV-2 measures this exact setup; changing it mid-sitting muddies the baseline. |

## A. Admin prerequisites (usually an admin; do these first)

- [ ] Dataverse search is on in the target environment (without it you cannot upload files).
- [ ] If using Claude: external models on in the Power Platform admin center **and** the
      Anthropic provider allowed in the Microsoft 365 admin center. Both, or nothing happens.
- [ ] Data policies (DLP) allow at least one channel, or the agent cannot be published.

## B. Create the agent

- [ ] New agent named **ORG Governor**.
- [ ] Description (it drives orchestration): "Drafts Artifact Generation Requests for ORG
      governance deliverables: classifies the engagement, selects clauses by reference,
      and replies with one fenced JSON block for the Registrar to validate."
- [ ] Generative orchestration is on.

## C. Model

- [ ] Overview, **Model**: set Claude Sonnet 4.6 (or your fallback).
- [ ] Write down the model and today's date in your notes.

## D. Instructions

- [ ] Overview, **Instructions**, **Edit**.
- [ ] Open `docs\governor-instructions-paste.txt`, select all, copy, paste. Paste the txt
      file, not the markdown one: Studio drops line breaks inside a paragraph and glues
      words together.
- [ ] Read what landed. There must be no glued words ("clauseselection", "TheRegistrar",
      "anyviolation"), and the artifact-type table must read as one sentence list.
- [ ] Save. If Studio rejects it for length, do not trim by hand: stop and tell me.

## E. Knowledge

- [ ] Upload the four files from `docs\governor\`, with these descriptions:

| File | Description |
|---|---|
| `intake.md` | Every artifact type the program defines, what each is for, and whether the engine can generate it. Consult when deciding which artifacts an engagement needs. |
| `drafter.md` | The exact ordered section titles, required clauses, and required placeholder tokens for every generable artifact type. Consult before writing any artifact content. |
| `payload.md` | The exact shape of an Artifact Generation Request and the rules its identity fields must satisfy. Consult before emitting an AGR. |
| `correction.md` | What every validation check means. Consult when the engine has rejected a request and the failure must be repaired. |

- [ ] Upload `fixtures\agr-sample-tcl-draft.json`, `agr-sample-ds-draft.json` and
      `agr-sample-aup-draft.json`, each described as: "A known-good Artifact Generation
      Request that the engine accepted. Use as a shape reference, never copy its values."
- [ ] Wait until every file shows ready. A file that errors on upload is usually a
      sensitivity label or Dataverse search being off.

## F. Settings

- [ ] Generative AI page: **web search off**, **ungrounded responses on**, moderation default.
- [ ] Confirm there are no tools, topics or connectors on the agent.
- [ ] Do not publish beyond yourself. Publishing is decided after GOV-2 (the gate is in
      `docs\governor-eval-setup.md`).

## G. Smoke tests (Studio test pane; a fresh conversation for each)

- [ ] **The eight names.** Ask it to list the eight artifact types it can generate. The
      names must match the table in the instructions exactly (DS, AUP, UQR, TCL, TAK, RAF,
      DCN, PIG). Invented names, a different set each time, is the failure the 2026-07-15
      eval caught.
- [ ] **Refusals.** Ask for a Runbook. Ask for a Standard Operating Procedure. Both must be
      refused as not generable.
- [ ] **No invented IDs.** Ask it to make up a Document ID. It must refuse and ask for a
      registered one.
- [ ] **It asks before it writes.** Ask for a Deployment Summary with no details. It should
      ask for the Document ID and client inputs, not emit anything.
- [ ] **The real hand-off (the one this unit exists to prove).**
  1. Open `evals\prompts\01-tcl.md`, copy everything below the line, paste it to the Governor.
  2. The reply should be one fenced `json` block and nothing else. Note any extra text.
  3. Click the **copy button on the code block**. Do not select with the mouse: a mouse
     selection copies rendered text and rewrites the bytes.
  4. Save to a scratch folder as `01-tcl.json`.
  5. In the Registrar window: **Choose folder**, pick the scratch folder, select the file.
     Registry: press **Clear** so none is chosen, or choose `evals\registry-eval.csv`
     (it has `ORG-TRN-TCL-701`). Do not use `fixtures\registry-sample.csv` here: a chosen
     registry is checked in draft runs too, and that ID is not in it, so a correct reply
     would fail. Then press **Check**.
  6. Expected: PASS in draft mode. Then press **Build** and **Open document**.
  7. If it fails: press **Copy failures for the Governor**, paste that into the same chat,
     copy the new reply the same way, save over the file, Check again. That is the full
     correction loop, and it is worth running once on purpose.

## H. Write down (this is the deliverable)

- [ ] Model and date; one agent or child agents.
- [ ] Each smoke test: pass or fail, with the reply if it failed.
- [ ] Did any reply contain text outside the fenced block? Did the same prompt behave
      differently on a retry?
- [ ] The hand-off: did Check pass first time? If not, which check IDs failed?
- [ ] Anything you changed from the defaults above, and why.

Then tell me. I will record the model in `docs\governor-eval-setup.md`, remove the
"unobserved hand-off" caveat from `docs\first-artifact-walkthrough.md` if the real
hand-off passed, and mark GOV-1 done in the roadmap. If anything in G failed, we decide
together whether it is an instruction fix (a config release) or a knowledge fix before
GOV-2, rather than editing live.

## Do not

- Add rules, examples or reminders to the instructions field.
- Move directives into knowledge to save space.
- Connect any tool, flow or connector that reaches the engine.
- Write instructions that mention citations or references.
- Score with Studio's General quality metric: it counts correct refusals as failures.

## What comes next

**GOV-2:** run the eval harness against this agent. Twelve replies (four prompts, three
runs each) go into `evals\runs\claude-sonnet-4.6\` (use the folder name for the model you
chose), copied with the code block's copy button, then scored with
`scripts\eval-governor.ps1`. Read `docs\governor-eval-setup.md` first.
