# Tamaneen API v1

Types live in `web-patient/src/lib/api/types.ts` (patient) and `web-staff/src/app/core/api/types.ts` (staff).
Every response is localized by `Accept-Language` (`ar` default, `en`). Errors are ProblemDetails with a stable `code`.
"Gate" means the text or answers go through the crisis worker first. If the gate blocks, the response has
`crisis: true` (or HTTP 409 with code `error.crisis.locked` when the chat is already locked) and the client opens P06.

## Public

| Method | Path | Body → Response | Screen |
|---|---|---|---|
| GET | /v1/meta/config | → PublicConfig | P01, P14, P15, P35 |
| GET | /v1/meta/locale | → LocaleResponse | all |

## Auth (anonymous; the patient BFF stores tokens in HttpOnly cookies)

| Method | Path | Body → Response | Screen |
|---|---|---|---|
| POST | /v1/auth/eligibility | EligibilityRequest → EligibilityResponse | P15 |
| GET | /v1/auth/nickname | → NicknameSuggestion | P02 |
| POST | /v1/auth/signup | SignupRequest → 201 ChallengeResponse | P02 |
| POST | /v1/auth/challenges/{id}/resend | → ChallengeResponse (429 + retryAfterSeconds during the cooldown) | P16 |
| POST | /v1/auth/signup/verify | VerifyRequest → SignupVerifiedResponse | P16 → P17 |
| POST | /v1/auth/login | LoginRequest → LoginResponse (401 + remainingAttempts, 423 + retryAfterSeconds) | P18 |
| POST | /v1/auth/login/mfa | MfaRequest → MfaResponse | P19 |
| POST | /v1/auth/login/mfa/sms | { mfaToken } → 204 | P19 |
| POST | /v1/auth/recover | RecoverStartRequest → 202 ChallengeResponse (the same response whether or not the account exists) | P20 |
| POST | /v1/auth/recover/complete | RecoverCompleteRequest → 204 | P20 |
| POST | /v1/auth/recover/code | RecoverWithCodeRequest → 204 | P20 |
| POST | /v1/auth/refresh | { refreshToken } → TokenPair | BFF |
| POST | /v1/auth/logout | { refreshToken } → 204 | P31 |

Passwords: at least 12 characters and not on the breached-password list. Codes: 6 digits, 10 minutes, 5 attempts, resend after 60 s.
Login: 5 failures lock the account for 15 minutes.

## Patient (role patient)

