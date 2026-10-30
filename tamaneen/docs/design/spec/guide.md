# Mental Health Platform — Expert Review & Build Guide

Oct 19, 2026 · @ibrahim

The engineering spec is a sound base, but it is not ready to code from yet. The panel found 9 blocking defects: the crisis firewall is English-only regex with no fail-safe or paging, assessment scores are inferred by the LLM, one table cannot hold the rows the spec puts in it, and the audit trigger can be bypassed. This guide reviews the spec, then gives the corrected design that developers can build from.

## 1. Expert panel review

The spec's direction is right: a modular monolith, a crisis gate in front of the LLM, pseudonymous identity, and an evidence inspector for therapists. Its gaps are in the details that keep patients safe and the data defensible. Severity: **Blocker** = must be fixed before any code is written on that area; **High** = fix before the pilot starts; **Medium** = fix during the pilot.

**Panel**

| Expert | Lens |
| --- | --- |
| A — System Architect & Security | Cloud architecture, identity, encryption, threat model |
| C — Healthcare Compliance & Clinical Lead | HIPAA/GDPR, clinical safety, liability, crisis protocol |
| U — UX/UI & Product Designer | Patient and clinician experience, accessibility, Arabic/RTL |
| P — Product Manager & Business Analyst | Scope, workflow efficiency, pilot metrics, operations |
| D — Backend/Frontend & Database Engineer | Schema, APIs, data integrity, implementability |

### 1.1 Blocking defects (9)

| ID | Expert | Finding in the spec | Why it matters | Required fix |
| --- | --- | --- | --- | --- |
| B-01 | C, A | The crisis firewall is one English regex. The pattern has a bug (`want\ resolved` matches nothing useful), "suicide" alone fires Level 4 (so "suicide prevention" does too), there is no negation, and no Arabic. | Missed crises in any language but English; constant false alarms in English. | A versioned, clinician-owned rule set per language (Arabic + English), with normalisation (diacritics, Arabizi, spelling variants), negation and context rules, plus a local risk classifier and questionnaire rules (section 2.5). |
| B-02 | A, C | The firewall only notifies the SignalR group "Supervisors". Nothing pages a person who is not looking at the console, nothing acts when `EscalationTimerExpiresAtUtc` passes, and if the firewall throws, the code path falls through to the LLM. | A crisis at 03:00 reaches nobody. | Fail-closed design: any error = crisis path. A durable escalation worker pages on-call by phone call + SMS, then backup, then clinical lead, on timers stored in the database. |
| B-03 | C | "Standardized metrics (PHQ-9/GAD-7) are derived dynamically from conversation outputs." | An LLM-inferred PHQ-9 is not a PHQ-9. It is clinically invalid and indefensible. | Ask each item word for word as a structured card; store item answers; score with code (section 3). The LLM never produces a score. |
| B-04 | D | Therapist registration "creates a verification task in `App.TriageQueue`", but that table requires `PatientId` and `IntakeSessionId` (NOT NULL, with FKs). | The insert fails; the feature cannot be built as written. | A separate `App.TherapistApplications` table and verification queue (section 3). |
| B-05 | A, C | Audit immutability relies on an `UPDATE, DELETE` trigger. `TRUNCATE`, `DISABLE TRIGGER` or `DROP` by a db\_owner bypass it. | The audit trail is not defensible to a regulator. | SQL Server 2022 append-only ledger table (`LEDGER = ON (APPEND_ONLY = ON)`), daily digest exported to immutable blob storage, and no db\_owner for the app login. |
| B-06 | A | `EmailHash` is an unsalted SHA-256, and signup "generates SHA-256 hash of provided credentials". | Emails can be recovered by dictionary attack; a fast hash for passwords is crackable. | Passwords: ASP.NET Core Identity hasher (PBKDF2, 600k iterations) or Argon2id. Lookups: HMAC-SHA-256 with a secret pepper kept in Key Vault. |
| B-07 | C, D | Risk levels run 1 = Low → 4 = Critical, the opposite of the PRD (Level 1 = crisis). PHQ-9 20–27 maps to no level, PHQ-9 item 9 is ignored, and GAD-7 uses `> 10` instead of the standard `≥ 10`. | Teams will read levels backwards; the most severe depression scores fall through. | One shared enum, `RiskLevel { Routine = 1, Elevated = 2, High = 3, Critical = 4 }`, used by the PRD, code and UI. Complete threshold table in section 2.5. |
| B-08 | A, D | RLS is promised but no predicate or policy is defined. The therapist dashboard also lists "Unassigned" patients with "Accept Patient Assignment", which RLS would hide and the triage process forbids. | Without a policy, any therapist can read any patient. | RLS predicate functions using `SESSION_CONTEXT` (section 3). Therapists see only patients with an active `CareAssignment`; routing is done by supervisors only. |
| B-09 | C, U, D | Signup collects only an alias and password, yet `EmailHash` is NOT NULL. There is no consent step, no age (18+) check, no location/jurisdiction check, no emergency contact, and no account recovery. | Insert fails; a minor or out-of-area patient can enroll; a crisis cannot be acted on; a locked-out patient loses their record. | Onboarding steps: eligibility → consents → contact (email or phone, verified) → optional safety contact + location consent. Recovery via verified contact + recovery codes. |

### 1.2 High-priority findings

| ID | Expert | Finding | Required fix |
| --- | --- | --- | --- |
| H-01 | D, A | "Always Encrypted" columns are typed `VARBINARY(MAX)`. Always Encrypted encrypts typed columns declared `ENCRYPTED WITH (...)`; a VARBINARY column means app-side encryption. The spec mixes the two. | Use Always Encrypted for identity/contact columns; use app-level envelope encryption (one key per patient, wrapped by Key Vault) for clinical free text, which also enables crypto-shredding. |
| H-02 | C | `CrisisEvents.RawMessageExcerpt` stores up to 500 characters in plain text, and "RedactSensitiveText" only truncates. | Encrypt the excerpt with the patient key; show it only after a supervisor opens the event. |
| H-03 | P, C | Video rooms, "Connect Direct Audio" and WebRTC are in the spec, but the pilot PRD excludes video and recording. | Remove from pilot. Store an external meeting link per appointment; crisis contact uses the phone. |
| H-04 | A, C | Patient text goes to OpenAI/Anthropic after regex + NER PII stripping. NER misses names in Arabic and free text. | Only a BAA/DPA-covered, in-region, zero-retention endpoint. Treat stripped text as still PHI. |
| H-05 | C | Two sign-offs are described (therapist "Approve Assessment" and supervisor "Approve Routing") without saying which is required for what. | Supervisor approves triage and routing; therapist approves the clinical file before it enters the record. Both are logged. |
| H-06 | C | "Escalate to Local Emergency Services (911)" claims to "dispatch response teams". The platform cannot dispatch anyone, and 911 is US-only. | The button opens a scripted protocol with the local number from configuration (e.g. 997/911/112); the clinician makes the call and logs the outcome. |
| H-07 | D | Missing tables: consents, eligibility, therapist availability, care assignments, treatment plans, AI suggestions (model/prompt versions), evidence links, check-ins, on-call schedule, crisis actions, notifications/outbox, documents. | Full corrected schema in section 3. |
| H-08 | A | Refresh tokens stored encrypted (reversible) and JWT hashes stored per token. | Store only a hash of the refresh token; rotate on use; detect reuse and revoke the token family. |
| H-09 | C, D | `Patients.CurrentRiskLevel` defaults to 1 (Low) before any assessment. | NULL = not yet assessed; the UI shows "Not assessed". |
| H-10 | U | No Arabic or right-to-left support is specified, and crisis content is English-only. | Arabic + English from day one: `dir="rtl"`, logical CSS properties, localized crisis lines and scripts, validated Arabic PHQ-9/GAD-7. |
| H-11 | A | No threat model for prompt injection: a patient can type instructions that make the AI skip safety questions or leak its prompt. | The crisis gate runs before the LLM; fixed questionnaire cards are not generated by the LLM; output validator blocks diagnosis words and off-script content. |

### 1.3 Medium findings

| ID | Expert | Finding | Required fix |
| --- | --- | --- | --- |
| M-01 | U | MFA is mandatory at patient signup. A distressed person may give up. | MFA optional for patients (strongly offered), required for all staff. |
| M-02 | U, C | Public therapist ratings and hourly rates in a free mental-health pilot invite harm and conflict. | Remove public ratings and rates from the pilot; collect private feedback for supervisors only. |
| M-03 | U | The mood widget uses 1–5 while the PRD uses 0–10. | One 0–10 scale everywhere. |
| M-04 | U | Patients see a "Score Breakdown" of stress levels after intake. Raw scores can alarm people and read as a diagnosis. | Show plain-language reflections only; scores are clinician-facing. |
| M-05 | P | No pilot metrics or instrumentation are defined in the spec. | Event tracking for the PRD metrics, with no PHI in analytics. |
| M-06 | D | `SpecialtiesJson` in a JSON column makes matching queries slow and unvalidated. | A `TherapistSpecialties` join table with a lookup table. |
| M-07 | P | "Submit Intake Early" could skip the safety questions. | Early submit always runs the C-SSRS screener first. |

## 2. Architecture and tech stack

Patients use a Next.js web app; therapists, supervisors and admins use one Angular staff portal; both call one .NET 8 modular-monolith API on SQL Server 2022. The crisis service runs as its own process so that an API outage or a slow LLM never stops crisis detection or paging.

### 2.1 Technology stack

| Layer | Choice | Why |
| --- | --- | --- |
| Patient web app | Next.js 14 (App Router), TypeScript, React Server Components, Tailwind with logical (RTL-safe) properties, next-intl for Arabic + English, PWA service worker | Fast first load on phones, server-side rendering of public pages, and a server-side BFF that keeps tokens out of the browser |
| Patient BFF | Next.js route handlers (`/api/*`) proxy to the .NET API; tokens in HttpOnly, Secure, SameSite=Strict cookies | No access token is ever readable by page JavaScript |
| Staff portal | Angular 17+ (standalone components, signals, NgRx SignalStore), Angular Material + CDK, Angular i18n with RTL, role-based route guards | Dense dashboards and long forms; strong typing for a long-lived clinical tool |
| API | .NET 8 ASP.NET Core, Clean Architecture modular monolith, MediatR, FluentValidation, EF Core 8 for writes, Dapper for read models, RFC 7807 ProblemDetails, API versioning | One deployable, clear module borders, easy to split later |
| API contracts | OpenAPI 3.1 → generated TypeScript clients (NSwag) for both frontends | One source of truth for request and response types |
| Crisis service | Separate .NET 8 worker (same solution): rule engine, local ONNX risk classifier, escalation loop | Keeps running when the API or LLM is down; its own health checks and alerts |
| Background jobs | Hangfire on SQL Server (reminders, weekly reports, retention purge) + transactional outbox | Durable, retryable, visible jobs |
| Real-time | ASP.NET Core SignalR with Azure SignalR Service | Live intake chat, crisis console, dashboard updates |
| Database | SQL Server 2022 (Azure SQL Managed Instance) | Ledger tables, Row-Level Security, Always Encrypted, point-in-time restore |
| Keys and secrets | Azure Key Vault Managed HSM | Column master keys, per-patient key wrapping, HMAC pepper |
| Files | Azure Blob Storage, private, customer-managed keys, malware scanning on upload | License documents |
| De-identification | Microsoft Presidio as a sidecar container, with Arabic and English recognisers | Strips names, phones, emails, addresses before text reaches the LLM |
| LLM | One BAA/DPA-covered, in-region, zero-retention endpoint behind an internal AI gateway module | Provider can be swapped without touching business code |
| Paging and messaging | Voice + SMS provider under BAA for paging (primary) plus an incident-paging tool as a second channel; email under BAA | Two independent channels for crisis alerts |
| Edge | Azure Front Door with WAF, rate limiting, bot protection | Blocks abuse before it reaches the API |
| Observability | OpenTelemetry → Azure Monitor / Application Insights, Serilog, with a PHI-scrubbing processor | Traces and alerts without patient data in logs |
| Delivery | GitHub Actions, Bicep (or Terraform), dev / staging / prod, CodeQL, dependency and secret scanning | Repeatable, reviewed releases |
| Testing | xUnit + Testcontainers (SQL Server), Vitest (Next.js), Jest/Karma (Angular), Playwright end-to-end, crisis evaluation suite as a CI gate | Crisis rules cannot ship without passing the evaluation set |

Azure is the recommendation because it pairs well with .NET and SQL Server; the region must be inside the pilot jurisdiction or one its health-data law allows.

### 2.2 Deployment view

&#91;embedded content: deployment view · 2 apps, API, crisis worker, AI path, database\]

The API calls the crisis worker before anything else; only messages that pass go out through de-identification to the LLM. The crisis worker writes its own events and pages people directly, so it does not depend on the API being up.

### 2.3 Solution layout

```text
/src
  /web-patient            Next.js 14 app (patients) + BFF route handlers
  /web-staff              Angular 17 app (therapists, supervisors, admins)
  /api
    Platform.Api          ASP.NET Core host: endpoints, auth, SignalR hubs
    Platform.SharedKernel Result types, RiskLevel enum, clock, ids, errors
    /Modules
      Identity            sign-up, login, MFA, recovery, tokens, roles
      Consent             consent versions, records, eligibility
      Intake              intake sessions, messages, questionnaire engine
      Assessment          PHQ-9 / GAD-7 / C-SSRS item storage and scoring
      Triage              triage cases, decisions, matching suggestions
      Clinical            care assignments, clinical file, treatment plans
      Scheduling          availability, appointments, reminders
      Homework            tasks, check-ins, weekly reports, feedback
      Credentialing       therapist applications, documents, licenses
      AiGateway           de-identification, prompts, LLM calls, validation
      Audit               ledger writes, access-log queries, exports
      Notifications       outbox dispatch: email, SMS, push, SignalR
  /crisis
    Crisis.Worker         rule engine, classifier, escalation loop, paging
    Crisis.Rules          versioned rule packs (ar, en) + evaluation set
/db                       SQL migrations (DbUp), RLS policies, seed data
/infra                    Bicep modules per environment
/tests                    unit, integration, contract, e2e, crisis-eval
```

Modules talk only through public interfaces or domain events; each owns its own tables and never queries another module's tables directly.

### 2.4 How one patient message flows

1. The patient sends a message on `/intake`. The Next.js BFF attaches the access token and forwards it to `POST /v1/intake/{id}/messages`.
2. The API stores the message (encrypted with the patient key), then calls the crisis service synchronously with a 1.5-second budget.
3. **Crisis service:** normalises the text, runs rules and the classifier, and returns `Pass` or `Blocked`. On `Blocked` it opens a crisis event and the outbox row in one transaction, and the API returns the fixed crisis script. The LLM is never called.
4. **On `Pass`:** the questionnaire engine decides the next step. If the next item is a PHQ-9 / GAD-7 / C-SSRS question, code renders it as a fixed card; the LLM is not involved.
5. Otherwise the AI gateway de-identifies the text, builds the prompt from the versioned template and the intake state, and calls the LLM (20-second timeout, one retry).
6. The output validator checks the reply: no diagnosis words, no medication advice, correct language, no echoed personal data. A failed check gives a safe fixed reply.
7. The reply is stored with its model and prompt versions and pushed to the patient over SignalR.

### 2.5 Crisis service design

This table replaces the escalation matrix in the spec and the levels in the PRD. The highest level triggered wins; the AI can raise a level but never lower it.

| Level | Name | Triggers (any one) | Automatic action | Human response |
| --- | --- | --- | --- | --- |
| 4 | Critical | C-SSRS Q4 or Q5 "yes"; C-SSRS Q6 "yes" in the past 3 months; critical keyword rule (plan, intent, means, timeline); reported overdose or acute self-injury; threat to harm another person | Lock chat, full-screen crisis screen with local emergency number, page on-call by call + SMS, notify assigned therapist | Acknowledge ≤ 2 min; contact patient ≤ 5 min |
| 3 | High | C-SSRS Q1, Q2 or Q3 "yes"; PHQ-9 item 9 ≥ 1; high keyword rule (passive ideation); classifier ≥ threshold; abuse disclosure | Pause intake, fixed safety script, crisis line shown, page on-call | Acknowledge ≤ 15 min; contact ≤ 60 min |
| 2 | Elevated | PHQ-9 ≥ 15 with item 9 = 0; GAD-7 ≥ 15 | Priority triage queue | Supervisor review ≤ 4 h |
| 1 | Routine | PHQ-9 ≤ 14, GAD-7 ≤ 14, no risk items | Standard triage queue | Supervisor review ≤ 24 h |

**Rules for the service**

- **Fail closed:** any exception in the gate opens a system crisis event and shows crisis resources. The message is never passed to the LLM on error.
- **Rule packs** are JSON files per language, versioned in `Crisis.Rules`. Each rule has an id, level, patterns, negation patterns and examples. A change needs a clinician-approved pull request and a green evaluation run.
- **Normalisation before matching:** lower-case, Arabic diacritics and tatweel removed, alef/ya/ta-marbuta forms unified, Arabizi digits mapped (3→ع, 7→ح, 2→ء), repeated letters collapsed.
- **Classifier:** a local ONNX model, 300 ms timeout. A timeout does not block the message: rules still apply. If the classifier is down for more than 60 seconds, a CR-7 system event pages the on-call engineer and clinician.
- **Escalation loop:** every 15 seconds the worker selects open events whose `NextEscalationAtUtc` has passed (`WITH (UPDLOCK, READPAST)`), pages the next tier (on-call → backup → clinical lead), and sets the next deadline. Timers live in the database, so a restart loses nothing.
- **Lock:** a Critical or High event sets `IntakeSessions.Status = 'Locked'`; only a clinician closing the event unlocks it.

