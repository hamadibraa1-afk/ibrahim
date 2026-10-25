# Tamaneen API v1

Types live in `web-patient/src/lib/api/types.ts` (patient) and `web-staff/src/app/core/api/types.ts` (staff).
Every response is localized by `Accept-Language` (`ar` default, `en`). Errors are ProblemDetails with a stable `code`.
"Gate" means the text or answers go through the crisis worker first. If the gate blocks, the response has
`crisis: true` (or HTTP 409 with code `error.crisis.locked` when the chat is already locked) and the client opens P06.

## Public

| Method | Path | Body → Response | Screen |
|---|---|---|---|
| GET | /v1/meta/config | → PublicConfig (includes `signupsOpen`) | P01, P02, P12, P14, P15, P35 |
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
| POST | /v1/auth/recover | RecoverStartRequest → 202 ChallengeResponse (the same response whether or not the account exists; patients and staff, a staff code goes to the work email and two-step verification is not reset) | P20, S06 |
| POST | /v1/auth/recover/complete | RecoverCompleteRequest → 204 | P20, S06 |
| POST | /v1/auth/recover/code | RecoverWithCodeRequest → 204 | P20 |
| POST | /v1/auth/refresh | { refreshToken } → TokenPair | BFF |
| POST | /v1/auth/logout | { refreshToken } → 204 | P31 |

Passwords: at least 12 characters and not on the breached-password list. Codes: 6 digits, 10 minutes, 5 attempts, resend after 60 s.
Login: 5 failures lock the account for 15 minutes.
Sign-up pause (EC-26): while `PublicConfig.signupsOpen` is false, eligibility and sign-up answer 503 `error.auth.signups_paused`
and P01, P02, P12 and P15 show the emergency numbers instead of the start button. Sign-in is not affected.

## Patient (role patient)

