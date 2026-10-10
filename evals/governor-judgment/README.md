# Governor judgment eval

Test sets that measure the Governor's judgment in Copilot Studio: does it recommend the
right governed artifacts, refuse the right requests, and avoid fabricating governance
state. Each file is a `Question,Expected response` CSV in the exact dialect Copilot
Studio's evaluation upload accepts (UTF-8 with BOM, CRLF line endings, every field
quoted).

This is distinct from the contract harness under `evals/` proper
(`scripts/eval-governor.ps1`), which scores whether a produced AGR passes the engine's
validation checks. Judgment here, contract there. See `docs/governor-eval-setup.md` for
how to run and score a round, including the refusal-trap metric fix.

## Files

- `01-artifact-orchestration.csv` - complex artifact-orchestration scenarios plus
  refusal traps. Expected responses are grounded in the real ORG catalog: the eight
  generation targets (DS, AUP, UQR, TCL, TAK, RAF, DCN, PIG) by their canonical names,
  and the common non-targets (RBK, SOP, DQR) that the Governor should decline.

## Scoring note

Do not grade the refusal-trap rows with Copilot Studio's default General quality metric.
It treats a correct refusal as a non-answer and fails it. Class each row as
ANSWER-expected or REFUSE-expected and score the traps with the rubric in
`docs/governor-eval-setup.md`.
