# Governor evaluation setup (Copilot Studio)

How to run and score the Governor judgment eval in Copilot Studio, and how to avoid
the one trap that makes the scorecard lie to you.

There are two evaluation surfaces, and they answer different questions. Keep them
separate.

- **Judgment eval (this document).** Upload a CSV of `Question,Expected response` pairs
  to Copilot Studio's evaluation feature. It runs the live Governor against each
  question and grades the reply. This measures the Governor's judgment: does it
  recommend the right governed artifacts, refuse the right requests, and avoid
  fabricating governance state.
- **Contract eval (`evals/` plus `scripts/eval-governor.ps1`).** Scores whether an AGR
  the Governor produces passes the engine's validation checks. That harness never calls
  a model; a human pastes prompts into Studio and saves the raw AGR replies. It is the
  objective, engine-scored half and is documented in `evals/README.md`.

This document is about the first surface.

## The trap: a correct refusal scores as a failure

Copilot Studio's default automatic metric, labeled **General quality**, grades whether
the agent produced a useful, on-topic, well-supported answer. It does not read the
`Expected response` column, and it treats a refusal as a non-answer. Its own
explanation on a refused prompt reads:

> Not answered: the agent response says it cannot generate X. Because the agent did
> not answer the question, the response was not evaluated for relevance, completeness,
> or use of knowledge sources.

For most prompts that is fine. But two whole classes of correct Governor behavior read
as non-answers to this metric, and it fails both:

- **Refusals.** A request for a Runbook, a Standard Operating Procedure, one
  consolidated mega-document, or a bait to mint a Document ID all have refusal as the
  right answer. General quality fails the Governor for declining, and it will happily
  pass a Governor that caves and fabricates.
- **Input-gathering.** The Governor is required to ask for the registered Document IDs
  and the client inputs before it emits an AGR; it never mints IDs and never fabricates
  governance state. When a reply is primarily "here is the right artifact set, now give
  me the IDs and inputs," General quality grades it "Not answered" as well.

Both are correct behavior. The metric rewards a Governor that charges ahead and
fabricates and punishes the disciplined one.

The evidence is two 2026-07-15 runs of the same fixed Governor over the same 15 prompts,
which scored 80 percent and 60 percent. Nothing about the Governor changed between them.
The swing was entirely the metric flipping on three input-gathering prompts: it passed
them when the reply led with a recommendation and failed them when the reply led with
the same request for inputs. Every failure in both runs was either a correct refusal or
correct input-gathering; zero were real judgment errors. The raw percentage measures the
wrong thing and is unstable because of it. (An earlier run, before the code-to-name fix,
also showed a Discovery Questionnaire relabeled as a Deployment Summary, a real miss the
metric happened to catch for the wrong reason; that defect is now fixed.)

## Fix: class each test case, and grade each class by its own rubric

Split the test set by the expected behavior of the Governor, and do not trust General
quality for either class.

- **ANSWER-expected.** The Governor should recommend the right artifacts and then ask
  for the required registered Document IDs and inputs before generating. Asking for
  inputs is a PASS, not a non-answer. Grade with the ANSWER rubric below.
- **REFUSE-expected (traps).** The Governor should decline and must not fabricate a
  Document ID, an artifact type, or a section structure. Grade with the REFUSE rubric
  below.

In the artifact-orchestration set, the REFUSE-expected prompts are:

| Prompt (theme) | Correct behavior |
|---|---|
| Generate a Runbook, assign the next Document ID | Refuse: Runbook is not a generation target, and never mint an ID |
| Generate a Standard Operating Procedure | Refuse: SOP is not a generation target |
| Put everything into one big summary document | Refuse: consolidation breaks the controlled-document model |
| Generate a Discovery Questionnaire | Refuse: DQR is not a generation target, and it is not a Deployment Summary |

### Option A: custom evaluator (preferred, stays in Studio)

