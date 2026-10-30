# Tamaneen (طمأنينة): handoff for the next developer or AI assistant

Read this file first. It tells you what the project is, where everything is, how to run it, the rules the code follows, what is done, what is open, and the roadmap.

- **Repository:** https://github.com/hamadibraa1-afk/ibrahim
- **Branch:** `claude/brave-bardeen-3nott4`. The repository's default branch holds a different project, so always switch to this branch.
- **Project folder:** `tamaneen/`. Everything else in the repository belongs to the other project.
- **Download as ZIP:** https://github.com/hamadibraa1-afk/ibrahim/archive/refs/heads/claude/brave-bardeen-3nott4.zip

## 1. What it is

Tamaneen is an Arabic-first, bilingual (Arabic RTL / English LTR) mental-health care platform for a pilot in Saudi Arabia.

- A patient signs up anonymously with a nickname and gets either:
  - an AI intake conversation ("Rafiq"), which a human supervisor reviews before matching; or
  - direct booking, after a short safety screen.
- After that the patient books sessions with a licensed therapist, and therapists run the care.
- Every piece of patient text passes a **crisis gate** before anything else processes it. Risk language opens a crisis event, locks the chat, and pages on-call staff in real time.

The design source is `Tamaninah-Design-Handoff-v1.0.pdf`, which the owner has. It covers 145 screens:
- **P01–P68:** patient app.
- **S01–S77:** staff portal.

Its structured content is in this repository:

| Path | What it holds |
|---|---|
| `docs/design/spec/g1.json` … `g4.json` | One object per screen: route, elements, buttons with actions, API, states, rules and next screens. g1 = P01–P36, g2 = P37–P68, g3 = S01–S38, g4 = S39–S77. |
| `docs/design/spec/guide.md` | The engineering guide: routes (§6), the button catalog (PB-/SB- codes, §7), endpoints (§4.2), edge cases (EC- codes, §8), rules. It is large, so grep it. |
| `docs/design/mockups/*.dc.html` | The HTML mockup of each screen. A spec object's `file` field names its mockup. |

## 2. Stack and layout

```
tamaneen/
  Tamaneen.sln                    .NET 8 solution (global.json pins SDK 8.0.x)
  backend/src/
    Tamaneen.SharedKernel         ErrorCodes, DomainException, AppRoles, IClock, INotifier
    Tamaneen.Identity             accounts, auth (credentials, challenges, sessions, TOTP), consents, profiles
    Tamaneen.Clinical             scheduling (slots, appointments, waitlist), intake, triage, care (plans, notes, homework, safety plan, closure)
    Tamaneen.Crisis               crisis events, gate, chat locks, actions
    Tamaneen.Crisis.Rules         Arabic/English risk rule packs (JSON) and the evaluator
    Tamaneen.Crisis.Worker        separate service: crisis gate endpoint, crisis console API (S04), SignalR hub, escalation loops
    Tamaneen.AiGateway            the only path to the LLM: de-identify → prompt → model → validate → rehydrate; DemoLanguageModel for keyless trials
    Tamaneen.Infrastructure       EF Core + SQL Server, migrations, row-level security, envelope encryption, audit ledger
    Tamaneen.Hosting              shared host setup (auth, localization, problem details, session context)
    Tamaneen.Api                  the main API: endpoints under Endpoints/, notifications, sessions (video), dev seed
  backend/tests/
    Tamaneen.UnitTests            xUnit
    Tamaneen.IntegrationTests     xUnit with a real SQL Server (one database per run)
  web-patient/                    Next.js 15 App Router + next-intl (ar/en), Tailwind, PWA, BFF routes under src/app/api
  web-staff/                      Angular 20 + Angular Material + ngx-translate (ar/en), SignalR client
  docker-compose.yml              the full local stack
  docs/                           this file, the API contract, the guides, the design specs, the reviews
```

