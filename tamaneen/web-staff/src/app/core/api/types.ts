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
  status: 'Open' | 'Acknowledged' | 'Closed';
  openedAtUtc: string;
  ackDeadlineUtc: string;
  categories: string[];
  dir: 'rtl' | 'ltr';
  /** Where it was raised (mood, intake_chat, ...) and its label in the request language. */
  source: string;
  sourceLabel: string;
  acknowledgedBy: string | null;
}
export type CrisisActionKind = 'contacted' | 'local_emergency' | 'note' | 'handover';
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
}
export interface RevealRequest { totpCode: string; reason: string }
export interface RevealedContact { contactPhone: string | null; trustedName: string | null; trustedPhone: string | null; locationConsent: boolean; hideAt: string }
export interface Excerpt { excerpt: string; hideAt: string }
export interface CrisisActionRequest { kind: CrisisActionKind; text?: string }
export interface CloseCrisisRequest { outcome: 'safe_contacted' | 'emergency_services' | 'unreachable_referred' | 'false_alarm'; reason: string }
export interface CrisisHealth { gate: 'ok' | 'degraded' | 'down'; classifier: 'ok' | 'degraded' | 'down'; comms: 'ok' | 'degraded' | 'down'; onCall: string | null; backup: string | null }

/** S02 clinical file (GET /v1/staff/patients/{id}/file). `version` is also the ETag sent back as If-Match on approve. */
export type StatementKey = 'concern' | 'goals' | 'supports' | 'priorCare' | 'hypotheses';
export interface FileStatement { key: StatementKey; text: string; editedByPatient: boolean; editedByTherapist: boolean; hypothesis: boolean; sourceCount: number }
export interface TranscriptTurn { id: string; turn: number; role: 'patient' | 'assistant'; text: string; at: string }
export interface ScoreItem { item: number; text: string; value: number | null }
export type Severity = 'minimal' | 'mild' | 'moderate' | 'moderately_severe' | 'severe';
export interface InstrumentScore { instrument: 'PHQ-9' | 'GAD-7'; total: number | null; severity: Severity | null; item9: number | null; version: string; items: ScoreItem[] }
export interface CssrsResult { positive: boolean; level: number; version: string; completedAt: string; items: { index: number; text: string; answer: boolean | null }[] }
export interface FileCorrection { key: string; aiText: string | null; patientText: string }
export interface PatientFile {
  pseudonymId: string;
  riskLevel: RiskLevel;
  crisisFloor: number;
  firstSessionAt: string | null;
  path: 'intake' | 'direct';
  version: string;
  generatedByAi: boolean;
  statements: FileStatement[];
  transcript: TranscriptTurn[];
  transcriptRemoved: boolean;
  phq9: InstrumentScore | null;
  gad7: InstrumentScore | null;
  cssrs: CssrsResult | null;
  corrections: FileCorrection[];
  addenda: string[];
  approval: { approved: boolean; approvedBy: string | null; approvedAt: string | null };
  feedback: { rating: number | null; comment: string | null };
  canEdit: boolean;
  submittedAt: string | null;
}
export interface Evidence { statementKey: StatementKey; messageIds: string[]; turns: number[]; removed: boolean }
export interface CaseloadItem { pseudonymId: string; riskLevel: RiskLevel; fileApproved: boolean; nextSessionAt: string | null }
export interface ManualCrisisResult { crisisEventId: string; level: RiskLevel }

/** S03 triage review. */
export interface TriageClaim { by: string | null; until: string | null; mine: boolean }
export interface TriageQueueItem {
  id: string;
  pseudonymId: string;
  path: 'intake' | 'direct';
  contactRequested: boolean;
  riskLevel: RiskLevel;
  phq9: number | null;
  gad7: number | null;
  cssrsPositive: boolean | null;
  submittedAt: string;
  dueAt: string;
  claim: TriageClaim;
  pendingRiskLevel: RiskLevel | null;
}
export interface SuggestedTherapist { id: string; rank: number; displayName: string; specialties: string[]; shared: string[]; languages: string[]; load: number; capacity: number; nextAvailableAt: string | null }
export type RuleReason = 'phq9' | 'gad7' | 'cssrs' | 'crisis' | 'routine';
export interface TriageCaseDetail {
  id: string;
  pseudonymId: string;
  path: 'intake' | 'direct';
  contactRequested: boolean;
  status: 'pending' | 'reviewed' | 'matched' | 'escalated' | 'closed';
  riskLevel: RiskLevel;
  ruleRiskLevel: RiskLevel;
  ruleReasons: RuleReason[];
  aiRiskLevel: RiskLevel | null;
  crisisFloor: number;
  phq9: number | null;
  gad7: number | null;
  cssrsPositive: boolean | null;
  phq9Item9: number | null;
  language: Lang;
  carePath: string | null;
  summary: string;
  suggestions: SuggestedTherapist[];
  suggestionsSaved: boolean;
  claim: TriageClaim;
  pendingCoSign: { riskLevel: RiskLevel; requestedBy: string | null; requestedAt: string } | null;
  canCoSign: boolean;
  submittedAt: string;
  dueAt: string;
  serverNow: string;
}
export type TriageDecisionKind = 'approve' | 'override_risk' | 'reassign' | 'escalate' | 'close' | 'cosign';
export type ReferralCode = 'crisis_line' | 'primary_care' | 'psychiatry' | 'community_support' | 'emergency_services';
export interface TriageDecisionRequest { kind: TriageDecisionKind; reason?: string; riskLevel?: RiskLevel; therapistIds?: string[]; focus?: string[]; referrals?: ReferralCode[]; approve?: boolean }
export interface TriageDecisionResult { kind: TriageDecisionKind; status: TriageCaseDetail['status']; riskLevel: RiskLevel; coSignRequested: boolean; crisisEventId: string | null }
