import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { debounceTime } from 'rxjs';
import { RealtimeService } from '../../core/services/realtime.service';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { DashboardService } from '../../core/services/dashboard.service';
import { ProposalService } from '../../core/services/proposal.service';
import { AttachmentListComponent } from '../../core/components/attachment-list.component';
import {
  ClassificationLabels, DashboardStats, ImpactReport, Proposal,
  ProposalStatus, ProposalStatusBadgeClass, ProposalStatusLabels,
} from '../../core/models/proposal.model';

@Component({
  selector: 'app-admin-dashboard',
  standalone: true,
  imports: [CommonModule, RouterLink, AttachmentListComponent],
  templateUrl: './admin-dashboard.component.html',
})
export class AdminDashboardComponent implements OnInit {
  private dashboardService = inject(DashboardService);
  private proposalService = inject(ProposalService);
  private realtime = inject(RealtimeService);
  private destroyRef = inject(DestroyRef);

  readonly ProposalStatusLabels = ProposalStatusLabels;
  readonly ProposalStatusBadgeClass = ProposalStatusBadgeClass;
  readonly ClassificationLabels = ClassificationLabels;
  readonly ProposalStatus = ProposalStatus;

  stats = signal<DashboardStats | null>(null);
  impact = signal<ImpactReport | null>(null);
  loading = signal(true);
  selectedProposal = signal<Proposal | null>(null);

  ngOnInit() {
    this.load();
    this.realtime.proposalChanges
      .pipe(debounceTime(1000), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load(true));
  }

  load(silent = false) {
    if (!silent) this.loading.set(true);
    this.dashboardService.getStats().subscribe({
      next: s => { this.stats.set(s); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
    this.proposalService.impactReport().subscribe({ next: r => this.impact.set(r), error: () => {} });
  }

  maxDeptCount(): number {
    const list = this.stats()?.byDepartment ?? [];
    return list.length ? Math.max(...list.map(d => d.count)) : 1;
  }

  openDetails(p: Proposal) { this.selectedProposal.set(p); }
  closeDetails() { this.selectedProposal.set(null); }
  openForm(p: Proposal) { this.proposalService.openOfficialForm(p.id); }
  openCertificate(p: Proposal) { this.proposalService.openCertificate(p.id); }
}
