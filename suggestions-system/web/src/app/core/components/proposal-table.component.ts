import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { StatusBadgeComponent } from './status-badge.component';
import { ClassificationLabels, Proposal, ProposalStatus } from '../models/proposal.model';

/** جدول موحّد لعرض المقترحات في كل القوائم، مع زر فتح لصفحة التفاصيل. */
@Component({
  selector: 'app-proposal-table',
  standalone: true,
  imports: [CommonModule, RouterLink, StatusBadgeComponent],
  templateUrl: './proposal-table.component.html',
})
export class ProposalTableComponent {
  @Input({ required: true }) proposals: Proposal[] = [];
  @Input() loading = false;
  @Input() emptyMessage = 'لا توجد مقترحات مطابقة.';

  /** إخفاء اسم مقدّم الطلب (واجهة الفرز). */
  @Input() hideSubmitter = false;
  @Input() showClassification = false;
  @Input() showSla = false;
  @Input() showDecision = false;
  /** نص زر الإجراء الأساسي. */
  @Input() actionLabel = 'عرض';

  readonly ClassificationLabels = ClassificationLabels;
  readonly ProposalStatus = ProposalStatus;

  hoursLeft(p: Proposal): number {
    return Math.round((new Date(p.slaDueAt).getTime() - Date.now()) / 3_600_000);
  }

  isOverdue(p: Proposal): boolean {
    return this.hoursLeft(p) < 0
      && p.status !== ProposalStatus.Accepted && p.status !== ProposalStatus.Rejected;
  }

  signedCount(p: Proposal): number { return p.committeeVotes.filter(v => v.signed).length; }
}
