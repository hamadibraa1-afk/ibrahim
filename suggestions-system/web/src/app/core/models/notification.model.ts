export interface AppNotification {
  id: number;
  type: string;
  message: string;
  proposalId: number | null;
  proposalTitle: string | null;
  isRead: boolean;
  createdAt: string;
}

/** أيقونة ولون لكل نوع إشعار. */
export const NotificationStyles: Record<string, { icon: string; cls: string }> = {
  NewSubmission:       { icon: '📥', cls: 'bg-blue-50 text-blue-700' },
  ScreeningDecision:   { icon: '🔍', cls: 'bg-amber-50 text-amber-700' },
  AssignedToCommittee: { icon: '👥', cls: 'bg-indigo-50 text-indigo-700' },
  CommitteeSigned:     { icon: '✍️', cls: 'bg-violet-50 text-violet-700' },
  ExecutiveDecision:   { icon: '⚖️', cls: 'bg-emerald-50 text-emerald-700' },
  SlaBreach:           { icon: '⏰', cls: 'bg-red-50 text-red-700' },
  OwnerAssigned:       { icon: '🎯', cls: 'bg-teal-50 text-teal-700' },
  Escalation:          { icon: '🚨', cls: 'bg-orange-50 text-orange-700' },
  ImpactMeasurementDue:     { icon: '📏', cls: 'bg-cyan-50 text-cyan-700' },
  ImpactMeasurementOverdue: { icon: '⌛', cls: 'bg-red-50 text-red-700' },
  ImpactSubmitted:     { icon: '📊', cls: 'bg-indigo-50 text-indigo-700' },
  ImpactVerified:      { icon: '🏅', cls: 'bg-emerald-50 text-emerald-700' },
};

/** حدث الدفع اللحظي من الخادم (SignalR) عند وصول إشعار جديد. */
export interface NotificationPush { notification: AppNotification; unreadCount: number; }

/** حدث الدفع اللحظي عند انتقال مقترح في سير العمل — لا يحمل أي بيانات هوية. */
export interface ProposalChangedEvent { proposalId: number; status: string; }
