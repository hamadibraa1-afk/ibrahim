import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { ProposalService } from '../../core/services/proposal.service';
import { AttachmentListComponent } from '../../core/components/attachment-list.component';
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
];

@Component({
  selector: 'app-my-proposals',
  standalone: true,
  imports: [CommonModule, RouterLink, AttachmentListComponent],
  templateUrl: './my-proposals.component.html',
})
export class MyProposalsComponent implements OnInit {
  private proposalService = inject(ProposalService);

  readonly ProposalStatusLabels = ProposalStatusLabels;
  readonly ProposalStatusBadgeClass = ProposalStatusBadgeClass;
  readonly ProposalStatus = ProposalStatus;
  readonly ClassificationLabels = ClassificationLabels;
  readonly steps = STEPS;

  proposals = signal<Proposal[]>([]);
  loading = signal(true);
  expandedId = signal<number | null>(null);

  get stats() {
    const list = this.proposals();
    const accepted = list.filter(p => p.status === ProposalStatus.Accepted).length;
    const rejected = list.filter(p => p.status === ProposalStatus.Rejected).length;
    return { total: list.length, accepted, rejected, inProgress: list.length - accepted - rejected };
  }

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    this.proposalService.getAll({ mine: true }).subscribe({
      next: data => { this.proposals.set(data); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  toggleExpand(p: Proposal) {
    this.expandedId.update(id => id === p.id ? null : p.id);
  }

  stepState(p: Proposal, i: number): 'done' | 'now' | 'pending' | 'rejected' {
    const current = this.steps.findIndex(s => s.key.includes(p.status));
    if (p.status === ProposalStatus.Rejected && i === 4) return 'rejected';
    if (i < current) return 'done';
    if (i === current) return 'now';
    return 'pending';
  }

  activeImpacts(p: Proposal): string[] { return selectedImpacts(p); }

  canDelete(p: Proposal): boolean { return p.status === ProposalStatus.Submitted; }

  deleteProposal(p: Proposal) {
    if (!this.canDelete(p)) return;
    if (!confirm(`هل تريد حذف المقترح "${p.title}"؟`)) return;
    this.proposalService.delete(p.id).subscribe(() => this.load());
  }

  openForm(p: Proposal) { this.proposalService.openOfficialForm(p.id); }
  openCertificate(p: Proposal) { this.proposalService.openCertificate(p.id); }
}
