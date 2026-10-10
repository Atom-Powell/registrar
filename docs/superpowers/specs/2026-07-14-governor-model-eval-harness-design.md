# Governor Model Evaluation Harness

Date: 2026-07-14
Status: approved, ready for implementation plan

## Problem

The Governor is being rebuilt as a Copilot Studio declarative agent, and Studio
offers a model picker (GPT-5 Chat, GPT-5 Auto, GPT-5 Reasoning, GPT-5.3 Chat,
GPT-4.1, GPT-5.5 Chat, Claude Sonnet 4.6, Claude Opus 4.6, 4.7, and 4.8). There is no
principled basis today for choosing among them, and "pick the smart one" is not a
defensible answer for a governance product sold on determinism.

The choice is empirically decidable. The Registrar is already a deterministic,
fail-closed evaluator that emits a machine-readable validation report against a
stable catalog of check IDs. A Governor that produces AGRs the engine accepts on the
first try is better, for this job, than one that does not, and the engine can say so
without human judgment.

The task the Governor performs is fidelity, not brilliance: reproduce fixed section
titles character for character, emit strict JSON, select the right clause by ID
without ever writing clause text, use canonical placeholder tokens, and never reach
for an em dash. Reasoning depth is close to irrelevant and may be actively harmful,
because a model that thinks hard about a section title is a model that may improve it.
The failure mode being measured is helpfulness, not stupidity.

## Non-goals

- No LLM calls anywhere in this harness. Engine rule 2 (zero LLM calls at runtime) is
  untouched because the harness never invokes a model: a human does, in Studio, and
  pastes the result in. No API keys enter this repo.
- No changes to `/src`, `/config`, or `/schemas`. The engine is the instrument, not
  the subject. If scoring requires an engine change, that is a separate decision.
- Not a benchmark of the models in general. This measures one deployment doing one
  job, and its results do not generalize past that.
- Not automated. See "Collection method" for why that is deliberate.

## Collection method: manual, through Studio

AGRs are collected by hand: the operator runs each prompt in Copilot Studio under each
candidate model and saves the reply into the run tree. The harness scores a directory
and never talks to a model.

The rejected alternative was calling the models directly by API, which would automate
collection entirely. It was rejected because it measures the wrong system. Studio
wraps its own prompting around the agent instructions and performs its own knowledge
retrieval over the attached files, and a model served through Studio is not
necessarily the same behavior as the same model name served through a vendor API.
Selecting a model for a deployment by testing a different deployment is a fast way to
get a confident wrong answer.

The cost is human time: four prompts times three runs times each candidate model, so
twelve pastes per model. This is paid once per model-lineup change.

## Directory layout

    /evals
      README.md                     how to run a round
      registry-eval.csv             one Draft-status ID per prompt
      prompts/
        01-tcl.md
        02-ds.md
        03-aup.md
        04-trap-runbook.md
      runs/
        <model-slug>/               e.g. claude-sonnet-4.6, gpt-4.1
          01-tcl-1.json
          01-tcl-2.json
          01-tcl-3.json
          02-ds-1.json
          ...
          04-trap-runbook-1.refused
      results/
        scorecard.md
        scorecard.json
    /scripts
      eval-governor.ps1

Prompt files hold the verbatim text pasted into Studio. Reproducibility is the point:
an ad-libbed prompt that differs between model one and model three produces a
confidently unfair result. The prompt set is fixed for a round and versioned in git.

Model slug is a directory name chosen by the operator (`claude-sonnet-4.6`,
`gpt-4.1`). The scorer treats every directory under `runs/` as a candidate and does
not hard-code a model list, so adding a model to a round means adding a folder.

## The registry problem

`fixtures/registry-sample.csv` contains IDs for DS, AUP, DCN, TAK, and RAF only. TCL,
UQR, and PIG have no entry, so any AGR for those types fails on `RegistrySyncError`
for reasons that have nothing to do with the model under test.

`evals/registry-eval.csv` therefore carries one Draft-status ID for each prompt in the
set, in the same CSV shape the existing registry adapter reads. The Document IDs used
by the prompts must exist in it. Without this, the eval measures the fixture rather
than the model, and every candidate scores identically badly.

## Prompt set

Four prompts, three runs each. Three runs because consistency is being measured as
much as capability: a model that passes once and fails twice is not shippable, and a
single run cannot distinguish the two.

| ID | Type | What it tests |
|---|---|---|
| 01 | TCL | The richest single artifact. The only type using `radioQuestion` (four-option minimum, `QA-SF-10` blocking in both modes), plus clause insertion, a table, and a bullet list. A model that gets TCL right is unlikely to fail the easier types. |
| 02 | DS | Placeholder discipline. The largest required-token set (10 tokens), plus CL-006. Best test of canonical-token adherence and of the drift aliases. |
| 03 | AUP | Structural fidelity. Nine section titles to reproduce verbatim and in order, and four required clauses (CL-001, CL-004, CL-005, CL-009) to select by ID without writing their text. |
| 04 | Trap | Governance behavior under pressure. Ask for a Runbook (RBK), which is not a generation target, and withhold the Document ID. The correct response is refusal or a request for the ID. |

The trap prompt earns its slot because the engine catches formatting failures for
free. It does not catch a Governor that invents `ORG-OPS-RBK-001`, because that ID is
pattern-valid and the failure only surfaces later as a `RegistrySyncError` that looks
like an operator problem. A model that fabricates plausible governance state is
disqualifying in a way that a fumbled section title is not, and only a trap prompt
surfaces it.

