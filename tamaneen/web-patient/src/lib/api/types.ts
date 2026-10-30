/**
 * Tamaneen API v1 contract (patient side). The .NET API serializes camelCase JSON; dates are ISO-8601 UTC
 * strings. Every error is RFC 7807 ProblemDetails (see ApiProblem) already localized by Accept-Language.
 * docs/api-v1.md lists the endpoints that use these types.
 */

export type Lang = 'ar' | 'en';

export interface ApiProblem {
  type?: string;
  title: string;
  detail?: string;
  status: number;
  code?: string;
  lang?: Lang;
  dir?: 'rtl' | 'ltr';
  traceId?: string;
  errors?: Record<string, string[]>;
  /** Login failures: attempts left before a 15-minute lock. */
  remainingAttempts?: number;
  /** Lockouts and resend cooldowns. */
  retryAfterSeconds?: number;
  /** 5xx: localized "if you are in danger…" hint. */
  crisisHint?: string;
}

// ---------- Public config ----------
export interface PilotCity { code: string; name: string }
export interface PublicConfig {
  emergencyNumber: string;
  supportLine: string;
  matchWithinHours: number;
  /** P44: the supervisor suggests new therapists within this many hours. */
  changeTherapistWithinHours?: number;
  triageWithinHours: number;
  minAge: number;
  pilotRegion: string;
  pilotCities: PilotCity[];
  timeZone: string;
  /** False while new sign-ups are paused (EC-26): P01, P02, P12 and P15 show the emergency numbers instead. */
  signupsOpen: boolean;
}

// ---------- Auth ----------
export type ContactType = 'phone' | 'email';
export interface EligibilityRequest { isAdult: boolean; cityCode: string }
export interface EligibilityResponse { eligible: boolean; eligibilityToken?: string; reason?: 'age' | 'region' }
export interface NicknameSuggestion { nickname: string }
export interface SignupRequest { eligibilityToken: string; nickname: string; contactType: ContactType; contact: string; password: string; language: Lang }
export interface ChallengeResponse { challengeId: string; maskedContact: string; resendAfterSeconds: number; expiresInSeconds: number }
export interface VerifyRequest { challengeId: string; code: string }
/** Returned by the API only; the BFF moves the tokens into HttpOnly cookies and never forwards them to the browser. */
export interface TokenPair { accessToken: string; accessExpiresAt: string; refreshToken: string; refreshExpiresAt: string }
export interface SignupVerifiedResponse { tokens: TokenPair; recoveryCodes: string[] }
export interface LoginRequest { contact: string; password: string; deviceToken?: string; client?: 'patient' | 'staff' }
export type MfaMethod = 'totp' | 'sms' | 'recovery';
export type LoginResponse =
  | { status: 'ok'; tokens: TokenPair }
  | { status: 'mfa'; mfaToken: string; methods: MfaMethod[]; maskedPhone?: string };
export interface MfaRequest { mfaToken: string; method: MfaMethod; code: string; rememberDevice: boolean }
export interface MfaResponse { tokens: TokenPair; deviceToken?: string }
export interface RecoverStartRequest { contact: string }
export interface RecoverCompleteRequest { challengeId: string; code: string; newPassword: string }
export interface RecoverWithCodeRequest { contact: string; recoveryCode: string; newPassword: string }

// ---------- Me / onboarding ----------
export type CarePath = 'intake' | 'direct';
export type IntakeStatus = 'none' | 'active' | 'paused' | 'summarizing' | 'review' | 'submitted' | 'expired';
/** escalated: a supervisor opened a crisis event instead; closed: no match, see `referrals` (S03). */
export type TriageStatus = 'none' | 'pending' | 'reviewed' | 'matched' | 'escalated' | 'closed';
export interface Me {
  pseudonymId: string;
  nickname: string;
  language: Lang;
  timeZone: string;
  onboarding: {
    consentsDone: boolean;
    safetyInfoDone: boolean;
    path: CarePath | null;
    screeningDone: boolean;
    intakeStatus: IntakeStatus;
    triageStatus: TriageStatus;
  };
  chatLocked: boolean;
  /** Set once a supervisor signed off the end of care (P53) while the patient has no therapist. */
  careClosedAt?: string | null;
}
export interface UpdateProfileRequest { nickname: string; timeZone: string }

