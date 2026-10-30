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
| T-501 | Patient app button for "I want to continue care" (`POST /v1/me/care/continue`) during ongoing care. The API exists. Use the care-journey spec in `docs/design/spec`; Arabic and English text; unit test and e2e. | blocked — implementation and patient checks complete; full gate awaits CI billing unlock and T-504 | Codex, 2026-10-30 | codex/T-501-continue-care |
| T-502 | Add the phase 5 screens (P41–P44, P46, P53, P54, P56, P63, P66, P67, S18–S22, S32, S33, S35, S41, S57, closures queue) to the bilingual test map PDF, as `docs/testmap/` source plus the generated PDF. | todo | | |
| T-503 | Phase 5 gate: full §4 run, a design-conformance review of the new button, Docker rebuild, and owner sign-off. | todo | | |
| T-504 | Make the S21 profile date assertion portable across supported browser Intl data (Chrome 156 adds a comma after the weekday; currently 191/192 staff tests pass). | todo | | |

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

### 2026-10-30 · Codex · T-501 · draft PR #1
- Resumed `codex/T-501-continue-care`; verified `https://api.github.com` returns HTTP 200, fetched integration, and opened https://github.com/hamadibraa1-afk/ibrahim/pull/1 into `claude/brave-bardeen-3nott4` before application changes.
- Added the ongoing-care home action using the existing `ContinueCare` contract and BFF. POST records the request, disables repeated presses while pending, shows localized confirmation, and offers retry on failure. GET restores confirmation after reload. Uses the same appointment-history/no-signed-off-closure care eligibility convention as P44; hidden before a booking and after care closure. No API, database, safety, or staff behavior changed.
- Design review: P09 (`g1.json`, `P09-Dashboard.dc.html`) is the daily ongoing-care hub; the secondary card follows its existing cards and styling. P53/S57 and guide §10.4 require honoring the patient's wish to continue before closure. Arabic/English copy, existing touch-size buttons, logical layout and live status/error notices follow the patient UI conventions. Existing API/TS contracts are reused; updated the API contract's obsolete "API only" note.
- Validation: backend build 0 warnings / 0 errors; backend unit 358 passed; SQL integration 114 passed; EF migration model check clean; patient TypeScript and lint clean, unit 371 passed, production build passed, full patient e2e 33 passed / 1 intentional skip (API 5180, worker 5190). Staff production build passed; staff unit 191 passed / 1 failed (T-504 below). `git diff --check` passed. Temporary source API/worker processes were stopped after testing; the isolated development database remains for reproduction.
- Environment: the pre-existing Docker API is stale (`/v1/me/care/continue` returns 404), so the first patient e2e attempt was 32 passed / 1 failed / 1 intentional skip. Migrated a separate `TamaneenT501` development database and ran this checkout's API on 5180 plus crisis worker on 5190 for the corrected run; existing containers were not rebuilt or replaced.
- Gate blockers: GitHub Actions run 41065794780 never started any job; check annotation explicitly says the account is locked due to a billing issue. Staff unit suite: 191 passed / 1 failed at `profile.component.spec.ts:53` because Chrome 156 renders "Monday, 19 October 2026" while the existing assertion requires no comma; tracked as T-504. PR stays draft; no ready/merge action. Staff e2e, compose smoke, Docker rebuild and owner sign-off remain for T-503.

### 2026-10-30 · Codex · T-501 · codex/T-501-continue-care
- Pulled integration branch `claude/brave-bardeen-3nott4` at `b002fd2a723bd5e7b9ff80fe6fb3cce4e3935e5a`, read AGENTS → HANDOFF → PROGRESS, and claimed the first todo task on the required task branch. Pushed the claim.
- **Blocked before implementation:** opening the required draft PR with `gh pr create` failed: `Post https://api.github.com/graphql: Forbidden`. A separate HTTPS request to `https://api.github.com/zen` failed at the proxy CONNECT step with HTTP 403. Git HTTPS fetch/push works; this does not establish GitHub API access. Do not request a replacement token based only on `gh auth status` while the API destination is blocked.
- **Tests:** none run (0 passed); only task-board documentation changed. No application files changed. No PR was created, marked ready, or merged; CI was not validated.
- **Next agent:** after the owner enables `api.github.com` in environment network settings, retry draft PR creation into `claude/brave-bardeen-3nott4`, then resume T-501 on this branch. If API authorization still fails after connectivity works, diagnose the existing injected GitHub authentication. Follow the design and run HANDOFF §4 before marking done. The existing care-continue endpoint is in `backend/src/Tamaneen.Api/Endpoints/CareJourneyEndpoints.cs` and its contract is already in `docs/api-v1.md`.

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