```csharp
public sealed class CrisisGate(
    ITextNormalizer normalizer, IRulePackProvider rules, IQuestionnaireRules questionnaire,
    IRiskClassifier classifier, ICrisisEventStore events, ICrisisScripts scripts,
    ILogger<CrisisGate> log) : ICrisisGate
{
    public async Task<GateResult> EvaluateAsync(PatientMessage msg, CancellationToken ct)
    {
        try
        {
            var pack = rules.Current(msg.Language);
            var text = normalizer.Normalize(msg.Text, msg.Language);

            var hits = new List<RuleHit>();
            hits.AddRange(pack.Match(text));                       // keyword + negation rules
            hits.AddRange(questionnaire.Evaluate(msg.StructuredAnswer));

            var score = await classifier.TryScoreAsync(text, TimeSpan.FromMilliseconds(300), ct);
            if (score is { } s && s >= pack.ClassifierThreshold)
                hits.Add(RuleHit.Classifier(s));

            var level = hits.Count == 0 ? RiskLevel.None : hits.Max(h => h.Level);
            if (level < RiskLevel.High)
                return GateResult.Pass(level, hits);

            // One transaction: crisis event + escalation schedule + outbox page request
            var evt = await events.OpenAsync(msg, level, hits, pack.Version, ct);
            return GateResult.Blocked(evt.Id, level, scripts.For(level, msg.Language));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogCritical(ex, "Crisis gate failed; failing closed");
            var evt = await events.OpenSystemFailureAsync(msg.PatientId, ct);
            return GateResult.Blocked(evt.Id, RiskLevel.High, scripts.SystemFailure(msg.Language));
        }
    }
}
```

### 2.6 AI gateway rules

| Rule | Implementation |
| --- | --- |
| Only one way out | All LLM calls go through `AiGateway.CompleteAsync`; a CI check fails the build if any other module references an LLM SDK |
| De-identify first | Presidio sidecar replaces names, phones, emails, national ids, addresses with tokens (`<PERSON_1>`); the token map stays in memory for the request only |
| Versioned prompts | Prompt templates live in the repo with an id and version; every call stores `PromptVersion`, `ModelId`, latency and token counts |
| Structured output | Summaries are requested as JSON matching a schema; each statement carries the `MessageId` values it came from. A statement with no source is dropped |
| Output validator | Blocks diagnosis labels and medication advice in patient-facing text, wrong language, links, and any echoed token from the de-identification map |
| Budgets | 20 s timeout, 1 retry with jitter, circuit breaker after 5 failures in 60 s (Polly) |
| Fallback | When the circuit is open, intake switches to the static form with the same questionnaires; homework replies use fixed templates |

### 2.7 Identity and session security

| Area | Design |
| --- | --- |
| Patient sign-in | Email or phone (verified by one-time code) + password; passkeys and TOTP offered; 10 recovery codes shown once at signup |
| Staff sign-in | Organisation identity provider (OIDC) or ASP.NET Identity with TOTP/passkey required; no staff account without MFA |
| Tokens | Access token 10 min; refresh token 30 days (patients) / 8 h (staff), rotated on every use, stored only as a hash; reuse revokes the whole family |
| Claims | `sub` (UserId), `role`, `pid` (PseudonymId); no names or emails in tokens |
| Step-up | Decrypting a patient's emergency contact or location, exporting audit data, and approving a license each need a fresh MFA check (within 5 min) |
| Browser | Strict CSP, HSTS, `frame-ancestors 'none'`, CSRF token on BFF and Angular calls, no PHI in URLs or local storage |
| Rate limits | Login 5 attempts / 15 min per account and per IP; intake messages 30 / min per patient; signup 3 / hour per IP |

## 3. Data model and database security

The corrected schema has 43 tables across three schemas (`App`, `Audit`, `Security`). Three rules shape it: identity and contact data are encrypted with Always Encrypted; clinical free text is encrypted in the application with one key per patient; and every table that holds patient data carries `PatientId` so Row-Level Security can filter it.

### 3.1 Encryption model

| Data | Method | Why |
| --- | --- | --- |
| Email, phone, legal name, emergency contact, location, license number | Always Encrypted (randomized; deterministic only for the license number, so duplicates can be detected) with the column master key in Key Vault Managed HSM | DBAs and the database engine never see plain text |
| Chat messages, summaries, notes, treatment plans, check-in notes, crisis excerpts | AES-256-GCM in the application with a per-patient data key, wrapped by a Key Vault key and stored in `App.PatientKeys` | Deleting the patient's data key makes all their clinical text unreadable (crypto-shredding), including in backups |
| Login lookup (email or phone) | HMAC-SHA-256 with a pepper held in Key Vault | Lets the API find an account without storing a reversible or guessable value |
| Passwords, refresh tokens, recovery codes | ASP.NET Identity hasher (PBKDF2-SHA-256, 600,000 iterations) for passwords and recovery codes; SHA-256 for random 256-bit refresh tokens | Industry practice for each kind of secret |
| Everything at rest | Transparent Data Encryption with a customer-managed key | Covers files, logs and backups |

### 3.2 Core DDL

The scripts below are the tables the review changed or added. Run them through DbUp migrations in this order.

```sql
-- 0. SCHEMAS
CREATE SCHEMA [App];      GO
CREATE SCHEMA [Audit];    GO
CREATE SCHEMA [Security]; GO

-- 1. IDENTITY
CREATE TABLE [App].[Users] (
    [UserId]            UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Users_Id] DEFAULT NEWSEQUENTIALID(),
    [PseudonymId]       NVARCHAR(32)     NOT NULL,          -- e.g. P-7Q4K-82XM, shown in staff UIs
    [Role]              NVARCHAR(20)     NOT NULL,
    [LoginLookupHmac]   VARBINARY(32)    NOT NULL,          -- HMAC-SHA256(pepper, normalized email/phone)
    [PasswordHash]      NVARCHAR(400)    NOT NULL,
    [MfaRequired]       BIT              NOT NULL CONSTRAINT [DF_Users_MfaReq] DEFAULT 0,
    [TotpSecretCipher]  VARBINARY(256)   NULL,
    [PreferredLocale]   NVARCHAR(10)     NOT NULL CONSTRAINT [DF_Users_Locale] DEFAULT N'ar',
    [Status]            NVARCHAR(20)     NOT NULL CONSTRAINT [DF_Users_Status] DEFAULT N'PendingVerification',
    [FailedLoginCount]  INT              NOT NULL CONSTRAINT [DF_Users_Fail] DEFAULT 0,
    [LockoutEndUtc]     DATETIME2(3)     NULL,
    [CreatedAtUtc]      DATETIME2(3)     NOT NULL CONSTRAINT [DF_Users_Created] DEFAULT SYSUTCDATETIME(),
    [LastLoginUtc]      DATETIME2(3)     NULL,
    [RowVersion]        ROWVERSION       NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([UserId]),
    CONSTRAINT [UQ_Users_Pseudonym] UNIQUE ([PseudonymId]),
    CONSTRAINT [UQ_Users_Lookup] UNIQUE ([LoginLookupHmac]),
    CONSTRAINT [CK_Users_Role] CHECK ([Role] IN (N'Patient', N'Therapist', N'Supervisor', N'ClinicalLead', N'Admin')),
    CONSTRAINT [CK_Users_Status] CHECK ([Status] IN (N'PendingVerification', N'Active', N'Locked', N'Suspended', N'Deleted'))
);

CREATE TABLE [App].[UserContacts] (
    [UserId]        UNIQUEIDENTIFIER NOT NULL,
    [ContactType]   NVARCHAR(10)     NOT NULL,              -- Email | Phone
    [ContactValue]  NVARCHAR(256)    ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = [CEK_Identity],
                        ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NOT NULL,
    [VerifiedAtUtc] DATETIME2(3)     NULL,
    CONSTRAINT [PK_UserContacts] PRIMARY KEY ([UserId], [ContactType]),
    CONSTRAINT [FK_UserContacts_Users] FOREIGN KEY ([UserId]) REFERENCES [App].[Users]([UserId]),
    CONSTRAINT [CK_UserContacts_Type] CHECK ([ContactType] IN (N'Email', N'Phone'))
);

CREATE TABLE [App].[RefreshTokens] (
    [TokenId]        UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_RT_Id] DEFAULT NEWSEQUENTIALID(),
    [UserId]         UNIQUEIDENTIFIER NOT NULL,
    [FamilyId]       UNIQUEIDENTIFIER NOT NULL,             -- all rotations of one login share a family
    [TokenHash]      VARBINARY(32)    NOT NULL,
    [ExpiresAtUtc]   DATETIME2(3)     NOT NULL,
    [ConsumedAtUtc]  DATETIME2(3)     NULL,                 -- set when rotated; reuse => revoke family
    [RevokedAtUtc]   DATETIME2(3)     NULL,
    [CreatedAtUtc]   DATETIME2(3)     NOT NULL CONSTRAINT [DF_RT_Created] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([TokenId]),
    CONSTRAINT [UQ_RefreshTokens_Hash] UNIQUE ([TokenHash]),
    CONSTRAINT [FK_RefreshTokens_Users] FOREIGN KEY ([UserId]) REFERENCES [App].[Users]([UserId])
);

-- 2. CONSENT
CREATE TABLE [App].[ConsentDocuments] (
    [ConsentDocId]   INT IDENTITY(1,1) NOT NULL,
    [ConsentType]    NVARCHAR(30)  NOT NULL,   -- PlatformUse | AiProcessing | ShareWithTherapist | ResearchDeidentified | LocationForEmergency
    [Version]        NVARCHAR(10)  NOT NULL,
    [Locale]         NVARCHAR(10)  NOT NULL,
    [BodySha256]     VARBINARY(32) NOT NULL,
    [IsRequired]     BIT           NOT NULL,
    [PublishedAtUtc] DATETIME2(3)  NOT NULL,
    CONSTRAINT [PK_ConsentDocuments] PRIMARY KEY ([ConsentDocId]),
    CONSTRAINT [UQ_ConsentDocuments] UNIQUE ([ConsentType], [Version], [Locale])
);

CREATE TABLE [App].[ConsentRecords] (
    [ConsentRecordId] BIGINT IDENTITY(1,1) NOT NULL,
    [UserId]          UNIQUEIDENTIFIER NOT NULL,
    [ConsentDocId]    INT              NOT NULL,
    [Decision]        NVARCHAR(10)     NOT NULL,            -- Granted | Withdrawn
    [Channel]         NVARCHAR(20)     NOT NULL,            -- Web | Staff
    [RecordedAtUtc]   DATETIME2(3)     NOT NULL CONSTRAINT [DF_CR_At] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_ConsentRecords] PRIMARY KEY ([ConsentRecordId]),
    CONSTRAINT [FK_ConsentRecords_Users] FOREIGN KEY ([UserId]) REFERENCES [App].[Users]([UserId]),
    CONSTRAINT [FK_ConsentRecords_Docs] FOREIGN KEY ([ConsentDocId]) REFERENCES [App].[ConsentDocuments]([ConsentDocId]),
    CONSTRAINT [CK_ConsentRecords_Decision] CHECK ([Decision] IN (N'Granted', N'Withdrawn'))
) WITH (LEDGER = ON (APPEND_ONLY = ON));                    -- consent history can never be rewritten

-- 3. PATIENTS
CREATE TABLE [App].[Patients] (
    [PatientId]           UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_Patients_Id] DEFAULT NEWSEQUENTIALID(),
    [UserId]              UNIQUEIDENTIFIER NOT NULL,
    [AliasName]           NVARCHAR(50)     NOT NULL,
    [AdultConfirmedAtUtc] DATETIME2(3)     NOT NULL,
    [JurisdictionCode]    NVARCHAR(10)     NOT NULL,        -- pilot region code; checked at signup
    [PreferredLanguage]   NVARCHAR(10)     NOT NULL,
    [CurrentRiskLevel]    TINYINT          NULL,            -- NULL = not yet assessed
    [Status]              NVARCHAR(20)     NOT NULL CONSTRAINT [DF_Patients_Status] DEFAULT N'Onboarding',
    [CreatedAtUtc]        DATETIME2(3)     NOT NULL CONSTRAINT [DF_Patients_Created] DEFAULT SYSUTCDATETIME(),
    [RowVersion]          ROWVERSION       NOT NULL,
    CONSTRAINT [PK_Patients] PRIMARY KEY ([PatientId]),
    CONSTRAINT [UQ_Patients_User] UNIQUE ([UserId]),
    CONSTRAINT [FK_Patients_Users] FOREIGN KEY ([UserId]) REFERENCES [App].[Users]([UserId]),
    CONSTRAINT [CK_Patients_Risk] CHECK ([CurrentRiskLevel] BETWEEN 1 AND 4),
    CONSTRAINT [CK_Patients_Status] CHECK ([Status] IN (N'Onboarding', N'Intake', N'AwaitingTriage', N'Matched', N'InCare', N'Discharged', N'Deleted'))
);

CREATE TABLE [App].[PatientKeys] (
    [PatientId]      UNIQUEIDENTIFIER NOT NULL,
    [WrappedDek]     VARBINARY(512)   NULL,                 -- NULL after crypto-shred
    [KekKeyVersion]  NVARCHAR(64)     NOT NULL,
    [CreatedAtUtc]   DATETIME2(3)     NOT NULL CONSTRAINT [DF_PK_Created] DEFAULT SYSUTCDATETIME(),
    [ShreddedAtUtc]  DATETIME2(3)     NULL,
    CONSTRAINT [PK_PatientKeys] PRIMARY KEY ([PatientId]),
    CONSTRAINT [FK_PatientKeys_Patients] FOREIGN KEY ([PatientId]) REFERENCES [App].[Patients]([PatientId])
);

CREATE TABLE [App].[PatientSafetyInfo] (
    [PatientId]             UNIQUEIDENTIFIER NOT NULL,
    [EmergencyContactName]  NVARCHAR(100) ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = [CEK_Identity],
                                ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NULL,
    [EmergencyContactPhone] NVARCHAR(32)  ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = [CEK_Identity],
                                ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NULL,
    [PatientPhone]          NVARCHAR(32)  ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = [CEK_Identity],
                                ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NULL,
    [LocationText]          NVARCHAR(300) ENCRYPTED WITH (COLUMN_ENCRYPTION_KEY = [CEK_Identity],
                                ENCRYPTION_TYPE = RANDOMIZED, ALGORITHM = 'AEAD_AES_256_CBC_HMAC_SHA_256') NULL,
    [LocationConsentAtUtc]  DATETIME2(3)  NULL,
    [UpdatedAtUtc]          DATETIME2(3)  NOT NULL CONSTRAINT [DF_PSI_Upd] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_PatientSafetyInfo] PRIMARY KEY ([PatientId]),
    CONSTRAINT [FK_PatientSafetyInfo_Patients] FOREIGN KEY ([PatientId]) REFERENCES [App].[Patients]([PatientId])
);

-- 4. INTAKE AND ASSESSMENTS
CREATE TABLE [App].[IntakeSessions] (
    [IntakeSessionId]  UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_IS_Id] DEFAULT NEWSEQUENTIALID(),
    [PatientId]        UNIQUEIDENTIFIER NOT NULL,
    [Status]           NVARCHAR(25)     NOT NULL CONSTRAINT [DF_IS_Status] DEFAULT N'InProgress',
    [Mode]             NVARCHAR(10)     NOT NULL CONSTRAINT [DF_IS_Mode] DEFAULT N'Chat',   -- Chat | Form (fallback)
    [CurrentStep]      NVARCHAR(50)     NOT NULL,           -- intake state machine node
    [CompletionPct]    TINYINT          NOT NULL CONSTRAINT [DF_IS_Pct] DEFAULT 0,
    [StartedAtUtc]     DATETIME2(3)     NOT NULL CONSTRAINT [DF_IS_Start] DEFAULT SYSUTCDATETIME(),
    [LastActivityUtc]  DATETIME2(3)     NOT NULL CONSTRAINT [DF_IS_Last] DEFAULT SYSUTCDATETIME(),
    [SubmittedAtUtc]   DATETIME2(3)     NULL,
    [ExpiresAtUtc]     DATETIME2(3)     NOT NULL,           -- StartedAt + 14 days
    [RowVersion]       ROWVERSION       NOT NULL,
    CONSTRAINT [PK_IntakeSessions] PRIMARY KEY ([IntakeSessionId]),
    CONSTRAINT [FK_IntakeSessions_Patients] FOREIGN KEY ([PatientId]) REFERENCES [App].[Patients]([PatientId]),
    CONSTRAINT [CK_IntakeSessions_Status] CHECK ([Status] IN
        (N'InProgress', N'Paused', N'Locked', N'AwaitingPatientReview', N'Submitted', N'Expired', N'Abandoned'))
);
CREATE UNIQUE INDEX [UX_IntakeSessions_OpenPerPatient] ON [App].[IntakeSessions]([PatientId])
    WHERE [Status] IN (N'InProgress', N'Paused', N'Locked', N'AwaitingPatientReview');

CREATE TABLE [App].[IntakeMessages] (
    [MessageId]        UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_IM_Id] DEFAULT NEWSEQUENTIALID(),
    [IntakeSessionId]  UNIQUEIDENTIFIER NOT NULL,
    [PatientId]        UNIQUEIDENTIFIER NOT NULL,           -- duplicated for RLS
    [TurnIndex]        INT              NOT NULL,
    [ClientMessageId]  UNIQUEIDENTIFIER NULL,               -- idempotency key from the client
    [SenderRole]       NVARCHAR(10)     NOT NULL,           -- Patient | AI | System
    [Kind]             NVARCHAR(20)     NOT NULL,           -- Text | QuestionCard | Answer | CrisisScript
    [Ciphertext]       VARBINARY(MAX)   NOT NULL,           -- AES-256-GCM, patient key
    [GateOutcome]      NVARCHAR(10)     NULL,               -- Pass | Blocked | Error (patient messages)
    [GateRuleIds]      NVARCHAR(400)    NULL,
    [PromptVersion]    NVARCHAR(40)     NULL,               -- AI messages
    [ModelId]          NVARCHAR(80)     NULL,
    [CreatedAtUtc]     DATETIME2(3)     NOT NULL CONSTRAINT [DF_IM_At] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_IntakeMessages] PRIMARY KEY ([MessageId]),
    CONSTRAINT [UQ_IntakeMessages_Turn] UNIQUE ([IntakeSessionId], [TurnIndex]),
    CONSTRAINT [FK_IntakeMessages_Sessions] FOREIGN KEY ([IntakeSessionId]) REFERENCES [App].[IntakeSessions]([IntakeSessionId]),
    CONSTRAINT [CK_IntakeMessages_Sender] CHECK ([SenderRole] IN (N'Patient', N'AI', N'System'))
);
CREATE UNIQUE INDEX [UX_IntakeMessages_ClientId] ON [App].[IntakeMessages]([IntakeSessionId], [ClientMessageId])
    WHERE [ClientMessageId] IS NOT NULL;

CREATE TABLE [App].[Assessments] (
    [AssessmentId]     UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AS_Id] DEFAULT NEWSEQUENTIALID(),
    [PatientId]        UNIQUEIDENTIFIER NOT NULL,
    [InstrumentCode]   NVARCHAR(20)     NOT NULL,           -- PHQ9 | GAD7 | CSSRS_SCREEN
    [InstrumentLocale] NVARCHAR(10)     NOT NULL,           -- validated translation used
    [Source]           NVARCHAR(10)     NOT NULL,           -- Intake | Homework | Booking
    [IntakeSessionId]  UNIQUEIDENTIFIER NULL,
    [TotalScore]       TINYINT          NULL,               -- computed by code when complete
    [SeverityBand]     NVARCHAR(30)     NULL,
    [ScoringVersion]   NVARCHAR(10)     NOT NULL,
    [StartedAtUtc]     DATETIME2(3)     NOT NULL CONSTRAINT [DF_AS_Start] DEFAULT SYSUTCDATETIME(),
    [CompletedAtUtc]   DATETIME2(3)     NULL,
    CONSTRAINT [PK_Assessments] PRIMARY KEY ([AssessmentId]),
    CONSTRAINT [FK_Assessments_Patients] FOREIGN KEY ([PatientId]) REFERENCES [App].[Patients]([PatientId]),
    CONSTRAINT [CK_Assessments_Instrument] CHECK ([InstrumentCode] IN (N'PHQ9', N'GAD7', N'CSSRS_SCREEN'))
);

CREATE TABLE [App].[AssessmentItemResponses] (
    [AssessmentId]   UNIQUEIDENTIFIER NOT NULL,
    [PatientId]      UNIQUEIDENTIFIER NOT NULL,
    [ItemNo]         TINYINT          NOT NULL,
    [ResponseValue]  TINYINT          NULL,                 -- NULL = skipped
    [AnsweredAtUtc]  DATETIME2(3)     NOT NULL CONSTRAINT [DF_AIR_At] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_AssessmentItemResponses] PRIMARY KEY ([AssessmentId], [ItemNo]),
    CONSTRAINT [FK_AIR_Assessments] FOREIGN KEY ([AssessmentId]) REFERENCES [App].[Assessments]([AssessmentId])
);
```

