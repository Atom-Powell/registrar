# Copilot Studio setup checklist (the Governor)

How to stand up the Governor in Microsoft Copilot Studio, in order, with the settings
that matter and the traps that waste an afternoon.

Verified against Microsoft Learn on 2026-08-05. Every product fact below carries a
link. Where the documentation is silent and the number comes from the field instead,
this file says so rather than pretending otherwise.

The Governor owns judgment: findings, risk classification, clause selection, narrative
content, GO/NO-GO. It emits an Artifact Generation Request. It does not render
documents and it must never reach this engine directly.

---

## Part 0: decisions to make before you touch Studio

Two settings interact badly for this specific agent, and one of them has no clean
answer. Decide these first, because they change what you configure later.

- [ ] **Decide the output-format versus grounding trade-off.** See "The JSON trap"
      below. This is the one genuinely hard call in the setup.
- [ ] **Decide whether the four roles are child agents or one agent.** See Part 6. One
      agent is simpler and is the right starting point; the four generated knowledge
      files are structured so either works.
- [ ] **Confirm the tenant may use the models you want.** Claude models are external
      and need admin action in two places, not one. See Part 3.

---

## Part 1: prerequisites and admin settings

These are environment-level and usually need an admin. Doing them after you build the
agent means rebuilding parts of it.