| Method | Path | Body → Response | Screen |
|---|---|---|---|
| GET | /v1/me | → Me + `ETag` (profile version) | all (routing) |
| PUT | /v1/me/profile | `If-Match` + UpdateProfileRequest → Me + `ETag` (428 `error.settings.version_required` without If-Match, 412 `error.settings.version_changed` when another device saved first) | P31 |
| PUT | /v1/me/language | { language } | toggle, P31 |
| GET/PUT | /v1/me/consents | → Consent[] / UpdateConsentsRequest → Consent[] | P03, P11 |
| GET/PUT | /v1/me/safety-info | SafetyInfo + `ETag` / `If-Match` + UpdateSafetyInfoRequest → SafetyInfo + `ETag` (trustedName ≤ 100 and trustedPhone go together: one without the other is a 400 field error; 428 without If-Match, 412 on a stale version) | P21, P34 |
| DELETE | /v1/me/safety-info/trusted-contact | → 204 + `ETag` (the new safety-info version) | P34 |
| PUT | /v1/me/path | { path } → Me | P22 |
| POST | /v1/me/contact-request?source= | → ContactRequestCreated (`expectedBy`, `withinHours` from the triage SLA) | P04, P22 |
| GET | /v1/me/security | → SecurityOverview | P32 |
| PUT | /v1/me/password | ChangePasswordRequest → 204 | P32 |
| POST | /v1/me/totp/setup · /v1/me/totp/confirm · DELETE /v1/me/totp | → TotpSetup · CodeRequest → 204 · ReauthRequest { password, code } → 204 | P17, P32 |
| POST | /v1/me/recovery-codes | ReauthRequest { password, code? } → RecoveryCodes (code required while the authenticator is on) | P32 |
| DELETE | /v1/me/sessions/{id} · POST /v1/me/sessions/revoke-all | → 204 | P32 |
| GET/PUT | /v1/me/notifications | NotificationPrefs | P33 |
| GET | /v1/me/inbox?cursor=&limit= | → Inbox { items: InboxItem[], unreadCount, nextCursor, counts } (newest first; `cursor` is the `nextCursor` of the previous page) | P46 |
| GET | /v1/me/inbox/unread-count | → { unreadCount } | header bell |
| POST | /v1/me/inbox/{id}/read · /v1/me/inbox/read-all | → 204 (404 for an entry that is not yours) | P46 |
| GET | /v1/crisis/follow-up | → CrisisFollowUp (`state`: none, open, closed; audited `crisis.follow_up_viewed`) | P38 |
| POST | /v1/crisis/follow-up/seen | → 204 (the follow-up notices are read; home stops opening P38) | P38 |
| GET | /v1/me/access-log | → AccessLogEntry[] | P11 |
| GET/POST | /v1/me/data-export | → DataRequestStatus / DataExportRequest { password } → DataRequestStatus (400 error.auth.password_mismatch) | P11 |
| POST | /v1/me/delete-account | DeleteAccountRequest → 202 (409 error.privacy.deletion_crisis_open while a crisis event is open, EC-55) | P11 |
| GET | /v1/screening/cssrs | → ScreeningForm | P23 |
| POST | /v1/screening/cssrs | ScreeningSubmit { answers: the answers given so far, 1–6 } → GateOutcome (gate). P23 sends it after every answer, so a «yes» escalates at once; only the complete set of 6 is stored | P23 |
| POST | /v1/intake | → IntakeState (starts or resumes) | P04 |
| GET | /v1/intake/current | → IntakeState (404 if none) | P04, P22 |
| POST | /v1/intake/{id}/messages | SendMessageRequest → IntakeTurnResponse (gate, then AI; at most 2,000 characters; a repeated `clientMessageId` is answered with the current state and stored once) | P04 |
| POST | /v1/intake/{id}/answers | AnswerRequest → IntakeTurnResponse (gate) | P04 |
| POST | /v1/intake/{id}/pause | → IntakeState | P04 |
| POST | /v1/intake/{id}/form-mode | → IntakeState (`formMode: true`: questionnaires at once, finish without more turns, no AI summary) | P04 |
| POST | /v1/intake/{id}/complete | → IntakeSummary (AI writes the sections; fallback: empty sections) | P04 → P05 |
| GET/PUT | /v1/intake/{id}/summary | IntakeSummary / UpdateSummaryRequest → IntakeSummary (gate; each section at most 2,000 characters) | P05 |
| POST | /v1/intake/{id}/submit | → TriageState | P05 → P36 |
| GET | /v1/triage | → TriageState | P36 |
| POST | /v1/triage/addendum | AddToSummaryRequest → TriageState (gate) | P36 |
| GET | /v1/crisis/status | → CrisisStatus | P06 |
| POST | /v1/crisis/help | HelpRequest { requestId? } → HelpResponse (gate source help_now: always opens a level 4 event; the same requestId is answered from the first call and never reaches the gate again) | help button, P06, P14 |
| GET | /v1/matches | → MatchList | P07 |
| POST | /v1/matches/{therapistId}/dismiss | DismissRequest? `{ reason?: specialty, gender, language, schedule, other }` → MatchList | P07 |
| GET | /v1/therapists?q&specialty&language&gender&evening | → TherapistSummary[] | P24 |
| GET | /v1/therapists/{id} | → TherapistProfile | P25 |
| GET | /v1/therapists/{id}/slots?days=14 | → Slot[] | P08 |
| POST | /v1/bookings/holds | { slotId } → Hold (5 minutes) | P08 |
| POST | /v1/bookings/holds/{holdId}/confirm | → AppointmentDetail | P08 → P26 |
| DELETE | /v1/bookings/holds/{holdId} | → 204 | P08 |
| GET | /v1/me/waitlist | → Waitlist (`status`: none, waiting, available; `offer` once a fitting time opened) | P56 |
| PUT | /v1/me/waitlist | JoinWaitlistRequest { therapistId?, gender: any/female/male, language: any/ar/en, timesOfDay: morning/afternoon/evening[], days: weekdays/weekend[] } → Waitlist (joins, or changes the preferences and keeps the place; booking eligibility required, 400 field errors for unknown values) | P56 (from P07, P24, P25) |
| DELETE | /v1/me/waitlist | → 204 (leaves) | P56 |
| GET | /v1/appointments | → AppointmentList | P27 |
| GET | /v1/appointments/{id} | → AppointmentDetail | P26, P28 |
| GET | /v1/appointments/{id}/calendar | → text/calendar `appointment.ics`, neutral title «موعد» / "Appointment" with no platform, therapist or patient name (PB-42) | P26 |
| POST | /v1/appointments/{id}/cancel | CancelRequest { reasonCode: schedule_conflict, feeling_better, unwell, therapist_fit, other } → AppointmentDetail (400 field error without a known reason; 409 within 24 h) | P28 |
| POST | /v1/appointments/{id}/reschedule | RescheduleRequest → AppointmentDetail | P28 |
| GET | /v1/home | → HomeSummary | P09 |
| POST | /v1/mood | MoodEntryRequest → MoodSaveResponse (note gated) | P09, P30 |
| GET | /v1/mood?days=7 | → MoodEntry[] | P30 |
| GET | /v1/homework | → HomeworkList | P29 |
| GET | /v1/homework/{id} | → HomeworkDetail | P10 |
| POST | /v1/homework/{id}/checkins | CheckinRequest → CheckinResponse (note gated) | P10 |
| GET/PUT | /v1/safety-plan | SafetyPlan / UpdateSafetyPlanRequest (gate; sections and contact names). `contacts`: up to 5 `{ name ≤ 100, phone }`, encrypted with the patient key like the sections | P37 |