| Method | Path | Body → Response | Screen |
|---|---|---|---|
| GET | /v1/me | → Me | all (routing) |
| PUT | /v1/me/profile | UpdateProfileRequest → Me | P31 |
| PUT | /v1/me/language | { language } | toggle, P31 |
| GET/PUT | /v1/me/consents | → Consent[] / UpdateConsentsRequest → Consent[] | P03, P11 |
| GET/PUT | /v1/me/safety-info | SafetyInfo / UpdateSafetyInfoRequest | P21, P34 |
| DELETE | /v1/me/safety-info/trusted-contact | → 204 | P34 |
| PUT | /v1/me/path | { path } → Me | P22 |
| POST | /v1/me/contact-request | → ContactRequestCreated | P04, P22 |
| GET | /v1/me/security | → SecurityOverview | P32 |
| PUT | /v1/me/password | ChangePasswordRequest → 204 | P32 |
| POST | /v1/me/totp/setup · /v1/me/totp/confirm · DELETE /v1/me/totp | → TotpSetup · CodeRequest → 204 · CodeRequest → 204 | P17, P32 |
| POST | /v1/me/recovery-codes | → RecoveryCodes | P32 |
| DELETE | /v1/me/sessions/{id} · POST /v1/me/sessions/revoke-all | → 204 | P32 |
| GET/PUT | /v1/me/notifications | NotificationPrefs | P33 |
| GET | /v1/me/access-log | → AccessLogEntry[] | P11 |
| GET/POST | /v1/me/data-export | → DataRequestStatus | P11 |
| POST | /v1/me/delete-account | DeleteAccountRequest → 202 | P11 |
| GET | /v1/screening/cssrs | → ScreeningForm | P23 |
| POST | /v1/screening/cssrs | ScreeningSubmit → GateOutcome (gate) | P23 |
| POST | /v1/intake | → IntakeState (starts or resumes) | P04 |
| GET | /v1/intake/current | → IntakeState (404 if none) | P04, P22 |
| POST | /v1/intake/{id}/messages | SendMessageRequest → IntakeTurnResponse (gate, then AI) | P04 |
| POST | /v1/intake/{id}/answers | AnswerRequest → IntakeTurnResponse (gate) | P04 |
| POST | /v1/intake/{id}/pause | → IntakeState | P04 |
| POST | /v1/intake/{id}/complete | → IntakeSummary (AI writes the sections; fallback: empty sections) | P04 → P05 |
| GET/PUT | /v1/intake/{id}/summary | IntakeSummary / UpdateSummaryRequest → IntakeSummary (gate) | P05 |
| POST | /v1/intake/{id}/submit | → TriageState | P05 → P36 |
| GET | /v1/triage | → TriageState | P36 |
| POST | /v1/triage/addendum | AddToSummaryRequest → TriageState (gate) | P36 |
| GET | /v1/crisis/status | → CrisisStatus | P06 |
| POST | /v1/crisis/help | → HelpResponse (gate source help_now: always opens a level 4 event) | help button |
| GET | /v1/matches | → MatchList | P07 |
| POST | /v1/matches/{therapistId}/dismiss | → MatchList | P07 |
| GET | /v1/therapists?q&specialty&language&gender&evening | → TherapistSummary[] | P24 |
| GET | /v1/therapists/{id} | → TherapistProfile | P25 |
| GET | /v1/therapists/{id}/slots?days=14 | → Slot[] | P08 |
| POST | /v1/bookings/holds | { slotId } → Hold (5 minutes) | P08 |
| POST | /v1/bookings/holds/{holdId}/confirm | → AppointmentDetail | P08 → P26 |
| DELETE | /v1/bookings/holds/{holdId} | → 204 | P08 |
| GET | /v1/appointments | → AppointmentList | P27 |
| GET | /v1/appointments/{id} | → AppointmentDetail | P26, P28 |
| GET | /v1/appointments/{id}/calendar | → text/calendar | P26 |
| POST | /v1/appointments/{id}/cancel | → AppointmentDetail (409 within 24 h) | P28 |
| POST | /v1/appointments/{id}/reschedule | RescheduleRequest → AppointmentDetail | P28 |
| GET | /v1/home | → HomeSummary | P09 |
| POST | /v1/mood | MoodEntryRequest → MoodSaveResponse (note gated) | P09, P30 |
| GET | /v1/mood?days=7 | → MoodEntry[] | P30 |
| GET | /v1/homework | → HomeworkList | P29 |
| GET | /v1/homework/{id} | → HomeworkDetail | P10 |
| POST | /v1/homework/{id}/checkins | CheckinRequest → CheckinResponse (note gated) | P10 |
| GET/PUT | /v1/safety-plan | SafetyPlan / UpdateSafetyPlanRequest (gate) | P37 |

Booking needs either a passed safety check (direct path) or a reviewed triage (intake path).

## Staff (therapist, supervisor, clinical lead; the TOTP step is mandatory)

| Method | Path | Body → Response | Screen |
|---|---|---|---|
| GET | /v1/staff/me | → StaffMe | shell |
| GET | /v1/staff/dashboard | → TherapistDashboard | S01 |
| GET | /v1/staff/patients/{pseudonymId}/intake | → StaffIntakeView (audited) | S01 |
| POST | /v1/staff/patients/{pseudonymId}/intake/approve | → 204 | S01 |
| POST | /v1/staff/appointments/{id}/outcome | { outcome } → 204 | S01 |
| (worker) GET | /v1/crisis/events/open | → OpenCrisisEvent[] | S01, S04 |
| (worker) GET | /v1/crisis/events/{id} | → CrisisEventDetail | S04 |
| (worker) POST | /v1/crisis/events/{id}/acknowledge | → OpenCrisisEvent | S01, S04 |
| (worker) POST | /v1/crisis/events/{id}/excerpt | { totpCode } → { excerpt } (audited) | S04 |
| (worker) POST | /v1/crisis/events/{id}/reveal-contact | { totpCode, reason } → RevealedContact (audited, shown for 10 minutes) | S04 |
| (worker) POST | /v1/crisis/events/{id}/actions | { kind: contacted, local_emergency, note, handover; text } → CrisisEventDetail | S04 |
| (worker) POST | /v1/crisis/events/{id}/close | { outcome, reason } → 204 | S04 |
| (worker) GET | /v1/crisis/health | → CrisisHealth | S04 |
| (worker) hub | /hubs/crisis?lang= | crisisAlert, crisisUpdated | S01, S04 |