**Key documents**
- `docs/api-v1.md`: the API contract. Every endpoint must be listed here.
- `docs/frontend-patient.md`: patient-app conventions and the mapping from spec routes to the routes actually built (the routes are renamed from the spec on purpose).
- `docs/local-trial.md`: how to run the platform on a laptop and test it (Arabic).
- `docs/Tamaneen-Test-Map-v1.0.pdf`: the manual test plan (Arabic + English). It does not cover phase 5 yet.
- `docs/Tamaneen-Gap-Report-and-Plan.pdf`: all 145 screens with their status and the phase plan.
- `docs/reviews/`: design-conformance reviews and the open fix lists (see §6).
- `README.md`: architecture notes for each phase.

## 3. Run it

**Everything in Docker** (what the owner uses):
```bash
cd tamaneen
docker compose up --build
```

| Service | Address |
|---|---|
| Patient app | http://localhost:3000 |
| Staff portal | http://localhost:4200 |
| API | http://localhost:5080 |
| Crisis worker | http://localhost:5090 |
| SQL Server | localhost,14333 |

- Without an SMS provider, verification codes go to the test inbox: http://localhost:5080/dev/inbox. It exists in Development only.
- Video sessions use a local "test room" (DevVideoProvider). AI replies use DemoLanguageModel unless you set `ANTHROPIC_API_KEY`.

**Development without Docker for the apps:**
```bash
docker compose up -d sql
export TAMANEEN_SQL='Server=localhost,14333;Database=TamaneenDev;User Id=sa;Password=Tamaneen!Dev2026;TrustServerCertificate=True'
dotnet tool restore && dotnet ef database update -p backend/src/Tamaneen.Infrastructure
dotnet build Tamaneen.sln
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 dotnet run --no-build --project backend/src/Tamaneen.Api --no-launch-profile
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5090 dotnet run --no-build --project backend/src/Tamaneen.Crisis.Worker --no-launch-profile
cd web-patient && npm ci && TAMANEEN_API_URL=http://localhost:5080 TAMANEEN_EMERGENCY_NUMBER=911 npm run dev
cd web-staff && npm ci && npx ng serve --port 4200
```

**Test accounts** (Development seed; never use them in production)
- **Staff:** all accounts are `@staff.tamaneen.dev`, password `Tamaneen-Dev-2026!`, authenticator (TOTP) secret `ORQW2YLOMVSW4LLEMV3C25DPORYC2MRQ`.

  | Account | Role |
  |---|---|
  | fahad | supervisor, backup on-call |
  | lead | clinical lead |
  | khalid | on-call therapist |
  | sara, noura, mona | therapists |

- **Patients:** none are seeded. Sign up in the app; the code appears in /dev/inbox.
- **SQL:** user `sa`, password `Tamaneen!Dev2026`.

## 4. Tests: the gate every change must pass

```bash
dotnet build Tamaneen.sln                                   # must give 0 warnings, 0 errors
dotnet test backend/tests/Tamaneen.UnitTests
TAMANEEN_TEST_SQL='Server=localhost,14333;User Id=sa;Password=Tamaneen!Dev2026;TrustServerCertificate=True' \
  dotnet test backend/tests/Tamaneen.IntegrationTests
TAMANEEN_SQL='Server=localhost;Database=x;TrustServerCertificate=True' \
  dotnet ef migrations has-pending-model-changes -p backend/src/Tamaneen.Infrastructure   # must say no changes
cd web-patient && npx tsc --noEmit && npm run lint && npx vitest run && npm run build
cd web-staff && CHROME_BIN=<chromium> npx ng test --watch=false && npx ng build
# e2e (API and worker running on 5080/5090):
cd web-patient && E2E_API_URL=http://localhost:5080 npx playwright test
cd web-staff && npx playwright test
# the Docker stack itself:
cd web-patient && npx playwright test -c playwright.compose.config.ts
```

Last full run, after merging phase 5 and the patient fix list:

| Suite | Passed |
|---|---|
| Backend unit | 353 |
| Integration | 108 |
| Patient app unit | 367 |
| Staff portal unit | 176 |
| Patient e2e | 33 (1 skipped on purpose) |
| Staff e2e | 20 |
| Against the containers | 3 + 20 |

## 5. Rules the code follows (do not break them)

