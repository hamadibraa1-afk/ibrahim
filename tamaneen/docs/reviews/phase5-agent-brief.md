# Phase 5 brief: running care end to end

## Context
Tamaneen v1.0 is built and tested: patient screens P01–P37 plus P56 and P66, and staff screens S01–S04, S06, S13 and S22.
Phase 5 adds what a therapist and a patient need for a real course of care.

## Design sources (read-only, under /tmp/claude-0/-home-user-ibrahim/7db83462-ec14-55f6-bda4-8fd12e4a7ce7/scratchpad/)
- spec/g1.json … g4.json: one object per screen, keyed by "code", with elements, buttons, api, states, rules and next.
- mockups/project/<file>.dc.html: the mockup for each screen. The spec's "file" field names it.
- spec/guide.md: the engineering guide. It is large, so grep it for routes, endpoints, rules and edge cases.
- The design PDF that both of these came from: Tamaninah-Design-Handoff-v1.0.pdf.

## Repository
- You work in your own git worktree. The project lives in `tamaneen/`.
- Read these first:
  - docs/api-v1.md and docs/frontend-patient.md;
  - README.md, the phase 4 sections;
  - the existing code you build on.
- Patient routes are renamed from the spec (see docs/frontend-patient.md). Keep that convention for new screens, e.g. `/appointments/[id]/lobby`, not `/sessions/[id]/lobby`.
- The staff portal uses plain routes like `/patients/:id`, not `/t/...`.

## Non-negotiable conventions (same as phases 1–4)
- **Backend:**
  - .NET 8 modular monolith.
  - Every new table with a PatientId is under the RLS policy `security.PatientDataPolicy`: drop the predicates before ALTERing a function and re-add them.
  - Patient free text is encrypted with the patient's key (IPatientDataProtector, field name in ProtectedFields or Fields).
  - Patient free text passes the crisis gate (CrisisGateClient) before it is processed.
  - Staff reads of patient data are audited.
  - Errors are DomainException with ErrorCodes, mapped in DomainExceptionHandler, with BOTH ar and en resx entries.
  - The API answers in Accept-Language.
- **Patient app:**
  - Next.js App Router with next-intl. Every string lives in messages/ar and messages/en.
  - RTL and LTR both work.
  - Use the existing components/ui primitives and the BFF (lib/api/client.ts).
  - Writes that the design says must survive offline go through components/pwa/outbox.ts.
- **Staff portal:**
  - Angular 20, Material and ngx-translate. Every string goes in assets/i18n/ar.json and en.json.
  - Add menu entries by role in the existing nav.
- Never invent features that are not in the spec or guide. Where the guide gives no endpoint, follow docs/api-v1.md conventions and document what you add.
- Update docs/api-v1.md and the TS types (web-patient/src/lib/api/types.ts, web-staff/src/app/core/api/types.ts) for every contract change.
- Migrations: name them `Phase5<Area><Change>`. Generate with `TAMANEEN_SQL='Server=localhost;Database=x;TrustServerCertificate=True' dotnet ef migrations add <Name> -p tamaneen/backend/src/Tamaneen.Infrastructure -o Persistence/Migrations`. Run `dotnet tool restore` in tamaneen/ first.
- **Notifications:** do NOT send any yourself; another agent builds the notification service. At every point where the design says the patient or a staff member is notified, add a call to the interface below, which you add if it doesn't exist yet. Use EXACTLY this shape:

  ```csharp
  // Tamaneen.SharedKernel/Notifications/INotifier.cs
  namespace Tamaneen.SharedKernel.Notifications;
  public interface INotifier { Task NotifyAsync(Notification notification, CancellationToken ct); }
  public sealed record Notification(Guid RecipientUserId, string Kind, IReadOnlyDictionary<string, string> Data, string? Link = null);
  ```

  Register a no-op `NullNotifier` in the API host with `TryAddSingleton<INotifier, NullNotifier>`, so the notifications agent's registration wins.
  `Kind` is a dotted code such as `appointment.cancelled_by_therapist`. List every kind you emit in your final report.
  `Data` holds non-sensitive values only: ids, times, the therapist's display name. Never patient free text.

## Verification (mandatory before each commit)
- `dotnet build tamaneen/Tamaneen.sln`: 0 warnings, 0 errors.
- Unit tests: `dotnet test tamaneen/backend/tests/Tamaneen.UnitTests`.
- Integration tests: `TAMANEEN_TEST_SQL='Server=localhost,14333;User Id=sa;Password=Tamaneen!Dev2026;TrustServerCertificate=True' dotnet test tamaneen/backend/tests/Tamaneen.IntegrationTests`.
  - SQL Server is running.
  - If Docker is down, run: `sudo rm -f /var/run/docker.pid; (sudo nohup dockerd >/dev/null 2>&1 &); sleep 20; cd <worktree>/tamaneen && docker compose up -d sql`.
- Patient app (run `npm ci` first in a fresh worktree): `npx tsc --noEmit`, `npm run lint`, `npx vitest run`, `npm run build`.
- Staff portal (run `npm ci` first): `CHROME_BIN=/opt/pw-browsers/chromium npx ng test --watch=false`, then `npx ng build`.
- Add tests for everything you build:
  - backend integration tests, including an RLS test showing each role sees only what it should;
  - unit tests for the UI.
- You may ADD Playwright e2e specs (web-patient/tests/e2e, web-staff/e2e) but do NOT run Playwright: ports are shared. The lead runs e2e after merging.

## Finish
- Commit after each completed screen or part, with the trailers:
  Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01EHohyfEjJ9mvShmhfZFZ51
- Do not push.
- Final message (under 300 words):
  - branch name and head commit;
  - what you built, by screen code;
  - what you deliberately left out, and why;
  - test counts;
  - the notification kinds you emit;
  - every shared file you touched outside your area.
