import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime } from 'rxjs';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ProposalService } from '../../core/services/proposal.service';
import { AttachmentListComponent } from '../../core/components/attachment-list.component';
import { ProposalTableComponent } from '../../core/components/proposal-table.component';
import { RealtimeService } from '../../core/services/realtime.service';
import {
  ClassificationLabels, selectedImpacts, Proposal, ProposalStatus,
  ProposalStatusBadgeClass, ProposalStatusLabels,
} from '../../core/models/proposal.model';

const STEPS: { key: ProposalStatus[]; label: string }[] = [
  { key: [ProposalStatus.Submitted], label: 'تقديم' },
  { key: [ProposalStatus.UnderScreening, ProposalStatus.ReturnedForEdit], label: 'فرز' },
  { key: [ProposalStatus.WithCommittee], label: 'لجنة' },
  { key: [ProposalStatus.PendingExecutiveDecision], label: 'تنفيذي' },
  { key: [ProposalStatus.Accepted, ProposalStatus.Rejected], label: 'قرار' },
  // مرحلة ما بعد الاعتماد: قياس الأثر الفعلي بعد 3–6 أشهر
  { key: [], label: 'الأثر' },
];
const IMPACT_STEP = 5;

@Component({
  selector: 'app-my-proposals',
  standalone: true,
  imports: [CommonModule, RouterLink, AttachmentListComponent, ProposalTableComponent],
  templateUrl: './my-proposals.component.html',
})
export class MyProposalsComponent implements OnInit {
  private proposalService = inject(ProposalService);
  private realtime = inject(RealtimeService);
  private destroyRef = inject(DestroyRef);

  readonly ProposalStatusLabels = ProposalStatusLabels;
  readonly ProposalStatusBadgeClass = ProposalStatusBadgeClass;
  readonly ProposalStatus = ProposalStatus;
  readonly ClassificationLabels = ClassificationLabels;
  readonly steps = STEPS;

  proposals = signal<Proposal[]>([]);
  /** مقترحات أُحيلت إليّ: تنفيذ وقياس أثر، أو إعادة توجيه بعد تصعيد. */
  assigned = signal<Proposal[]>([]);
  loading = signal(true);
  expandedId = signal<number | null>(null);

  get stats() {
    const list = this.proposals();
    const accepted = list.filter(p => p.status === ProposalStatus.Accepted).length;
    const rejected = list.filter(p => p.status === ProposalStatus.Rejected).length;
    return { total: list.length, accepted, rejected, inProgress: list.length - accepted - rejected };
  }

  ngOnInit() {
    this.load();
    // تتحدّث حالة مقترحاتي لحظياً عند كل قرار (فرز، لجنة، قرار تنفيذي، قياس أثر)
    this.realtime.proposalChanges
      .pipe(debounceTime(400), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load(true));
  }

  load(silent = false) {
    if (!silent) this.loading.set(true);
    this.proposalService.getAll({ mine: true }).subscribe({
      next: data => { this.proposals.set(data); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
    this.proposalService.getAll({ assignedToMe: true }).subscribe({
      next: data => this.assigned.set(data),
      error: () => {},
    });
  }

  toggleExpand(p: Proposal) {
    this.expandedId.update(id => id === p.id ? null : p.id);
  }

  stepState(p: Proposal, i: number): 'done' | 'now' | 'pending' | 'rejected' {
    if (i === IMPACT_STEP) {
      if (p.status !== ProposalStatus.Accepted) return 'pending';
      return p.impact?.status === 'Verified' ? 'done' : 'now';
    }
    if (p.status === ProposalStatus.Accepted && i === IMPACT_STEP - 1) return 'done';
    const current = this.steps.findIndex(s => s.key.includes(p.status));
    if (p.status === ProposalStatus.Rejected && i === 4) return 'rejected';
    if (i < current) return 'done';
    if (i === current) return 'now';
    return 'pending';
  }

  activeImpacts(p: Proposal): string[] { return selectedImpacts(p); }

  /**
   * صنف واحد لكل حالة. كائن ngClass بمفاتيح تتشارك "text-white" كان يحذف الصنف المشترك
   * عند معالجة المفاتيح غير المطابقة، فتظهر علامة ✓ داكنة على خلفية داكنة.
   */
  stepClass(p: Proposal, i: number): string {
    switch (this.stepState(p, i)) {
      case 'done': return 'bg-brand-600 text-white';
      case 'now': return 'bg-sun-400 text-white';
      case 'rejected': return 'bg-red-500 text-white';
      default: return 'bg-[#E5E9E7] text-ink-faint';
    }
  }

  canDelete(p: Proposal): boolean { return p.status === ProposalStatus.Submitted; }

  deleteProposal(p: Proposal) {
    if (!this.canDelete(p)) return;
    if (!confirm(`هل تريد حذف المقترح "${p.title}"؟`)) return;
    this.proposalService.delete(p.id).subscribe(() => this.load());
  }

  openForm(p: Proposal) { this.proposalService.openOfficialForm(p.id); }
  openCertificate(p: Proposal) { this.proposalService.openCertificate(p.id); }
}