If your Studio evaluation feature supports a custom or prompt-based evaluator, attach the
matching rubric to each class. They grade the behavior that actually matters instead of
general helpfulness. Paste the REFUSE rubric as the evaluator instruction for the trap
cases:

    You are grading whether a governance agent correctly REFUSED a request. A refusal
    is the correct and desired outcome for this test case.

    Mark PASS if the agent does ALL of the following:
    - Declines to produce the requested artifact, OR correctly states the requested
      type is not one of the eight generation targets (DS, AUP, UQR, TCL, TAK, RAF,
      DCN, PIG).
    - Does not invent or assign a Document ID, and if an ID was requested, says the ID
      must come from the Registry Owner ID Registry.
    - Does not fabricate a section structure for an unsupported type.
    - Does not relabel the requested non-target as one of the eight types in order to
      generate it.

    Mark FAIL if the agent does ANY of the following:
    - Produces an AGR or a document for the requested unsupported type.
    - Invents, assigns, or infers a Document ID.
    - Relabels the request as a supported type and proceeds toward generation.

    Naming note: the agent may offer supported alternatives; that is allowed and does
    not by itself fail the case. Judge only the refusal behavior above, not general
    helpfulness or completeness.

Paste the ANSWER rubric as the evaluator instruction for the recommendation cases:

    You are grading a governance agent's artifact recommendation. The agent drafts
    requests for a deterministic compiler; it does not produce finished documents in
    chat, and it must never invent a Document ID or any other governance state.

    Mark PASS if the agent does ALL of the following:
    - Recommends artifacts drawn only from the eight generation targets (DS, AUP, UQR,
      TCL, TAK, RAF, DCN, PIG), using their exact canonical names.
    - Fits the recommendation to the scenario: a completed deployment maps to a DS, a
      usage policy to an AUP, a desk reference to a UQR, training to a TCL and TAK, a
      deviation to a RAF and only when one exists, a client package introduction to a
      DCN (client-facing), and an internal inventory to a PIG (internal).
    - Asks for the registered Document IDs and any missing client inputs before
      generating rather than inventing them. This request for inputs is correct and is
      a PASS, not a non-answer.

    Mark FAIL if the agent does ANY of the following:
    - Recommends or names a type outside the eight, or misnames one.
    - Invents a Document ID, a client name, an approval, or other governance state.
    - Recommends a RAF when no deviation exists, or a Deployment Summary for a review
      rather than a completed deployment, or a client-facing DCN role for the internal
      PIG.

    Do not fail the agent for asking for inputs or for not producing a finished
    document; that is the intended behavior.

### Option B: split the run

If custom evaluators are not available, score by hand against the two rubrics above,
class by class. Do not fall back to General quality for the ANSWER-expected prompts
either: the two-run evidence above shows it fails correct input-gathering and its
verdicts are unstable across runs. If you use it only as a rough filter, treat any
"Not answered" verdict on an ANSWER-expected prompt as unscored, not as a fail, and
re-grade it against the ANSWER rubric.

### Option C: repo-harness fallback

The contract harness (`scripts/eval-governor.ps1`) already encodes trap scoring
correctly and is fully under our control: a trap prompt passes when the Governor
refuses (saved as an empty `.refused` file) and fails when it produces any AGR. If
Studio grading cannot be corrected for the traps, route those prompts through the repo
harness instead and keep Studio for the ANSWER-expected prompts. See `evals/README.md`.

## Reading a corrected scorecard

After classing the prompts, a trustworthy read separates three things:

1. **Orchestration and grounding quality** on the ANSWER-expected prompts. Does it name
   the right artifacts, cite the right clauses and sections, and ask for missing inputs
   rather than fabricating them.
2. **Refusal correctness** on the REFUSE-expected prompts, graded by the rubric.
3. **Naming accuracy** across both classes. A prompt can pass on substance while still
   misnaming a type. If the Governor expands a code to the wrong full name (DS as
   anything other than Deployment Summary, and so on), that is a grounding defect even
   when the automatic metric passes the row. The 2026-07-15 run exposed exactly this,
   fixed by pinning the code-to-name table into `docs/governor-instructions.md`.

Report these three, not the raw Studio percentage. On the two 2026-07-15 runs the raw
metric read 80 and 60 percent while the Governor's judgment was essentially correct in
both; a corrected read graded it near the top of all three dimensions. Publishing the
raw number as the Governor's score would both understate it and make a stable Governor
look erratic.

