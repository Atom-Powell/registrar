# QA Checklist Reconciliation

| Field | Value |
| --- | --- |
| Document | QA Checklist Reconciliation (Master Consolidated v1.0 vs engine catalog) |
| Owner | Registry Owner |
| Status | Findings / decision record (no config or code changes made) |
| Date | 2026-07-11 |
| Sources | docs/AI_Governance_Program_Master_Consolidated_v1_0.md Section 7.9 and Section 10; config/qa-checklist.json; config/style-rules.json; config/artifact-types.json |

## Purpose

Reconcile the Pre-Delivery QA Checklist material in the consolidated master
(Section 7.9, plus the version-drift item in Section 10.2 item 2) against the
engine's machine-executable check catalog (config/qa-checklist.json). This is a
findings pass. It records what the engine enforces, what it cannot or should not
enforce, and which decisions belong to the documentation owner.

## 1. The altitude problem (the core finding)

There are three distinct QA artifacts in play, not two. Conflating them is what
makes the "drift" look larger than it is.

| # | Artifact | Scope | Nature |
| --- | --- | --- | --- |
| A | Master Section 7.9 Pre-Delivery QA Checklist (domains A-I) | The whole client delivery package for the M365 Connector for Claude v1.2 rollout | Mostly human/manual, connector-package specific |
| B | The engine catalog, config/qa-checklist.json | Each generated artifact's conformance to the Master Template System | Machine-executable, template-system wide |
| C | The Section 10.2 item 2 drift: v1.2 short form (10 checks) vs v1.0 long form (nine domains A-I) | Two versions of artifact A | The version conflict flagged during consolidation |

Key point: the engine catalog (B) already performs its own merge, of
ORG-MST-WFE-001 Section 4.5 (12-point short form) plus ORG-MST-SPEC-001 Section 9.
That is a different document lineage from the package checklist (A). Per the
master's own Section 10.2 item 7, the WFE and MST Spec documents are not present
in this consolidation; they are external dependencies. So the engine validates
against a checklist family that lives outside the master, while delivery teams
run the Section 7.9 package checklist. That lineage separation is a larger risk
than the v1.2-vs-v1.0 version number, and it is the thing to decide deliberately.

## 2. Section 7.9 domains A-I mapped to engine enforcement

Coverage legend: Covered (engine decides pass/fail alone), Assisted (engine
verifies what it can and emits an operator attestation), Manual (human judgment
the engine surfaces but never auto-passes), Out of lane (not an engine
generation target), Gap (the master requires it and the engine could enforce it
but does not yet).

