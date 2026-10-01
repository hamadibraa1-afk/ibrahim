import { Component, Input, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { AuditService } from '../services/audit.service';
import { AuditActionLabels, AuditActionStyles, AuditLog } from '../models/audit.model';

/** الخط الزمني لسجل إجراءات مقترح — يظهر داخل صفحة تفاصيل المقترح. */
@Component({
  selector: 'app-audit-timeline',
  standalone: true,
  imports: [CommonModule],
  template: `
    @if (loading()) {
      <div class="text-[12px] text-ink-faint py-3">جارِ تحميل السجل...</div>
    } @else if (!logs().length) {
      <div class="text-[12px] text-ink-faint py-3">لا توجد إجراءات مسجَّلة بعد.</div>
    } @else {
      <div class="relative">
        <!-- الخط الرأسي -->
        <div class="absolute right-[13px] top-2 bottom-2 w-px bg-[#E3E8E5]"></div>

        @for (log of logs(); track log.id) {
          <div class="relative flex gap-3.5 pb-4 last:pb-0">
            <div class="w-7 h-7 rounded-full flex items-center justify-center shrink-0 z-10 ring-4 ring-white"
                 [ngClass]="styleFor(log.action)">
              <div class="w-2 h-2 rounded-full bg-current"></div>
            </div>
            <div class="flex-1 min-w-0 pt-0.5">
              <div class="flex flex-wrap items-baseline gap-x-2">
                <span class="text-[12.5px] font-bold text-ink">{{ labelFor(log.action) }}</span>
                <span class="text-[11px] text-ink-faint">— {{ log.actorName }}</span>
              </div>
              @if (log.notes) {
                <div class="text-[11.5px] text-ink-soft mt-1 bg-[#F8FAF9] border border-[#EEF1EF] rounded-lg px-2.5 py-1.5">
                  {{ log.notes }}
                </div>
              }
              <div class="text-[10.5px] text-ink-faint mt-1">{{ log.createdAt | date:'d MMMM y — h:mm a' }}</div>
            </div>
          </div>
        }
      </div>
    }
  `,
})
export class AuditTimelineComponent implements OnInit {
  @Input({ required: true }) proposalId!: number;

  private service = inject(AuditService);
  logs = signal<AuditLog[]>([]);
  loading = signal(true);

  ngOnInit() {
    this.service.forProposal(this.proposalId).subscribe({
      next: d => { this.logs.set(d); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  labelFor(action: string) { return AuditActionLabels[action] ?? action; }
  styleFor(action: string) { return AuditActionStyles[action] ?? 'bg-gray-100 text-gray-600'; }
}