```sql
-- 5. AI OUTPUT, TRIAGE, CARE
CREATE TABLE [App].[AiSuggestions] (
    [SuggestionId]     UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_AI_Id] DEFAULT NEWSEQUENTIALID(),
    [PatientId]        UNIQUEIDENTIFIER NOT NULL,
    [Kind]             NVARCHAR(30)     NOT NULL,           -- PatientSummary | ClinicianFile | TriageSuggestion | MatchSuggestion
    [SubjectId]        UNIQUEIDENTIFIER NOT NULL,           -- IntakeSessionId or TriageCaseId
    [ContentCipher]    VARBINARY(MAX)   NOT NULL,           -- JSON, patient key
    [ModelId]          NVARCHAR(80)     NOT NULL,
    [PromptVersion]    NVARCHAR(40)     NOT NULL,
    [Status]           NVARCHAR(20)     NOT NULL CONSTRAINT [DF_AI_Status] DEFAULT N'Draft',
    [DecidedByUserId]  UNIQUEIDENTIFIER NULL,
    [DecidedAtUtc]     DATETIME2(3)     NULL,
    [CreatedAtUtc]     DATETIME2(3)     NOT NULL CONSTRAINT [DF_AI_At] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_AiSuggestions] PRIMARY KEY ([SuggestionId]),
    CONSTRAINT [CK_AiSuggestions_Status] CHECK ([Status] IN
        (N'Draft', N'PatientCorrected', N'Approved', N'Edited', N'Dismissed', N'Superseded'))
);

CREATE TABLE [App].[AiEvidenceLinks] (
    [SuggestionId]  UNIQUEIDENTIFIER NOT NULL,
    [PatientId]     UNIQUEIDENTIFIER NOT NULL,
    [StatementKey]  NVARCHAR(40)     NOT NULL,              -- id of a statement inside the JSON
    [MessageId]     UNIQUEIDENTIFIER NOT NULL,
    CONSTRAINT [PK_AiEvidenceLinks] PRIMARY KEY ([SuggestionId], [StatementKey], [MessageId]),
    CONSTRAINT [FK_AEL_Suggestions] FOREIGN KEY ([SuggestionId]) REFERENCES [App].[AiSuggestions]([SuggestionId])
    -- no FK to IntakeMessages: raw messages are purged after 90 days (section 3.5)
);

CREATE TABLE [App].[TriageCases] (
    [TriageCaseId]     UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_TC_Id] DEFAULT NEWSEQUENTIALID(),
    [PatientId]        UNIQUEIDENTIFIER NOT NULL,
    [IntakeSessionId]  UNIQUEIDENTIFIER NULL,               -- NULL for direct booking (safety screen only)
    [RuleRiskLevel]    TINYINT          NOT NULL,
    [AiRiskLevel]      TINYINT          NULL,
    [FinalRiskLevel]   TINYINT          NULL,               -- set by the supervisor
    [Status]           NVARCHAR(20)     NOT NULL CONSTRAINT [DF_TC_Status] DEFAULT N'PendingReview',
    [ClaimedByUserId]  UNIQUEIDENTIFIER NULL,               -- soft lock while a supervisor reviews
    [ClaimedUntilUtc]  DATETIME2(3)     NULL,
    [DueAtUtc]         DATETIME2(3)     NOT NULL,
    [CreatedAtUtc]     DATETIME2(3)     NOT NULL CONSTRAINT [DF_TC_At] DEFAULT SYSUTCDATETIME(),
    [RowVersion]       ROWVERSION       NOT NULL,
    CONSTRAINT [PK_TriageCases] PRIMARY KEY ([TriageCaseId]),
    CONSTRAINT [FK_TriageCases_Patients] FOREIGN KEY ([PatientId]) REFERENCES [App].[Patients]([PatientId]),
    CONSTRAINT [CK_TriageCases_AiNotLower] CHECK ([AiRiskLevel] IS NULL OR [AiRiskLevel] >= [RuleRiskLevel]),
    CONSTRAINT [CK_TriageCases_Status] CHECK ([Status] IN (N'PendingReview', N'Approved', N'Routed', N'Escalated', N'Closed'))
);

CREATE TABLE [App].[TriageDecisions] (
    [DecisionId]      BIGINT IDENTITY(1,1) NOT NULL,
    [TriageCaseId]    UNIQUEIDENTIFIER NOT NULL,
    [PatientId]       UNIQUEIDENTIFIER NOT NULL,
    [DecidedByUserId] UNIQUEIDENTIFIER NOT NULL,
    [Action]          NVARCHAR(20)     NOT NULL,            -- Approve | OverrideRisk | Reassign | Escalate | Close
    [FromRiskLevel]   TINYINT          NULL,
    [ToRiskLevel]     TINYINT          NULL,
    [TherapistId]     UNIQUEIDENTIFIER NULL,
    [Reason]          NVARCHAR(1000)   NULL,                -- required by the API for overrides
    [CreatedAtUtc]    DATETIME2(3)     NOT NULL CONSTRAINT [DF_TD_At] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_TriageDecisions] PRIMARY KEY ([DecisionId])
) WITH (LEDGER = ON (APPEND_ONLY = ON));

CREATE TABLE [App].[CareAssignments] (
    [CareAssignmentId] UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_CA_Id] DEFAULT NEWSEQUENTIALID(),
    [PatientId]        UNIQUEIDENTIFIER NOT NULL,
    [TherapistId]      UNIQUEIDENTIFIER NOT NULL,
    [Status]           NVARCHAR(10)     NOT NULL CONSTRAINT [DF_CA_Status] DEFAULT N'Active',
    [StartedAtUtc]     DATETIME2(3)     NOT NULL CONSTRAINT [DF_CA_Start] DEFAULT SYSUTCDATETIME(),
    [EndedAtUtc]       DATETIME2(3)     NULL,
    [EndReason]        NVARCHAR(200)    NULL,
    CONSTRAINT [PK_CareAssignments] PRIMARY KEY ([CareAssignmentId]),
    CONSTRAINT [CK_CareAssignments_Status] CHECK ([Status] IN (N'Active', N'Ended'))
);
CREATE UNIQUE INDEX [UX_CareAssignments_OneActive] ON [App].[CareAssignments]([PatientId]) WHERE [Status] = N'Active';
CREATE INDEX [IX_CareAssignments_Therapist] ON [App].[CareAssignments]([TherapistId], [Status]) INCLUDE ([PatientId]);

-- 6. CRISIS
CREATE TABLE [App].[CrisisEvents] (
    [CrisisEventId]        UNIQUEIDENTIFIER NOT NULL CONSTRAINT [DF_CE_Id] DEFAULT NEWSEQUENTIALID(),
    [PatientId]            UNIQUEIDENTIFIER NULL,           -- NULL only for platform-wide system events
    [RiskLevel]            TINYINT          NOT NULL,       -- 3 High | 4 Critical
    [Source]               NVARCHAR(15)     NOT NULL,       -- Intake | CheckIn | HelpNow | Manual | System
    [RuleIds]              NVARCHAR(400)    NOT NULL,
    [RulePackVersion]      NVARCHAR(20)     NOT NULL,
    [ExcerptCipher]        VARBINARY(4000)  NULL,           -- patient key; shown only inside the event view
    [Status]               NVARCHAR(25)     NOT NULL CONSTRAINT [DF_CE_Status] DEFAULT N'Open',
    [EscalationTier]       TINYINT          NOT NULL CONSTRAINT [DF_CE_Tier] DEFAULT 1,   -- 1 on-call, 2 backup, 3 lead
    [NextEscalationAtUtc]  DATETIME2(3)     NULL,           -- NULL once acknowledged
    [AcknowledgedByUserId] UNIQUEIDENTIFIER NULL,
    [AcknowledgedAtUtc]    DATETIME2(3)     NULL,
    [ContactedAtUtc]       DATETIME2(3)     NULL,
    [ResolvedAtUtc]        DATETIME2(3)     NULL,
    [Outcome]              NVARCHAR(30)     NULL,
    [CreatedAtUtc]         DATETIME2(3)     NOT NULL CONSTRAINT [DF_CE_At] DEFAULT SYSUTCDATETIME(),
    [RowVersion]           ROWVERSION       NOT NULL,
    CONSTRAINT [PK_CrisisEvents] PRIMARY KEY ([CrisisEventId]),
    CONSTRAINT [CK_CrisisEvents_Level] CHECK ([RiskLevel] IN (3, 4)),
    CONSTRAINT [CK_CrisisEvents_Status] CHECK ([Status] IN
        (N'Open', N'Acknowledged', N'InContact', N'EmergencyServicesCalled', N'Resolved', N'ClosedUnreachable', N'FalsePositive'))
);
CREATE INDEX [IX_CrisisEvents_Due] ON [App].[CrisisEvents]([NextEscalationAtUtc]) WHERE [NextEscalationAtUtc] IS NOT NULL;

CREATE TABLE [App].[CrisisActions] (
    [ActionId]       BIGINT IDENTITY(1,1) NOT NULL,
    [CrisisEventId]  UNIQUEIDENTIFIER NOT NULL,
    [PatientId]      UNIQUEIDENTIFIER NULL,
    [ActorUserId]    UNIQUEIDENTIFIER NULL,                 -- NULL = system
    [ActionType]     NVARCHAR(30)     NOT NULL,             -- Paged | Acknowledged | CallAttempt | Contacted | EmergencyCalled | Note | Resolved
    [Detail]         NVARCHAR(1000)   NULL,
    [CreatedAtUtc]   DATETIME2(3)     NOT NULL CONSTRAINT [DF_CAc_At] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_CrisisActions] PRIMARY KEY ([ActionId])
) WITH (LEDGER = ON (APPEND_ONLY = ON));

CREATE TABLE [App].[OnCallShifts] (
    [ShiftId]      INT IDENTITY(1,1) NOT NULL,
    [UserId]       UNIQUEIDENTIFIER NOT NULL,
    [Tier]         TINYINT          NOT NULL,               -- 1 primary, 2 backup, 3 clinical lead
    [StartsAtUtc]  DATETIME2(3)     NOT NULL,
    [EndsAtUtc]    DATETIME2(3)     NOT NULL,
    CONSTRAINT [PK_OnCallShifts] PRIMARY KEY ([ShiftId]),
    CONSTRAINT [CK_OnCallShifts_Range] CHECK ([EndsAtUtc] > [StartsAtUtc]),
    CONSTRAINT [CK_OnCallShifts_Tier] CHECK ([Tier] BETWEEN 1 AND 3)
);

-- 7. OUTBOX AND AUDIT
CREATE TABLE [App].[Outbox] (
    [OutboxId]       BIGINT IDENTITY(1,1) NOT NULL,
    [MessageType]    NVARCHAR(100)  NOT NULL,
    [PayloadJson]    NVARCHAR(MAX)  NOT NULL,              -- ids only, never PHI
    [Priority]       TINYINT        NOT NULL CONSTRAINT [DF_OB_Pri] DEFAULT 5,   -- 1 = crisis
    [CreatedAtUtc]   DATETIME2(3)   NOT NULL CONSTRAINT [DF_OB_At] DEFAULT SYSUTCDATETIME(),
    [ProcessedAtUtc] DATETIME2(3)   NULL,
    [Attempts]       INT            NOT NULL CONSTRAINT [DF_OB_Att] DEFAULT 0,
    [NextAttemptUtc] DATETIME2(3)   NULL,
    [LastError]      NVARCHAR(1000) NULL,
    CONSTRAINT [PK_Outbox] PRIMARY KEY ([OutboxId])
);
CREATE INDEX [IX_Outbox_Pending] ON [App].[Outbox]([Priority], [OutboxId]) WHERE [ProcessedAtUtc] IS NULL;

CREATE TABLE [Audit].[AccessLog] (
    [LogId]          BIGINT IDENTITY(1,1) NOT NULL,
    [ActorUserId]    UNIQUEIDENTIFIER NULL,                 -- NULL = system job
    [ActorRole]      NVARCHAR(20)     NOT NULL,
    [Action]         NVARCHAR(60)     NOT NULL,             -- Read | Create | Update | Export | Decrypt | AiRead | Login ...
    [ResourceType]   NVARCHAR(60)     NOT NULL,
    [ResourceId]     NVARCHAR(64)     NULL,
    [PatientId]      UNIQUEIDENTIFIER NULL,                 -- powers the patient's own access-log view
    [Outcome]        NVARCHAR(10)     NOT NULL,             -- Success | Denied | Error
    [Reason]         NVARCHAR(400)    NULL,                 -- break-glass and step-up reasons
    [IpAddress]      NVARCHAR(45)     NULL,
    [UserAgent]      NVARCHAR(255)    NULL,
    [CorrelationId]  NVARCHAR(64)     NULL,
    [TimestampUtc]   DATETIME2(3)     NOT NULL CONSTRAINT [DF_AL_At] DEFAULT SYSUTCDATETIME(),
    CONSTRAINT [PK_AccessLog] PRIMARY KEY ([LogId])
) WITH (LEDGER = ON (APPEND_ONLY = ON));
CREATE INDEX [IX_AccessLog_Patient] ON [Audit].[AccessLog]([PatientId], [TimestampUtc]);
```

### 3.3 Other tables

| Table | Key columns | Notes |
| --- | --- | --- |
| `App.RecoveryCodes` | UserId, CodeHash, UsedAtUtc | 10 per patient, single use |
| `App.Therapists` | TherapistId, UserId, DisplayName, LegalName (AE), LicenseNumber (AE deterministic), LicenseAuthority, LicenseExpiresOn, Status, MaxActivePatients, Bio, LanguagesCsv | Status: Draft, Submitted, InReview, Approved, Rejected, Suspended, Expired |
| `App.TherapistApplications` | ApplicationId, TherapistId, Status, SubmittedAtUtc, ReviewerUserId, DecidedAtUtc, DecisionReason, RegistryCheckRef | Replaces the TriageQueue misuse (B-04) |
| `App.TherapistDocuments` | DocumentId, TherapistId, DocType, BlobPath, Sha256, SizeBytes, ScanStatus (Pending, Clean, Infected), UploadedAtUtc | PDF/JPG/PNG ≤ 10 MB; served only through short-lived SAS links |
| `App.Specialties`, `App.TherapistSpecialties` | SpecialtyId, Code, NameEn, NameAr / TherapistId, SpecialtyId | Replaces SpecialtiesJson |
| `App.AvailabilitySlots` | SlotId, TherapistId, StartUtc, EndUtc, Status (Open, Held, Booked, Blocked), HeldUntilUtc, RowVersion | Booking holds a slot for 5 min, then confirms |
| `App.Appointments` | AppointmentId, PatientId, TherapistId, SlotId (unique), StartUtc, EndUtc, Status (Booked, Cancelled, Completed, NoShow), MeetingLinkCipher, CancelledBy, CancelReason | External meeting link; no video in the pilot |
| `App.MatchSuggestions` | TriageCaseId, Rank, TherapistId, ReasonsJson, ScoreBreakdownJson | Top 3 per case |
| `App.TreatmentPlans` | PlanId, PatientId, TherapistId, Version, ContentCipher, CreatedAtUtc | Therapist-authored; every save is a new version |
| `App.ClinicalFileApprovals` | ApprovalId, PatientId, SuggestionId, TherapistId, Decision, EditedContentCipher, CreatedAtUtc | Ledger, append-only |
| `App.HomeworkTemplates` | TemplateId, Code, TitleEn, TitleAr, BodyEn, BodyAr, ModuleType | Clinician-approved library |
| `App.HomeworkTasks` | TaskId, PatientId, TherapistId, TemplateId, Title, InstructionsCipher, Schedule (Daily, Weekdays, Custom), StartsOn, DueOn, Status | Only therapists create or change tasks |
| `App.HomeworkCheckIns` | CheckInId, TaskId, PatientId, Outcome (Done, Partly, NotToday), Mood (0–10), NoteCipher, GateOutcome, CreatedAtUtc | Every note goes through the crisis gate |
| `App.HomeworkFeedback` | FeedbackId, TaskId, PatientId, TherapistId, BodyCipher, ReadAtUtc | Therapist → patient |
| `App.WeeklyReports` | ReportId, PatientId, TherapistId, PeriodStart, PeriodEnd, ContentCipher, GeneratedAtUtc | Generated 24 h before the next session |
| `App.NotificationPreferences` | UserId, Channel, QuietHoursStart, QuietHoursEnd, ReminderFrequency | Crisis messages ignore quiet hours |
| `App.DataRequests` | RequestId, UserId, Type (Export, Delete), Status, RequestedAtUtc, CompletedAtUtc | Export ≤ 7 days, delete ≤ 30 days |
| `App.RuleChangeRequests` | RequestId, RulePackVersion, ProposedByUserId, ApprovedByUserId, EvalRunId, Status | Crisis rule governance |
| `App.QaReviews` | ReviewId, IntakeSessionId, PatientId, ReviewerUserId, MissedRisk (bit), Findings, CreatedAtUtc | 10% weekly sample |
| `App.FeatureFlags` | Key, Value, UpdatedBy, UpdatedAtUtc | Kill switches: AI intake, homework AI, new signups |

