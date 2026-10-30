# Tamaneen: task board and work log

This file is the shared memory between AI agents (Codex, Claude, any other) and the owner. Read it before starting. Update it in the same pull request as your change. The rules are in `../AGENTS.md`.

## How to use this board

- **Statuses:** `todo`, `in-progress`, `blocked`, `done`.
- **Claim a task:**
  1. Create your task branch from `claude/brave-bardeen-3nott4`.
  2. On it, set the status to `in-progress` and fill in **Owner** (agent name and date) and **Branch**.
  3. Push and open a draft pull request straight away, so other agents see the claim.
- **A claim older than 7 days with no commits** may be taken over. Say so in the work log.
- **Order:** take the first `todo` task from the top, unless the owner names another. Tasks marked **needs owner** wait for the owner's decision; do not start them.
- **New tasks:** add them with the next free id in their phase.

## Task board

### Phase 5: finish

| Id | Task | Status | Owner | Branch |
|---|---|---|---|---|
| T-501 | Patient app button for "I want to continue care" (`POST /v1/me/care/continue`) during ongoing care. The API exists. Use the care-journey spec in `docs/design/spec`; Arabic and English text; unit test and e2e. | todo | | |
| T-502 | Add the phase 5 screens (P41–P44, P46, P53, P54, P56, P63, P66, P67, S18–S22, S32, S33, S35, S41, S57, closures queue) to the bilingual test map PDF, as `docs/testmap/` source plus the generated PDF. | todo | | |
| T-503 | Phase 5 gate: full §4 run, a design-conformance review of the new button, Docker rebuild, and owner sign-off. | todo | | |

### Phase 6: operations and admin

| Id | Task | Status | Owner | Branch |
|---|---|---|---|---|
| T-601 | Decide therapist onboarding: S26 users plus S73 import, or S07–S12 self-apply. | needs owner | | |
| T-602 | S26 users and S05 licence verification (after T-601). | todo | | |
| T-603 | S27 audit log viewer. | todo | | |
| T-604 | S29 system health and S30 kill switches. | todo | | |
| T-605 | S43 platform settings and S72 announcements and maintenance. | todo | | |
| T-606 | S23 on-call and S24 rota, replacing the fixed seed on-call. | todo | | |
| T-607 | S28 crisis rules with review and approval. Must not weaken the crisis gate. | todo | | |
| T-608 | S51 data requests and the P11 export file. | todo | | |
| T-609 | S50 capacity and P59 re-consent. | todo | | |
| T-610 | Phase 6 gate: §4, design review, Docker rebuild, test map, owner sign-off. | todo | | |

### Phase 7: patient engagement

| Id | Task | Status | Owner | Branch |
|---|---|---|---|---|
| T-701 | P48 progress. | todo | | |
| T-702 | P50 journal (the AI reflection prompt exists). | todo | | |
| T-703 | P49 library with S44 content management. | todo | | |
| T-704 | Decide messaging scope and expected response time. | needs owner | | |
| T-705 | P39/P40/P62 messaging with S36 and S62 (after T-704). | todo | | |
| T-706 | P51 support with S52 complaints. | todo | | |
| T-707 | P47 preferences, P52 display, P57 dark mode. | todo | | |
| T-708 | P55 tour and P58 app lock. | todo | | |
| T-709 | P60 Ramadan and prayer-time booking, P61 attendance certificate. | todo | | |
| T-710 | P65 install guide and push notifications. | todo | | |
| T-711 | Phase 7 gate. | todo | | |

### Phase 8: quality, analytics and the rest

| Id | Task | Status | Owner | Branch |
|---|---|---|---|---|
| T-801 | S25 QA, S31 metrics, S74 reports, S17 weekly reports. | todo | | |
| T-802 | S34 risk assessment, S37 measures, S40 event review, S59 deterioration alerts, S58 safeguarding. | todo | | |
| T-803 | S38 consult and transfer, S39 reassign, S56 session prep. | todo | | |
| T-804 | S60 note templates and AI draft, S61 referral letter. | todo | | |
| T-805 | S45–S49 instruments, catalogs, roles and notification rules. | todo | | |
| T-806 | S53 incidents, S54 surveys, S67/S68 AI settings and evaluation. | todo | | |
| T-807 | S55, S63–S66: leave, wellbeing, CPD, absence (S65 enables the P67 substitute), no-shows. | todo | | |
| T-808 | S69–S71, S75–S77, P64 medications, P68 subscription (optional). | todo | | |
| T-809 | Phase 8 gate. | todo | | |

### Readiness, before any real patient (in parallel)

| Id | Task | Status | Owner | Branch |
|---|---|---|---|---|
| T-901 | Real hosting with HTTPS, managed SQL Server, backups restored as a test. | needs owner | | |
| T-902 | Key Vault for all secrets; rotate every dev key. | todo | | |
| T-903 | Monitoring, with an alert if the crisis worker is down. | todo | | |
| T-904 | Anthropic key and a clinical evaluation of the AI. | needs owner | | |
| T-905 | Clinical review of crisis rules, questionnaires (validated Arabic PHQ-9, GAD-7, C-SSRS) and translations. | needs owner | | |
| T-906 | Penetration test, PDPL privacy review, accessibility audit, load test. | needs owner | | |

### Workflow

| Id | Task | Status | Owner | Branch |
|---|---|---|---|---|
| T-001 | Shared agent workflow: `AGENTS.md`, `CLAUDE.md`, this board, GitHub Actions CI, Codex setup script. | done | Claude, 2026-10-30 | claude/brave-bardeen-3nott4 |

## Work log (newest first)

Each entry: date, agent, task id, branch or PR, what changed, test results, notes for the next agent.

### 2026-10-30 · Claude · T-001 · claude/brave-bardeen-3nott4
- Added `AGENTS.md` (root and `tamaneen/`), `tamaneen/CLAUDE.md`, this file, `.github/workflows/tamaneen-ci.yml` and `scripts/codex-setup.sh`.
- CI runs the backend build, unit and integration tests (SQL Server service), the migration check, and the patient and staff app checks on every push and pull request that touches `tamaneen/`. It does not run the e2e suites; run those locally (§4 of HANDOFF).
- **CI status:** the first run (https://github.com/hamadibraa1-afk/ibrahim/actions/runs/41062758177) failed within 3 seconds with no runner assigned and no logs. The account's GitHub Actions setup (billing or spending limit) stopped it before any step ran, not the code. When the owner fixes that, re-run it. Until CI runs, run the §4 checks locally and report the counts in the work log.
- **Next:** T-501.

### 2026-10-30 · Claude · phases 1–5 (summary) · claude/brave-bardeen-3nott4 @ 4a123d5
- Built phases 1–5 from the v1.0 design: patient screens P01–P38, P41–P44, P46, P53, P54, P56, P63, P66, P67; staff screens S01–S04, S06, S13–S16, S18–S22, S32, S33, S35, S41, S57, the therapist session page and the closures queue; notifications, offline outbox, waitlist, video abstraction, Docker stack.
- Both phase 5 review fix lists are merged (`docs/reviews/phase5-open-fixes-*.md`).
- **Tests:** backend unit 358, integration 114, patient unit 368, staff unit 192, patient e2e 33 (1 skipped on purpose), staff e2e 20, against the containers 3 + 20. All passed.
- **Notes:**
  - Inside `provideAppInitializer`, call `inject()` before the first `await` (it caused NG0203).
  - Integration hosts wrap the notifier in `RecordingNotifier`, which must keep forwarding to `Inner`.
