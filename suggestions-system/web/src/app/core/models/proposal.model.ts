export enum ProposalStatus {
  Submitted = 'Submitted',
  UnderScreening = 'UnderScreening',
  ReturnedForEdit = 'ReturnedForEdit',
  WithCommittee = 'WithCommittee',
  PendingExecutiveDecision = 'PendingExecutiveDecision',
  Accepted = 'Accepted',
  Rejected = 'Rejected',
}

export const ProposalStatusLabels: Record<ProposalStatus, string> = {
  [ProposalStatus.Submitted]: 'قيد المراجعة',
  [ProposalStatus.UnderScreening]: 'جاري الفرز',
  [ProposalStatus.ReturnedForEdit]: 'أُعيد للتعديل',
  [ProposalStatus.WithCommittee]: 'لدى اللجنة',
  [ProposalStatus.PendingExecutiveDecision]: 'بانتظار القرار التنفيذي',
  [ProposalStatus.Accepted]: 'مقبول',
  [ProposalStatus.Rejected]: 'مرفوض',
};

export const ProposalStatusBadgeClass: Record<ProposalStatus, string> = {
  [ProposalStatus.Submitted]: 'badge-gray',
  [ProposalStatus.UnderScreening]: 'badge-amber',
  [ProposalStatus.ReturnedForEdit]: 'badge-amber',
  [ProposalStatus.WithCommittee]: 'badge-blue',
  [ProposalStatus.PendingExecutiveDecision]: 'badge-purple',
  [ProposalStatus.Accepted]: 'badge-green',
  [ProposalStatus.Rejected]: 'badge-red',
};

export enum ProposalClassification {
  Excellent = 'Excellent',
  VeryGood = 'VeryGood',
  Good = 'Good',
  Acceptable = 'Acceptable',
  NotApplicable = 'NotApplicable',
}

export const ClassificationLabels: Record<ProposalClassification, string> = {
  [ProposalClassification.Excellent]: 'متميز',
  [ProposalClassification.VeryGood]: 'جيد جداً',
  [ProposalClassification.Good]: 'جيد',
  [ProposalClassification.Acceptable]: 'مقبول',
  [ProposalClassification.NotApplicable]: 'غير قابل للتطبيق',
};

export enum NotApplicableReason {
  NotFeasible = 'NotFeasible',
  OutOfScope = 'OutOfScope',
  ViolatesPolicy = 'ViolatesPolicy',
}

export const NotApplicableReasonLabels: Record<NotApplicableReason, string> = {
  [NotApplicableReason.NotFeasible]: 'غير مجدي',
  [NotApplicableReason.OutOfScope]: 'خارج نطاق الاختصاص',
  [NotApplicableReason.ViolatesPolicy]: 'مخالف للوائح والسياسات',
};

export enum ExecutiveDecisionValue {
  Accepted = 'Accepted',
  Rejected = 'Rejected',
}

export interface Attachment {
  id: number;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  uploadedAt: string;
}

