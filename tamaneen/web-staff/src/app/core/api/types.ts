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
  path: 'intake' | 'direct' | 'change';
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
  path: 'intake' | 'direct' | 'change';
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
  path: 'intake' | 'direct' | 'change';
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

// ---------- Therapist session page (/sessions/:appointmentId): lobby and live ----------
export type VideoProvider = 'dev' | 'embed';
/** POST /v1/staff/appointments/{id}/join. `token` is the session pass the presence heartbeat needs. */
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
/** GET/POST /v1/staff/appointments/{id}/presence. `peer` is the patient. Countdowns use serverNow. */
export interface SessionState {
  appointmentId: string;
  serverNow: string;
  startsAt: string;
  durationMinutes: number;
  opensAt: string;
  closesAt: string;
  open: boolean;
  status: 'confirmed' | 'cancelled_by_patient' | 'cancelled_by_therapist' | 'completed' | 'no_show';
  peer: SessionPeer;
  therapistLate: boolean;
  lateOptionsAfterMinutes: number;
  heartbeatSeconds: number;
  provider: VideoProvider;
  patientCrisisOpen: boolean;
}
/** GET /v1/staff/appointments/{id}/session (audited `session.opened`). */
export interface TherapistSessionPage {
  appointmentId: string;
  pseudonymId: string;
  sequenceNumber: number;
  riskLevel: RiskLevel | null;
  state: SessionState;
  preSessionNote: string | null;
  preSessionNoteAt: string | null;
}
// ---------- S18 schedule, S19 availability (scheduling) ----------

export type AppointmentStatus = 'confirmed' | 'cancelled_by_patient' | 'cancelled_by_therapist' | 'completed' | 'no_show';
/** SB-26 reasons a therapist picks; labels in i18n `schedule.reasons`. The patient never sees which one. */
export type TherapistCancelReason = 'emergency' | 'illness' | 'schedule_conflict' | 'technical' | 'other';
export interface StaffAppointment {
  id: string;
  pseudonymId: string;
  startsAt: string;
  durationMinutes: number;
  sequenceNumber: number;
  status: AppointmentStatus;
  riskLevel: RiskLevel;
  filePending: boolean;
  outcomeRecorded: boolean;
  canRecordOutcome: boolean;
  canCancel: boolean;
  canReportLate: boolean;
  therapistLateMinutes: number | null;
  cancelledBy: 'patient' | 'therapist' | 'care_ended' | null;
  cancelReasonCode: string | null;
  missed: boolean;
}
export interface Schedule { from: string; to: string; timeZone: string; items: StaffAppointment[] }

/** One of the therapist's own free times (GET /v1/staff/free-slots): S18 reschedule, S33 book the next session. */
export interface FreeSlot { slotId: string; startsAt: string; durationMinutes: number }

export type SlotStatus = 'open' | 'held' | 'booked' | 'blocked';
export interface AvailabilitySlot { slotId: string; startsAt: string; endsAt: string; status: SlotStatus; source: 'template' | 'manual' }
/** day: 0 Sunday … 6 Saturday; start and end "HH:mm" in the therapist's time zone. */
export interface WeeklyPeriod { day: number; start: string; end: string }
export interface AvailabilityBreak { id?: string | null; kind: 'leave' | 'absence'; startsAt: string; endsAt: string }
export interface Availability {
  therapistId: string;
  timeZone: string;
  sessionMinutes: number;
  sessionLengths: number[];
  bufferMinutes: number;
  bufferOptions: number[];
  weekly: WeeklyPeriod[];
  exceptions: AvailabilityBreak[];
  horizonDays: number;
  licenseVerified: boolean;
  updatedAt: string | null;
  slots: AvailabilitySlot[];
  /** Booked or held times the saved pattern no longer covers; never cancelled automatically (SB-28). */
  affected: AvailabilitySlot[];
}
export interface SaveAvailabilityRequest { sessionMinutes: number; bufferMinutes: number; weekly: WeeklyPeriod[]; exceptions: AvailabilityBreak[] }
export interface AvailabilityPreview { added: number; removed: number; affected: AvailabilitySlot[] }
export interface SaveAvailabilityResult extends AvailabilityPreview { availability: Availability }
// ---------- Phase 5: notifications, alerts, profile, account (S41, S20, S21, S32) ----------