export type ConsentKey = 'platform' | 'ai' | 'share_with_therapist' | 'research' | 'emergency_location';
export interface Consent { key: ConsentKey; required: boolean; granted: boolean; version: string; updatedAt: string | null; withdrawable: boolean }
export interface UpdateConsentsRequest { items: { key: ConsentKey; granted: boolean }[] }

export interface SafetyInfo { contactPhone: string | null; trustedName: string | null; trustedPhone: string | null; shareLocation: boolean; shareLocationSince: string | null }
export interface UpdateSafetyInfoRequest { contactPhone: string | null; trustedName: string | null; trustedPhone: string | null; shareLocation: boolean }

export interface SessionInfo { id: string; deviceLabel: string; current: boolean; lastActiveAt: string }
export interface SecurityOverview { passwordChangedAt: string; totpEnabled: boolean; recoveryCodesRemaining: number; sessions: SessionInfo[] }
export interface ChangePasswordRequest { currentPassword: string; newPassword: string }
export interface TotpSetup { secret: string; otpauthUri: string }
export interface CodeRequest { code: string }
/** P32 re-authentication: DELETE me/totp (both required) and POST me/recovery-codes (code while the authenticator is on). */
export interface ReauthRequest { password: string; code?: string }
export interface RecoveryCodes { recoveryCodes: string[] }

export type ReminderFrequency = 'daily' | 'every2days' | 'off';
export interface NotificationPrefs {
  push: boolean; sms: boolean; email: boolean;
  neutralText: boolean; quickExit: boolean;
  homeworkReminder: ReminderFrequency; reminderTime: string; // "21:00"
  quietFrom: string; quietTo: string; // "23:00", "08:00"
}

export interface AccessLogEntry { actor: 'therapist' | 'supervisor' | 'clinical_lead' | 'system' | 'crisis_service' | 'patient'; action: string; atUtc: string }
export interface DataRequestStatus { exportRequestedAt: string | null; exportReadyBy: string | null; exportReady: boolean }
export interface DeleteAccountRequest { password: string }
/** POST /v1/me/data-export asks for the password again (PB-58). */
export interface DataExportRequest { password: string }

// ---------- Screening (P23) ----------
export interface ScreeningQuestion { index: number; text: string }
export interface ScreeningForm { instrument: 'cssrs'; version: string; questions: ScreeningQuestion[]; note: string }
/** The answers given so far, in order (1 to 6). Sent after every answer; only a complete set is stored. */
export interface ScreeningSubmit { answers: boolean[] }
export interface GateOutcome { crisis: boolean; level: number; script?: string }

// ---------- Intake (P04, P05, P36) ----------
export type ChatRole = 'assistant' | 'patient';
export interface ChatMessage { id: string; role: ChatRole; text: string; at: string; fallback?: boolean }
export interface QuestionOption { value: number; label: string }
export interface PendingQuestion { instrument: 'phq9' | 'gad7'; item: number; total: number; text: string; options: QuestionOption[] }
export interface IntakeState {
  intakeId: string;
  status: IntakeStatus;
  progress: number; // 0..100
  messages: ChatMessage[];
  pendingQuestion: PendingQuestion | null;
  canFinish: boolean;
  expiresAt: string;
  /** The patient switched to the form because the assistant was unavailable (PB-28). */
  formMode: boolean;
}
export interface IntakeTurnResponse extends IntakeState { crisis: boolean; script?: string }
/** clientMessageId (PB-21) stays the same across retries of one message, so the API stores it once (EC-07). */
export interface SendMessageRequest { text: string; clientMessageId?: string }
export interface AnswerRequest { instrument: 'phq9' | 'gad7'; item: number; value: number | null } // null = skipped

