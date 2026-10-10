# Governor model evaluation

Scores Copilot Studio model candidates by how few blocking checks their AGRs incur.
The engine is the scorer. This harness never calls a model: you do, in Studio.

## Running a round

1. Pick a model in Copilot Studio. Choose a slug for it, for example
   `claude-sonnet-4.6` or `gpt-4.1`, and create `evals/runs/<slug>/`.
2. For each prompt in `evals/prompts/`, paste the text below the line into the
   Governor and save the reply as `evals/runs/<slug>/<prompt-id>-<run>.json`.
   Run each prompt three times: runs 1, 2, and 3. Consistency is being measured
   as much as capability, and one run cannot tell a fluke from a habit.
3. Prompt 04 is the trap. See its file: a refusal is a PASS and is recorded as an
   empty `.refused` file, and any AGR it produces is a FAIL.
4. Save the model's raw reply. Do not clean it up. The scorer recovers the payload
   the way the engine does, so a fenced block or a sentence of preamble around the
   JSON is recovered rather than failed: the output rule asks for a fenced `json`
   block, and scoring stricter than the engine would measure the harness instead of
   the model. MALFORMED-JSON means no recoverable payload at all. Hand-cleaning a
   reply still launders the failure you are trying to measure, so do not.
5. Score the round:

       pwsh ./scripts/eval-governor.ps1

   Results land in `evals/results/scorecard.md` and `scorecard.json`.

## Before you trust a round

Run the harness self-test. It scores a known-good AGR, a known-bad one, and a
malformed file, and checks it gets all three right:

    pwsh ./scripts/eval-governor.ps1 -SelfTest

If the self-test fails, the scorecard is not trustworthy: a harness that scores a
known-bad AGR as passing would launder exactly the failures it exists to catch.

## What is scored

Every AGR is built twice, in draft and in promotion mode, against
`evals/registry-eval.csv`. Draft answers "can this model produce a valid document
at all." Promotion answers "can it produce one that could actually ship."

The most useful column in the scorecard is the failing check-ID frequency table.
"This model failed two checks" is a number. "This model fails QA-ENG-05 on every
run because it keeps emitting em dashes" is a decision, and it may be fixable by
amending the Governor instructions rather than switching models.
