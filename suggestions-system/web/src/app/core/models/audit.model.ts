export interface AuditLog {
  id: number;
  proposalId: number;
  proposalCode: string;
  actorName: string;
  actorRole: string;
  action: string;
  fromStatus: string | null;
  toStatus: string | null;
  notes: string | null;
  createdAt: string;
}

export const AuditActionLabels: Record<string, string> = {
  Created: 'تقديم المقترح',
  Edited: 'تعديل المقترح',
  Resubmitted: 'إعادة إرسال بعد التعديل',
  Deleted: 'حذف المقترح',
  ScreenApproved: 'قبول الفرز وإحالة للجنة',
  ScreenReturned: 'إعادة للتعديل',
  ScreenRejected: 'رفض في الفرز',
  CommitteeStudySaved: 'حفظ دراسة اللجنة',
  CommitteeSigned: 'توقيع عضو لجنة',
  ExecutiveAccepted: 'موافقة الإدارة التنفيذية',
  ExecutiveRejected: 'رفض الإدارة التنفيذية',
  OwnerAssigned: 'تعيين مسؤول تنفيذ',
  AttachmentAdded: 'إضافة مرفق',
  AttachmentRemoved: 'حذف مرفق',
  Escalated: 'تصعيد بسبب التأخير',
  AutoEscalated: 'تصعيد تلقائي (تجاوز المهلة)',
  IdentityRevealed: 'كشف هوية مقدّم الطلب',
  ImpactScheduled: 'جدولة قياس الأثر الفعلي',
  ImpactWindowOpened: 'فتح نافذة قياس الأثر',
  ImpactMeasured: 'تسجيل قياس الأثر الفعلي',
  ImpactVerified: 'اعتماد قياس الأثر',
  ImpactReturned: 'إعادة قياس الأثر للتصحيح',
  CommitteeSeatReleased: 'سحب مقعد عضو لجنة غير متاح',
};

export const AuditActionStyles: Record<string, string> = {
  Created: 'bg-blue-50 text-blue-700',
  Edited: 'bg-gray-100 text-gray-700',
  Resubmitted: 'bg-indigo-50 text-indigo-700',
  Deleted: 'bg-red-50 text-red-700',
  ScreenApproved: 'bg-emerald-50 text-emerald-700',
  ScreenReturned: 'bg-amber-50 text-amber-700',
  ScreenRejected: 'bg-red-50 text-red-700',
  CommitteeStudySaved: 'bg-violet-50 text-violet-700',
  CommitteeSigned: 'bg-violet-50 text-violet-700',
  ExecutiveAccepted: 'bg-emerald-50 text-emerald-700',
  ExecutiveRejected: 'bg-red-50 text-red-700',
  OwnerAssigned: 'bg-teal-50 text-teal-700',
  Escalated: 'bg-orange-50 text-orange-700',
  AutoEscalated: 'bg-orange-50 text-orange-700',
  IdentityRevealed: 'bg-sky-50 text-sky-700',
  ImpactScheduled: 'bg-cyan-50 text-cyan-700',
  ImpactWindowOpened: 'bg-cyan-50 text-cyan-700',
  ImpactMeasured: 'bg-indigo-50 text-indigo-700',
  ImpactVerified: 'bg-emerald-50 text-emerald-700',
  ImpactReturned: 'bg-amber-50 text-amber-700',
  CommitteeSeatReleased: 'bg-gray-100 text-gray-700',
};