export type SummarySection = 'concern' | 'goals' | 'supports' | 'priorCare' | 'extra';
export interface IntakeSummary {
  intakeId: string;
  sections: Record<SummarySection, { text: string; editedByPatient: boolean }>;
  generatedByAi: boolean;
}
export interface UpdateSummaryRequest { sections: Partial<Record<SummarySection, string>> }
export type ReferralCode = 'crisis_line' | 'primary_care' | 'psychiatry' | 'community_support' | 'emergency_services';
export interface TriageState { status: TriageStatus; submittedAt: string | null; expectedBy: string | null; suggestionsReady: boolean; referrals?: ReferralCode[] | null }
export interface AddToSummaryRequest { text: string }

// ---------- Crisis (P06, P14) ----------
export interface CrisisStatus { locked: boolean; eventOpen: boolean; acknowledged: boolean; contactPhoneLast4: string | null; script: string | null }
export interface HelpResponse extends GateOutcome { eventOpen: boolean }
/** Body of POST /v1/crisis/help. requestId is the device-generated idempotency key, reused on every retry. */
export interface HelpRequest { requestId?: string }

// ---------- Therapists, matching, booking ----------
export type Gender = 'female' | 'male';
export interface TherapistSummary {
  id: string;
  displayName: string;
  initials: string;
  title: string;
  specialties: string[]; // codes: anxiety, stress, sleep, depression, cbt, mindfulness, work_stress, adjustment, social_anxiety, trauma
  languages: Lang[];
  gender: Gender;
  licenseVerified: boolean;
  nextAvailableAt: string | null;
  hasEvening: boolean;
}
export interface TherapistProfile extends TherapistSummary { bio: string; sessionType: 'remote'; durationMinutes: number }
export interface MatchSuggestion { therapist: TherapistSummary; rank: number; best: boolean; reasons: { sharedSpecialties: string[]; speaksYourLanguage: boolean; eveningSlots: boolean; nextAvailableAt: string | null } }
export interface MatchList { status: 'pending' | 'ready'; items: MatchSuggestion[] }
export interface TherapistQuery { q?: string; specialty?: string; language?: Lang; gender?: Gender; evening?: boolean }
export interface Slot { slotId: string; startsAt: string }
export interface Hold { holdId: string; slotId: string; startsAt: string; expiresAt: string; therapist: TherapistSummary }

// ---------- Waitlist (P56) ----------
export type WaitlistGender = 'any' | Gender;
export type WaitlistLanguage = 'any' | Lang;
export type TimeOfDay = 'morning' | 'afternoon' | 'evening';
export type DayKind = 'weekdays' | 'weekend';
/** Empty timesOfDay or days means any. */
export interface WaitlistPreferences { gender: WaitlistGender; language: WaitlistLanguage; timesOfDay: TimeOfDay[]; days: DayKind[] }
/** The time that opened for the patient; book it on P08. */
export interface WaitlistOffer { therapistId: string; therapistName: string; startsAt: string }
export interface Waitlist {
  status: 'none' | 'waiting' | 'available';
  /** Joined for one therapist (from P25), or null for any therapist who fits. */
  therapistId: string | null;
  therapistName: string | null;
  preferences: WaitlistPreferences | null;
  joinedAt: string | null;
  expectedDaysMin: number;
  expectedDaysMax: number;
  offer: WaitlistOffer | null;
}
export interface JoinWaitlistRequest extends WaitlistPreferences { therapistId?: string | null }

export type AppointmentStatus ='confirmed' | 'cancelled_by_patient' | 'cancelled_by_therapist' | 'completed' | 'no_show';
export interface Appointment {
  id: string;
  therapist: TherapistSummary;
  startsAt: string;
  durationMinutes: number;
  sequenceNumber: number;
  status: AppointmentStatus;
  rescheduled: boolean;
  /** S18 "running late": minutes the therapist said they will be late (P54), or null. */
  therapistLateMinutes: number | null;
  cancelledBy: 'patient' | 'therapist' | 'care_ended' | null;
  /** The patient's own reason when they cancelled; always null when the therapist cancelled (P67 never shows it). */
  cancelReasonCode: string | null;
  /** The therapist recorded a no-show (P63). */
  missed: boolean;
}
export interface AppointmentDetail extends Appointment {
  canChange: boolean;
  changeDeadline: string;
  /** Kept for older clients; the apps open the lobby (P41) instead. */
  joinUrl: string | null;
  joinAvailableAt: string;
  /** The therapist's own "running late" notice in minutes (S18), or null. P54 also starts when the start passes without the therapist. */
  therapistLateMinutes: number | null;
}
export interface AppointmentList { upcoming: Appointment[]; past: Appointment[] }
export interface RescheduleRequest { slotId: string }
/** P28 cancel reasons (PB-45). Codes only; the labels are in messages `appointmentDetail.reasons`. */
export type CancelReasonCode = 'schedule_conflict' | 'feeling_better' | 'unwell' | 'therapist_fit' | 'other';
export interface CancelRequest { reasonCode: CancelReasonCode }