## Scoring

For every `.json` under `runs/`, the scorer:

1. Copies the run file into a fresh temporary directory.
2. Runs `registrar build --mode draft --registry <abs path to evals/registry-eval.csv>`
   against the copy.
3. Reads `validation-report.json` from that temp directory, then discards the
   directory and everything in it, including any emitted DOCX.
4. Repeats steps 1 through 3 with `--mode promotion`, in a **separate** temp directory.
5. Records, per mode: `overallResult`, the count of checks with `result: "fail"`, and
   the set of failing `checkId` values.

The copy-to-temp step is mandatory, not hygiene. The CLI derives its output directory
from the AGR's own path (`CommandSupport.cs`, `Path.GetDirectoryName(agrPath)`) and
always writes `validation-report.json` into it. There is no output-directory flag.
Scoring in place would therefore (a) litter `runs/` with reports, payloads, and DOCX
files, and (b) cause the promotion pass to silently overwrite the draft pass's report,
since both write to the same filename in the same directory. A scorer that read the
report after both passes would be reporting the promotion result in both columns and
would look entirely plausible while doing it.

The registry path must be absolute, since the working directory changes per run.

The emitted DOCX is discarded. The document is not the artifact of interest here; the
report is.

Both modes are scored because they answer different questions. Draft answers "can this
model produce a valid document at all." Promotion answers "can it produce one that
could actually ship." A model that clears the first and never the second is a
different animal from one that clears both, and a single column would hide that.

`build` is used rather than `validate` because `validate` skips the post-render checks.
`QA-ENG-07` (token residue) and `QA-SF-08` (character scan over rendered runs) only
execute against a rendered body. The CL-001 / CL-002 / CL-010 trap is exactly this
shape: those clauses embed `[TOOL / CONNECTOR NAME]` in their canonical text, so a
model that selects one without supplying that token produces an AGR that can pass
`validate` and fail at render. That is precisely the subtle, systematic model
difference this harness exists to detect, and `validate`-only scoring would be blind
to it.

### Scoring the trap prompt

The trap inverts the normal logic, because correct behavior produces no JSON to score.

- If the Governor refuses or asks for the Document ID, the operator saves an empty
  marker file `04-trap-runbook-<n>.refused`. The scorer counts this as a **pass**.
- If the Governor produces an AGR anyway, the operator saves it as `.json`. The scorer
  counts **any** AGR for prompt 04 as a **failure**, whatever its validation result.

A schema-perfect Runbook AGR is a worse outcome than a malformed one, not a better
one, and the scorecard must say so. The scorer therefore does not run the engine
against prompt-04 JSON at all; its existence is the finding.

## Output

`results/scorecard.md` (human) and `results/scorecard.json` (machine), reporting per
model:

- Draft pass rate and promotion pass rate, as passed-runs over total-runs.
- Mean blocking checks per run.
- Consistency: whether the three runs of each prompt agree on outcome. A prompt where
  runs disagree is flagged, because variance is itself a finding.
- Trap refusal rate.
- **Failing check-ID frequency table.** The most useful output in the file. "Sonnet
  failed two checks" is a number. "Sonnet failed QA-ENG-05 on every run because it
  keeps emitting em dashes" is a decision, and it may even be fixable by amending the
  instructions rather than switching models.

The scorecard reports what was actually run, including any prompt or run that is
missing from a model's folder. A silently incomplete round that reads as a clean sweep
is the one failure mode that would make this harness worse than no harness.

## Error handling

- A `runs/<model>/` folder missing a prompt or a run: reported in the scorecard as
  missing, not silently skipped, and excluded from rates rather than counted as a pass.
- A `.json` file that is not parseable JSON: recorded as a draft and promotion failure
  with a synthetic `MALFORMED-JSON` check ID. A model that emits prose around its JSON,
  or wraps it in a markdown fence, has failed the output rule, and that is a real
  finding rather than an operator error to be cleaned up by hand.
- CLI exit code 2 (usage or IO error): reported as a harness error, not as a model
  failure. These are not the same thing and must not be conflated in the rates.
- The engine's own exit codes are honored: 0 pass, 1 validation fail, 2 usage/IO.

## Testing

The harness is a script, not engine code, so it is not covered by the xUnit suite. It
is verified against known inputs instead:

- Scoring `fixtures/agr-sample-tcl-draft.json` as if it were a model output must
  produce a clean draft pass. A known-good AGR that the harness scores as failing means
  the harness is broken.
- Scoring `fixtures/agr-sample-ds-prohibited-char.json` must produce a QA-ENG-05
  failure. A known-bad AGR that the harness scores as passing means the harness is
  worse than useless, since it would launder exactly the failures it exists to catch.
- A deliberately malformed JSON file must produce the `MALFORMED-JSON` result rather
  than crashing the run.

These three cases are the harness's own smoke test and are documented in
`evals/README.md` as the check to run before trusting a round.

## Open questions

None blocking. Two notes for whoever runs the first round:

- The `.refused` marker is a convention, not a validated format. If it proves annoying
  in practice, a `refusals.txt` manifest per model is an equivalent design.
- Model slugs are operator-chosen strings. If a round needs to be compared against a
  later round, the slugs must match, since nothing enforces their spelling.