### 3.4 Row-Level Security

```sql
CREATE FUNCTION [Security].[fn_CanAccessPatient](@PatientId UNIQUEIDENTIFIER)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN
    SELECT 1 AS [Allowed]
    WHERE CAST(SESSION_CONTEXT(N'Role') AS NVARCHAR(20)) IN (N'Supervisor', N'ClinicalLead', N'System', N'CrisisWorker')
       OR (CAST(SESSION_CONTEXT(N'Role') AS NVARCHAR(20)) = N'Patient'
           AND @PatientId = CAST(SESSION_CONTEXT(N'PatientId') AS UNIQUEIDENTIFIER))
       OR (CAST(SESSION_CONTEXT(N'Role') AS NVARCHAR(20)) = N'Therapist'
           AND EXISTS (SELECT 1
                       FROM [App].[CareAssignments] AS ca
                       WHERE ca.[PatientId] = @PatientId
                         AND ca.[TherapistId] = CAST(SESSION_CONTEXT(N'TherapistId') AS UNIQUEIDENTIFIER)
                         AND ca.[Status] = N'Active'));
GO

CREATE SECURITY POLICY [Security].[PatientDataPolicy]
    ADD FILTER PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[IntakeSessions],
    ADD BLOCK  PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[IntakeSessions] AFTER INSERT,
    ADD FILTER PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[IntakeMessages],
    ADD BLOCK  PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[IntakeMessages] AFTER INSERT,
    ADD FILTER PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[Assessments],
    ADD FILTER PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[AssessmentItemResponses],
    ADD FILTER PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[AiSuggestions],
    ADD FILTER PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[AiEvidenceLinks],
    ADD FILTER PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[TriageCases],
    ADD FILTER PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[CrisisEvents],
    ADD FILTER PREDICATE [Security].[fn_CanAccessPatient]([PatientId]) ON [App].[PatientSafetyInfo]
    -- add the same pair for TreatmentPlans, HomeworkTasks, HomeworkCheckIns, HomeworkFeedback,
    -- WeeklyReports and Appointments
WITH (STATE = ON, SCHEMABINDING = ON);
```

- `CareAssignments` is left out of the policy so the predicate does not filter itself; the API restricts it instead.
- The `Admin` role is not in the predicate, so platform admins see no clinical data at all.
- Supervisors see everything in the pilot because they run triage and crisis; every read they make is still written to the audit log.
- The app's SQL login is not `db_owner`, has no `ALTER` on the `Security` schema, and cannot run `ALTER SECURITY POLICY`.

The API sets the session context every time EF Core or Dapper opens a pooled connection (the pool clears it on reset):

```csharp
public sealed class SessionContextInterceptor(ICurrentActor actor) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken ct = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            EXEC sp_set_session_context @key = N'UserId',      @value = @uid,  @read_only = 1;
            EXEC sp_set_session_context @key = N'Role',        @value = @role, @read_only = 1;
            EXEC sp_set_session_context @key = N'PatientId',   @value = @pid,  @read_only = 1;
            EXEC sp_set_session_context @key = N'TherapistId', @value = @tid,  @read_only = 1;
            """;
        Add(cmd, "@uid", actor.UserId);
        Add(cmd, "@role", actor.Role);
        Add(cmd, "@pid", actor.PatientId);
        Add(cmd, "@tid", actor.TherapistId);
        await cmd.ExecuteNonQueryAsync(ct);

        static void Add(DbCommand c, string name, object? value)
        {
            var p = c.CreateParameter();
            p.ParameterName = name;
            p.Value = value ?? DBNull.Value;
            c.Parameters.Add(p);
        }
    }
}
```

### 3.5 Retention

| Data | Kept for | Then |
| --- | --- | --- |
| Clinical record (approved file, plans, assessments, appointments) | As local law requires (typically 7–10 years for adults) | Crypto-shred the patient key, delete rows |
| Raw intake chat messages | 90 days after the clinical file is approved | Delete rows; evidence links show "source removed" |
| Crisis events and actions | 10 years | Ledger rows are kept; excerpts become unreadable after crypto-shred |
| Audit log | 6 years minimum | Ledger history kept; digests in immutable storage |
| Unverified accounts / abandoned intakes | 30 days / 14 days of inactivity | Delete; patient notified before deletion |

## 4. API and services catalog

All endpoints sit under `/v1`, return RFC 7807 errors, and require an `Idempotency-Key` header on every POST that creates something. The service ids (SVC-xx) are what the page and button tables in sections 6 and 7 refer to.

### 4.1 Conventions

| Topic | Rule |
| --- | --- |
| Auth | Bearer access token (staff) or BFF cookie (patients); role and resource checks in a MediatR pipeline behaviour, then RLS in SQL |
| Errors | `type`, `title`, `status`, `detail`, `traceId`, `code` (e.g. `INTAKE_LOCKED`, `SLOT_TAKEN`, `STEP_UP_REQUIRED`) |
| Concurrency | `ETag` from `RowVersion`; updates need `If-Match`; stale → 412 with the current state |
| Paging | `?cursor=&limit=` (max 100) |
| Audit | Every request that reads or changes patient data writes one `Audit.AccessLog` row in the same transaction |
| PHI in URLs | Never; ids only |

### 4.2 Endpoints

| SVC | Method and path | Who | Purpose |
| --- | --- | --- | --- |
| **Identity** |  |  |  |
| SVC-01 | `POST /v1/auth/signup` | Public | Create patient account (alias, contact, password); sends verification code |
| SVC-02 | `POST /v1/auth/verify-contact` | Public | Confirm the 6-digit code; activates the account |
| SVC-03 | `POST /v1/auth/login` | Public | Password step; returns `mfa_required` or tokens |
| SVC-04 | `POST /v1/auth/mfa/verify` | Public | TOTP / passkey / SMS step |
| SVC-05 | `POST /v1/auth/refresh` | Any | Rotate refresh token |
| SVC-06 | `POST /v1/auth/logout` | Any | Revoke token family |
| SVC-07 | `POST /v1/auth/recover` and `/recover/confirm` | Public | Recovery by verified contact or recovery code |
| SVC-08 | `POST /v1/auth/step-up` | Staff | Fresh MFA for sensitive actions (valid 5 min) |
| SVC-09 | `GET/PUT /v1/me/security` | Any | Password change, MFA setup, sessions list, revoke session |
| **Consent and eligibility** |  |  |  |
| SVC-10 | `GET /v1/consents/current?locale=` | Public | Current consent documents |
| SVC-11 | `POST /v1/me/consents` | Patient | Grant or withdraw a consent |
| SVC-12 | `POST /v1/me/eligibility` | Patient | Adult confirmation + jurisdiction |
| SVC-13 | `PUT /v1/me/safety-info` | Patient | Emergency contact, phone, location consent |
| **Intake** |  |  |  |
| SVC-14 | `POST /v1/intake` | Patient | Start or resume the open intake |
| SVC-15 | `GET /v1/intake/{id}` | Patient | Session state, step, progress, last 50 messages |
| SVC-16 | `POST /v1/intake/{id}/messages` | Patient | Send text; runs the crisis gate; returns AI reply or crisis script |
| SVC-17 | `POST /v1/intake/{id}/answers` | Patient | Answer a questionnaire card item |
| SVC-18 | `POST /v1/intake/{id}/skip` | Patient | Skip the current topic or item |
| SVC-19 | `POST /v1/intake/{id}/pause` | Patient | Pause and save |
| SVC-20 | `POST /v1/intake/{id}/request-human` | Patient | Ask for a human; opens a High-priority triage case |
| SVC-21 | `POST /v1/intake/{id}/finish` | Patient | Ends chat (runs C-SSRS first if not done); generates summary drafts |
| SVC-22 | `GET/PUT /v1/intake/{id}/summary` | Patient | Read and correct the patient summary |
| SVC-23 | `POST /v1/intake/{id}/submit` | Patient | Confirm summary; creates the triage case |
| **Crisis** |  |  |  |
| SVC-24 | `POST /v1/crisis/help-now` | Patient | Help Now button; opens a Critical event |
| SVC-25 | `GET /v1/crisis/events?status=` | Supervisor | Open and recent events |
| SVC-26 | `GET /v1/crisis/events/{id}` | Supervisor | Event detail, timeline, excerpt |
| SVC-27 | `POST /v1/crisis/events/{id}/acknowledge` | Supervisor, Therapist | Stop paging, take ownership |
| SVC-28 | `POST /v1/crisis/events/{id}/reveal-contact` | Supervisor | Step-up required; returns decrypted contact and location; high-severity audit |
| SVC-29 | `POST /v1/crisis/events/{id}/actions` | Supervisor | Log call attempt, contact made, emergency services called, note |
| SVC-30 | `POST /v1/crisis/events/{id}/resolve` | Supervisor | Outcome + unlock intake; FalsePositive needs a reason |
| SVC-31 | `POST /v1/crisis/events/{id}/handover` | Supervisor | Pass ownership to another on-call clinician |
| SVC-32 | `POST /v1/crisis/manual` | Therapist, Supervisor | Raise a crisis for a patient by hand |
| SVC-33 | `GET/PUT /v1/oncall/shifts` | ClinicalLead | On-call rota |
| **Triage and matching** |  |  |  |
| SVC-34 | `GET /v1/triage/cases?status=&level=` | Supervisor | Queue sorted by level, then due time |
| SVC-35 | `POST /v1/triage/cases/{id}/claim` | Supervisor | Soft lock for 15 min |
| SVC-36 | `GET /v1/triage/cases/{id}` | Supervisor | Case file, scores, AI suggestion, match suggestions |
| SVC-37 | `POST /v1/triage/cases/{id}/decisions` | Supervisor | Approve, OverrideRisk, Reassign, Escalate, Close (reason required except Approve) |
| SVC-38 | `GET /v1/matching/suggestions` | Patient | Approved suggestions with reasons |
| SVC-39 | `GET /v1/therapists?specialty=&language=` | Patient | Browse all approved therapists |
| SVC-40 | `GET /v1/therapists/{id}` | Patient | Public profile |
| SVC-41 | `POST /v1/matching/not-a-fit` | Patient | Decline a suggestion (optional reason) |
| **Scheduling** |  |  |  |
| SVC-42 | `GET /v1/therapists/{id}/slots?from=&to=` | Patient | Open slots |
| SVC-43 | `POST /v1/bookings/hold` | Patient | Hold a slot for 5 min |
| SVC-44 | `POST /v1/bookings/confirm` | Patient | Confirm; creates appointment and care assignment (first booking) |
| SVC-45 | `GET /v1/appointments` | Patient, Therapist | My appointments |
| SVC-46 | `POST /v1/appointments/{id}/reschedule` | Patient, Therapist | Move to another slot |
| SVC-47 | `POST /v1/appointments/{id}/cancel` | Patient, Therapist | Cancel with reason |
| SVC-48 | `POST /v1/appointments/{id}/outcome` | Therapist | Completed or NoShow |
| SVC-49 | `GET/PUT /v1/therapist/availability` | Therapist | Weekly pattern + exceptions; generates slots |
| **Clinical** |  |  |  |
| SVC-50 | `GET /v1/patients` | Therapist | My caseload (active assignments) |
| SVC-51 | `GET /v1/patients/{id}/file` | Therapist | Clinical file with evidence links |
| SVC-52 | `GET /v1/patients/{id}/evidence/{statementKey}` | Therapist | Source messages for one statement |
| SVC-53 | `POST /v1/patients/{id}/file/approve` | Therapist | Approve as is, or with edits |
| SVC-54 | `POST /v1/patients/{id}/file/dismiss-item` | Therapist | Dismiss one AI statement with reason |
| SVC-55 | `GET/POST /v1/patients/{id}/plans` | Therapist | Read versions / save a new plan version |
| SVC-56 | `POST /v1/patients/{id}/risk-flag` | Therapist | Raise risk level (lowering goes through a supervisor) |
| SVC-57 | `POST /v1/patients/{id}/discharge` | Therapist | End care assignment with reason |
| SVC-58 | `POST /v1/patients/{id}/file-feedback` | Therapist | Rate intake file usefulness |
| **Homework** |  |  |  |
| SVC-59 | `GET /v1/homework/templates` | Therapist | Library |
| SVC-60 | `POST /v1/patients/{id}/homework` | Therapist | Assign a task |
| SVC-61 | `PUT/DELETE /v1/homework/{taskId}` | Therapist | Edit or cancel a task |
| SVC-62 | `GET /v1/me/homework` | Patient | My tasks |
| SVC-63 | `POST /v1/homework/{taskId}/check-ins` | Patient | Check-in (outcome, mood, note); runs the crisis gate |
| SVC-64 | `POST /v1/homework/{taskId}/feedback` | Therapist | Feedback to patient |
| SVC-65 | `GET /v1/patients/{id}/reports/weekly` | Therapist | Weekly progress reports |
| SVC-66 | `POST /v1/me/mood` | Patient | Daily mood check-in (0–10); note runs the crisis gate |
| **Credentialing** |  |  |  |
| SVC-67 | `POST /v1/therapist-applications` | Public | Create applicant account + draft application |
| SVC-68 | `POST /v1/therapist-applications/{id}/documents` | Applicant | Get an upload SAS URL; scan on arrival |
| SVC-69 | `POST /v1/therapist-applications/{id}/submit` | Applicant | Submit for review |
| SVC-70 | `GET /v1/admin/applications` | Admin | Verification queue |
| SVC-71 | `POST /v1/admin/applications/{id}/decision` | Admin (+ step-up) | Approve, reject, request more info |
| SVC-72 | `POST /v1/admin/therapists/{id}/suspend` | Admin, ClinicalLead | Suspend; flags affected appointments |
| **Privacy and audit** |  |  |  |
| SVC-73 | `GET /v1/me/access-log` | Patient | Who viewed my file |
| SVC-74 | `POST /v1/me/data-requests` | Patient | Export or delete my data |
| SVC-75 | `GET /v1/admin/audit?filters` | Admin (+ step-up) | Search audit log |
| SVC-76 | `POST /v1/admin/audit/export` | Admin (+ step-up) | Signed export with ledger digest |
| **Operations** |  |  |  |
| SVC-77 | `GET/POST /v1/admin/rule-changes` | ClinicalLead, Admin | Propose, approve and deploy crisis rule packs |
| SVC-78 | `GET/POST /v1/qa/reviews` | Supervisor | Weekly 10% intake sample review |
| SVC-79 | `GET /v1/admin/metrics` | Supervisor, Admin | Pilot metrics |
| SVC-80 | `GET/PUT /v1/admin/feature-flags` | Admin | Kill switches |
| SVC-81 | `GET /v1/admin/health` | Admin | Crisis gate, classifier, LLM, paging, queues |
| SVC-82 | `GET/PUT /v1/me/notifications` | Any | Notification preferences |

### 4.3 Real-time hubs (SignalR)

| Hub | Group | Server → client events |
| --- | --- | --- |
| `/hubs/intake` | `intake:{sessionId}` | `AiTyping`, `AiMessage`, `QuestionCard`, `SessionLocked`, `CrisisScript`, `SessionResumed` |
| `/hubs/crisis` | `oncall`, `supervisors`, `therapist:{id}` | `CrisisOpened`, `CrisisEscalated`, `CrisisAcknowledged`, `CrisisResolved`, `GateHealthChanged` |
| `/hubs/staff` | `supervisors`, `therapist:{id}` | `TriageCaseCreated`, `TriageCaseClaimed`, `PatientAssigned`, `AppointmentBooked`, `CheckInFlagged`, `ReportReady` |
| `/hubs/patient` | `patient:{id}` | `AppointmentChanged`, `HomeworkAssigned`, `FeedbackReceived`, `MatchReady` |

SignalR is a convenience channel only. Crisis paging always goes through the outbox to phone call and SMS, so it works with no browser open.

### 4.4 Background jobs

| Job | Schedule | What it does |
| --- | --- | --- |
| Escalation loop (Crisis.Worker) | Every 15 s | Pages the next tier for unacknowledged events |
| Outbox dispatcher | Continuous, priority 1 first | Sends SMS, calls, email, push, SignalR; retries with backoff |
| Gate heartbeat | Every 30 s | Synthetic message through the gate; alerts if it fails twice |
| Homework reminders | Every 15 min | Sends due reminders outside quiet hours |
| Weekly report builder | Hourly | Builds reports 24 h before each next session |
| Triage SLA watcher | Every 5 min | Alerts the supervisor channel when a case is near its due time |
| License expiry | Daily | Warns at 30 days; suspends new bookings on expiry |
| Intake expiry | Daily | Marks idle intakes Expired after 14 days, with a reminder at day 10 |
| Retention purge | Nightly | Deletes raw chat after 90 days; executes data-deletion requests |
| Ledger digest | Daily | Stores the database ledger digest in immutable blob storage |
| QA sampler | Weekly | Picks 10% of completed intakes for review |

## 5. User journeys, end to end

Each journey lists the screens in order, what the system does at each step, and the status it writes. A Help Now button and the crisis line are visible on every patient screen after sign-up.

### 5.1 Patient