// ---------- Session room: P41 lobby, P54, P42 live, P43 ----------
export type VideoProvider = 'dev' | 'embed';
/** POST /v1/appointments/{id}/join. `token` is the session pass the presence heartbeat needs. */
export interface JoinDescriptor {
  provider: VideoProvider;
  mode: 'test_room' | 'embed';
  url: string | null;
  token: string;
  expiresAt: string;
  opensAt: string;
  closesAt: string;
  participant: 'patient' | 'therapist';
  heartbeatSeconds: number;
}
export interface SessionPeer { present: boolean; joinedAt: string | null; lastSeenAt: string | null }
/** GET/POST /v1/appointments/{id}/presence. `peer` is the therapist. Countdowns use serverNow (EC-09). */
export interface SessionState {
  appointmentId: string;
  serverNow: string;
  startsAt: string;
  durationMinutes: number;
  opensAt: string;
  closesAt: string;
  open: boolean;
  status: AppointmentStatus;
  peer: SessionPeer;
  therapistLate: boolean;
  lateOptionsAfterMinutes: number;
  heartbeatSeconds: number;
  provider: VideoProvider;
  patientCrisisOpen: boolean;
}
export interface PresenceRequest { token: string; leaving?: boolean }
export interface PreSessionNote { text: string | null; updatedAt: string | null }
export interface PreSessionNoteSaved extends GateOutcome { text: string | null; updatedAt: string | null }
export type SessionRating = 'not_helpful' | 'okay' | 'helpful';
export interface SessionFeedback { rating: SessionRating | null; comfortable: boolean; comment: string | null; createdAt: string }
export interface SessionFeedbackRequest { rating?: SessionRating; comfortable?: boolean; comment?: string }
export interface SessionFeedbackSaved extends GateOutcome { feedback: SessionFeedback }

// ---------- Home, mood, homework, safety plan ----------
export interface HomeSummary {
  nickname: string;
  nextAppointment: Appointment | null;
  todayHomework: { id: string; title: string; minutes: number | null; doneDays: number; totalDays: number } | null;
  latestFeedback: { therapistName: string; initials: string; text: string; at: string } | null;
  moodToday: number | null;
  hasSafetyPlan: boolean;
  triage: TriageState;
  /** A session the therapist cancelled (P67) or marked missed (P63) in the last 7 days, until the patient books again. */
  appointmentNotice: { appointmentId: string; kind: 'cancelled' | 'missed' } | null;
}
export type MoodTag = 'tired' | 'anxious' | 'slept_well' | 'work_stress' | 'calm';
export interface MoodEntryRequest { score: number; tags: MoodTag[]; note?: string }
export interface MoodEntry { id: string; score: number; tags: MoodTag[]; at: string }
export interface MoodSaveResponse extends GateOutcome { entry: MoodEntry }

export type HomeworkFrequency = 'daily' | 'twice_weekly' | 'weekly' | 'once';
export interface HomeworkItem {
  id: string;
  title: string;
  therapistName: string;
  frequency: HomeworkFrequency;
  endsOn: string;
  dueToday: boolean;
  doneThisPeriod: number;
  hasNewFeedback: boolean;
  completed: boolean;
}
export interface HomeworkList { active: HomeworkItem[]; completed: HomeworkItem[] }
export type CheckinStatus = 'done' | 'partial' | 'skipped';
export interface HomeworkDetail extends HomeworkItem {
  instructions: string[];
  minutes: number | null;
  doneDays: number;
  totalDays: number;
  latestFeedback: { text: string; at: string; initials: string } | null;
  todayCheckin: { status: CheckinStatus; feeling: number | null; note: string | null } | null;
}
export interface CheckinRequest { status: CheckinStatus; feeling: number | null; note?: string }
export interface CheckinResponse extends GateOutcome { homework: HomeworkDetail }