export type NotificationCategory = 'risk' | 'appointments' | 'messages' | 'homework' | 'care' | 'security' | 'system';
/** S41 filter buttons; "system" is every category but risk, appointments and messages. */
export type InboxFilter = 'all' | 'risk' | 'appointments' | 'messages' | 'system';
/** `pinned`: a live, unacknowledged risk alert (id is the crisis event id); it is never "read", only acknowledged. */
export interface InboxItem { id: string; kind: string; category: NotificationCategory; text: string; link: string | null; createdAt: string; read: boolean; pinned: boolean; data: Record<string, string> }
export interface Inbox { items: InboxItem[]; unreadCount: number; nextCursor: string | null; counts: Record<InboxFilter, number> }
export interface UnreadCount { unreadCount: number }

export type AlertStatusFilter = 'open' | 'handled' | 'all';
export interface Alert {
  eventId: string;
  number: string;
  pseudonymId: string;
  level: RiskLevel;
  status: CrisisStatus;
  source: string;
  ruleIds: string[];
  openedAt: string;
  ackDeadline: string;
  acknowledgedBy: string | null;
  acknowledgedAt: string | null;
  closedAt: string | null;
  closeOutcome: string | null;
}
export type AlertHistoryKind = 'crisis' | 'risk_raised' | 'no_show';
/** One row of the S20 30-day history: ids, levels and codes only (never a clinician's typed reason). */
export interface AlertHistoryItem {
  kind: AlertHistoryKind;
  id: string;
  pseudonymId: string;
  level: RiskLevel;
  at: string;
  /** crisis · risk_raised · repeated, high_risk or missed (no_show). */
  detail: string;
  /** crisis: { number }; risk_raised: { from, to }. */
  params: Record<string, string>;
  /** crisis: the close outcome; risk_raised: raised; no_show: rebooked or not_rebooked. */
  final: string;
  by: string | null;
  eventId: string | null;
}
export interface Alerts { items: Alert[]; openCount: number; handledCount: number; history: AlertHistoryItem[] }

export interface StaffProfile {
  displayNameAr: string;
  displayNameEn: string;
  titleAr: string;
  titleEn: string;
  bioAr: string;
  bioEn: string;
  specialties: string[];
  languages: Lang[];
  gender: 'female' | 'male';
  sessionType: 'remote';
  sessionMinutes: number;
  capacity: number;
  currentPatients: number;
  acceptingNewPatients: boolean;
  licenseVerified: boolean;
  specialtyCatalog: string[];
  /** yyyy-MM-dd, set when the licence is verified. */
  licenseExpiresOn: string | null;
  /** Display name of the admin or lead who last changed the verification. */
  licenseVerifiedBy: string | null;
  licenseVerifiedAt: string | null;
  /** Verified and expiring within 30 days (or past): the S21 warning banner. */
  licenseExpiringSoon: boolean;
}
/** PUT /v1/staff/therapists/{userId}/license (admin or clinical lead). */
export interface LicenseVerification { userId: string; verified: boolean; expiresOn: string | null; verifiedBy: string | null; verifiedAt: string | null; expiringSoon: boolean }
export interface UpdateStaffProfileRequest {
  displayNameAr: string;
  displayNameEn: string;
  titleAr: string;
  titleEn: string;
  bioAr: string;
  bioEn: string;
  specialties: string[];
  languages: Lang[];
  capacity: number;
  acceptingNewPatients: boolean;
}