export function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} بايت`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(0)} كيلوبايت`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} ميجابايت`;
}

export interface CommitteeVoteDto {
  id: number;
  memberId: number;
  memberName: string;
  signed: boolean;
  signedAt: string | null;
}

/** ما يحق للمستخدم الحالي فعله بالمقترح الآن — يحسبه الخادم، والواجهة تعرضه فقط. */
export interface ProposalActions {
  canEdit: boolean;
  canDelete: boolean;
  canScreen: boolean;
  canStudy: boolean;
  canSign: boolean;
  canDecide: boolean;
  canAssignOwner: boolean;
  canEscalate: boolean;
  canMeasureImpact: boolean;
  canVerifyImpact: boolean;
}

export type ImpactAssessmentStatus = 'Scheduled' | 'Open' | 'Submitted' | 'Verified';

export const ImpactStatusLabels: Record<ImpactAssessmentStatus, string> = {
  Scheduled: 'مجدول — لم تُفتح نافذة القياس',
  Open: 'بانتظار القياس',
  Submitted: 'بانتظار اعتماد القياس',
  Verified: 'معتمد',
};

export const ImpactStatusBadgeClass: Record<ImpactAssessmentStatus, string> = {
  Scheduled: 'badge-gray',
  Open: 'badge-blue',
  Submitted: 'badge-purple',
  Verified: 'badge-green',
};

/** قياس الأثر الفعلي بعد التطبيق (3 إلى 6 أشهر من الاعتماد). */
export interface ImpactAssessment {
  id: number;
  status: ImpactAssessmentStatus;
  opensAt: string;
  dueAt: string;
  isOverdue: boolean;
  actualAnnualSavings: number | null;
  actualAnnualRevenue: number | null;
  implementationCost: number | null;
  hoursSavedPerMonth: number | null;
  beneficiariesReached: number | null;
  satisfactionBefore: number | null;
  satisfactionAfter: number | null;
  targetAchievementPercent: number | null;
  impactRating: number | null;
  summary: string | null;
  evidenceReference: string | null;
  totalAnnualBenefit: number | null;
  roiPercent: number | null;
  measuredAt: string | null;
  measuredByName: string | null;
  verifiedAt: string | null;
  verifiedByName: string | null;
  reviewNotes: string | null;
  customFields: ProposalFieldValue[];
}

export interface MeasureImpactRequest {
  actualAnnualSavings: number | null;
  actualAnnualRevenue: number | null;
  implementationCost: number | null;
  hoursSavedPerMonth: number | null;
  beneficiariesReached: number | null;
  satisfactionBefore: number | null;
  satisfactionAfter: number | null;
  targetAchievementPercent: number | null;
  impactRating: number | null;
  summary: string | null;
  evidenceReference: string | null;
  customFields?: Record<string, string>;
}

export interface ImpactReport {
  approvedProposals: number;
  scheduled: number;
  open: number;
  overdue: number;
  awaitingVerification: number;
  verified: number;
  totalAnnualSavings: number;
  totalAnnualRevenue: number;
  totalImplementationCost: number;
  totalAnnualBenefit: number;
  portfolioRoiPercent: number | null;
  totalHoursSavedPerMonth: number;
  totalBeneficiaries: number;
  averageTargetAchievement: number | null;
  averageImpactRating: number | null;
  byDepartment: { department: string; verifiedCount: number; totalAnnualBenefit: number; totalImplementationCost: number; roiPercent: number | null }[];
  topByRoi: { proposalId: number; proposalCode: string; title: string; department: string; totalAnnualBenefit: number | null; roiPercent: number | null; impactRating: number | null }[];
}

/** سياسة المراجعة المعمّاة كما يطبّقها الخادم. */
export interface BlindReviewPolicy {
  stage: 'CommitteeReferral' | 'CommitteeApproval' | 'ExecutiveApproval';
  revealOnStatus: ProposalStatus;
  description: string;
}

export interface Proposal {
  id: number;
  proposalCode: string;
  title: string;
  implementationMechanism: string;
  submissionReasons: string;
  department: string;
  /** صفر عندما تكون الهوية محجوبة عن المستخدم الحالي. */
  submitterId: number;
  submitterName: string;
  /** المراجعة معمّاة: الاسم وبيانات التواصل محجوبة عن هذا المستخدم حتى مرحلة الكشف. */
  submitterHidden: boolean;
  identityRevealedAt: string | null;
  status: ProposalStatus;
  screenerNotes: string | null;
  rejectionReason: string | null;
  classification: ProposalClassification | null;
  notApplicableReasonValue: NotApplicableReason | null;
  committeeStudy: string | null;
  committeeRecommendation: string | null;
  executiveDecision: ExecutiveDecisionValue | null;
  executiveDecisionNotes: string | null;
  executiveDecisionByName: string | null;
  executiveDecisionAt: string | null;
  ownerId: number | null;
  ownerName: string | null;
  attachments: Attachment[];
  stageEnteredAt: string;
  slaDueAt: string;
  submittedAt: string;
  updatedAt: string;
  committeeVotes: CommitteeVoteDto[];
  customFields: ProposalFieldValue[];
  resubmitCount: number;
  lastResubmittedAt: string | null;
  escalatedAt: string | null;
  escalationNote: string | null;
  /** 0 لا تصعيد، 1 المدير المباشر، 2 الإدارة التنفيذية. */
  escalationLevel: number;
  /** المدير الذي أُعيد توجيه المقترح إليه تلقائياً لاتخاذ الإجراء. */
  escalatedToId: number | null;
  escalatedToName: string | null;
  impact: ImpactAssessment | null;
  actions: ProposalActions;
}

export interface CreateProposalRequest {
  title: string;
  implementationMechanism: string;
  submissionReasons: string;
  /** قيم الحقول التي أضافها مدير النظام من الإعدادات — المفتاح هو fieldKey. */
  customFields?: Record<string, string>;
}

/** فلاتر البحث المتاحة على قوائم المقترحات. */
export interface ProposalQuery {
  mine?: boolean;
  /** مقترحات أُحيلت إليّ: مسؤول تنفيذ أو مدير صُعِّد إليه المقترح. */
  assignedToMe?: boolean;
  escalated?: boolean;
  forScreening?: boolean;
  forCommittee?: boolean;
  forExecutiveDecision?: boolean;
  decided?: boolean;
  status?: string;
  search?: string;
  department?: string;
  classification?: string;
  overdue?: boolean;
  from?: string;
  to?: string;
  sort?: 'newest' | 'oldest' | 'dueSoon' | 'title';
}

export interface UpdateProposalRequest extends CreateProposalRequest {}

export interface ProposalFieldValue {
  fieldId: number;
  fieldKey: string;
  labelAr: string;
  fieldType: string;
  /** SuggestionData | SuggestionDetails | Impact */
  section: string;
  value: string | null;
}

/** يستخرج أسماء عناصر "أثر التطبيق" المحدَّدة في مقترح. */
export function selectedImpacts(p: Proposal): string[] {
  return p.customFields
    .filter(f => f.section === 'Impact' && String(f.value).toLowerCase() === 'true')
    .map(f => f.labelAr);
}

export interface ScreenDecisionRequest { action: 'Approve' | 'ReturnForEdit' | 'Reject'; notes?: string; }
export interface CommitteeStudyRequest {
  classification: ProposalClassification;
  notApplicableReasonValue?: NotApplicableReason | null;
  committeeStudy: string;
  committeeRecommendation: string;
}
export interface ExecutiveDecisionRequest { decision: 'Accepted' | 'Rejected'; notes?: string; }
export interface ReviewImpactRequest { approve: boolean; notes?: string; }
export interface AssignOwnerRequest { ownerId: number; }

export interface PendingByStage { screening: number; committee: number; executiveDecision: number; }

export interface DashboardStats {
  totalProposals: number;
  acceptanceRate: number;
  averageProcessingDays: number;
  slaBreaches: number;
  escalated: number;
  pendingByStage: PendingByStage;
  byDepartment: { department: string; count: number }[];
  recentDecisions: Proposal[];
}