| # | Step | Screen | System action | Status after |
| --- | --- | --- | --- | --- |
| 1 | Lands on the site, reads how it works and the "what the AI will and won't do" panel | `/`, `/how-it-works` | Static, SSR, no tracking cookies before consent | — |
| 2 | Confirms 18+ and location in the pilot area | `/start/eligibility` | Out of area or under 18 → shown local services, no account created | — |
| 3 | Picks an alias (or generates one), enters email or phone, sets a password | `/signup` | SVC-01: user + patient rows, patient key created, code sent | User `PendingVerification`, Patient `Onboarding` |
| 4 | Enters the 6-digit code | `/signup/verify` | SVC-02 | User `Active` |
| 5 | Saves 10 recovery codes; may set up TOTP or a passkey | `/signup/recovery-codes` | Codes shown once, stored hashed | — |
| 6 | Reads and accepts the required consents; chooses optional ones | `/onboarding/consents` | SVC-11, one ledger row per consent | — |
| 7 | Optionally adds phone, emergency contact, and consent to share location in an emergency | `/onboarding/safety` | SVC-13; skip allowed, reminder later | — |
| 8 | Chooses "Start intake" or "Book directly" | `/onboarding/choose-path` | — | Patient `Intake` |
| 9a | Talks with the AI; answers PHQ-9, GAD-7 and C-SSRS screener cards | `/intake` | SVC-14–18; every message through the crisis gate | Intake `InProgress` |
| 9b | Book directly: answers the C-SSRS screener only | `/intake/safety-check` | SVC-17; a triage case is still created (safety-only) | — |
| 10 | Finishes the chat | `/intake` | SVC-21: C-SSRS forced if missing; summary drafts generated | Intake `AwaitingPatientReview` |
| 11 | Reads the plain-language summary, corrects it, submits | `/intake/summary` | SVC-22, SVC-23; triage case created with rule-based level | Intake `Submitted`, Patient `AwaitingTriage`, Triage `PendingReview` |
| 12 | Waits; sees the expected review time and crisis resources | `/dashboard` (waiting state) | Supervisor reviews within SLA (section 2.5) | Triage `Approved` |
| 13 | Gets a notification; sees 3 suggested therapists with reasons; may browse all | `/matching` | SVC-38–41 | Patient `Matched` |
| 14 | Opens a profile, picks a slot, confirms | `/therapists/[id]`, `/book/[therapistId]` | SVC-42–44: slot held 5 min, appointment created, care assignment created, therapist notified | Appointment `Booked`, Patient `InCare`, Triage `Routed` |
| 15 | Before the session: reminders at 24 h and 1 h; opens the meeting link the therapist provided | `/appointments/[id]` | Hangfire reminders | — |
| 16 | Between sessions: does homework, checks in, logs mood, reads feedback | `/homework`, `/homework/[taskId]`, `/check-in` | SVC-62, 63, 66; notes through the crisis gate | Check-in rows |
| 17 | Manages privacy: consents, access log, export, delete | `/settings/privacy` | SVC-11, 73, 74 | Data request rows |
| 18 | Ends care (therapist discharges, or patient deletes account) | `/dashboard` | SVC-57 or SVC-74 | Patient `Discharged` or `Deleted` |

**Crisis branch (any step after 3):** gate blocks a message, or the patient taps Help Now → chat locks → full-screen crisis view with the local emergency number, crisis line, and "A member of our clinical team is being contacted now" → on-call clinician calls the patient → event resolved → chat unlocks or patient is routed to urgent care.

### 5.2 Therapist

| # | Step | Screen | System action | Status after |
| --- | --- | --- | --- | --- |
| 1 | Creates an applicant account (email, password, TOTP required) | `/staff/apply` | SVC-67 | Therapist `Draft` |
| 2 | Fills personal details, license (authority, number, expiry), specialties, languages, bio | `/staff/apply/profile`, `/staff/apply/license` | Draft saved per step | — |
| 3 | Uploads license and certificates | `/staff/apply/documents` | SVC-68: direct-to-blob upload, malware scan, SHA-256 stored | Document `Pending` → `Clean` |
| 4 | Reviews and submits | `/staff/apply/review` | SVC-69 | Therapist `Submitted` |
| 5 | Waits; sees status and any "more info" requests | `/staff/apply/status` | Email on every change | `InReview` → `Approved` / `Rejected` |
| 6 | After approval: sets weekly availability and meeting link | `/t/availability`, `/t/profile` | SVC-49 generates slots 8 weeks ahead | Profile visible to matching |
| 7 | Gets "new patient booked" alert; opens the patient's clinical file | `/t/dashboard` → `/t/patients/:id` | SVC-50, 51; access audited | — |
| 8 | Reads the AI file; clicks statements to see source chat lines; edits, dismisses or approves | `/t/patients/:id/intake` | SVC-52–54 | File `Approved` / `Edited` |
| 9 | Holds the session (external meeting tool); records outcome | `/t/schedule` | SVC-48 | Appointment `Completed` / `NoShow` |
| 10 | Writes or updates the treatment plan | `/t/patients/:id/plan` | SVC-55: new version each save | Plan v1, v2 … |
| 11 | Assigns homework from the library or own text | `/t/patients/:id/homework` | SVC-60 | Task `Active` |
| 12 | Reads check-ins and the weekly report before the next session; sends feedback | `/t/patients/:id/reports` | SVC-63 events, SVC-65, SVC-64 | — |
| 13 | Receives crisis alerts for own patients; acknowledges or hands to on-call | Alert banner → `/t/alerts` | SVC-27 | Event `Acknowledged` |
| 14 | Discharges the patient when care ends | `/t/patients/:id` | SVC-57 | Care assignment `Ended` |

### 5.3 Supervisor (on-call clinician)

| # | Step | Screen | System action |
| --- | --- | --- | --- |
| 1 | Signs in with MFA at shift start; marks "On shift" | `/s/on-call` | Shift checked against rota; paging number confirmed by test SMS |
| 2 | Works the triage queue, highest level and nearest due time first | `/s/triage` | SVC-34 |
| 3 | Claims a case (15-min lock), reads AI file, scores, chat evidence, match suggestions | `/s/triage/:id` | SVC-35, 36 |
| 4 | Approves, overrides risk (reason), reassigns therapist suggestions, or escalates to crisis | `/s/triage/:id` | SVC-37; patient notified when approved |
| 5 | On a crisis page (phone + console): acknowledges within 2 or 15 min | `/s/crisis/:eventId` | SVC-27 stops escalation |
| 6 | Reads the excerpt; reveals contact and location (step-up MFA) | `/s/crisis/:eventId` | SVC-28, high-severity audit |
| 7 | Calls the patient; logs each attempt and the outcome | same | SVC-29 |
| 8 | If imminent danger and unreachable, follows the local emergency protocol script and logs the call | same | SVC-29 (`EmergencyCalled`) |
| 9 | Resolves the event with an outcome; writes the 72-hour review | same | SVC-30; review task created |
| 10 | Weekly: reviews the 10% QA sample for missed risk | `/s/qa` | SVC-78; misses feed the evaluation set |
| 11 | At shift end: hands over open events | `/s/on-call` | SVC-31 |

### 5.4 Admin and clinical lead

| # | Step | Screen | System action |
| --- | --- | --- | --- |
| 1 | Opens the verification queue; checks each license against the official registry | `/a/verifications`, `/a/verifications/:id` | SVC-70; documents shown through 5-min SAS links |
| 2 | Approves (step-up MFA), rejects with reason, or requests more info | same | SVC-71; applicant emailed |
| 3 | Manages users: suspend, unlock, reset MFA (with second-admin approval) | `/a/users` | SVC-72 |
| 4 | Clinical lead sets the on-call rota | `/s/on-call/rota` | SVC-33; gaps in coverage pause new signups automatically |
| 5 | Clinical lead reviews and approves crisis rule changes after a green evaluation run | `/a/rules` | SVC-77 |
| 6 | Searches and exports the audit log for compliance | `/a/audit` | SVC-75, 76 (step-up) |
| 7 | Watches system health; uses kill switches | `/a/health`, `/a/flags` | SVC-80, 81 |
| 8 | Reviews pilot metrics weekly | `/a/metrics` | SVC-79 |

### 5.5 Case lifecycle

| Object | States, in order | Who moves it |
| --- | --- | --- |
| Patient | Onboarding → Intake → AwaitingTriage → Matched → InCare → Discharged (or Deleted at any point) | System, supervisor, therapist, patient |
| Intake session | InProgress ⇄ Paused; InProgress → Locked (crisis) → InProgress; InProgress → AwaitingPatientReview → Submitted; idle 14 days → Expired | Patient, crisis service, clinician |
| Triage case | PendingReview → Approved → Routed → Closed; PendingReview → Escalated (crisis) | Supervisor, system |
| Crisis event | Open → Acknowledged → InContact → Resolved; Acknowledged → EmergencyServicesCalled → Resolved; Acknowledged → ClosedUnreachable; Acknowledged → FalsePositive | Crisis worker, supervisor |
| Therapist | Draft → Submitted → InReview → Approved / Rejected; Approved → Suspended / Expired | Applicant, admin, system |
| Appointment | Booked → Completed / NoShow / Cancelled | Patient, therapist |

## 6. Pages and routes

The pilot has 76 routes: 39 in the patient app (Next.js) and 37 in the staff portal (Angular). Every patient route below `/onboarding` shows the Help Now button and a link to `/crisis-resources` in its header.

### 6.1 Patient app (Next.js, `app.<domain>`)

| Route | Access | Purpose | Services |
| --- | --- | --- | --- |
| `/` | Public | Landing: what the service is, who it is for, Start button | — |
| `/how-it-works` | Public | Steps, "what the AI will and won't do", human review promise | — |
| `/privacy`, `/terms` | Public | Privacy notice and terms (Arabic + English) | SVC-10 |
| `/crisis-resources` | Public, cached offline | Local emergency number, crisis lines, what to do now | Static, PWA cache |
| `/start/eligibility` | Public | 18+ and pilot-area check | SVC-12 (after signup) / client check before |
| `/signup` | Public | Alias, contact, password | SVC-01 |
| `/signup/verify` | Public | Enter 6-digit code; resend | SVC-02 |
| `/signup/recovery-codes` | Patient | Show and download recovery codes once | SVC-09 |
| `/login` | Public | Contact + password | SVC-03 |
| `/login/mfa` | Public | TOTP / passkey / SMS code | SVC-04 |
| `/recover` | Public | Recovery by contact or recovery code | SVC-07 |
| `/onboarding/consents` | Patient | Required and optional consents | SVC-10, SVC-11 |
| `/onboarding/safety` | Patient | Phone, emergency contact, location consent | SVC-13 |
| `/onboarding/choose-path` | Patient | Start intake or book directly | — |
| `/intake` | Patient | AI intake chat with questionnaire cards and progress | SVC-14–21, `/hubs/intake` |
| `/intake/safety-check` | Patient | C-SSRS screener for direct booking | SVC-17 |
| `/intake/summary` | Patient | Review and correct the summary; submit | SVC-22, SVC-23 |
| `/dashboard` | Patient | Home: status of my case, next appointment, homework, mood check-in | SVC-45, 62, 66, `/hubs/patient` |
| `/help-now` | Patient | Crisis screen (also shown as an overlay when the gate blocks) | SVC-24 |
| `/matching` | Patient | 3 suggested therapists with reasons | SVC-38, SVC-41 |
| `/therapists` | Patient | Browse and filter all approved therapists | SVC-39 |
| `/therapists/[id]` | Patient | Profile: approach, specialties, languages, availability preview | SVC-40, SVC-42 |
| `/book/[therapistId]` | Patient | Pick a slot, confirm | SVC-42, 43, 44 |
| `/book/confirmed` | Patient | Confirmation, add to calendar, what to expect | SVC-45 |
| `/appointments` | Patient | Upcoming and past sessions | SVC-45 |
| `/appointments/[id]` | Patient | Detail, meeting link (shown 10 min before), reschedule, cancel | SVC-45, 46, 47 |
| `/homework` | Patient | My tasks and feedback | SVC-62 |
| `/homework/[taskId]` | Patient | Task instructions, check-in form, therapist feedback | SVC-62, SVC-63 |
| `/check-in` | Patient | Daily mood 0–10 + optional note | SVC-66 |
| `/settings/profile` | Patient | Alias, language, time zone | SVC-09 |
| `/settings/security` | Patient | Password, MFA, passkeys, active sessions, recovery codes | SVC-09 |
| `/settings/privacy` | Patient | Consents, access log, export, delete | SVC-11, 73, 74 |
| `/settings/notifications` | Patient | Channels, quiet hours, reminder frequency | SVC-82 |
| `/settings/safety` | Patient | Edit emergency contact and location consent | SVC-13 |
| `/offline` | Any | Shown by the service worker with crisis numbers | PWA |
| `/session-expired` | Any | Re-login prompt; unsent draft kept locally for 10 min | — |
| `/maintenance` | Any | Planned downtime page with crisis numbers | Feature flag |
| `/not-found` | Any | 404 with links home and to crisis resources | — |

### 6.2 Staff portal (Angular, `staff.<domain>`)

| Route | Access | Purpose | Services |
| --- | --- | --- | --- |
| `/staff/login` | Public | Staff sign-in (OIDC or password) | SVC-03 |
| `/staff/login/mfa` | Public | MFA step (always required) | SVC-04 |
| `/staff/recover` | Public | Recovery; MFA reset needs an admin | SVC-07 |
| `/staff/apply` | Public | Create applicant account | SVC-67 |
| `/staff/apply/profile` | Applicant | Personal and practice details | SVC-67 |
| `/staff/apply/license` | Applicant | Authority, number, expiry, specialties | SVC-67 |
| `/staff/apply/documents` | Applicant | Upload license and certificates | SVC-68 |
| `/staff/apply/review` | Applicant | Check everything, submit | SVC-69 |
| `/staff/apply/status` | Applicant | Status, messages from reviewer | SVC-67 |
| `/t/dashboard` | Therapist | Alerts, today's sessions, new patients, files awaiting approval | SVC-45, 50, `/hubs/staff` |
| `/t/patients` | Therapist | Caseload list | SVC-50 |
| `/t/patients/:id` | Therapist | Patient overview: risk, scores trend, next session, actions | SVC-51 |
| `/t/patients/:id/intake` | Therapist | Evidence inspector: AI file left, chat timeline right | SVC-51–54, SVC-58 |
| `/t/patients/:id/plan` | Therapist | Treatment plan editor with version history | SVC-55 |
| `/t/patients/:id/homework` | Therapist | Assign, edit, cancel tasks; library picker | SVC-59–61, SVC-64 |
| `/t/patients/:id/reports` | Therapist | Weekly reports, check-in feed, mood chart | SVC-65 |
| `/t/schedule` | Therapist | Calendar of sessions; mark outcome | SVC-45–48 |
| `/t/availability` | Therapist | Weekly pattern, exceptions, meeting link | SVC-49 |
| `/t/alerts` | Therapist | Crisis and risk alerts for my patients | SVC-25 (filtered), SVC-27 |
| `/t/profile` | Therapist | Public profile, specialties, languages, capacity | SVC-49 |
| `/s/triage` | Supervisor | Triage queue with SLA timers | SVC-34, `/hubs/staff` |
| `/s/triage/:id` | Supervisor | Case review and decision | SVC-35–37 |
| `/s/crisis` | Supervisor | Crisis console: open events with timers, gate health | SVC-25, `/hubs/crisis` |
| `/s/crisis/:eventId` | Supervisor | Event workspace: excerpt, contact reveal, action log, scripts | SVC-26–31 |
| `/s/on-call` | Supervisor | Start/end shift, handover | SVC-31, SVC-33 |
| `/s/on-call/rota` | ClinicalLead | Rota editor, coverage gaps | SVC-33 |
| `/s/qa` | Supervisor | Weekly QA sample review | SVC-78 |
| `/a/verifications` | Admin | Therapist application queue | SVC-70 |
| `/a/verifications/:id` | Admin | Application detail, documents, registry check, decision | SVC-70, SVC-71 |
| `/a/users` | Admin | Users: suspend, unlock, MFA reset | SVC-72 |
| `/a/audit` | Admin | Audit search and export | SVC-75, SVC-76 |
| `/a/rules` | ClinicalLead, Admin | Crisis rule-pack change requests and evaluation results | SVC-77 |
| `/a/health` | Admin | Gate, classifier, LLM, paging, queue health | SVC-81 |
| `/a/flags` | Admin | Kill switches | SVC-80 |
| `/a/metrics` | Supervisor, Admin | Pilot metrics dashboard | SVC-79 |
| `/staff/settings` | Staff | Password, MFA, sessions, notifications | SVC-09, SVC-82 |
| `/staff/forbidden` | Staff | Shown when a guard denies a route | — |

## 7. Button catalog

Every button below has one handler and one API action. Shared rules for all of them: a button is disabled while its request runs and shows a spinner; a failed call keeps the user's input and shows the error code's message; destructive actions ask for confirmation; buttons the user's role cannot use are not rendered at all.

### 7.1 Patient app — public, sign-up and onboarding

| ID | Button | Page | What it does | API | Rules |
| --- | --- | --- | --- | --- | --- |
| PB-01 | Start | `/` | Goes to eligibility | — | — |
| PB-02 | I need help now | `/`, every public page | Opens `/crisis-resources` | — | Works offline |
| PB-03 | Call \[local number\] | `/crisis-resources`, `/help-now` | Opens `tel:` link | — | Number from jurisdiction config |
| PB-04 | Continue | `/start/eligibility` | Checks 18+ and area; routes to signup or to local-services message | — | Stored after signup via SVC-12 |
| PB-05 | Generate alias | `/signup` | Replaces alias with a random two-word alias | Client only | Never uses real names |
| PB-06 | Create account | `/signup` | Validates fields; creates account; sends code | SVC-01 | Password ≥ 10 chars, not in breached list; rate-limited |
| PB-07 | Verify | `/signup/verify` | Checks the code | SVC-02 | 5 tries, then new code required |
| PB-08 | Resend code | `/signup/verify` | New code | SVC-01 (resend) | Enabled after 60 s; max 5 per hour |
| PB-09 | Download codes / I saved them | `/signup/recovery-codes` | Downloads a .txt; continues | — | Continue enabled only after download or copy |
| PB-10 | Set up authenticator (optional) | `/signup/recovery-codes`, `/settings/security` | Shows QR, verifies first code | SVC-09 | — |
| PB-11 | Sign in | `/login` | Password step | SVC-03 | Lockout after 5 failures for 15 min |
| PB-12 | Verify | `/login/mfa` | MFA step; sets cookies; redirects to last route | SVC-04 | — |
| PB-13 | Forgot password | `/login` | Goes to `/recover` | — | — |
| PB-14 | Send recovery code / Use recovery code | `/recover` | Starts recovery by contact or code | SVC-07 | Same response whether or not the account exists |
| PB-15 | Read full text | `/onboarding/consents` | Expands consent text | — | Must open each required consent |
| PB-16 | Accept and continue | `/onboarding/consents` | Records each consent | SVC-11 | Disabled until all required are ticked |
| PB-17 | Save safety details | `/onboarding/safety` | Saves contact and location consent | SVC-13 | Phone format check |
| PB-18 | Skip for now | `/onboarding/safety` | Continues; reminder in 3 days | — | Shown with a short "why this helps" note |
| PB-19 | Start intake | `/onboarding/choose-path` | Starts intake | SVC-14 | — |
| PB-20 | Book directly | `/onboarding/choose-path` | Goes to safety check, then therapist list | — | Safety check cannot be skipped |