Idempotency (P66 outbox, EC-01, EC-07): `POST /v1/mood`, `POST /v1/homework/{id}/checkins` and `PUT /v1/safety-plan` accept an
`Idempotency-Key` header (one GUID per write, the same on every retry; the BFF forwards it). The first request with a key runs
and its successful response is stored, encrypted with the patient's key; a repeat gets that first response back with
`Idempotent-Replayed: true` and runs nothing (no second entry, no second pass through the gate). A failed first request
(4xx, 5xx, gate unavailable) frees the key. While the first copy is still running a repeat gets 409 `error.idempotency.in_progress`
with `Retry-After`; the same key on another endpoint is 422 `error.idempotency.key_reused`; a malformed key is 400
`error.idempotency.invalid_key`. Keys are per patient (RLS). Intake turns use `clientMessageId` and Help now `requestId` instead.

Notifications (P46, S41; guide `GET /me/notifications`). The spec's `/v1/me/notifications` already holds the P33
preferences, so the inbox is `/v1/me/inbox` (patients) and `/v1/staff/inbox` (staff). An InboxItem is
`{ id, kind, category, text, link, createdAt, read, pinned, data }`: `text` is written in the request language from the
catalog (`Tamaneen.Api/Notifications`, resources `notify.<kind>`), `link` is an app-relative path in the reader's app,
`data` holds only ids, times and display names (never patient free text). Categories: risk, appointments, messages,
homework, care, security, system. Every entry is stored in `identity.Notifications` under its own row-level security
policy (`security.NotificationPolicy`): a person reads and marks only their own; the notification service writes.
A patient may also get an SMS or e-mail copy on their sign-up contact: always for crisis kinds; otherwise when the
P33 switch for that channel is on, or, for important kinds (appointment changes, reminders, waitlist, matches ready),
while app notifications are on (the web app has no push channel yet, P33 "بديل SMS"). Quiet hours (patient time zone)
hold the copy until they end, except crisis kinds. Copies never name the therapist or say session, crisis or
homework; "neutral notifications" send «لديك تذكير». Staff have no readable contact, so staff notices are in-app.
Kinds: appointment.booked, appointment.rescheduled, appointment.cancelled, appointment.cancelled_by_therapist,
appointment.running_late, appointment.missed, appointment.reminder_24h, appointment.reminder_1h,
session.therapist_joined, waitlist.slot_open, triage.matches_ready, homework.assigned, homework.feedback, care.ended,
safety_plan.updated, crisis.follow_up, security.new_sign_in (patient); staff.appointment.booked|rescheduled|cancelled,
care.change_requested, crisis.closed, safety_plan.updated, security.new_sign_in (staff). Other modules call
`INotifier.NotifyAsync(new Notification(recipientUserId, kind, data, link?))`; an unknown kind is stored with the
generic text. A background worker (once a minute) sends the 24 h and 1 h reminders, the crisis follow-ups and the held
copies; each reminder and follow-up has a per-recipient dedupe key, so restarts never repeat one. In Development the
copies appear at `/dev/inbox`.