| Section 7.9 domain | Engine coverage | Verdict |
| --- | --- | --- |
| A. Internal-note and placeholder sweep | Characters: QA-SF-08 and QA-ENG-05 (Covered). Placeholders: QA-SPEC-07 and QA-ENG-07 (Covered). Internal-note detection: QA-SF-09 rule states it, but implementation is deferred because no AGR field models an internal note. "No client-opaque acronyms" is not modeled. | Partial - real gap |
| B. Runbook document integrity | The Runbook type (RBK) is generationTarget=false; the engine never renders a runbook. | Out of lane by design |
| C. Scope integrity (connector is not Cowork; read-only and audit claims scoped) | QA-SPEC-14 (audit claims must be CL-006 or evidence-backed) plus QA-SPEC-15 (RCD language must be CL-007 or evidence-backed) enforce exactly these overclaim risks via clause gating. | Covered for generated artifacts |
| D. Signature and completion state | RAF inclusion: QA-SPEC-12 (Assisted). Correct required clause present: QA-ENG-04. Physical initialing and "ready for signature" are outside engine reach. | Assisted plus manual |
| E. Companion document integrity (AUP prohibited-use matches regime; examples per vertical) | Section presence and order: QA-ENG-02. Per-user acknowledgment model: manualOnly item. "Prohibited-use list matches the client's regulatory regime" is semantic judgment. | Mostly manual |
| F. Inventory and cross-reference sync | This concerns connector-inventory.json synchronized with its governance document (Section 6 discovery tooling), not an engine artifact. | Out of lane |
| G. Branding and formatting (Montserrat body; Dark Navy #002060 headings; white-on-navy header rows; footer on every page; no collapsible headings) | style-rules.json visualIdentity: Montserrat body and heading fonts; headingNavy #002060; footerTemplate; no-collapsible-headings in contentLint. QA-SPEC-19 makes prior header formats structurally impossible. | Covered - the engine's strongest domain (see open item in Section 4) |
| H. Accuracy and freshness (Anthropic docs re-checked within 30 days; Microsoft licensing re-verified; token-lifetime language exact) | Clause gating keeps audit and RCD claims canonical, but "re-verify against current Microsoft Learn" is inherently time-sensitive external fact. | Manual by nature (correct per engine rule 2: zero runtime judgment) |
| I. Delivery logistics | Filename convention and version match: QA-SF-12 (Covered). Manifest and registry update: QA-SPEC-18 (Assisted). "Cover note confirms no internal-only artifacts" requires DCN, whose section schema is undefined. | Partial - blocked on DCN |

## 3. Decisions for the documentation owner

1. Lineage, not just version. Decide whether the engine catalog
   (config/qa-checklist.json) should reference the Section 7.9 package checklist
   (A-I) as a source alongside WFE and SPEC, or whether the two remain
   deliberately separate layers: per-artifact conformance (engine) versus
   per-package delivery readiness (Section 7.9). Recommendation: keep them
   decoupled. The engine correctly owns only the mechanical per-artifact subset
   (domain A characters, domain C, domain G, domain I naming). Section 7.9's
   domain B, domain F, domain H, and the physical-signature items must never
   become engine auto-passes, per engine rule 2 (determinism, no runtime
   judgment).

2. The v1.2-vs-v1.0 version call (Section 10.2 item 2). The A-I long form is the
   superset and is what Section 7.9 already consolidates. Re-versioning it to
   v1.3 as the single authoritative package gate is the clean resolution; the
   v1.2 10-check short form omits most of domains B, F, and H. This is a program
   decision and does not by itself change the engine.

## 4. Confirmed engine gaps

Both were already implied by the CLAUDE.md known-gaps list; Section 7.9 confirms
them against the delivery-side requirement.

1. Internal-only-note detection under QA-SF-09. Domain A requires that every
   internal note be removed from client-facing output. The engine's QA-SF-09
   rule text already claims this, but detection is deferred because no AGR data
   model represents an internal note. Closing it is first an AGR data-model
   decision (how a note is represented so the engine can fail closed on it),
   then a validation change.

2. DCN (Delivery Cover Note) section schema is undefined. Domain I's "cover note
   confirms no internal-only artifacts are in the package" cannot be enforced
   until DCN is authored. This is the same TAK/RAF/DCN schema-authoring gap noted
   in CLAUDE.md; the master Section 7.1 now supplies candidate source content for
   the DCN schema.

## 5. Open verification item (branding)

Domain G calls for white-on-navy table header rows. style-rules.json assigns
headingNavy (#002060) as the table header fill, but the header text color
(white on navy) is not explicit in the config. This is a single narrow item to
confirm in the renderer (Registrar.Rendering) before treating domain G as fully
closed. It is not a defect until verified; it is flagged so it is not assumed.

## 6. Summary

The engine and the Section 7.9 package checklist operate at different altitudes
and should stay decoupled. The engine already covers the mechanical, per-artifact
subset of Section 7.9 well (characters, placeholders, scope-and-claim gating via
clauses, branding, filename). The genuinely open engine work is two items:
internal-note detection (QA-SF-09) and the DCN schema. The version-drift
question (Section 10.2 item 2) is a program-side decision about the package
checklist, not an engine change. No config or code was modified in producing
this reconciliation.

## 7. Update - items actioned 2026-07-11

Following this reconciliation, three follow-ups were taken (test-first):

1. Domain G (Section 5 verification item) - **confirmed closed.** Body-table
   header rows render bold white `#FFFFFF` text on the navy `#002060` fill
   (`Registrar.Rendering/Blocks/TableBlock.cs:60`). One residual config-purity
   item (the white is hard-coded, not read from `style-rules.json`) is logged as
   tech-debt item 24; it does not affect the rendered result.

2. Section 4 gap 1 - **closed.** Internal-only-note detection under QA-SF-09 is
   implemented: `contentBlock` carries an optional `internalOnly` flag, and
   `WarnCalloutLayer` fails any `internalOnly` block in a client-facing artifact
   at promotion. Covered by `WarnCalloutLayerTests`.

3. Section 4 gap 2 - **closed.** The DCN section schema is authored (status
   `defined`, five ordered sections, required placeholders, required clauses
   CL-006/CL-007). The Copy-Ready Message renders as a body paragraph; the
   distinct `copyReadyMessage` block stays deferred. DCN now generates
   end-to-end (fixture `agr-sample-dcn-draft`, golden `overallResult: pass`).

The decisions in Section 3 (lineage decoupling; the v1.2-vs-v1.0 package-checklist
version call) remain open program-side decisions and were not changed here.