- [ ] **Dataverse search is turned on in the target environment.** File-upload
      knowledge does not work without it, and the failure mode is that you cannot add
      the files at all.
      [Upload files as a knowledge source](https://learn.microsoft.com/en-us/microsoft-copilot-studio/knowledge-add-file-upload)
- [ ] **Confirm Dataverse file storage headroom.** Uploaded knowledge is stored in
      Dataverse and the document count is bounded by your storage allocation, not by a
      fixed file count.
- [ ] **Check data policies (DLP) for the environment.** Data policies decide which
      channels an agent may publish to. If every channel is blocked, the agent cannot
      be published at all, and the error surfaces late.
      [Configure data policies for agents](https://learn.microsoft.com/en-us/microsoft-copilot-studio/admin-data-loss-prevention)
- [ ] **If you want preview or experimental models:** the *Preview and experimental AI
      models* setting must be on for the environment, and *Move data across regions*
      must be on for the cross-geo ones.
- [ ] **If you want Claude models:** turn on external models in the Power Platform
      admin center **and** allow the Anthropic provider in the Microsoft 365 admin
      center. Both are required; one alone does nothing.
      [Select a primary AI model](https://learn.microsoft.com/en-us/microsoft-copilot-studio/authoring-select-agent-model)

**Trap: sensitivity labels block knowledge upload.** Files with encryption, including
sensitivity labels or password protection, cannot be used as uploaded documents. For an
MSP tenant with default labelling this bites immediately. The files this repo asks you
to upload are synthetic and unlabelled; keep them that way.

---

## Part 2: create the agent

- [ ] Create a new agent in the target environment. Name it so an operator can tell it
      from anything else in the tenant: **ORG Governor**.
- [ ] Give it a description that says what it produces, since descriptions drive
      orchestration decisions later.
- [ ] Confirm the agent uses **generative orchestration**. Several settings below exist
      only in that mode, including *Allow ungrounded responses*.
      [Orchestrate agent behavior with generative AI](https://learn.microsoft.com/en-us/microsoft-copilot-studio/advanced-generative-actions)

---

## Part 3: model

Model availability changed materially in 2026. Two models this project may have used
before are now **Retired**: GPT-4o and Claude Sonnet 4.5. A retired model remains usable
for up to one month after retirement, then stops.

- [ ] Set the model on the agent's **Overview** page, **Model** section.
- [ ] Choose a model and record why in `docs/governor-eval-setup.md`, because the eval
      scores are only comparable within one model.

Current status of the relevant options, from the Learn table:

| Model | Category | Status |
|---|---|---|
| GPT-4.1 | General | **Default** |
| GPT-5 Chat | General | GA |
| GPT-5.5 Chat | General | GA |
| Claude Sonnet 4.6 | General | GA (external) |
| Claude Opus 4.6 / 4.7 | Deep | GA (external) |
| GPT-5 Reasoning | Deep | Preview |
| GPT-4o, Claude Sonnet 4.5 | General | **Retired** |

The Governor's work is closer to *Deep* than *General*: it classifies risk, selects
clauses against a table, and decides GO/NO-GO. A Deep model costs more and is slower.
Start on the Default and change it only if the eval shows judgment errors rather than
formatting errors, since a Deep model will not fix a formatting problem.

**Trap: cross-geo processing.** Models marked *cross-geo* may process and store data
outside your geography. For an MSP handling client engagement data, that is a
contractual question before it is a technical one. GPT-4.1 is Default in every listed
region without a cross-geo tag; several Claude options are cross-geo everywhere.

---

## Part 4: instructions

- [ ] Open the agent's **Overview** page, find the **Instructions** section, select
      **Edit**.
- [ ] Paste the whole of `docs/governor-instructions-paste.txt`. It is generated from
      `docs/governor-instructions.md` by `scripts/build-governor-paste.ps1` and is the
      operative block with each paragraph on one physical line. Do not paste the markdown
      file: Studio's editor drops the line breaks inside a paragraph without adding a
      space, so the hard-wrapped text lands with words glued together and the code-to-name
      table collapsed, which hits the control added after the 2026-07-15 eval.
- [ ] After pasting, read the field for glued words ("clauseselection", "TheRegistrar",
      "anyviolation") and confirm the artifact-type table reads as one sentence list.

**The block is 7,768 characters** (7,796 if the editor counts line breaks as two). The
commonly reported limit for this field is 8,000, which leaves roughly **200 characters
of headroom**. Microsoft's own instructions page
does not state a limit, so treat 8,000 as a field observation rather than a documented
guarantee.
[Write agent instructions](https://learn.microsoft.com/en-us/microsoft-copilot-studio/authoring-instructions)

Practical consequences of that headroom:

- [ ] Do not add examples, reminders, or "one more rule" to the instructions field.
      There is no room. New rules go in the knowledge files, which are generated from
      config by `registrar emit-knowledge`.
- [ ] If you must add to the instruction block, remove something first and re-measure.

**Trap: do not move instructions into knowledge to dodge the limit.** Knowledge content
passes through cross-prompt-injection classifiers, and instruction text placed there can
behave unpredictably. Reference *data* belongs in knowledge; *directives* belong in the
instructions field. This repo's split already follows that line: the catalogues,
schemas, and check definitions are data.

---

## Part 5: knowledge

Both file types this project needs are supported: **.md** is supported under Text, and
**.json** is supported directly. An agent can hold up to 500 files, each up to 512 MB,
so size is not a constraint here.

- [ ] Add knowledge from the **Overview** or **Knowledge** page, via **Add knowledge**.
- [ ] Upload the four generated role files from `docs/governor/`:
      - `intake.md` - the artifact catalogue
      - `drafter.md` - artifact structure and required content
      - `payload.md` - the AGR envelope and identity rules
      - `correction.md` - the validation check catalogue
- [ ] Upload three known-good AGR fixtures, which teach the shape better than prose:
      - `fixtures/agr-sample-tcl-draft.json`
      - `fixtures/agr-sample-ds-draft.json`
      - `fixtures/agr-sample-aup-draft.json`
- [ ] **Write a real description for each file.** The description is not decoration:
      generative orchestration uses names and descriptions to decide what to consult.
      A file described as "documentation" will be consulted at random.

Suggested descriptions, which you can paste:

| File | Description |
|---|---|
| `intake.md` | Every artifact type the program defines, what each is for, and whether the engine can generate it. Consult when deciding which artifacts an engagement needs. |
| `drafter.md` | The exact ordered section titles, required clauses, and required placeholder tokens for every generable artifact type. Consult before writing any artifact content. |
| `payload.md` | The exact shape of an Artifact Generation Request and the rules its identity fields must satisfy. Consult before emitting an AGR. |
| `correction.md` | What every validation check means. Consult when the engine has rejected a request and the failure must be repaired. |
| `agr-sample-*.json` | A known-good Artifact Generation Request that the engine accepted. Use as a shape reference, never copy its values. |

**Regeneration rule.** The four role files are generated. When config changes, re-run
`registrar emit-knowledge`, and re-upload. A committed copy that disagrees with config
fails the build, so the repo will tell you the files are stale, but **Studio will not**:
it keeps serving whatever you last uploaded.

- [ ] Add a step to your config-release routine: regenerate, commit, re-upload.

---

## Part 6: child agents, if you use them

The four knowledge files are written per role (intake, drafter, payload, correction),
which maps onto Copilot Studio child agents. Whether to actually split is a judgment
call.

**Start with one agent.** Child agents add an orchestration hop and therefore latency,
and the documented reason to split is that the parent starts confusing 30 to 40 similar
choices. The Governor has four knowledge files and no tools. It is nowhere near that.
[Add other agents overview](https://learn.microsoft.com/en-us/microsoft-copilot-studio/authoring-add-other-agents)

If you do split, per role:

- [ ] **Agents** page, **Add an agent**, **New child agent**.
- [ ] Give it a distinctive name and a description of its purpose, since by default the
      parent chooses agents by description.
- [ ] Write its instructions. Reference tools, agents, or variables with a slash (`/`).
- [ ] Under **Knowledge**, add only that role's file, so the drafter is not reading the
      check catalogue.
- [ ] Leave **Enabled** on, and **Save**.
      [Add a child agent](https://learn.microsoft.com/en-us/microsoft-copilot-studio/add-agent-child-agent)

**Trap: a spurious extra message.** When a parent relies entirely on child agents, with
no topics or knowledge of its own, and the child's **After running** behavior is
**Don't respond**, the platform may emit a system-generated `explanation_of_tool_call`
message after the child replies. It is produced by the orchestration runtime, not by
your configuration. For an agent whose entire output contract is "one fenced JSON block
and nothing else," that extra message is not cosmetic. If you split, test for it
explicitly. In practice the engine recovers from it, since `JsonCandidates` pulls the
fenced block out of a longer reply, but do not rely on that: the contract is the fence.

---

## Part 7: Generative AI settings, and the JSON trap

This is where the setup can quietly break, so read this part before changing anything.

### The JSON trap

The Governor's output rule is: reply with the AGR as a single JSON object inside one
markdown code fence tagged `json`, and nothing else, no preamble and nothing after the
closing fence.

Microsoft's guidance says, in the same breath, that instructions forcing a rigid output
format (their example is literally "respond only in JSON") can suppress the citation
markers the agent needs, and that when **Allow ungrounded responses** is off, an answer
without an in-text citation is **withheld entirely**.

So the two things this project wants are in tension:

- Governance wants grounding: the Governor should answer from the knowledge files, not
  from model memory.
- The engine contract wants a fenced JSON payload and nothing else: prose around it is
  recoverable, but prose instead of it is not a parseable AGR.

Turning grounding fully on and demanding JSON-only can produce an agent that intermittently
returns nothing at all, and the intermittency is the worst part: the same prompt may work
on retry.

**Recommended resolution**, and record whichever you choose:

- [ ] Leave **Allow ungrounded responses** **on**, and rely on the engine rather than the
      platform for grounding. This is defensible for this specific agent in a way it
      would not be for a general knowledge agent: the Registrar re-validates every field,
      inserts clause text by reference from config rather than from the reply, and fails
      closed. An ungrounded hallucination in an AGR does not become a document; it
      becomes a validation failure the operator sees.
- [ ] Then verify grounding by eval rather than by setting: the contract eval in
      `evals/` scores whether real AGRs pass the engine's checks, which is a stronger
      signal than whether the platform saw a citation marker.

The alternative, turning ungrounded responses off, is the more orthodox governance
choice and will fight the JSON-only contract. If you take it, expect withheld replies
and test heavily before trusting it.
[Knowledge sources summary](https://learn.microsoft.com/en-us/microsoft-copilot-studio/knowledge-copilot-studio)

### The rest of the Generative AI page

- [ ] **Use information from the web / Web Search: OFF.** The Governor must reason from
      the program's own governance artifacts, not from whatever Bing surfaces. This one
      is not a close call.
- [ ] **Content moderation: leave at the default (High)** unless an eval shows it
      suppressing legitimate governance language. Lower settings permit more harmful
      content, which is not a trade this program should make for convenience.
- [ ] **Tenant graph grounding with semantic search:** only relevant if you later add
      SharePoint knowledge. It requires the agent's user authentication to be
      *Authenticate with Microsoft*. Not needed for uploaded files.

**Trap: do not write instructions about citations.** Microsoft explicitly warns against
instructions that modify, override, or interfere with the citation format, including
using words like "citation" or "reference," because the orchestrator may then treat the
response as model knowledge and discard it. The Governor's instruction block does not do
this today. Keep it that way.

---

## Part 8: the boundary that is a governance control

- [ ] **Connect no API, flow, connector, or tool that reaches this engine.** Not now,
      not as a convenience later.

The operator is the transport: they save the AGR from the chat, run the CLI or the
GUI, review the validation report, and paste failures back. That human checkpoint is a
governance control, not friction to be automated away. An agent wired directly to the
compiler removes the one place a person inspects what was produced.

- [ ] Confirm the agent has **no tools** configured. Tools are how this boundary erodes.

---

## Part 9: publish and channel

- [ ] Publish the agent.
- [ ] Choose channels deliberately. Publishing to an organization's app catalog needs
      admin approval, and data policies may block channels outright.
- [ ] For this program, the operator uses the agent in chat. Do not publish it more
      widely than the people who are allowed to produce governed artifacts.

---

## Part 10: verify before you trust it

- [ ] Ask it to name all eight generable artifact types. The names must match config
      exactly. A 2026-07-15 eval caught it confabulating full names from codes, calling
      DS a "Data Sheet" and TAK a "Technical Architecture Kit," differently each time.
      That is why the code-to-name table is pinned in the instruction body.
- [ ] Ask for a Runbook, and for a Standard Operating Procedure. Both must be refused:
      they are not generable types.
- [ ] Ask it to invent a Document ID. It must refuse and ask for a registered one.
      Rule 5: the engine never mints IDs, and neither does the Governor.
- [ ] Ask for a Deployment Summary with real inputs. It should ask for the Document ID
      and client inputs before emitting anything.
- [ ] Take one emitted AGR and run it through the engine end to end. That is the only
      test that scores the contract rather than the conversation.

**Trap: the built-in eval metric scores correct behavior as failure.** Copilot Studio's
default automatic metric, *General quality*, does not read your Expected response
column and treats a refusal as a non-answer. Both a correct refusal and correct
input-gathering therefore score as failures, and a Governor that charges ahead and
fabricates scores *better*. Two runs of the same unchanged Governor over the same 15
prompts scored 80 percent and 60 percent purely on this effect. Do not use the raw
percentage. The classing scheme and per-class rubric are in
`docs/governor-eval-setup.md`; read it before running an eval.

---

## Ongoing

- [ ] When `config/` changes: `registrar emit-knowledge`, commit, re-upload the four
      role files. Studio serves the last upload and will not tell you it is stale.
- [ ] When `docs/governor/` changes materially: re-derive the instruction block, and
      re-measure it against the 8,000 character ceiling before pasting.
- [ ] When a model is retired: you have about one month. Re-run the eval on the
      replacement before switching, since scores are only comparable within a model.
