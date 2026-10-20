# Tamaneen (طمأنينة)

An AI-assisted mental health platform pilot. Patients use a Next.js web app. Therapists, supervisors, the clinical lead and administrators use an Angular portal. Both talk to one .NET 8 modular monolith on SQL Server 2022.

This folder holds **phase 1** (infrastructure, the database security model, and both frontends with the bilingual shell) and **phase 2** (the independent crisis engine).

## Layout

```text
tamaneen/
  Tamaneen.sln
  backend/src/
    Tamaneen.SharedKernel     RiskLevel, roles, supported languages, pseudonym ids, domain errors
    Tamaneen.Identity         UserAccount, PatientProfile (ciphertext only), contact lookup hashing
    Tamaneen.Clinical         CareAssignment (drives therapist row access)
    Tamaneen.Crisis           Crisis domain, text normalizer, phrase rules, risk evaluator, CrisisGate (fails closed)
    Tamaneen.Crisis.Rules     Versioned rule packs (ar incl. Gulf and Arabizi, en), questionnaire thresholds, scripts, evaluation set
    Tamaneen.Crisis.Worker    Separate process: gate endpoint, live console hub, escalation and paging loops
    Tamaneen.Hosting          Shared host pieces: localization, JWT, localized ProblemDetails, current user
    Tamaneen.AiGateway        The only way to a language model: de-identification, prompts, output validator, Claude client
    Tamaneen.Infrastructure   EF Core 8, migrations, RLS session context, envelope encryption, ledger audit
    Tamaneen.Api              ASP.NET Core host, Accept-Language localization, ProblemDetails, JWT
  backend/tests/
    Tamaneen.UnitTests
    Tamaneen.IntegrationTests  Real SQL Server 2022 via Testcontainers
  web-patient/   Next.js 15.5 App Router + React 19 + next-intl 4, BFF route handlers
  web-staff/     Angular 20 + @ngx-translate/core 17 + Angular Material 20
  ops/           Always Encrypted setup script
```

Modules reference only `SharedKernel`. `Infrastructure` brings them together in one `DbContext`, with a separate schema per module (`identity`, `clinical`, `crisis`, `ai`, `vault`, `audit`, `security`).

## Security model in the database

| Protection | How |
|---|---|
| Row-level security | Every table with a `PatientId` has filter and block predicates. Each connection writes the caller (`user_id`, `role`, `patient_id`) into `SESSION_CONTEXT` as read-only (`SessionContextInterceptor`). Patients see only their own rows. Therapists see only actively assigned patients. Supervisors, the clinical lead and the crisis service see all. **Admins and anonymous callers see none.** Only supervisors and the lead can create or end assignments. |
| Tamper-evident audit | `audit.AuditEntries` is an append-only ledger table. SQL Server rejects `UPDATE` and `DELETE`. |
| Crypto-shredding | Each patient has a 32-byte data key. It's wrapped by a key-encryption key in Azure Key Vault Managed HSM (or a local key in development and tests only; refused in Production). Profile fields are AES-256-GCM with associated data bound to patient and field. Destroying the wrapped key makes every copy of that patient's ciphertext unreadable, including copies in backups. |
| Pseudonymization | Staff, logs and the AI gateway see `P-XXXX-XXXX` ids. Email and phone are stored only as HMAC-SHA256 lookup hashes, with a pepper from Key Vault. |
| Always Encrypted | Provider wiring is in `AlwaysEncryptedSetup`. `ops/always-encrypted.ps1` encrypts `ContactLookupHash` with a Key Vault column master key. |

## Languages

- **API**: The `Accept-Language` header picks `ar` (default) or `en`. Responses carry `Content-Language`. Every error is RFC 7807 ProblemDetails with a localized `title`/`detail`, a stable `code`, plus `lang`, `dir` and `traceId`. 5xx errors add a crisis hint. The strings live in `Resources/SharedResource(.ar).resx`, and a test fails if the two files differ or if an error code has no text.
- **Patient app**: URLs are `/ar/...` and `/en/...`. The toggle in the header keeps the current page and query, updates `<html lang dir>`, and the choice is remembered in the `NEXT_LOCALE` cookie. Layout uses logical properties (`ms-`, `ps-`, `start-`), so it mirrors without per-language CSS. The BFF (`/api/v1/*`) forwards the chosen language and the HttpOnly access token to the API.
- **Staff portal**: `LanguageService` switches translations, `<html lang dir>`, and the `[dir]` that Angular Material reads, so menus and dialogs flip too. An HTTP interceptor adds `Accept-Language`, only for API calls.

## Crisis engine (phase 2)

`Tamaneen.Crisis.Worker` runs as its own process, so an API outage or a slow language model never stops crisis detection or paging.

