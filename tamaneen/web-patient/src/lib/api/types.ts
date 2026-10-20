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
  triageWithinHours: number;
  minAge: number;
  pilotRegion: string;
  pilotCities: PilotCity[];
  timeZone: string;
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
export type TriageStatus = 'none' | 'pending' | 'reviewed' | 'matched';
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

// ---------- Screening (P23) ----------
export interface ScreeningQuestion { index: number; text: string }
export interface ScreeningForm { instrument: 'cssrs'; version: string; questions: ScreeningQuestion[]; note: string }
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
}
export interface IntakeTurnResponse extends IntakeState { crisis: boolean; script?: string }
export interface SendMessageRequest { text: string }
export interface AnswerRequest { instrument: 'phq9' | 'gad7'; item: number; value: number | null } // null = skipped

export type SummarySection = 'concern' | 'goals' | 'supports' | 'priorCare' | 'extra';
export interface IntakeSummary {
  intakeId: string;
  sections: Record<SummarySection, { text: string; editedByPatient: boolean }>;
  generatedByAi: boolean;
}
export interface UpdateSummaryRequest { sections: Partial<Record<SummarySection, string>> }
export interface TriageState { status: TriageStatus; submittedAt: string | null; expectedBy: string | null; suggestionsReady: boolean }
export interface AddToSummaryRequest { text: string }

// ---------- Crisis (P06, P14) ----------
export interface CrisisStatus { locked: boolean; eventOpen: boolean; acknowledged: boolean; contactPhoneLast4: string | null; script: string | null }
export interface HelpResponse extends GateOutcome { eventOpen: boolean }

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

export type AppointmentStatus = 'confirmed' | 'cancelled_by_patient' | 'cancelled_by_therapist' | 'completed' | 'no_show';
export interface Appointment {
  id: string;
  therapist: TherapistSummary;
  startsAt: string;
  durationMinutes: number;
  sequenceNumber: number;
  status: AppointmentStatus;
  rescheduled: boolean;
}
export interface AppointmentDetail extends Appointment { canChange: boolean; changeDeadline: string; joinUrl: string | null; joinAvailableAt: string }
export interface AppointmentList { upcoming: Appointment[]; past: Appointment[] }
export interface RescheduleRequest { slotId: string }

// ---------- Home, mood, homework, safety plan ----------
export interface HomeSummary {
  nickname: string;
  nextAppointment: Appointment | null;
  todayHomework: { id: string; title: string; minutes: number | null; doneDays: number; totalDays: number } | null;
  latestFeedback: { therapistName: string; initials: string; text: string; at: string } | null;
  moodToday: number | null;
  hasSafetyPlan: boolean;
  triage: TriageState;
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
export interface SafetyPlan { sections: Record<SafetyPlanSection, string>; updatedAt: string | null; coAuthor: string | null }
export interface UpdateSafetyPlanRequest { sections: Record<SafetyPlanSection, string> }

export interface ContactRequestCreated { requestedAt: string }