### 7.2 Patient app — intake and summary

| ID | Button | Page | What it does | API | Rules |
| --- | --- | --- | --- | --- | --- |
| PB-21 | Send | `/intake` | Sends text with a client message id | SVC-16 | Disabled while waiting for reply; Enter sends, Shift+Enter new line; max 2,000 chars |
| PB-22 | Answer option (0–3 / Yes / No) | `/intake` question card | Records one questionnaire answer | SVC-17 | Large touch targets; answer can be changed until the card is left |
| PB-23 | Skip this question | `/intake` | Skips topic or item | SVC-18 | C-SSRS items cannot be skipped; shows "talk to a human" instead |
| PB-24 | Pause | `/intake` header | Saves and returns to dashboard | SVC-19 | — |
| PB-25 | Talk to a person | `/intake` header | Opens a High-priority triage case; shows expected wait | SVC-20 | Confirmation sheet first |
| PB-26 | Help Now | Header on every page | Opens crisis screen; opens Critical event | SVC-24 | Never disabled; works even if the API is slow (shows crisis numbers first, then sends) |
| PB-27 | Retry | `/intake` failed message | Resends with the same client message id | SVC-16 | Idempotent; no duplicate turns |
| PB-28 | Switch to form | `/intake` (shown when AI is down) | Moves to form mode with the same questions | SVC-14 (`mode=Form`) | Progress kept |
| PB-29 | Finish | `/intake` | Ends chat; generates summary | SVC-21 | Enabled at ≥ 60% or always after the core topics; C-SSRS runs first if missing |
| PB-30 | Edit | `/intake/summary` | Makes a paragraph editable | — | — |
| PB-31 | Save changes | `/intake/summary` | Saves patient corrections | SVC-22 | `If-Match`; marks text "patient-corrected" |
| PB-32 | Add something we missed | `/intake/summary` | Adds a free-text note | SVC-22 | Note goes through the crisis gate |
| PB-33 | Submit to the care team | `/intake/summary` | Locks summary; creates triage case | SVC-23 | Confirmation: "A clinician will review this within \[SLA\]" |

### 7.3 Patient app — matching, booking, appointments

| ID | Button | Page | What it does | API | Rules |
| --- | --- | --- | --- | --- | --- |
| PB-34 | View profile | `/matching`, `/therapists` | Opens therapist profile | SVC-40 | — |
| PB-35 | Why suggested? | `/matching` | Shows match reasons | — | From SVC-38 data |
| PB-36 | Not a fit | `/matching` | Removes suggestion; optional reason | SVC-41 | No penalty; supervisor sees if all 3 are declined |
| PB-37 | Browse all therapists | `/matching` | Goes to `/therapists` | — | — |
| PB-38 | Apply filters / Clear | `/therapists` | Filters by specialty, language, gender, availability | SVC-39 | Filters in query string (no PHI) |
| PB-39 | Book a session | `/therapists/[id]` | Goes to booking | — | Hidden if triage not approved and safety check not done |
| PB-40 | Slot (time button) | `/book/[therapistId]` | Holds the slot for 5 min | SVC-43 | Countdown shown; slot taken → refresh list |
| PB-41 | Confirm booking | `/book/[therapistId]` | Books; creates care assignment on first booking | SVC-44 | Idempotency key; hold expired → pick again |
| PB-42 | Add to calendar | `/book/confirmed`, `/appointments/[id]` | Downloads .ics without patient names | — | Title is neutral: "Appointment" |
| PB-43 | Open meeting link | `/appointments/[id]`, `/dashboard` | Opens the therapist's external link | — | Visible from 10 min before start |
| PB-44 | Reschedule | `/appointments/[id]` | Picks a new slot | SVC-46 | Allowed until 24 h before; later → contact therapist |
| PB-45 | Cancel | `/appointments/[id]` | Cancels with reason | SVC-47 | Confirmation; therapist notified |

### 7.4 Patient app — dashboard, homework, check-in, settings

| ID | Button | Page | What it does | API | Rules |
| --- | --- | --- | --- | --- | --- |
| PB-46 | Continue intake | `/dashboard` | Resumes intake | SVC-14 | Shown while intake is open |
| PB-47 | Mood 0–10 + Save | `/dashboard`, `/check-in` | Logs daily mood and optional note | SVC-66 | One per day (later entries update it); note through crisis gate |
| PB-48 | Open task | `/homework` | Opens task | — | — |
| PB-49 | Done / Partly / Not today | `/homework/[taskId]` | Records check-in | SVC-63 | "Not today" never shows a warning or streak loss |
| PB-50 | Save check-in | `/homework/[taskId]` | Saves outcome, mood, note | SVC-63 | Note through crisis gate; offline → queued and sent on reconnect |
| PB-51 | Mark feedback read | `/homework/[taskId]` | Marks therapist feedback read | SVC-64 (read flag) | Automatic when viewed |
| PB-52 | Save | `/settings/profile` | Updates alias, language, time zone | SVC-09 | Language switch re-renders RTL/LTR |
| PB-53 | Change password | `/settings/security` | Changes password; signs out other sessions | SVC-09 | Requires current password |
| PB-54 | Sign out this device / Sign out everywhere | `/settings/security` | Revokes sessions | SVC-06 | — |
| PB-55 | Regenerate recovery codes | `/settings/security` | New codes; old ones invalid | SVC-09 | Requires password + MFA if set |
| PB-56 | Withdraw / Grant | `/settings/privacy` | Changes one consent | SVC-11 | Withdrawing a required consent explains the effect and opens the delete flow |
| PB-57 | View access log | `/settings/privacy` | Lists who opened my file | SVC-73 | Shows role and time, not staff names, in the pilot |
| PB-58 | Export my data | `/settings/privacy` | Requests export | SVC-74 | Re-auth; ready within 7 days; link expires in 72 h |
| PB-59 | Delete my account | `/settings/privacy` | Requests deletion | SVC-74 | Re-auth + typed confirmation; explains retained records; open crisis event blocks it until resolved |
| PB-60 | Save preferences | `/settings/notifications` | Saves channels and quiet hours | SVC-82 | Crisis contact ignores quiet hours (stated on screen) |
| PB-61 | Sign out | Header menu | Ends session | SVC-06 | Clears cached pages |

### 7.5 Staff portal — sign-in and therapist application

| ID | Button | Page | What it does | API | Rules |
| --- | --- | --- | --- | --- | --- |
| SB-01 | Sign in | `/staff/login` | Password or OIDC redirect | SVC-03 | — |
| SB-02 | Verify | `/staff/login/mfa` | MFA | SVC-04 | MFA always required |
| SB-03 | Create applicant account | `/staff/apply` | Creates account and draft | SVC-67 | TOTP setup required before continuing |
| SB-04 | Save and continue | `/staff/apply/profile`, `/license` | Saves the step | SVC-67 | Draft autosaves every 30 s |
| SB-05 | Upload document | `/staff/apply/documents` | Gets SAS URL, uploads, waits for scan | SVC-68 | PDF/JPG/PNG ≤ 10 MB; infected → rejected with message |
| SB-06 | Remove document | `/staff/apply/documents` | Deletes a draft document | SVC-68 (DELETE) | Only before submit |
| SB-07 | Submit application | `/staff/apply/review` | Submits | SVC-69 | Disabled until license, expiry and ≥ 1 clean document present |
| SB-08 | Reply to reviewer | `/staff/apply/status` | Answers a "more info" request | SVC-67 | — |

### 7.6 Staff portal — therapist workspace

| ID | Button | Page | What it does | API | Rules |
| --- | --- | --- | --- | --- | --- |
| SB-09 | Open file | `/t/dashboard`, `/t/patients` | Opens patient overview | SVC-51 | Audited read |
| SB-10 | Acknowledge alert | `/t/dashboard` banner, `/t/alerts` | Acknowledges a crisis event for my patient | SVC-27 | Stops paging if first to acknowledge |
| SB-11 | Hand to on-call | `/t/alerts` | Leaves the event with on-call; adds a note | SVC-29 | — |
| SB-12 | Show source | `/t/patients/:id/intake` statement | Highlights source chat lines | SVC-52 | Shows "source removed" after 90-day purge |
| SB-13 | Edit statement | `/t/patients/:id/intake` | Inline edit of an AI statement | — | Saved with SB-15 |
| SB-14 | Dismiss statement | `/t/patients/:id/intake` | Removes a statement with reason | SVC-54 | Kept in audit, hidden from file |
| SB-15 | Approve file | `/t/patients/:id/intake` | Approves the clinical file (with edits) | SVC-53 | Required before the first plan can be saved |
| SB-16 | Rate this file | `/t/patients/:id/intake` | 1–5 usefulness + comment | SVC-58 | Feeds pilot metric |
| SB-17 | Raise risk level | `/t/patients/:id` | Raises level with reason | SVC-56 | Lowering is not offered here |
| SB-18 | Raise crisis | `/t/patients/:id` | Opens a manual crisis event | SVC-32 | Confirmation with level choice |
| SB-19 | Save plan | `/t/patients/:id/plan` | Saves a new plan version | SVC-55 | `If-Match`; conflict shows both versions |
| SB-20 | View version | `/t/patients/:id/plan` | Opens an older version, read-only | SVC-55 | — |
| SB-21 | Assign task | `/t/patients/:id/homework` | Opens builder; creates task | SVC-59, SVC-60 | Template or own text; schedule and due date required |
| SB-22 | Edit task / Cancel task | `/t/patients/:id/homework` | Changes or cancels a task | SVC-61 | Patient sees the change; cancel needs confirmation |
| SB-23 | Send feedback | `/t/patients/:id/homework`, `/reports` | Sends feedback on a task | SVC-64 | Preview before sending |
| SB-24 | Open weekly report | `/t/patients/:id/reports` | Shows the report | SVC-65 | Risk flags pinned at top |
| SB-25 | Mark completed / Mark no-show | `/t/schedule` | Records appointment outcome | SVC-48 | Enabled after start time |
| SB-26 | Reschedule / Cancel | `/t/schedule` | Moves or cancels a session | SVC-46, SVC-47 | Patient notified; reason required |
| SB-27 | Discharge patient | `/t/patients/:id` | Ends the care assignment | SVC-57 | Reason; future appointments cancelled; patient told how to return |
| SB-28 | Save availability | `/t/availability` | Saves weekly pattern and exceptions | SVC-49 | Booked slots cannot be removed (shown locked) |
| SB-29 | Save profile | `/t/profile` | Updates profile and capacity | SVC-49 | Capacity below current caseload is refused |

### 7.7 Staff portal — supervisor

| ID | Button | Page | What it does | API | Rules |
| --- | --- | --- | --- | --- | --- |
| SB-30 | Claim | `/s/triage` | Locks a case for 15 min and opens it | SVC-35 | Already claimed → shows who and until when |
| SB-31 | Release | `/s/triage/:id` | Releases the claim | SVC-35 (DELETE) | Automatic on leaving the page |
| SB-32 | Approve | `/s/triage/:id` | Approves level and match suggestions; notifies patient | SVC-37 (`Approve`) | Requires the claim |
| SB-33 | Override risk | `/s/triage/:id` | Changes level with reason | SVC-37 (`OverrideRisk`) | Lowering from High/Critical needs a clinical lead co-sign |
| SB-34 | Change suggestions | `/s/triage/:id` | Replaces suggested therapists | SVC-37 (`Reassign`) | Only approved, available therapists listed |
| SB-35 | Escalate to crisis | `/s/triage/:id` | Opens a crisis event | SVC-37 (`Escalate`) | — |
| SB-36 | Close case | `/s/triage/:id` | Closes without routing (e.g. out of scope) with reason and referral | SVC-37 (`Close`) | Patient sees referral resources |
| SB-37 | Acknowledge | `/s/crisis`, `/s/crisis/:eventId` | Takes ownership; stops escalation | SVC-27 | One owner; second click shows the owner |
| SB-38 | Reveal contact and location | `/s/crisis/:eventId` | Step-up MFA, then shows decrypted details for 10 min | SVC-08, SVC-28 | High-severity audit row; auto-hide after 10 min |
| SB-39 | Call patient | `/s/crisis/:eventId` | Opens `tel:` and logs a call attempt | SVC-29 (`CallAttempt`) | Outcome prompt after the call |
| SB-40 | Log contact made | `/s/crisis/:eventId` | Records contact and a short note | SVC-29 (`Contacted`) | Moves event to InContact |
| SB-41 | Call emergency services | `/s/crisis/:eventId` | Shows the local protocol script and number; logs the call | SVC-29 (`EmergencyCalled`) | Requires revealed location or a note why not |
| SB-42 | Add note | `/s/crisis/:eventId` | Adds a timeline note | SVC-29 (`Note`) | — |
| SB-43 | Hand over | `/s/crisis/:eventId`, `/s/on-call` | Transfers ownership | SVC-31 | Receiver must accept |
| SB-44 | Resolve | `/s/crisis/:eventId` | Closes with outcome; unlocks intake if safe | SVC-30 | Outcome required; FalsePositive needs reason; creates 72-h review task |
| SB-45 | Start shift / End shift | `/s/on-call` | Marks availability for paging | SVC-33 | End blocked while owning open events |
| SB-46 | Send test page | `/s/on-call` | Sends a test SMS and call | SVC-81 | Must succeed at shift start |
| SB-47 | Save rota | `/s/on-call/rota` | Saves shifts | SVC-33 | Coverage gaps highlighted; save allowed with warning |
| SB-48 | Submit review | `/s/qa` | Saves a QA review | SVC-78 | "Missed risk" opens a rule change request |

### 7.8 Staff portal — admin and clinical lead

| ID | Button | Page | What it does | API | Rules |
| --- | --- | --- | --- | --- | --- |
| SB-49 | Open registry | `/a/verifications/:id` | Opens the official license registry in a new tab | — | Registry reference typed into the record |
| SB-50 | View document | `/a/verifications/:id` | Opens the file via a 5-min SAS link | SVC-70 | Audited |
| SB-51 | Approve | `/a/verifications/:id` | Approves the therapist | SVC-71 | Step-up MFA; registry reference required |
| SB-52 | Reject | `/a/verifications/:id` | Rejects with reason | SVC-71 | Reason sent to applicant |
| SB-53 | Request more info | `/a/verifications/:id` | Sends a question | SVC-71 | — |
| SB-54 | Suspend / Reinstate | `/a/users` | Suspends or reinstates a user | SVC-72 | Reason; therapist suspension flags their patients to supervisors |
| SB-55 | Unlock account | `/a/users` | Clears lockout | SVC-72 | Audited |
| SB-56 | Reset MFA | `/a/users` | Clears MFA so the user re-enrolls | SVC-72 | Needs a second admin's approval |
| SB-57 | Search | `/a/audit` | Runs audit query | SVC-75 | Step-up; max 90-day window per query |
| SB-58 | Export | `/a/audit` | Signed export with ledger digest | SVC-76 | Step-up; export itself is audited |
| SB-59 | Propose change | `/a/rules` | Uploads a new rule pack version | SVC-77 | Starts an evaluation run automatically |
| SB-60 | Approve and deploy | `/a/rules` | Activates a rule pack | SVC-77 | Clinical lead only; evaluation must be green |
| SB-61 | Roll back | `/a/rules` | Re-activates the previous pack | SVC-77 | Immediate; audited |
| SB-62 | Toggle | `/a/flags` | Turns a kill switch on or off | SVC-80 | Confirmation; reason required |
| SB-63 | Run health check | `/a/health` | Synthetic checks on all parts | SVC-81 | — |
| SB-64 | Export metrics (CSV) | `/a/metrics` | Downloads aggregated metrics | SVC-79 | Aggregates only; no patient rows |

## 8. Edge cases and failure scenarios

The rule across all 56 cases: when in doubt, the system shows crisis resources and brings in a human; it never drops a patient's words silently. Each case below needs an automated test (unit, integration or Playwright) before the pilot starts.

### 8.1 Connectivity and client

| ID | Scenario | Detection | System behaviour | What the user sees |
| --- | --- | --- | --- | --- |
| EC-01 | Patient loses connection while sending an intake message | Fetch fails or times out | Message kept in IndexedDB with its client id; resent on reconnect; server dedupes by client id | "Not sent yet — we'll send it when you're back online" + Retry |
| EC-02 | Connection drops after the server got the message but before the reply | Reply never arrives | On reconnect the client calls SVC-15 and renders missed turns | Conversation catches up |
| EC-03 | SignalR disconnects | Hub `onclose` | Automatic reconnect with backoff (0, 2, 10, 30 s); falls back to polling SVC-15 every 5 s | Small "reconnecting" banner |
| EC-04 | Patient is offline and taps Help Now | `navigator.onLine` false or request fails | Crisis numbers shown immediately from the PWA cache; the request is queued | Local emergency number and crisis line, with "call now" buttons |
| EC-05 | Same patient opens intake in two tabs | Second tab gets a newer session lease | Older tab becomes read-only | "This conversation is open in another tab" |
| EC-06 | Access token expires mid-intake | 401 from API | BFF refreshes silently; on refresh failure keeps the draft locally for 10 min | Re-login screen; draft restored after login |
| EC-07 | Patient double-taps Send, Confirm booking or Submit | Same idempotency key | Server returns the first result | No duplicates |
| EC-08 | Old browser without required features | Feature check at load | Basic server-rendered form mode | Notice to update the browser; crisis numbers visible |
| EC-09 | Device clock is wrong | Server time used for all timers | Countdown uses server-sent deadlines | Correct times |