- **Gate** (`POST /internal/v1/gate/evaluate`, service key only, not exposed publicly): every patient message and questionnaire answer passes here first. Text is checked against **all** rule packs, whatever language the patient chose. The highest level wins (1 Routine to 4 Critical). At High or Critical, one transaction opens or raises the event, **locks the chat**, queues a page and writes audit. Then the console is alerted live. The patient gets the fixed crisis script (P06) in their language, with the emergency number. **Any failure fails closed**: crisis resources are shown and a system event pages a human.
- **Normalization**: Arabic diacritics, tatweel and letter variants are folded. Arabizi digits are mapped (3→a, 7→h, 5→kh…). Repeated letters are collapsed. Patterns are normalized the same way, so a rule is written once and still matches spelling variants.
- **Rule language**: phrases only, no regular expressions (`a|b`, `a_b`, `x*`, `x?`, `...`). Arabic clitics (و ف ب ل ك س ال) are handled automatically. Each rule carries examples and counter-examples, and a pack whose example fails is refused at startup. Exceptions cover idioms ("أموت فيك", "dying to see you") without hiding a real statement later in the same message.
- **Release gate**: the evaluation set must reach 100% crisis detection, ≥95% Critical-as-Critical, and ≤5% false alarms (`CrisisEvaluationSetTests`).
- **Level 4 protocol**: chat locked at once. Every responder connected to `/hubs/crisis` gets the alert in their own language (`?lang=ar|en`), with the patient's language. Acknowledgement is due within 2 minutes (`Crisis:Sla`). If nobody acknowledges, the escalation loop pages the next tier (on-call → backup → clinical lead) every window. The first acknowledgement wins and later ones get 409. Only a supervisor or the lead closes an event, with a reason, and that is the only way the chat reopens.
- **Paging**: transactional outbox (`crisis.PageRequests`) sent to a signed webhook (`X-Tamaneen-Signature`). Retries within a minute. In Production the worker refuses to start without an HTTPS paging channel.

> The rule packs and evaluation set are a first draft. The clinical lead must review them, and an independent evaluation set written by clinicians is needed before the pilot.

## AI gateway (phase 3)

Every AI feature calls `IAiGateway.AskAsync`; nothing else may reference a model SDK (`AiBoundaryTests`). One call runs:

1. **De-identify** the message and the last turns (Arabic and English). Phones in Latin, Arabic-Indic and +966 form, Saudi national IDs, e-mails, IBANs, Luhn-valid cards, links, IPs, dates and addresses are replaced, and so are names. A name is caught when it follows a cue ("اسمي", "أخوي", "الدكتورة", "my sister", "Dr."), when it is on the name list (with the و/ب/ل clitic), or when it is one of the patient's known identifiers. Names that are also everyday words (أمل, نور, فرح…) count only after a cue, so feelings and risk language reach the model unchanged. The same value keeps the same placeholder (`<PERSON_1>`), and placeholders typed by the user are defused. An optional Presidio sidecar (`Presidio:AnalyzerUrl`) adds recall; if it fails, the built-in recognizers still run, and if those fail nothing is sent.
2. **Prompt** from a versioned template (`Prompts/*.json`). The language rule is bound to the patient's language (ar or en), so the model replies in that language even when the patient mixes in the other one or writes Arabizi.
3. **Model**: Claude via the official Anthropic SDK (`Anthropic:Model`, default `claude-opus-5-5`; `Anthropic:ApiKey` from Key Vault). One call has a 20 s timeout per attempt, one jittered retry for transient errors, a circuit breaker, and a 35 s budget for the whole call. Without a key the gateway serves its fallback, and in Production the host refuses to start.
4. **Validate**: the reply is rejected if it is in the wrong script, empty, cut off or too long; if it contains a placeholder that was never issued, a raw personal value, a phone, link or e-mail, a diagnosis, a medication or dose, or self-harm method detail. A reply in the wrong language gets one rewrite request. Anything else rejected becomes the localized fallback, which points to the care team and the urgent-help button.
5. **Rehydrate** names, places and dates for the patient. Phone, ID, card and IBAN placeholders are never echoed back.
6. **Record** `ai.AiCallRecords`: pseudonym, purpose, prompt version, model, language, outcome, latency and tokens. No content is stored, and the column set is asserted by test.

## Run locally

Requirements: .NET 8 SDK, Node 20+, Docker.

```bash
docker run -d --name tamaneen-sql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Tamaneen!Dev2026' -p 14333:1433 mcr.microsoft.com/mssql/server:2022-latest
dotnet tool restore
TAMANEEN_SQL='Server=localhost,14333;Database=TamaneenDev;User Id=sa;Password=Tamaneen!Dev2026;TrustServerCertificate=True' \
  dotnet ef database update -p backend/src/Tamaneen.Infrastructure
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 dotnet run --project backend/src/Tamaneen.Api --no-launch-profile
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5090 dotnet run --project backend/src/Tamaneen.Crisis.Worker --no-launch-profile

cd web-patient && npm ci && TAMANEEN_API_URL=http://localhost:5080 TAMANEEN_EMERGENCY_NUMBER=911 npm run dev
cd web-staff && npm ci && npx ng serve
```

The development settings in `appsettings.Development.json` are non-secret local values. To try the AI gateway locally, set `Anthropic__ApiKey` in your environment; without it, AI calls return the localized fallback. Production reads keys from Key Vault (`Encryption:Provider=AzureKeyVault`, `Encryption:KeyVaultKeyId`) and refuses the local key provider.

## Tests

```bash
dotnet test                                        # unit + integration (starts SQL Server 2022 in Docker)
cd web-patient && npm run lint && npm run typecheck && npm test && npm run build && E2E_API_URL=http://localhost:5080 npx playwright test
cd web-staff && CHROME_BIN=/path/to/chrome npx ng test --watch=false && npx ng build && E2E_API_URL=http://localhost:5080 npx playwright test
```

Set `TAMANEEN_TEST_SQL` to run the integration tests against an existing server instead of a container.