P38 CrisisFollowUp: `{ state, eventId, closedAt, responder, talked, therapistInformed, therapistName,
nextAppointmentId, nextAppointmentAt, unread }`, built from structured data only (who handled the event, whether the
treating therapist was told, the next appointment), never the conversation, the level or the close reason.

Waitlist (P56): the queue is ordered by risk, then by time waited. Whenever a time becomes free (a patient cancels or moves a
session, a hold is released, a therapist records a cancellation, a patient joins), the scheduling service matches the
waiting entries against the bookable times of the next 14 days (times of day in the patient's time zone: morning 06–12,
afternoon 12–17, evening 17–23; weekend Friday and Saturday), one patient per time. The patient gets a neutral SMS or e-mail
(`WaitlistSlotOpen`) and `GET /v1/me/waitlist` shows `available` with the offer. `expectedDaysMin/Max` come from
`Platform:WaitlistExpectedDays*` until S50 computes them. The table is under row-level security on PatientId; the
scheduling service may read and mark entries.

Booking needs either a passed safety check (direct path) or a reviewed triage (intake path).
Gated writes that return an object (`PUT /v1/intake/{id}/summary`, `POST /v1/triage/addendum`, `PUT /v1/safety-plan`,
`POST /v1/mood`, `POST /v1/homework/{id}/checkins`, intake turns) also carry `crisis` and `script`. If the crisis worker
cannot be reached, the API answers 503 `error.crisis.unavailable` and does not process the content.
During scheduled maintenance (`Platform:Maintenance` true) every patient endpoint answers 503 `error.maintenance` and the app shows /maintenance (P35);
`/v1/meta`, `/v1/auth`, `/v1/crisis` and `/v1/staff` keep working.

## Staff (therapist, supervisor, clinical lead; the TOTP step is mandatory)