1. **Bilingual everywhere.**
   - Every UI string exists in Arabic and English: `web-patient/messages/{ar,en}/*.json` and `web-staff/src/assets/i18n/{ar,en}.json`.
   - Layout works in RTL and LTR.
   - API errors are `DomainException(ErrorCodes.X)`, mapped to an HTTP status in `Tamaneen.Hosting/DomainExceptionHandler.cs`, with text in **both** `SharedResource.resx` and `SharedResource.ar.resx`. The API answers in `Accept-Language`.
   - The AI replies in the patient's language.
2. **Row-level security.**
   - Every table with a `PatientId` is under the SQL Server policy `security.PatientDataPolicy`. Access depends on `SESSION_CONTEXT` (`user_id`, `role`, `patient_id`), which `SessionContextInterceptor` sets.
   - To change a predicate function, first drop the predicates that use it, ALTER the function, then add them back. See the existing migrations.
   - Every new patient table needs predicates **and** an integration test proving that the other roles can't read it.
3. **Encryption.**
   - Patient free text and contact data are encrypted with that patient's own key, through `IPatientDataProtector`. Field names live in `ProtectedFields` and `Fields`.
   - Deleting the account crypto-shreds them.
4. **Crisis gate.**
   - All patient free text goes to the crisis worker (`CrisisGateClient`) before it is processed.
   - If the worker is down, the API fails closed (503 `error.crisis.unavailable`) and shows help.
5. **Audit.**
   - Every staff read of patient data is appended to the audit ledger.
   - The patient sees who opened their file (P11 access log).
6. **Concurrency.** Shared editable records use ETag / If-Match: 428 without it, 412 when the version is stale. This covers patient settings, the clinical file, plans, notes and the safety plan.
7. **Notifications.**
   - Code calls `INotifier.NotifyAsync(new Notification(recipientUserId, "kind", data, link))`.
   - Each kind needs Arabic and English texts in `Tamaneen.Api/Notifications/NotificationCatalog.cs`.
   - SMS and e-mail copies must be neutral: no clinical words.
8. **Migrations.**
   - Generate with `TAMANEEN_SQL='Server=localhost;Database=x;TrustServerCertificate=True' dotnet ef migrations add <Name> -p backend/src/Tamaneen.Infrastructure -o Persistence/Migrations`.
   - Always re-check `has-pending-model-changes`.
9. **Patient app.**
   - The browser never sees tokens. The BFF in `src/app/api/` keeps them in HttpOnly cookies and checks the same origin.
   - Writes that must survive going offline use `components/pwa/outbox.ts` with an Idempotency-Key.
   - Nothing about the patient is stored unencrypted on the device.
10. **Production guards.** The host refuses to start in Production with:
    - the demo AI model;
    - the dev video provider;
    - missing keys.

## 6. Status

**Done and tested (phases 1–5):**
- **Patient screens:** P01–P38, P41–P44, P46, P53, P54, P56, P63, P66, P67.
- **Staff screens:** S01–S04, S06, S13–S16, S18–S22, S32, S33, S35, S41, S57, the therapist session page (`/sessions/:id`) and the supervisor closures queue.
- **Infrastructure:**
  - notification service, with reminders and quiet hours;
  - offline outbox;
  - waitlist;
  - video provider abstraction;
  - Docker stack.

**Phase 5 design-review fixes.**
- **Patient list: DONE and merged.** `docs/reviews/phase5-open-fixes-patient.md`:
  - the lobby routes cancelled or missed sessions to P67 or P63, and P43 only follows a real session;
  - P63 «مو بخير» is stored and alerts the therapist and the supervisors (`appointment.missed_not_ok`);
  - P53 return to care runs the P23 safety check, enforced by the server;
  - P38 shows the agreed follow-up time and support person, recorded by the responder when closing the event in S04;
  - P67 rebooking with the same therapist is a priority reschedule;
  - the homework fixes and one safety-plan section limit (1,000 characters).
