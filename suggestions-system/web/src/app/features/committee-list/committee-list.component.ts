import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ProposalService } from '../../core/services/proposal.service';
import { ProposalFiltersComponent } from '../../core/components/proposal-filters.component';
import { ProposalTableComponent } from '../../core/components/proposal-table.component';
import { Proposal, ProposalQuery, ProposalStatus } from '../../core/models/proposal.model';

interface Tab { key: string; label: string; query: ProposalQuery; }

@Component({
  selector: 'app-committee-list',
  standalone: true,
  imports: [CommonModule, ProposalFiltersComponent, ProposalTableComponent],
  templateUrl: './committee-list.component.html',
})
export class CommitteeListComponent implements OnInit {
  private proposalService = inject(ProposalService);

  readonly ProposalStatus = ProposalStatus;
  readonly tabs: Tab[] = [
    { key: 'pending',  label: 'بانتظار الدراسة', query: { forCommittee: true } },
    { key: 'signed',   label: 'مكتملة التوقيع',  query: { status: 'PendingExecutiveDecision' } },
    { key: 'decided',  label: 'صدر قرارها',      query: { decided: true } },
    { key: 'all',      label: 'كل المقترحات',    query: {} },
  ];

  activeTab = signal(this.tabs[0].key);
  proposals = signal<Proposal[]>([]);
  loading = signal(true);
  private filters: ProposalQuery = {};
  counts = signal<Record<string, number>>({});

  ngOnInit() { this.load(); this.loadCounts(); }

  get currentTab(): Tab {
    return this.tabs.find(t => t.key === this.activeTab()) ?? this.tabs[0];
  }

  selectTab(key: string) {
    this.activeTab.set(key);
    this.load();
  }

  onFilters(f: ProposalQuery) { this.filters = f; this.load(); }

  load() {
    this.loading.set(true);
    this.proposalService.getAll({ ...this.currentTab.query, ...this.filters }).subscribe({
      next: d => { this.proposals.set(d); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  /** أعداد كل تبويب — تُحمّل مرة واحدة بدون فلاتر البحث. */
  loadCounts() {
    this.tabs.forEach(t => {
      this.proposalService.getAll(t.query).subscribe({
        next: d => this.counts.update(c => ({ ...c, [t.key]: d.length })),
        error: () => {},
      });
    });
  }

}