export interface ActiveSession { id: string; deviceLabel: string; current: boolean; lastActiveAt: string }
export interface StaffSecurity { passwordChangedAt: string; totpEnabled: boolean; recoveryCodesRemaining: number; idleTimeoutMinutes: number; sessions: ActiveSession[] }
export interface StepUpRequest { password: string; code: string }
export interface StaffPasswordRequest { currentPassword: string; newPassword: string; code: string }
export interface TotpMove { secret: string; otpauthUri: string; moveToken: string; expiresAt: string }
export interface StaffNotificationPrefs { riskAlerts: true; bookings: boolean; weeklyReport: boolean; dailyDigest: boolean }
// ---------- Phase 5: care record (S14, S15, S16, S33, S35, S57) ----------

export type HomeworkFrequency = 'daily' | 'twice_weekly' | 'weekly' | 'once';
export interface HomeworkTemplate { code: string; title: string; instructions: string[]; frequency: HomeworkFrequency; minutes: number | null }
export interface StaffCheckin {
  id: string;
  day: string;
  status: 'done' | 'partial' | 'skipped';
  feeling: number | null;
  note: string | null;
  /** Crisis gate level of the note; 3 or 4 shows the risk badge. */
  gateLevel: number | null;
  feedback: string | null;
  feedbackAt: string | null;
  feedbackSeen: boolean;
}
export interface StaffHomework {
  id: string;
  title: string;
  instructions: string[];
  minutes: number | null;
  frequency: HomeworkFrequency;
  startsOn: string;
  endsOn: string;
  status: 'active' | 'ended' | 'cancelled';
  templateCode: string | null;
  doneThisWeek: number;
  weeklyTarget: number;
  averageFeeling: number | null;
  latestCheckin: StaffCheckin | null;
  checkins: StaffCheckin[];
}
export interface StaffHomeworkList { pseudonymId: string; current: StaffHomework[]; past: StaffHomework[]; canEdit: boolean }
export interface AssignHomeworkRequest { title: string; instructions: string[]; minutes?: number | null; frequency: HomeworkFrequency; startsOn: string; endsOn: string; templateCode?: string | null }
export interface UpdateHomeworkRequest { title: string; instructions: string[]; minutes?: number | null; frequency: HomeworkFrequency; endsOn: string }

export type GoalStatus = 'not_started' | 'ongoing' | 'improving';
export type PlanFrequency = 'weekly' | 'biweekly' | 'monthly';
export interface PlanGoal { text: string; status: GoalStatus }
export interface PlanVersion { version: number; createdAt: string; authorName: string; goals: PlanGoal[]; interventions: string; frequency: PlanFrequency; reviewOn: string; followUpNotes: string }
export interface PlanHistory { pseudonymId: string; current: PlanVersion | null; versions: { version: number; createdAt: string; authorName: string }[]; fileApproved: boolean; canEdit: boolean }
export interface SavePlanRequest { goals: PlanGoal[]; interventions: string; frequency: PlanFrequency; reviewOn: string; followUpNotes: string }

export type NoteTemplate = 'soap' | 'dap' | 'free';
export interface NoteAddendum { id: string; text: string; authorName: string; at: string }
export interface SessionNote {
  id: string;
  template: NoteTemplate;
  fields: Record<string, string>;
  summaryPoints: string[];
  shareSummary: boolean;
  status: 'draft' | 'signed';
  revision: number;
  updatedAt: string;
  signedAt: string | null;
  signedBy: string | null;
  addenda: NoteAddendum[];
}
export interface SessionNoteView {
  pseudonymId: string;
  appointment: { id: string; startsAt: string; durationMinutes: number; sequenceNumber: number; status: string };
  note: SessionNote | null;
  lastWeek: { homeworkDone: number; homeworkTarget: number; moodAverage: number | null; alerts: number };
  canEdit: boolean;
  templates: Record<NoteTemplate, string[]>;
}
export interface SaveNoteRequest { template: NoteTemplate; fields: Record<string, string>; summaryPoints: string[]; shareSummary: boolean }