- **Staff list: OPEN.** It may be in progress elsewhere; check the git log for "S18", "S32" and so on before starting.
- **Staff list, `docs/reviews/phase5-open-fixes-staff.md`:**
  - S18: "session done" opens the notes, plus a reschedule action;
  - S15: keep the draft while offline;
  - S20: history includes risk raises and no-shows;
  - S21: licence expiry and verification;
  - S32: enforce the 15-minute idle timeout, time zone, confirmation;
  - S33: the patient's pre-session note and booking the next session;
  - S35: relationship field;
  - S41: link to the pending file;
  - EC-52/EC-53 supervisor alerts;
  - S57: block closure when the patient asked to continue.
- **Full findings, with file:line evidence:** `docs/reviews/phase5-review-patient.md` and `phase5-review-staff.md`.

**Deferred on purpose:**

| Item | Why |
|---|---|
| CAPTCHA, passkeys | not chosen yet |
| Real SMS/e-mail and video providers | abstractions and dev adapters exist; a real provider is a configuration change |
| Data-export file generation | not built yet |
| Nearest-hospital list | not built yet |
| P67 same-time substitute therapist | needs S65 |
| Arabic questionnaire wording (PHQ-9, GAD-7, C-SSRS) | must be replaced with the validated Arabic versions |
| Crisis rule packs | need clinical review |

## 7. Roadmap

Do the phases in this order. Each one ends with the gate in §4, a design-conformance review of every screen (page, buttons, links, inputs: compare the code with `docs/design/spec` and the mockup), a Docker rebuild, an updated test map, and the owner's sign-off.

- **Phase 5 (finish).** Apply the open staff fix list above, then update the test map with the phase 5 screens.
- **Phase 6: operations and admin.**
  - S26 users and S73 importing therapists (or S07–S12 self-apply; the owner has to choose), plus S05 licence verification.
  - S27 audit log viewer, S29 system health, S30 kill switches, S43 platform settings, S72 announcements and maintenance.
  - S23 on-call and S24 rota (replacing the fixed seed on-call), and S28 crisis rules with review and approval.
  - S51 data requests and the export file (P11), S50 capacity, and P59 re-consent.
- **Phase 7: patient engagement.**
  - P48 progress, P50 journal (the AI reflection prompt exists), P49 library with S44 content management.
  - P39/P40/P62 messaging with S36 and S62. The owner must decide the messaging scope and the expected response time.
  - P51 support with S52 complaints, P47 preferences, P52 display, P57 dark mode, P55 tour, P58 app lock.
  - P60 Ramadan and prayer-time booking, P61 attendance certificate, P65 install guide and push notifications.
- **Phase 8: quality, analytics and the rest.**
  - S25 QA, S31 metrics, S74 reports, S17 weekly reports.
  - S34 risk assessment, S37 measures, S40 event review, S59 deterioration alerts, S58 safeguarding.
  - S38 consult and transfer, S39 reassign, S56 session prep, S60 note templates and AI draft, S61 referral letter.
  - S45–S49 instruments, catalogs, roles and notification rules, S53 incidents, S54 surveys, S67/S68 AI settings and evaluation.
  - S55, S63–S66: leave, wellbeing, CPD, absence (S65 then enables the P67 substitute), and no-shows.
  - S69–S71, S75–S77, P64 medications, P68 subscription (optional).
- **In parallel, before any real patient:**
  - real hosting with HTTPS, a managed SQL Server, and backups that are actually restored as a test;
  - Key Vault for all secrets, with every dev key rotated;
  - monitoring, with an alert if the crisis worker is down;
  - an Anthropic key and a clinical evaluation of the AI;
  - clinical review of the rules and translations;
  - a penetration test, a privacy review (PDPL), an accessibility audit and a load test.

## 8. How to work with an AI assistant on this code

- Give it this file plus the specific spec object(s) from `docs/design/spec/*.json` and the mockup for the screen you want.
- Ask for one screen or one fix list at a time.
- Insist on the rules in §5:
  - Arabic and English strings;
  - RLS and an integration test for any patient data;
  - the crisis gate for patient text;
  - audit for staff reads;
  - `docs/api-v1.md` and the TS types (`web-patient/src/lib/api/types.ts`, `web-staff/src/app/core/api/types.ts`) updated with every endpoint.
- Run the whole gate in §4 before committing. A change is not done until the build is clean and every suite passes.
- Commit to the same branch, or to a new branch made from it, and keep commits small and described.