| Method | Path | Body → Response | Screen |
|---|---|---|---|
| GET | /v1/staff/me | → StaffMe | shell |
| POST | /v1/staff/access-denied | { path } → 204 (audit `staff.access_denied`, the refused portal path as the reason) | S06 /forbidden |
| GET | /v1/staff/dashboard | → TherapistDashboard | S01 |
| GET | /v1/staff/inbox?category=all\|risk\|appointments\|messages\|system&cursor= | → Inbox (unacknowledged crisis events the reader may see come first with `pinned: true`; a therapist's are their own patients') | S41 |
| GET | /v1/staff/inbox/unread-count | → { unreadCount } (stored unread + pinned alerts) | shell bell |
| POST | /v1/staff/inbox/{id}/read · /v1/staff/inbox/read-all | → 204 (pinned alerts stay until acknowledged) | S41 |
| GET | /v1/staff/alerts?status=open\|handled\|all | → Alerts { items: Alert[], openCount, handledCount } (open events and those closed in the last 30 days; a therapist sees their own patients only; audited `staff.alerts_viewed`). Acknowledge and hand to on-call use the worker's acknowledge and `actions` (kind refer) | S20 |
| GET/PUT | /v1/staff/profile | → StaffProfile / UpdateStaffProfileRequest → StaffProfile (therapist; 400 `error.profile.capacity_below_caseload`, 400 `error.profile.contact_details` for phones, e-mails or links in the text, 400 field errors) | S21 |
| GET | /v1/staff/me/security | → StaffSecurity { passwordChangedAt, totpEnabled, recoveryCodesRemaining, idleTimeoutMinutes: 15, sessions } | S32 |
| PUT | /v1/staff/me/password | { currentPassword, newPassword, code } → 204 (the other sessions end) | S32 |
| POST | /v1/staff/me/totp/move | { password, code } → { secret, otpauthUri, moveToken, expiresAt } (nothing changes yet) | S32 |
| POST | /v1/staff/me/totp/move/confirm | { moveToken, code from the new device } → 204 (410 `error.totp.move_expired` after 10 minutes) | S32 |
| POST | /v1/staff/me/recovery-codes | { password, code } → RecoveryCodes | S32 |
| DELETE | /v1/staff/me/sessions/{id} · POST /v1/staff/me/sessions/revoke-others | → 204 | S32, S41 |
| GET/PUT | /v1/staff/me/notifications | StaffNotificationPrefs { riskAlerts (always true), bookings, weeklyReport, dailyDigest } | S32 |
| GET | /v1/staff/patients/{pseudonymId}/intake | → StaffIntakeView (audited) | S01 |
| POST | /v1/staff/patients/{pseudonymId}/intake/approve | → 204 | S01 |
| POST | /v1/staff/appointments/{id}/outcome | { outcome: attended, no_show, cancelled } → 204 | S01 |
| POST | /v1/staff/patients/{pseudonymId}/homework | { title, instructions[], minutes, frequency, startsOn, endsOn } → 201 | (S16 later) |
| POST | /v1/staff/checkins/{id}/feedback | { text } → 204 | (S16 later) |
| GET | /v1/staff/patients | → CaseloadItem[] (the therapist's active patients; supervisors see all assigned) | menu "My patients" |
| GET | /v1/staff/patients/{pseudonymId}/file | → PatientFile + `ETag` (audited `patient.file_opened`) | S02 |
| GET | /v1/staff/patients/{pseudonymId}/evidence/{statementKey} | → Evidence { messageIds, turns, removed } (audited `patient.evidence_viewed`) | S02 |
| POST | /v1/staff/patients/{pseudonymId}/file/approve | `If-Match` + { edits?: { key: text } } → PatientFile (428 without If-Match, 412 when the file changed, 409 when already approved; therapist) | S02 |
| POST | /v1/staff/patients/{pseudonymId}/file/dismiss-item | { statementKey, reason } → PatientFile (reason kept in the audit log; therapist) | S02 |
| POST | /v1/staff/patients/{pseudonymId}/file-feedback | { rating 1–5, comment? ≤ 500 } → 204 (therapist) | S02 |
| POST | /v1/staff/patients/{pseudonymId}/risk-flag | { riskLevel, reason } → { riskLevel } (higher than the current level only) | S02 |
| POST | /v1/staff/patients/{pseudonymId}/crisis | { level 3 or 4, note } → { crisisEventId, level } (through the crisis worker, gate source `staff`: opens or raises the event, locks the chat, pages) | S02 |
| GET | /v1/staff/triage | → TriageQueueItem[] (open cases, risk then due time; claim holder; supervisor, lead) | S03 |
| GET | /v1/staff/triage/{id} | → TriageCase (audited `triage.case_opened`) | S03 |
| GET | /v1/staff/triage/{id}/therapists | → SuggestedTherapist[] (approved, free capacity, a free slot) | S03 |
| POST · DELETE | /v1/staff/triage/{id}/claim | → TriageCase (15 minutes of server time; 409 `error.triage.claimed` + claimedBy, claimedUntil) · → 204 | S03 |
| POST | /v1/staff/triage/{id}/decisions | TriageDecisionRequest → TriageDecisionResult (claim required; reason required except approve) | S03 |
| POST | /v1/staff/triage/{id}/review | { riskLevel 1–4, focus: specialty codes } → creates up to three match suggestions; cannot lower the level (409) | older single step |
| (worker) GET | /v1/crisis/events/open | → OpenCrisisEvent[] | S01, S04 |
| (worker) GET | /v1/crisis/events/{id} | → CrisisEventDetail | S04 |
| (worker) POST | /v1/crisis/events/{id}/acknowledge | → OpenCrisisEvent | S01, S04 |
| (worker) POST | /v1/crisis/events/{id}/excerpt | { totpCode, reason? } → { excerpt, hideAt } (audited) | S04 |
| (worker) POST | /v1/crisis/events/{id}/reveal-contact | { totpCode, reason } → RevealedContact (audited, shown for 10 minutes) | S04 |
| (worker) POST | /v1/crisis/events/{id}/actions | { kind, text?, outcome? } → CrisisEventDetail. Kinds: call_attempt; call_outcome (outcome: answered, no_answer, voicemail, wrong_number); contacted (text required, moves the event to InContact); local_emergency, note (text required); refer (text required, S01: leaves the case with the on-call, allowed to anyone who sees the event); handover (back to the queue). Once someone owns the event, only the owner, a supervisor or the clinical lead may act (403 `error.crisis.not_owner`) | S01, S04 |
| (worker) GET | /v1/crisis/responders | → Responder[] (supervisors, the clinical lead and on-call clinicians, without the caller) | S04 |
| (worker) POST | /v1/crisis/events/{id}/handover | { toUserId, note? } → CrisisEventDetail (offer; the owner keeps the case) | S04 |
| (worker) POST | /v1/crisis/events/{id}/handover/accept | → CrisisEventDetail (the colleague asked becomes the owner; otherwise 409 `error.crisis.nothing_pending`) | S04 |
| (worker) POST | /v1/crisis/events/{id}/handover/decline | → CrisisEventDetail (the colleague declines or the owner withdraws) | S04 |
| (worker) POST | /v1/crisis/events/{id}/close | { outcome, reason } → OpenCrisisEvent. `unreachable_referred` from a supervisor is held (status stays open, `pendingClose` on the detail) until the clinical lead co-signs | S04 |
| (worker) POST | /v1/crisis/events/{id}/close/cosign | → OpenCrisisEvent (clinical lead only; closes and sets a review 72 hours later, shown on the timeline) | S04 |
| (worker) GET | /v1/crisis/health | → CrisisHealth | S04 |
| (worker) hub | /hubs/crisis?lang= | crisisAlert (kind: opened, raised, escalated, acknowledged, updated, closed) | S01, S04 |

Clinical file (S02). Statements are `concern`, `goals`, `supports`, `priorCare` and `hypotheses` (therapist only, always
"not a diagnosis"). Each links to the patient messages it came from: the summary model cites the patient lines it used, and
without a citation the messages sharing the most words are used. A statement with no source is not shown (EC-15); a
patient's own correction always is. The `version` (also the `ETag`) changes when the summary, an addendum, the level, an
edit, a dismissal or the approval changes. Supervisors and the lead read the file (S03) but only the treating therapist
edits, dismisses, rates and approves.

Triage decisions (S03): `kind` is `approve` (optional `therapistIds` ≤ 3 and `focus`; else the saved or proposed
suggestions), `override_risk` (`riskLevel`), `reassign` (`therapistIds`), `escalate` (opens or raises a crisis event, result
`crisisEventId`), `close` (`referrals`: crisis_line, primary_care, psychiatry, community_support, emergency_services; the
patient's `GET /v1/triage` shows status `closed` with `referrals`) or `cosign` (clinical lead, `approve: true|false`). The
level never goes below an open or recent (30 days) crisis event (422 `error.triage.below_crisis_floor`). Lowering from 3 or 4
by a supervisor waits for the clinical lead's co-sign (`coSignRequested: true`); approve and close are refused meanwhile. When the clinical lead lowers the level, that decision is their own co-sign (approved by the product owner).
Review deadlines follow the level: 24 h for 1, 4 h for 2, 1 h for 3 and 4. Every decision is audited with its reason.

Alerts and the open-events list carry `number` (CE-1001), `source`, `sourceLabel` and `acknowledgedBy`. Status is Open, Acknowledged, InContact or Closed. CrisisEventDetail adds `canAct`, `isOwner`, `pendingHandover`, `pendingClose`, `canCoSign`, `emergencyNumber`, `emergencyScript` (rule-pack responder script, with or without a known location), `recentCallAttempts`, `callAdvice` (local_emergency or unreachable after 3 calls in 15 minutes to a Critical patient, EC-33) and `reviewDueAtUtc`. The timeline ends with the next scheduled escalation while nobody owns the event. Supervisors, the
clinical lead and on-call staff receive every alert; other therapists only receive alerts for their own patients. The console endpoints follow the same rule through RLS: on-call therapists see every crisis event, and contact data and excerpts only while the event is open.
Staff refresh and sign-out send `{}`; the refresh token is the HttpOnly cookie `tamaneen_staff_rt` on `/v1/auth`.
