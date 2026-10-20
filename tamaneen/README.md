# Tamaneen (طمأنينة)

An AI-assisted mental health platform pilot. Patients use a Next.js web app. Therapists, supervisors, the clinical lead and administrators use an Angular portal. Both talk to one .NET 8 modular monolith on SQL Server 2022.

This folder holds **phase 1**: the infrastructure, the database security model, and both frontends with the bilingual shell (Arabic and English, right-to-left and left-to-right).

## Layout

```text
tamaneen/
  Tamaneen.sln
  backend/src/
    Tamaneen.SharedKernel     RiskLevel, roles, supported languages, pseudonym ids, domain errors
    Tamaneen.Identity         UserAccount, PatientProfile (ciphertext only), contact lookup hashing
    Tamaneen.Clinical         CareAssignment (drives therapist row access)
    Tamaneen.Crisis           CrisisEvent with acknowledgement deadline and close rules
    Tamaneen.AiGateway        AiCallRecord (metadata only, never text)
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

## Run locally

Requirements: .NET 8 SDK, Node 20+, Docker.

```bash
docker run -d --name tamaneen-sql -e ACCEPT_EULA=Y -e 'MSSQL_SA_PASSWORD=Tamaneen!Dev2026' -p 14333:1433 mcr.microsoft.com/mssql/server:2022-latest
dotnet tool restore
TAMANEEN_SQL='Server=localhost,14333;Database=TamaneenDev;User Id=sa;Password=Tamaneen!Dev2026;TrustServerCertificate=True' \
  dotnet ef database update -p backend/src/Tamaneen.Infrastructure
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 dotnet run --project backend/src/Tamaneen.Api --no-launch-profile

cd web-patient && npm ci && TAMANEEN_API_URL=http://localhost:5080 TAMANEEN_EMERGENCY_NUMBER=911 npm run dev
cd web-staff && npm ci && npx ng serve
```

The development settings in `appsettings.Development.json` are non-secret local values. Production reads keys from Key Vault (`Encryption:Provider=AzureKeyVault`, `Encryption:KeyVaultKeyId`) and refuses the local key provider.

## Tests

```bash
dotnet test                                        # unit + integration (starts SQL Server 2022 in Docker)
cd web-patient && npm run lint && npm run typecheck && npm test && npm run build && E2E_API_URL=http://localhost:5080 npx playwright test
cd web-staff && CHROME_BIN=/path/to/chrome npx ng test --watch=false && npx ng build && E2E_API_URL=http://localhost:5080 npx playwright test
```

Set `TAMANEEN_TEST_SQL` to run the integration tests against an existing server instead of a container.
