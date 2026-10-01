import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { debounceTime } from 'rxjs';
import { ProposalService } from '../../core/services/proposal.service';
import { RealtimeService } from '../../core/services/realtime.service';
import { AuthService } from '../../core/services/auth.service';
import { UserRole } from '../../core/models/user.model';
import {
  ImpactAssessmentStatus, ImpactReport, ImpactStatusBadgeClass, ImpactStatusLabels, Proposal,
} from '../../core/models/proposal.model';

interface Tab { key: string; label: string; status?: ImpactAssessmentStatus; overdue?: boolean; }

/**
 * متابعة الأثر المؤسسي والعائد على الاستثمار للمقترحات المعتمدة.
 * مدير النظام يرى التقرير الكامل وكل المقترحات؛ مسؤول التنفيذ يرى ما أُسند إليه.
 */
@Component({
  selector: 'app-impact-tracking',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './impact-tracking.component.html',
})
export class ImpactTrackingComponent implements OnInit {
  private proposals = inject(ProposalService);
  private realtime = inject(RealtimeService);
  private destroyRef = inject(DestroyRef);
  private auth = inject(AuthService);

  readonly ImpactStatusLabels = ImpactStatusLabels;
  readonly ImpactStatusBadgeClass = ImpactStatusBadgeClass;
  readonly isAdmin = computed(() => this.auth.role() === UserRole.Admin);

  readonly tabs: Tab[] = [
    { key: 'all', label: 'الكل' },
    { key: 'open', label: 'بانتظار القياس', status: 'Open' },
    { key: 'overdue', label: 'متأخرة', overdue: true },
    { key: 'submitted', label: 'بانتظار الاعتماد', status: 'Submitted' },
    { key: 'verified', label: 'معتمدة', status: 'Verified' },
    { key: 'scheduled', label: 'مجدولة', status: 'Scheduled' },
  ];

  activeTab = signal('all');
  items = signal<Proposal[]>([]);
  report = signal<ImpactReport | null>(null);
  loading = signal(true);

  ngOnInit() {
    this.load();
    this.realtime.proposalChanges
      .pipe(debounceTime(500), takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.load(true));
  }

  selectTab(key: string) { this.activeTab.set(key); this.load(); }

  load(silent = false) {
    if (!silent) this.loading.set(true);
    const tab = this.tabs.find(t => t.key === this.activeTab()) ?? this.tabs[0];
    this.proposals.impactList({ status: tab.status, overdue: tab.overdue }).subscribe({
      next: list => { this.items.set(list); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
    if (this.isAdmin()) {
      this.proposals.impactReport().subscribe({ next: r => this.report.set(r), error: () => {} });
    }
  }

  maxDeptBenefit(): number {
    const list = this.report()?.byDepartment ?? [];
    return list.length ? Math.max(1, ...list.map(d => d.totalAnnualBenefit)) : 1;
  }
}
