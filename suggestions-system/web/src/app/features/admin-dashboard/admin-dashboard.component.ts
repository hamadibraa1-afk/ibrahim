import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { DashboardService } from '../../core/services/dashboard.service';
import { ProposalService } from '../../core/services/proposal.service';
import { AttachmentListComponent } from '../../core/components/attachment-list.component';
import {
  ClassificationLabels, DashboardStats, Proposal,
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

  readonly ProposalStatusLabels = ProposalStatusLabels;
  readonly ProposalStatusBadgeClass = ProposalStatusBadgeClass;
  readonly ClassificationLabels = ClassificationLabels;
  readonly ProposalStatus = ProposalStatus;

  stats = signal<DashboardStats | null>(null);
  loading = signal(true);
  selectedProposal = signal<Proposal | null>(null);

  ngOnInit() { this.load(); }

  load() {
    this.loading.set(true);
    this.dashboardService.getStats().subscribe({
      next: s => { this.stats.set(s); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
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