### 8.2 AI and integration failures

| ID | Scenario | Detection | System behaviour | What the user sees |
| --- | --- | --- | --- | --- |
| EC-10 | LLM call times out or returns 5xx | 20 s timeout, Polly | One retry; then a fixed reply and offer to continue in form mode | "I'm having trouble right now. You can keep going with a short form." |
| EC-11 | LLM provider is down for minutes | Circuit breaker open | Intake switches to form mode for everyone; status alert to admin | Form mode banner |
| EC-12 | LLM API key or credentials invalid / expired | 401/403 from provider | Circuit opens at once; P1 alert; key rotation runbook | Same as EC-11 |
| EC-13 | LLM returns a diagnosis, medication advice or off-script text | Output validator | Reply replaced by a safe fixed reply; incident logged with prompt version | Neutral reply |
| EC-14 | LLM returns invalid JSON for a summary | Schema validation | Retry once with a repair prompt; then mark summary "needs manual write-up" for the supervisor | "Your summary is being prepared by our team" |
| EC-15 | Summary statement has no source message | Evidence check | Statement dropped before it is shown | — |
| EC-16 | De-identification sidecar is down | Health check / call fails | LLM calls blocked (never send raw text); form mode | Form mode banner |
| EC-17 | Patient types a prompt injection ("ignore your rules…") | Gate runs first; system prompt isolation | Questionnaire cards and crisis scripts are code, so they cannot be changed; validator checks output | Normal conversation |
| EC-18 | Patient writes in a mix of Arabic, English and Arabizi | Language detector | Normaliser handles all three; replies in the patient's chosen language | Normal conversation |
| EC-19 | Patient pastes a very long text or a file | Length check | Rejects over 2,000 characters, asks to send in parts; files not accepted | Inline message |
| EC-20 | Paging provider (SMS/voice) fails | Delivery webhook or error | Second channel used at once; alert to admin; escalation timer continues | — |
| EC-21 | Email provider fails | Outbox error | Retry with backoff for 24 h; in-app notice as backup | — |
| EC-22 | Malware found in an uploaded license document | Scan result | Document quarantined and marked Infected | "This file could not be accepted. Upload a different copy." |

### 8.3 Crisis edge cases

| ID | Scenario | Detection | System behaviour | What the user sees |
| --- | --- | --- | --- | --- |
| EC-23 | Crisis gate throws an error | Exception in gate | Fail closed: system crisis event, crisis script, LLM not called | Crisis screen |
| EC-24 | Classifier times out | 300 ms budget | Rules still apply; message continues if no rule fired; repeated timeouts → CR-7 system alert | Normal conversation |
| EC-25 | Nobody acknowledges within the time limit | Escalation loop | Pages backup, then clinical lead; every page logged | Patient keeps seeing "we are contacting you" + crisis numbers |
| EC-26 | No on-call shift is staffed | Rota check every 5 min | New signups and new intakes paused; existing patients see crisis lines on every screen; P1 alert | "New sign-ups are paused right now. If you need help now, call …" |
| EC-27 | Patient closes the app right after a crisis message | Event already open | Paging and calling continue regardless | — |
| EC-28 | Patient says "I was joking" after a trigger | New message | Event stays open; only a clinician can close it (FalsePositive with reason) | "Thanks for telling us. Someone will still check in with you." |
| EC-29 | False positive (e.g. "suicide prevention course", quoting a film) | Clinician review | Resolve as FalsePositive; phrase added to negation test cases after review | Chat unlocks |
| EC-30 | Crisis expressed in a homework note or mood note | Gate on SVC-63/66 | Same rules as intake | Crisis screen |
| EC-31 | Crisis expressed about someone else ("my brother wants to kill himself") | Third-party rule | High event, different script: resources for helping others | Guidance for helping another person |
| EC-32 | Patient discloses abuse of a child | Abuse rule | High event, mandatory-reporting checklist for the clinician | Support message |
| EC-33 | Patient is unreachable by phone | Call attempts logged | After 3 attempts in 15 min for Critical: emergency protocol if location is known; else ClosedUnreachable with lead sign-off and a follow-up task | — |
| EC-34 | Patient has no location on file | Reveal shows none | Script asks the clinician to seek location during the call; never auto-dispatch | — |
| EC-35 | Two supervisors acknowledge at once | RowVersion conflict | First wins; second sees the owner | "Already acknowledged by \[role\]" |
| EC-36 | A patient triggers many events in one hour | Event count | Events linked under one open episode; one owner; no paging storm | — |
| EC-37 | Patient in crisis is outside the pilot jurisdiction (travelling) | Location reveal / patient says so | Script gives the local international emergency number (112/911) and local resources | Resources for where they are |
| EC-38 | Crisis rule pack deploy breaks matching | Evaluation gate + gate heartbeat | Deploy blocked if evaluation fails; heartbeat failure triggers automatic rollback to previous pack | — |

### 8.4 Authentication and security

| ID | Scenario | Detection | System behaviour | What the user sees |
| --- | --- | --- | --- | --- |
| EC-39 | Brute-force or credential stuffing | Rate limits, WAF | Lockout 15 min; CAPTCHA after 3 failures; IP throttling | Generic "wrong details" message |
| EC-40 | Stolen refresh token is reused | Token already consumed | Whole token family revoked; user alerted | Signed out everywhere; notice to change password |
| EC-41 | Patient loses phone (MFA) and recovery codes | Recovery fails | Identity re-verification by the verified contact + supervisor-assisted reset after 24 h wait | Clear recovery steps |
| EC-42 | Therapist tries to open a patient not assigned to them | RLS returns nothing; API returns 404 | Audit row with Outcome = Denied; repeated denials alert | "Not found" |
| EC-43 | Staff opens many files quickly | > 20 files / hour | Alert to privacy officer | — |
| EC-44 | Fake or forged license | Registry check | Rejected; reason recorded | Rejection email |
| EC-45 | License expires during the pilot | Daily job | New bookings blocked; existing patients flagged to supervisor for reassignment | Therapist banner 30 days before |
| EC-46 | Admin tries to read clinical data | RLS has no Admin path | Nothing returned | — |

### 8.5 Human error and data integrity

| ID | Scenario | Detection | System behaviour | What the user sees |
| --- | --- | --- | --- | --- |
| EC-47 | Patient submits a summary with a mistake | — | Patient can add a correction from the dashboard until the therapist approves the file | "Add a correction" link |
| EC-48 | Supervisor lowers a risk level by mistake | Override audit | Lowering High/Critical needs a clinical lead co-sign; every override reversible by a new decision | Confirmation dialog with consequences |
| EC-49 | Two supervisors open the same triage case | Claim lock | Second sees read-only view and who holds it | Lock banner |
| EC-50 | Therapist saves a plan while another version was saved | `If-Match` 412 | Shows both versions side by side | Merge view |
| EC-51 | Two patients book the same slot | Hold + RowVersion | Second hold fails | "That time was just taken" + refreshed slots |
| EC-52 | Therapist cancels; patient has no other slot | Cancellation event | Supervisor notified to rebook within 48 h | Apology + "we'll help you rebook" |
| EC-53 | Patient does not show up twice | Outcome NoShow × 2 | Supervisor gets a safety follow-up task (missed sessions can signal risk) | Gentle check-in message |
| EC-54 | Patient withdraws AI-processing consent mid-intake | Consent change | Intake switches to form mode; no further LLM calls; prior AI drafts kept for clinicians only if lawful | Explanation of what changes |
| EC-55 | Patient asks for deletion while in care or with an open crisis event | Data request | Deletion blocked until the crisis event is resolved; legally required records kept, rest crypto-shredded | What is deleted and what is kept, and why |
| EC-56 | Database failover during writes | Transient SQL errors | EF Core retry policy for transient errors; outbox guarantees each notification is sent once | Brief "saving…" delay |

## 9. Build order and open decisions

Build the safety foundation first: nothing patient-facing ships until the crisis service, paging and audit pass their tests. Estimates assume 2-week sprints for a team of about 5 engineers plus a part-time clinical lead.

| Sprint | Deliverable | Done when |
| --- | --- | --- |
| 1 | Repo, CI/CD, IaC, environments; SQL migrations for identity, consent, audit ledger; RLS policy | Pipeline green; RLS tests prove a therapist cannot read an unassigned patient |
| 2 | Identity (signup, verify, login, MFA, recovery, refresh rotation) in API, Next.js BFF and Angular | Security tests pass, including token reuse |
| 3 | Crisis service: normaliser, Arabic + English rule packs, classifier, fail-closed gate, escalation loop, two-channel paging, crisis console | Evaluation set (≥ 200 cases, 50 crisis) passes with ≥ 95% recall; page reaches a phone in < 30 s |
| 4 | Consent and onboarding; intake state machine; questionnaire cards scored by code; form-mode fallback | PHQ-9/GAD-7/C-SSRS scoring unit tests match published scoring |
| 5 | AI gateway (de-identification, prompts, validator, circuit breaker); AI intake chat; summary + evidence links | Validator red-team set passes; no raw PII reaches the LLM in logs of 1,000 test chats |
| 6 | Triage queue and decisions; matching; therapist application and verification | End-to-end: intake → triage → approved suggestion |
| 7 | Availability, booking, appointments, reminders; therapist dashboard and evidence inspector; file approval | Double-booking tests pass |
| 8 | Treatment plans, homework, check-ins, weekly reports, feedback | Check-in notes go through the gate in tests |
| 9 | Privacy centre (access log, export, delete), retention jobs, QA sampling, metrics, feature flags | Deletion test crypto-shreds and leaves the ledger intact |
| 10 | Hardening: pen test, prompt-injection red team, load test, accessibility (WCAG 2.2 AA) and RTL review, runbooks, on-call drills | No open High findings; two successful crisis drills |

**Open decisions**

- [ ] Pilot jurisdiction, and therefore cloud region, local emergency number, health-data law and record-retention period.
- [ ] LLM provider and model that can sign a BAA/DPA with in-region, zero-retention processing.
- [ ] Staffed on-call hours: 24/7, or limited hours with signups paused outside them.
- [ ] Source and licence for validated Arabic versions of PHQ-9, GAD-7 and C-SSRS.
- [ ] Staff identity: organisation OIDC provider, or ASP.NET Identity with TOTP/passkeys.
- [ ] Who owns the crisis rule packs clinically, and the review cadence (suggested: weekly during the pilot).
- [ ] Whether the medical-device regulator in the chosen jurisdiction treats AI triage as regulated software.
- [ ] Whether therapists keep their own notes in the platform or in their existing system.

## 10. Addendum v2: new screens and the admin-managed platform

This addendum comes from the second persona review (patient, psychologist, systems analyst, tech lead). It adds 33 screens and one rule that overrides earlier hard-coded assumptions: **every list, setting, text, test and rule in the system is managed by hand from the admin portal.** Nothing a clinician or operator might need to change ships as a constant in code.

### 10.1 The admin-managed rule

| Rule | What it means in the build |
| --- | --- |
| No business constants in code | Emergency numbers, SLAs, pilot limits, quiet hours, retention periods, test items, score bands, scripts, FAQ, catalogs and notification rules all live in tables and are read through a cached `IConfigProvider` (5 min TTL, busted by outbox event `ConfigChanged`). |
| Two-tier approval | Ordinary changes (catalog item, FAQ, survey) save directly. **Safety-tier** changes (emergency numbers, crisis SLAs, crisis scripts, consents, any instrument with a safety item, crisis rules) go to `PendingChange` and need the clinical lead to approve. Author ≠ approver is enforced in the API, not the UI. |
| Versioned, never overwritten | Content, instruments and rules get a new version row on every change. Old results stay linked to the version that produced them. The previous version stays live until the new one is approved. |
| Reason + audit on every save | Every admin write requires a free-text reason and writes old value, new value, actor, reason and time to `AuditLog` in the same transaction. |
| Disable, don't delete | An item already referenced (a modality a therapist uses, a closure reason on old cases) can only be disabled. Disabled items disappear from new pickers but render on old records. |
| Bilingual before active | Arabic and English text are both required before an item can be activated. |
| Fail-safe defaults | If config can't be read, Crisis.Worker uses the last-known-good snapshot that ships in the deployment. It never falls back to "no rule". |

### 10.2 New patient screens (Next.js)

| Code | Screen | Route | Notes |
| --- | --- | --- | --- |
| P36–P39 | Messages list, chat with AI, safety plan, notifications | /messages, /chat, /safety-plan, /notifications | From the first review round |
| P40 | Chat thread with therapist | /messages/\[id\] | Async, no read-receipt pressure |
| P41 | Session lobby | /sessions/\[id\]/lobby | Device check, breathing exercise while waiting |
| P42 | Live session | /sessions/\[id\]/live | **Later phase.** PRD excludes in-app video for the pilot |
| P43 | Session summary | /sessions/\[id\]/summary | Homework + next booking |
| P44 | Change therapist | /care/change-therapist | No reason required, no blame |
| P45–P46 | Dialogs and states | — | Empty, loading, error, confirm |
| P47 | Preferences | /settings/preferences | Therapist gender, language, times, style. Options come from Catalogs |
| P48 | My progress | /progress | Friendly trend only. **Raw scores are never shown to the patient** |
| P49 | Self-help library | /library | ContentItems of type SelfHelp, clinically reviewed |
| P50 | Private journal | /journal | Private by default. Per-entry share toggle. Text still runs through the safety classifier |
| P51 | Support and FAQ | /support | FAQ from CMS. Complaint entry point. Crisis banner always shown |
| P52 | Accessibility | /settings/display | Font scale, light/dark/system theme, language, reduced motion, high contrast |
| P53 | End of care | /care/complete | Farewell, relapse-prevention plan, download summary, one-tap return |
| P54 | Therapist late | lobby state | Late time not deducted. After 15 min, offer free reschedule or another available therapist |
| P55 | Onboarding tour | /welcome/tour | 4 cards, skippable |
| P56 | Waitlist | /waitlist | Expected wait, widen preferences, weekly short check-in. Risk 3–4 skips the queue |
| P57 | Dark mode | theme | Contrast ≥ 4.5:1. Crisis red re-tuned for dark backgrounds |

### 10.3 New staff screens (Angular)

| Code | Screen | Role | Route |
| --- | --- | --- | --- |
| S33–S42 | Messages, notifications, safety-plan review, risk assessment, consults, transfers, incident reviews, reassignment, supervisor views | therapist / supervisor | from round 1 |
| S43 | Platform settings | admin | /admin/settings |
| S44 | Content manager | admin (+ lead approval) | /admin/content |
| S45 | Tests and scales list | admin | /admin/instruments |
| S46 | Test builder (5 steps: details, items and answers, scoring, safety rules, review) | admin (+ lead approval) | /admin/instruments/new |
| S47 | Catalogs (modalities, specialties, languages, closure reasons, homework categories, cancel reasons, complaint types, cities) | admin | /admin/catalogs |
| S48 | Roles and permissions matrix | admin | /admin/roles |
| S49 | Notification rules (event × recipient × channel × quiet hours × escalation) | admin | /admin/notification-rules |
| S50 | Capacity and waitlist | admin / supervisor | /admin/capacity |
| S51 | Data requests (export 7 days, delete 30 days) | admin / privacy officer | /admin/data-requests |
| S52 | Complaints | admin / lead | /admin/complaints |
| S53 | Security incidents (72-hour regulator notice) | admin / security officer | /admin/security-incidents |
| S54 | Surveys | admin | /admin/surveys |
| S55 | Leave mode with named coverage | therapist | /me/leave |
| S56 | Session prep (one screen) | therapist | /patients/\[id\]/prep |
| S57 | End-of-care summary | therapist (+ supervisor sign-off) | /patients/\[id\]/close |

### 10.4 Screen-level rules that matter for the build

- **Test builder (S46):** an instrument can't be activated without a license reference and a validated-translation source. An item can be marked safety-linked (e.g. PHQ-9 item 9, PCL-5 item 16). Any answer above its threshold creates a `RiskSignal` directly, outside the AI path. Locked instruments (C-SSRS) can only be changed by the clinical lead, and only after the evaluation set passes.
- **Notification text (S49):** SMS and push bodies carry **no clinical information** (no session, no crisis, no therapist name), because someone else may see the phone. Level 3–4 alerts ignore quiet hours and can't be switched off.
- **Roles (S48):** permissions attach to roles, never to people. Locked rules: an admin can't read clinical content; whoever writes a rule can't approve it; only a supervisor or lead can close a Level 4. Break-glass access lasts 60 min, needs a reason, notifies the lead and privacy officer, and is reviewed within 24 h.
- **Leave mode (S55):** a patient at risk 3 or with an active safety plan must have a **named** cover therapist before leave is approved. Crisis alerts route to on-call for the whole leave.
- **End of care (S57):** closing is blocked if risk was 3–4 in the last 14 days, or if the patient asked to continue. After supervisor sign-off the patient sees P53 and keeps the library, journal, safety plan and crisis button.
- **Data deletion (S51):** steps run in order: identity check → legal hold check → clinical review → crypto-shred the patient key → confirm to patient. What stays: content-free audit rows, and crisis records for the retention period set in S43.
- **Waitlist (S50/P56):** order is risk first, then wait time. Rescreen every 7 days. Supervisor alert after 10 days. Registration auto-pauses when an on-call gap is detected (setting in S43).

### 10.5 New tables (SQL Server 2022)

All tables carry `CreatedAt`, `CreatedBy`, `RowVersion`. Patient-owned rows also carry `PatientId` and sit under the existing RLS predicate (`SESSION_CONTEXT('patient_id')` / care-team membership). Clinical free text is encrypted with the per-patient key, so crypto-shredding works.

**Configuration (admin-managed)**

