import { DecimalPipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { Api } from '../../core/api';
import { TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Item { label: string; amount: number; isDeduction: boolean; }
interface Payslip { year: number; month: number; earnings: number; deductions: number; netPay: number; status: string; items: Item[]; }

@Component({
  selector: 'app-my-payslips',
  standalone: true,
  imports: [TPipe, DecimalPipe],
  template: `
    <h1 class="mb-4">{{ 'my.payslips' | t }}</h1>
    @for (p of items(); track p.year + '-' + p.month) {
      <section class="card mb-3 overflow-hidden">
        <header class="flex items-center gap-2 border-b border-line px-4 py-3">
          <span class="flex-1 font-semibold tabular" dir="ltr">{{ p.year }}-{{ p.month }}</span>
          <span class="badge" [class.green]="p.status === 'Closed'">{{ 'hr.pay.status.' + p.status | t }}</span>
          <span class="text-lg font-bold tabular">{{ p.netPay | number:'1.2-2' }}</span>
        </header>
        <div class="divide-y divide-line px-4">
          @for (i of p.items; track $index) {
            <div class="flex items-center gap-3 py-2 text-sm">
              <span class="flex-1">{{ i.label }}</span>
              <span class="tabular" [class.text-bad]="i.isDeduction">{{ i.isDeduction ? '−' : '+' }} {{ i.amount | number:'1.2-2' }}</span>
            </div>
          }
        </div>
      </section>
    } @empty { <div class="card-pad muted">{{ 'my.noPayslips' | t }}</div> }`,
})
export class MyPayslipsPage implements OnInit {
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly items = signal<Payslip[]>([]);

  async ngOnInit(): Promise<void> {
    try { this.items.set(await this.api.get<Payslip[]>('my/payslips')); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