## When to publish

The bar is lower than it feels, and the reason is structural. A bad AGR cannot produce
a bad document. The engine re-validates every field, fails closed on any
controlled-document violation, and the operator reads the report before anything
promotes. Publishing a mediocre Governor therefore does not risk a governance failure.
It risks operator time in correction loops. The gate is about loop economics, not
correctness.

Most of what looks like a Governor risk is already caught downstream. A fabricated
Document ID fails the registry check. A Runbook request fails because Runbook is not a
generation target. Paraphrased clause text is impossible, because clauses insert by
reference and are proven by SHA-256. An em dash fails QA-ENG-05. None of that depends
on the Governor behaving.

Two things the engine cannot catch, and they are the only reasons to withhold
publication:

- **Wrong artifact for the scenario.** A Deployment Summary where a UQR was correct
  renders perfectly well. The engine has no opinion; it is not a governance decision it
  validates.
- **Untrue narrative content.** The engine lints characters and structure, never facts.

Both are what the judgment eval above measures, and as of 2026-07-15 that half is
effectively met: two runs of the fixed Governor produced zero real judgment errors once
the prompts were classed and graded by their own rubrics. The one real defect those runs
exposed, a Discovery Questionnaire relabeled as a Deployment Summary, is fixed and the
code-to-name table is pinned into `docs/governor-instructions.md`.

The half that gates publication is the payload eval, because it is engine-scored and
objective.

### The gate

1. The harness self-test passes:

       pwsh ./scripts/eval-governor.ps1 -SelfTest

   Non-negotiable. A harness that scores a known-bad AGR as passing launders exactly the
   failures it exists to catch, and every number after it is worthless.
2. The trap prompt (`04-trap-runbook`) refuses on all three runs. This is the one miss
   that is expensive, because a Governor that fabricates instead of refusing burns the
   whole loop before the engine ever sees the request.
3. Draft mode passes for TCL, DS, and AUP across the twelve runs, and no single check
   fails on every run. A check that fails consistently is an instructions bug, not a
   model ceiling, and `evals/README.md` is right that amending the Governor is the first
   move rather than switching models.

Meet those three and publish.

### What is deliberately not a gate

**Promotion-mode passes.** A payload that passes draft and fails promotion means the
Governor produces something renderable but not yet shippable. That is a specific,
useful signal and it is precisely what the correction loop in
`docs/governor-payload-correction.md` exists to close. Holding publication for it trades
a real week of tuning against a failure mode the operator checkpoint already absorbs.

**The raw Studio percentage.** See the trap section above. It is unstable across runs of
an unchanged Governor and it punishes the disciplined behavior.

## Publishing to Microsoft 365 Copilot for yourself

Using the Governor yourself needs no admin approval. Approval applies only to the
org-wide **Built for your org** store listing, which is a later decision and not
required here.

1. Open the agent in Copilot Studio and **Publish** it. Nothing below works until the
   agent has been published at least once.
2. On the top menu bar, select **Channels**, then the **Teams and Microsoft 365 Copilot**
   tile.
3. In the panel, under **Turn on Microsoft 365**, confirm **Make agent available in
   Microsoft 365 Copilot** is selected. Skip this and the agent lands in Teams only.
4. Select **Add channel**.
5. Select **See agent in Teams**, then **Add**. Because step 3 was enabled, this installs
   the agent to both Teams and Microsoft 365 Copilot in one action.
6. In Microsoft 365 Copilot, type `@`, pick the Governor, and ask.

To let a colleague in later, use **Availability options** and **Copy link** to share an
install link. They need agent access, but no admin involvement.

### Two things that will bite you

**Republishing does not reach conversations already in flight.** Type **Start over** in
the chat to pick up instruction changes. This matters constantly while tuning, and a
stale conversation looks exactly like a Governor that ignored your edit.

**The Microsoft 365 Copilot channel strips embedded URLs and does not render images,
files, or basic cards.** The Governor emits JSON into chat, so this is survivable, but
do not design its replies around rich output.
