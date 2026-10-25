/** Tamaneen API v1 contract, staff side (S01, S04). See docs/api-v1.md. Dates are ISO-8601 UTC strings. */

export type Lang = 'ar' | 'en';
/** Same codes as core/session/staff-role.ts. */
export type StaffRole = 'therapist' | 'supervisor' | 'lead' | 'admin';

export interface ApiProblem { title: string; detail?: string; status: number; code?: string; remainingAttempts?: number; retryAfterSeconds?: number }

export interface TokenPair { accessToken: string; accessExpiresAt: string; refreshToken: string; refreshExpiresAt: string }
/** Staff sign-in: password first, then a mandatory authenticator code. The refresh token is an HttpOnly cookie set by the API. */
export interface StaffLoginRequest { contact: string; password: string; client: 'staff' }
export type StaffLoginResponse = { status: 'mfa'; mfaToken: string; methods: ('totp' | 'recovery')[] };
export interface StaffMfaRequest { mfaToken: string; method: 'totp' | 'recovery'; code: string; rememberDevice: false }
export interface StaffSession { accessToken: string; accessExpiresAt: string }
/** POST /v1/auth/login/mfa wraps the pair (same as the patient MfaResponse); POST /v1/auth/refresh returns it flat. */
export interface StaffMfaResponse { tokens: StaffSession; deviceToken?: string | null }

/** S06 /recover: the same answer whether or not the account exists; the code goes to the work email. */
export interface RecoverStartRequest { contact: string }
export interface ChallengeResponse { challengeId: string; maskedContact: string; resendAfterSeconds: number; expiresInSeconds: number }
export interface RecoverCompleteRequest { challengeId: string; code: string; newPassword: string }

export interface StaffMe { userId: string; displayName: string; role: StaffRole; language: Lang; onCall: boolean }

export type RiskLevel = 1 | 2 | 3 | 4;
export interface PendingProfile { pseudonymId: string; riskLevel: RiskLevel; phq9: number | null; gad7: number | null; firstSessionAt: string | null }
export interface TodaySession { appointmentId: string; startsAt: string; pseudonymId: string; sequenceNumber: number; riskLevel: RiskLevel | null; outcomeRecorded: boolean; hasNewCheckins: boolean }
export interface TherapistDashboard {
  displayName: string;
  date: string;
  alerts: OpenCrisisEvent[];
  stats: { todaySessions: number; pendingProfiles: number; newCheckins: number; capacityUsed: number; capacityMax: number; myPatients: number };
  pendingProfiles: PendingProfile[];
  todaySessions: TodaySession[];
}
export interface StaffIntakeView {
  pseudonymId: string;
  riskLevel: RiskLevel;
  phq9: number | null;
  gad7: number | null;
  cssrsPositive: boolean | null;
  sections: { concern: string; goals: string; supports: string; priorCare: string; extra: string };
  submittedAt: string | null;
  approved: boolean;
}
export type SessionOutcome = 'attended' | 'no_show' | 'cancelled';

/** InContact: the owner reached the patient; the event stays owned until it is closed with an outcome. */
export type CrisisStatus = 'Open' | 'Acknowledged' | 'InContact' | 'Closed';

/** Same shape as CrisisAlertMessage pushed on /hubs/crisis ("crisisAlert"). */
export interface OpenCrisisEvent {
  eventId: string;
  /** Human-facing number, e.g. "CE-1042". */
  number: string;
  kind: 'opened' | 'raised' | 'escalated' | 'acknowledged' | 'closed' | 'updated';
  pseudonymId: string;
  level: RiskLevel;
  levelLabel: string;
  title: string;
  body: string;
  patientLanguage: Lang;
  patientLanguageName: string;
  escalationTier: number;
  escalationTierLabel: string;
  status: CrisisStatus;
  openedAtUtc: string;
  ackDeadlineUtc: string;
  categories: string[];
  dir: 'rtl' | 'ltr';
  /** Where it was raised (mood, intake_chat, ...) and its label in the request language. */
  source: string;
  sourceLabel: string;
  acknowledgedBy: string | null;
}
/** refer: the patient's therapist leaves the case with the on-call without taking it (S01). */
export type CrisisActionKind = 'contacted' | 'local_emergency' | 'note' | 'handover' | 'call_attempt' | 'call_outcome' | 'refer';
export type CallOutcome = 'answered' | 'no_answer' | 'voicemail' | 'wrong_number';
export interface TimelineEntry { at: string; kind: string; text: string; actor: string | null }
export interface CrisisEventDetail extends OpenCrisisEvent {
  ruleIds: string[];
  rulePackVersion: string;
  contactDeadlineUtc: string | null;
  hasContactPhone: boolean;
  hasTrustedContact: boolean;
  locationConsent: boolean;
  maskedPhone: string | null;
  timeline: TimelineEntry[];
  canClose: boolean;
  /** False when someone else owns the event: the console is read-only for this user (EC-35). */
  canAct: boolean;
  isOwner: boolean;
  pendingHandover: PendingHandover | null;
  pendingClose: PendingClose | null;
  /** Clinical lead with an unreachable close waiting for her signature. */
  canCoSign: boolean;
  emergencyNumber: string;
  /** Rule-pack responder script for the local emergency procedure, in the request language; asks for the location when none is on file. */
  emergencyScript: string;
  recentCallAttempts: number;
  /** EC-33: after 3 calls in 15 minutes without reaching a Critical patient. */
  callAdvice: 'local_emergency' | 'unreachable' | null;
  reviewDueAtUtc: string | null;
}
export interface PendingHandover { toUserId: string; toName: string | null; fromName: string | null; requestedAtUtc: string; forMe: boolean }
export interface PendingClose { outcome: CloseCrisisRequest['outcome']; reason: string; byName: string | null; requestedAtUtc: string }
/** A colleague who can take a handover. */
export interface Responder { userId: string; displayName: string; role: StaffRole; onCall: boolean }
export interface HandoverRequest { toUserId: string; note?: string }
export interface RevealRequest { totpCode: string; reason: string }
export interface RevealedContact { contactPhone: string | null; trustedName: string | null; trustedPhone: string | null; locationConsent: boolean; hideAt: string }
export interface Excerpt { excerpt: string; hideAt: string }
export interface CrisisActionRequest { kind: CrisisActionKind; text?: string; outcome?: CallOutcome }
export interface CloseCrisisRequest { outcome: 'safe_contacted' | 'emergency_services' | 'unreachable_referred' | 'false_alarm'; reason: string }
export interface CrisisHealth { gate: 'ok' | 'degraded' | 'down'; classifier: 'ok' | 'degraded' | 'down'; comms: 'ok' | 'degraded' | 'down'; onCall: string | null; backup: string | null }