| Table | Key columns | Notes |
| --- | --- | --- |
| PlatformSettings | Key, ValueJson, Tier (Normal/Safety), Version, ActiveFrom | One row per key per version. Safety tier goes through PendingChanges |
| EmergencyContacts | Label\_ar, Label\_en, Number, ShowIn, SortOrder, IsActive | Read by the crisis screen and safety plan; snapshot shipped as fallback |
| SlaPolicies | RiskLevel, AckMinutes, ContactMinutes, ReviewMinutes, Version | Read by Crisis.Worker |
| PendingChanges | EntityType, EntityId, ProposedJson, Reason, ProposedBy, Status, DecidedBy, DecidedAt | Check: DecidedBy ≠ ProposedBy |
| ContentItems / ContentVersions | Type (CrisisScript, Consent, Homework, SelfHelp, Faq, SystemMessage, Translation), Key; Version, Body\_ar, Body\_en, Variables, Status | Only one Published version per item; `{variables}` resolve from PlatformSettings |
| Instruments / InstrumentVersions | Code, Name, Measures, IsLocked; Version, LicenseRef, TranslationSource, Status, UsedIn | Results FK to InstrumentVersionId |
| InstrumentItems | InstrumentVersionId, Ordinal, Text\_ar, Text\_en, AnswerSetId |  |
| AnswerSets / AnswerOptions | Label\_ar, Label\_en, Points | Shared across items |
| ScoreBands | InstrumentVersionId, Min, Max, Label, RaiseTriageTo |  |
| SafetyItemRules | InstrumentItemId, Threshold, RiskLevel, Action | Creates RiskSignal directly |
| Catalogs / CatalogItems | CatalogCode; Name\_ar, Name\_en, Description, SortOrder, IsActive, Flags | Disable only when referenced |
| Roles / Permissions / RolePermissions | Code; IsLocked | Locked pairs seeded and refused by API |
| NotificationRules | EventCode, RecipientRole, Channels, RespectQuietHours, RepeatEveryMin, EscalateAfterMin, TemplateKey | Level 3–4 rows: RespectQuietHours forced false |
| Surveys / SurveyQuestions / SurveyResponses | Trigger, Anonymous, SkipAfterCrisis; Type; Answers | Free text goes through the safety classifier |

**Care and operations**

| Table | Notes |
| --- | --- |
| SafetyPlans (+ versions) | Patient and therapist co-edit; every change versioned |
| SessionNotes | Encrypted; signed-at; addenda only after signing |
| Messages / MessageThreads | Patient↔therapist async; classifier on inbound |
| RiskAssessments | Structured form linked to RiskSignal / Alert |
| Consults, Transfers, ChangeTherapistRequests | State machines in 10.7 |
| IncidentReviews | Post-crisis review, owner, actions, due dates |
| Notifications | Per-user inbox; Channel, SentAt, ReadAt |
| Waitlist | PatientId, Preferences, RiskLevel, QueuedAt, LastRescreenAt, Status |
| PatientPreferences | TherapistGender, Languages, TimeSlots, Style (CatalogItem ids), QuietHours, Theme, FontScale |
| JournalEntries | Encrypted body, Mood, SharedWithTherapist, SharedAt |
| TherapistLeave / LeaveCoverage | From, To, Type, Status; PatientId, CoverTherapistId, ShareSummary |
| CareClosures | Reason (catalog), Summary\_patient, RelapsePlan, FollowUps, SignedOffBy |
| DataRequests | Type (Export/Delete/Correct), DueAt (7 or 30 days from settings), Steps, Status |
| Complaints | Type (catalog), Severity, Status, OwnerRole, AckDueAt, ResolveDueAt, HiddenFromSubject |
| SecurityIncidents / IncidentSteps | DetectedAt, Severity, AffectedCount, RegulatorDueAt (= DetectedAt + 72 h), step checklist |

### 10.6 New API endpoints (.NET 8)

All `/admin/*` endpoints need the matching permission from RolePermissions plus a `reason` field. Safety-tier writes return `202 Accepted` with a `pendingChangeId` instead of applying.

| Area | Endpoints |
| --- | --- |
| Settings | `GET/PUT /admin/settings/{key}` · `GET /admin/settings/history` · `POST /admin/pending-changes/{id}:approve` / `:reject` (lead only) |
| Content | `GET/POST /admin/content` · `POST /admin/content/{id}/versions` · `POST /admin/content/versions/{v}:submit` · `GET /admin/content/versions/{v}/diff` · `GET /content/{key}?lang=` (public, published only) |
| Instruments | `GET/POST /admin/instruments` · `POST /admin/instruments/{id}/versions` · `PUT .../items`, `.../bands`, `.../safety-rules` · `POST .../versions/{v}:submit` · `POST .../versions/{v}:eval` (runs the evaluation set) · `GET .../preview` |
| Catalogs | `GET /catalogs/{code}` (public) · `POST/PUT /admin/catalogs/{code}/items` · `POST .../items/{id}:disable` |
| Roles | `GET /admin/roles` · `PUT /admin/roles/{code}/permissions` · `POST /admin/break-glass` |
| Notifications | `GET/PUT /admin/notification-rules` · `GET /me/notifications` · `POST /me/notifications/{id}:read` |
| Capacity | `GET /admin/capacity` · `PUT /admin/therapists/{id}/capacity` · `GET /admin/waitlist` · `POST /admin/waitlist/{id}:match` · `POST /admin/registration:pause` |
| Privacy and trust | `GET/POST /admin/data-requests` · `POST .../{id}/steps/{step}:complete` · `GET/POST /admin/complaints` · `POST .../{id}:respond` · `GET/POST /admin/security-incidents` · `POST .../{id}/steps/{step}:complete` |
| Surveys | `GET/POST /admin/surveys` · `GET /admin/surveys/{id}/results` · `POST /surveys/{id}/responses` |
| Therapist | `POST /me/leave` · `PUT /me/leave/{id}/coverage` · `GET /patients/{id}/prep` · `POST /patients/{id}/closure` · `POST /closures/{id}:sign-off` |
| Patient | `GET/PUT /me/preferences` · `GET /me/progress` (bands and trend only, no raw scores) · `GET /library` · `GET/POST /me/journal` · `PUT /me/journal/{id}/share` · `GET /me/waitlist` · `GET /faq` |

### 10.7 State machines

- **Transfer:** Requested → SupervisorReview → Approved → HandoverNoteWritten → PatientNotified → Active (new therapist) | Rejected. The old therapist keeps read-only access for 14 days. A risk alert during a transfer goes to both therapists and on-call.
- **Consult:** Draft → Sent → Accepted → Answered → Closed | Declined. Shares the minimum: a de-identified summary unless the consultant is added to the care team.
- **Change therapist (patient-initiated):** Requested → Matching → Offered → Accepted → Transfer flow | Waitlisted. No reason required. The current therapist is told only after the new match is accepted.
- **Pending change (safety tier):** Proposed → InReview → Approved → Live | Rejected | Superseded. On Approved, an outbox `ConfigChanged` event fires and caches are busted.
- **Data request:** Received → IdentityVerified → LegalHoldChecked → ClinicalReview → Executed → Confirmed | Refused (with reason). A Hangfire job alerts the privacy officer 3 days before DueAt.
- **Security incident:** Detected → Contained → Scoped → RegulatorNotified → PatientsNotified (if high risk) → PostIncidentReview → Closed. It can't close before the review is done.

### 10.8 Build-order changes

1. Move the **config backbone** (PlatformSettings, PendingChanges, Catalogs, ContentItems, AuditLog with reasons) into Sprint 1. Every later module reads from it.
2. Build the **instrument engine** (InstrumentVersions, items, bands, SafetyItemRules) before intake, and seed PHQ-9, GAD-7 and C-SSRS through the same admin API used in production. No seed SQL with hard-coded items.
3. Add a CI test that fails if any Arabic or English user-facing string, emergency number or SLA value appears as a literal in the code.
4. Add an evaluation set per safety-linked instrument and per crisis script, run on every `:submit`.

### 10.9 Open decisions added

| # | Decision | Owner | Default if not decided |
| --- | --- | --- | --- |
| D-v2-1 | In-app video for sessions (P42) | Product + clinical lead | Out of pilot. Sessions use the existing external tool; P42 stays a later-phase design |
| D-v2-2 | Who holds the privacy-officer and security-officer roles in the pilot | Operations | Clinical lead (privacy) and tech lead (security) |
| D-v2-3 | Instrument licenses for PCL-5, ISI, WSAS in Arabic | Clinical lead | Not activated until a license and validated translation are on file |
| D-v2-4 | Does sharing a journal entry create a clinical record? | Clinical + legal | Yes: once shared, it's retained like a message |
| D-v2-5 | Show patients their raw test scores on request? | Clinical lead | No in the app. Therapist discusses in session |

## 11. Addendum v3: third review gaps

This section comes from the third persona review. It adds 31 screens (P58–P68, S58–S77). Pricing is **not** built now. It ships as an optional **Subscriptions add-on**, off by default (see 11.3).

### 11.1 New screens and routes

| Code | Screen | Route | Key rule |
| --- | --- | --- | --- |
| P58 | App lock, quick exit, discreet icon | /settings/privacy-lock | Biometric or PIN lock after 2 min idle. The quick-exit button on every screen replaces history and opens a neutral page. Notification bodies are hidden |
| P59 | Re-consent on policy change | /consent/update | Blocking screen when ConsentVersion changes: plain-language diff, accept, or see options (export/delete). The crisis button stays available |
| P60 | Booking with prayer times and Ramadan mode | /book | Slots exclude prayer windows (by city, with a margin). Ramadan windows come from S69 |
| P61 | Attendance certificate | /sessions/\[id\]/certificate | No diagnosis. Patient may hide the platform name. Verification code |
| P62 | Voice message | /messages/\[id\] | Voice note, auto-transcript, classifier runs on the transcript |
| P63 | Missed session | /sessions/\[id\]/missed | No penalty. Therapist note. Mood check. Rebook or talk now |
| P64 | My medications and psychiatry referral consent | /care/medications | Record only, never prescribe. Referral sends only what the patient ticks |
| P65 | iOS install prompt for notifications | /welcome/install | Shown on iOS Safari when not installed. SMS fallback for critical reminders |
| P66 | Weak connection | global state | Outbox stored on device, delivery ticks. The crisis call works offline (tel: link) |
| P67 | Therapist cancelled | /sessions/\[id\]/cancelled | Same-slot substitute offered first, or priority rebook. Session not counted |
| P68 | Subscription | /subscribe | **Only when the Subscriptions add-on is on** |
| S58 | Safeguarding report (harm to others, child or vulnerable adult abuse) | /patients/\[id\]/safeguarding/new | Auto-opened by the classifier. Clinical lead decides whether to make an external report. Locked once sent |
| S59 | Deterioration alerts | /alerts/deterioration | Rising score, no improvement after N sessions, disengagement. Thresholds set by the lead |
| S60 | Session note with template and AI draft | /patients/\[id\]/notes/new | SOAP/DAP/BIRP from S75. The AI draft is never saved unsigned. Disabled if the patient opted out |
| S61 | Referral letter | /patients/\[id\]/referrals/new | Needs patient consent (P64). Secure link expires in 14 days. Follow-up reminder |
| S62 | Flag AI response | /messages/\[id\]/flag | "Missed risk" is treated as a safety incident and added to the eval set |
| S63 | Therapist wellbeing | /me/wellbeing | Load caps. Debrief offered after a Level 4. Shared with the supervisor only in aggregate |
| S64 | Supervision and CPD | /me/cpd | CPD hours, supervision cases, license expiry warning at 60/30/7 days |
| S65 | Emergency absence | /me/absence | Same-day sessions get suggested substitutes. Patients choose substitute or rebook |
| S66 | No-show follow-up (supervisor) | /supervisor/no-shows | Risk 3–4: call within 60 min; 3 failed attempts escalate to supervisor and emergency contact |
| S67 | AI settings | /admin/ai | Versioned prompts, pinned model and fallback, locked guardrails, cost cap. Risk path works past the cap |
| S68 | AI evaluation | /admin/ai/eval | Publishing is blocked unless every safety set passes. Sets: Gulf dialect, Arabizi, code-mixed, harm to others, prompt injection, no-diagnosis, tone |
| S69 | Calendar | /admin/calendar | Working hours, public holidays, Ramadan windows, prayer-time blocking, Hijri dates |
| S70 | Integrations | /admin/integrations | SMS primary and backup, voice-call fallback, email, identity (optional), push, AI provider. Daily connection test |
| S71 | Add-ons | /admin/modules | Module toggles (11.3). Safety modules aren't listed and can't be turned off |
| S72 | Announcements and maintenance | /admin/announcements | The maintenance mode keeps the crisis button, emergency numbers, cached safety plan and staff alerts running |
| S73 | Bulk import therapists | /admin/users/import | Validates rows. Sends invitations only. Accounts still go through license verification |
| S74 | Reports | /admin/reports | Aggregate only. Cells under 10 patients suppressed |
| S75 | Templates | /admin/templates | Note templates, letters, certificate, end-of-care summary. Versioned with approval |
| S76 | Special cases | /admin/special-cases | Under-18 suspected, moved out of region, duplicate account, legal request, patient death. Configurable steps per type |
| S77 | Backup and DR | /admin/dr | RTO ≤ 60 min, RPO ≤ 5 min, monthly restore drill, static crisis page on a separate host |

### 11.2 New tables

| Table | Notes |
| --- | --- |
| SafeguardingReports, SafeguardingSteps | Type (HarmToOthers, ChildAbuse, VulnerableAdult), Level, VerbatimQuote (encrypted), Assessment, LeadDecision, ExternalReportRef. Append-only after submit |
| ReportingAuthorities | Admin-managed list of agencies and numbers, per region |
| DeteriorationRules, DeteriorationAlerts | InstrumentId, RiseThreshold, WindowDays, NoImprovementSessions, DisengagementDays |
| NoteTemplates (+ versions), DocumentTemplates (+ versions) | Fields JSON, Body\_ar, Body\_en, variables. SessionNotes gets TemplateVersionId and AiDraftUsed |
| Referrals | Destination (catalog), Priority, Attachments, PatientConsentAt, LinkExpiresAt, Outcome |
| PatientMedications | Name, Dose, Since, Prescriber. Patient-entered, never prescribed |
| AiResponseFlags | MessageId, Type, BetterResponse, AddedToEvalSet, IsSafetyIncident |
| AiPromptVersions, AiModelConfig, AiBudget | Status Draft/Approved/Live. Model pinned by version string. MonthlyCap; risk path exempt |
| EvalSets, EvalCases, EvalRuns, EvalResults | Set threshold. A run is tied to a prompt and model version. A failing run blocks publish |
| TherapistLoad, DebriefRequests | Computed weekly load. Debrief created after a Level 4 |
| CpdRecords, SupervisionSessions | Hours, evidence file. LicenseExpiry already on Therapists; add reminder jobs |
| Absences | Type Emergency/Planned, auto-substitute suggestions, patient choice |
| NoShowRules, NoShowFollowUps | Grace minutes, call-within by risk level, attempts, escalation |
| CalendarHolidays, WorkingHours, RamadanSettings, PrayerTimeCache | Prayer times computed per city daily by a Hangfire job |
| Integrations, IntegrationHealth | Provider, Role (Primary/Backup), SecretRef (Key Vault only), LastTestAt |
| Modules | Code, IsEnabled, IsSafetyCritical (cannot be disabled), ChangedBy, Reason |
| Announcements, MaintenanceWindows | Audience, From/To, message keys |
| ImportBatches, ImportRows | Validation results, invite status |
| ReportDefinitions, ReportRuns | Small-cell suppression threshold from settings |
| SpecialCases, SpecialCaseTypes, SpecialCaseSteps | Steps per type are admin-configured |
| AttendanceCertificates | SessionId, VerifyCode, HidePlatformName |
| ConsentVersions + PatientConsents.VersionId | A version change forces P59 at next login |
| PatientDeviceSettings | AppLock, QuickExit, DiscreetIcon, HideNotificationContent |

### 11.3 Add-on (module) system and Subscriptions

- `Modules` table plus an `IModuleGate` service. The API checks `[RequiresModule("subscriptions")]` on endpoints. The Next.js and Angular apps read `/modules` at load and hide routes, menu items and buttons for disabled modules. A disabled module leaves **no trace in the UI**.
- **Subscriptions add-on (off by default):**
  - **Off:** no prices, no `/subscribe`, no invoices, and booking has no payment step.
  - **On:** packages, payment provider and insurance check settings appear in admin. Booking requires an active subscription or a single-session payment. Existing patients get a grace period set by the admin.
  - **Always free, whether on or off:** the crisis button, safety plan, crisis contact and emergency numbers.
  - Tables reserved now, not built: `Plans`, `Subscriptions`, `Payments`, `Invoices`, `Refunds`. They're built only when the add-on is scheduled.
- Other toggleable modules: AI chat, journal, self-help library, voice messages, attendance certificate, surveys, in-app video (later phase).
- Safety-critical modules (crisis, safety plan, risk screening, alerts) are seeded with `IsSafetyCritical = 1`. The API refuses to disable them.

### 11.4 Crisis path hardening

- **Harm to others** is a first-class risk type alongside self-harm. It has its own classifier labels, eval set and SLA, and routes to S58.
- **Alert delivery chain:** push + SMS primary → SMS backup after 30 s → automated voice call → supervisor escalation. Each hop is recorded in `AlertDeliveries`.
- **No-show at risk 3–4** creates an outreach task (S66) with the same escalation as an unacknowledged alert.
- **Offline:** the crisis screen and emergency numbers are cached by the service worker. The cached safety plan is readable offline.
- **Static crisis page** on a separate host and DNS, used when the main app is down.

### 11.5 Build-order additions

1. Sprint 1: `Modules` and `IModuleGate`, with consent versioning (P59).
2. Before intake goes live: harm-to-others labels, plus dialect, Arabizi and prompt-injection eval sets (S68), all passing.
3. Before the pilot: SMS backup and voice fallback (S70), no-show outreach (S66), quick exit and app lock (P58), first DR drill (S77).
4. During the pilot: deterioration alerts, note templates and AI drafts, referrals, wellbeing, CPD, calendar and Ramadan, voice messages, certificates, reports, import.
5. Later: Subscriptions add-on, in-app video.

### 11.6 Open decisions added

| # | Decision | Default |
| --- | --- | --- |
| D-v3-1 | Which external body receives safeguarding reports in the pilot region | Clinical lead names it before launch. It's set in ReportingAuthorities |
| D-v3-2 | Can patients opt out of AI-drafted notes? | Yes, from privacy settings |
| D-v3-3 | Voice notes retention | Same as text messages; the transcript is kept, audio deleted after 90 days |
| D-v3-4 | National identity verification (e.g. Nafath or equivalent) | Optional, off for the pilot |
| D-v3-5 | Native mobile app vs PWA | PWA for the pilot with the iOS install prompt; revisit after pilot |
