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
};