export type SafetyPlanSection = 'warningSigns' | 'copingAlone' | 'distractions' | 'peopleToAsk' | 'professionals' | 'safeEnvironment' | 'reasonToLive';
export interface PlanContact { name: string; phone: string }
export type AuthorRole = 'patient' | 'therapist' | 'supervisor' | 'lead' | 'admin';
export interface StaffSafetyPlan {
  pseudonymId: string;
  sections: Record<SafetyPlanSection, string>;
  contacts: PlanContact[];
  /** Shared with the patient's P37; also the ETag. */
  version: number;
  updatedAt: string | null;
  updatedBy: string | null;
  updatedByRole: AuthorRole | null;
  emergency: { emergencyNumber: string; supportLine: string; therapistName: string };
  canEdit: boolean;
}
export interface SafetyPlanVersionSummary { version: number; createdAt: string; authorRole: AuthorRole; authorName: string }
export interface SafetyPlanVersion extends SafetyPlanVersionSummary { sections: Record<SafetyPlanSection, string>; contacts: PlanContact[] }
export interface UpdateSafetyPlanRequest { sections: Partial<Record<SafetyPlanSection, string>>; contacts: PlanContact[] }

export type ClosureReason = 'goals_met' | 'patient_choice' | 'transferred' | 'no_contact' | 'other';
export type ClosureFollowUp = 'none' | 'phq9_1m_3m';
export interface Closure {
  id: string;
  status: 'draft' | 'submitted' | 'signed_off' | 'rejected';
  reasonCode: ClosureReason | '';
  sessionCount: number;
  durationWeeks: number;
  patientSummary: string;
  warningSigns: string;
  whatToDo: string;
  followUp: ClosureFollowUp;
  keepLibrary: boolean;
  submittedAt: string | null;
  rejectionReason: string | null;
  decidedBy: string | null;
  decidedAt: string | null;
  revision: number;
}
export interface ClosureView {
  pseudonymId: string;
  closure: Closure | null;
  prefill: { sessionCount: number; durationWeeks: number };
  measures: { instrument: string; start: number | null; end: number | null }[];
  /** Risk 3–4 in the last 14 days: sending and signing off are refused. */
  blocked: boolean;
  canEdit: boolean;
  canSignOff: boolean;
  therapistName: string;
}
export interface SaveClosureRequest { reasonCode: ClosureReason | ''; sessionCount: number; durationWeeks: number; patientSummary: string; warningSigns: string; whatToDo: string; followUp: ClosureFollowUp; keepLibrary: boolean; submit: boolean }
export interface ClosureQueueItem { id: string; pseudonymId: string; therapistName: string; reasonCode: ClosureReason; sessionCount: number; submittedAt: string; blocked: boolean }

export interface OverviewAppointment { id: string; startsAt: string; durationMinutes: number; sequenceNumber: number; status: string; noteStatus: 'none' | 'draft' | 'signed' }
export interface PatientOverview {
  pseudonymId: string;
  riskLevel: RiskLevel;
  crisisFloor: number;
  careStartedAt: string | null;
  inCare: boolean;
  closureStatus: Closure['status'] | null;
  fileApproved: boolean;
  phq9AtIntake: number | null;
  gad7AtIntake: number | null;
  nextAppointment: OverviewAppointment | null;
  pastAppointments: OverviewAppointment[];
  mood: { at: string; score: number }[];
  moodAverage7Days: number | null;
  plan: { version: number; createdAt: string; reviewOn: string; goals: PlanGoal[] } | null;
  homework: { id: string; title: string; frequency: HomeworkFrequency; doneThisWeek: number; weeklyTarget: number; averageFeeling: number | null }[];
  safetyPlan: { exists: boolean; version: number; updatedAt: string | null; updatedByRole: AuthorRole | null };
  alerts: { eventId: string; level: number; status: string; source: string; openedAt: string }[];
  canEdit: boolean;
}