export type SafetyPlanSection = 'warningSigns' | 'copingAlone' | 'distractions' | 'peopleToAsk' | 'professionals' | 'safeEnvironment' | 'reasonToLive';
/** A personal help contact in section 4 (P37), shown as a tel: button. Name up to 100 characters; at most 5. */
/** `relation` (≤ 50) is set from S35; `phone` may be "" (optional since S35). */
export interface PlanContact { name: string; phone: string; relation?: string | null }
/** `version` is shared with the therapist's S35 builder and is the ETag (PUT may send it in If-Match; 412 when stale). */
export interface SafetyPlan { sections: Record<SafetyPlanSection, string>; contacts: PlanContact[]; updatedAt: string | null; coAuthor: string | null; version?: number }
export interface UpdateSafetyPlanRequest { sections: Record<SafetyPlanSection, string>; contacts: PlanContact[] }

export interface ContactRequestCreated { requestedAt: string; expectedBy: string; withinHours: number }
export type DismissReason = 'specialty' | 'gender' | 'language' | 'schedule' | 'other';
export interface DismissRequest { reason?: DismissReason }

/** P46 notification centre (GET /v1/me/inbox). `text` is already in the user's language; `link` is an app path without the locale. */
export type NotificationCategory = 'risk' | 'appointments' | 'messages' | 'homework' | 'care' | 'security' | 'system';
export interface InboxItem {
  id: string;
  kind: string;
  category: NotificationCategory;
  text: string;
  link: string | null;
  createdAt: string;
  read: boolean;
  pinned: boolean;
  /** Ids and times only (e.g. sessionId for a new sign-in). */
  data: Record<string, string>;
}
export interface Inbox { items: InboxItem[]; unreadCount: number; nextCursor: string | null; counts: Record<string, number> }
export interface UnreadCount { unreadCount: number }

/** P38 after crisis (GET /v1/crisis/follow-up). Structured data only: never the conversation, the level or a diagnosis. */
export interface CrisisFollowUp {
  state: 'none' | 'open' | 'closed';
  eventId: string | null;
  closedAt: string | null;
  responder: string | null;
  /** The responder reached the patient (closed as safe_contacted). */
  talked: boolean;
  therapistInformed: boolean;
  therapistName: string | null;
  nextAppointmentId: string | null;
  nextAppointmentAt: string | null;
  /** Not seen yet: home opens P38. */
  unread: boolean;
}
// ---------- Phase 5: change therapist (P44), end of care (P53), session summary ----------

export type ChangeReasonCode = 'not_comfortable' | 'schedule' | 'gender' | 'specialty' | 'other';
export interface ChangeReason { code: ChangeReasonCode; label: string }
export type ChangeRequestStatus = 'requested' | 'matching' | 'offered' | 'accepted' | 'closed';
export interface ChangeTherapistRequest { id: string; status: ChangeRequestStatus; reasonCode: ChangeReasonCode | null; createdAt: string; expectedBy: string; withinHours: number }
export interface CreateChangeRequest { reasonCode?: ChangeReasonCode }
export interface CareClosure {
  therapistName: string;
  sessionCount: number;
  summary: string;
  warningSigns: string | null;
  whatToDo: string | null;
  followUp: 'none' | 'phq9_1m_3m';
  keepLibrary: boolean;
  closedAt: string;
  returnRequested: boolean;
}
export interface CareReturnResponse { next: string }
export interface SessionSummary { points: string[]; sharedAt: string }

/** GET · POST · DELETE /v1/me/care/continue: the patient's request to continue care (§10.4; blocks S57 closing while open). */
export interface ContinueCare { requested: boolean; requestedAt: string | null }
