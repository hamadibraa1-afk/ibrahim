import { Component, OnInit, inject, signal } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { Backdrop } from '../../core/backdrop';
import { hmin, uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Cycle { id: string; year: number; month: number; status: string; monthDays: number; maxDeductionPercent: number; calculatedAt: string | null; approvedAt: string | null; closedAt: string | null; lines: number; totalNet: number; }
interface Item { label: string; amount: number; isDeduction: boolean; }
interface Payslip { id: string; employeeName: string; employeeNumber: string | null; departmentName: string | null; basicSalary: number; scheduledDays: number; presentDays: number; absentDays: number; leaveDays: number; unpaidLeaveDays: number; lateMinutes: number; overtimeMinutes: number; earnings: number; deductions: number; cappedDeductions: number; netPay: number; items: Item[]; }
interface Blocker { code: string; count: number; }

/** A payroll month: open, calculate, settle what is pending, approve, close, export. */
@Component({
  selector: 'app-hr-payroll',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop, DecimalPipe],
  template: `
    <div class="toolbar"><h1 class="m-0">{{ 'hr.nav.payroll' | t }}</h1><span class="spacer"></span>
      @if (auth.canManageHr()) {
        <input type="number" class="w-24" min="2020" max="2100" [(ngModel)]="year">
        <input type="number" class="w-20" min="1" max="12" [(ngModel)]="month">
        <button class="btn primary" [disabled]="busy()" (click)="open()">{{ 'hr.pay.open' | t }}</button>
      }
    </div>

    <div class="grid gap-3 mb-6 [grid-template-columns:repeat(auto-fill,minmax(15rem,1fr))]">
      @for (c of cycles(); track c.id) {
        <button class="card p-4 text-start transition-all hover:-translate-y-0.5 hover:shadow-lift"
                [class.border-brand]="selected()?.id === c.id" (click)="select(c)">
          <div class="flex items-center gap-2">
            <span class="text-lg font-bold tabular" dir="ltr">{{ c.year }}-{{ c.month | number:'2.0-0' }}</span>
            <span class="badge" [class.yellow]="c.status === 'Draft' || c.status === 'Review'"
                  [class.green]="c.status === 'Approved'" [class.blue]="c.status === 'Closed'">{{ 'hr.pay.status.' + c.status | t }}</span>
          </div>
          <div class="mt-2 text-xs text-muted">{{ 'hr.pay.employees' | t }}: {{ c.lines }}</div>
          <div class="text-sm font-semibold tabular">{{ 'hr.pay.net' | t }}: {{ c.totalNet | number:'1.2-2' }}</div>
        </button>
      } @empty { <div class="card-pad muted">{{ 'hr.pay.none' | t }}</div> }
    </div>

    @if (selected(); as c) {
      <div class="card-pad mb-4">
        <div class="flex flex-wrap items-center gap-2">
          <h2 class="flex-1">{{ 'hr.pay.cycle' | t }} <span dir="ltr">{{ c.year }}-{{ c.month | number:'2.0-0' }}</span></h2>
          @if (auth.canManageHr()) {
            @if (c.status === 'Draft' || c.status === 'Review') {
              <button class="btn" [disabled]="busy()" (click)="act('calculate')">{{ 'hr.pay.calculate' | t }}</button>
              <button class="btn primary" [disabled]="busy() || !c.calculatedAt" (click)="act('approve')">{{ 'hr.pay.approve' | t }}</button>
            }
            @if (c.status === 'Approved') { <button class="btn primary" [disabled]="busy()" (click)="act('close')">{{ 'hr.pay.close' | t }}</button> }
            @if (c.status === 'Closed' && auth.canAdmin()) { <button class="btn danger" (click)="act('reopen')">{{ 'hr.pay.reopen' | t }}</button> }
          }
          <button class="btn" [disabled]="!payslips().length" (click)="exportCsv(c)">{{ 'common.export' | t }}</button>
        </div>

        @if (blockers().length) {
          <div class="alert mt-3">
            <strong>{{ 'hr.pay.blockers' | t }}:</strong>
            @for (b of blockers(); track b.code) { <div class="small">{{ ('err.' + b.code) | t }} ({{ b.count }})</div> }
          </div>
        }
        <p class="mt-3 text-xs text-muted">{{ 'hr.pay.lead' | t }}</p>
      </div>

      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'emp.number' | t }}</th><th>{{ 'att.employee' | t }}</th><th>{{ 'hr.org.department' | t }}</th>
          <th>{{ 'hr.emp.salary' | t }}</th><th>{{ 'hr.pay.days' | t }}</th><th>{{ 'dash.absent' | t }}</th>
          <th>{{ 'att.late' | t }}</th><th>{{ 'hr.pay.earnings' | t }}</th><th>{{ 'hr.pay.deductions' | t }}</th>
          <th>{{ 'hr.pay.net' | t }}</th><th></th></tr></thead>
        <tbody>
          @for (p of payslips(); track p.id) {
            <tr><td dir="ltr" class="tabular">{{ p.employeeNumber }}</td><td>{{ p.employeeName }}</td><td>{{ p.departmentName }}</td>
              <td class="tabular">{{ p.basicSalary | number:'1.2-2' }}</td>
              <td class="tabular">{{ p.presentDays }}/{{ p.scheduledDays }}</td>
              <td class="tabular" [class.text-bad]="p.absentDays > 0">{{ p.absentDays }}</td>
              <td class="tabular">{{ hmin(p.lateMinutes) }}</td>
              <td class="tabular">{{ p.earnings | number:'1.2-2' }}</td>
              <td class="tabular" [class.text-bad]="p.cappedDeductions > 0">{{ p.cappedDeductions | number:'1.2-2' }}</td>
              <td class="tabular font-semibold">{{ p.netPay | number:'1.2-2' }}</td>
              <td><button class="btn sm" (click)="slip.set(p)">{{ 'hr.pay.slip' | t }}</button></td></tr>
          } @empty { <tr><td colspan="11" class="muted">{{ 'hr.pay.notCalculated' | t }}</td></tr> }
        </tbody>
        @if (payslips().length) {
          <tfoot><tr><td colspan="7">{{ 'common.total' | t }}</td>
            <td class="tabular">{{ total('earnings') | number:'1.2-2' }}</td>
            <td class="tabular">{{ total('cappedDeductions') | number:'1.2-2' }}</td>
            <td class="tabular">{{ total('netPay') | number:'1.2-2' }}</td><td></td></tr></tfoot>
        }
      </table></div>
    }

    @if (slip(); as p) {
      <div class="modal-back" appBackdrop (dismiss)="slip.set(null)"><div class="modal">
        <div class="modal-head"><h2>{{ 'hr.pay.slip' | t }} — {{ p.employeeName }}</h2>
          <button class="btn sm" (click)="slip.set(null)">{{ 'common.close' | t }}</button></div>
        <div class="divide-y divide-line">
          @for (i of p.items; track $index) {
            <div class="flex items-center gap-3 py-2 text-sm">
              <span class="flex-1">{{ i.label }}</span>
              <span class="tabular" [class.text-bad]="i.isDeduction">{{ i.isDeduction ? '−' : '+' }} {{ i.amount | number:'1.2-2' }}</span>
            </div>
          }
        </div>
        <div class="mt-4 rounded-xl bg-raised p-4">
          <div class="flex justify-between text-sm"><span>{{ 'hr.pay.earnings' | t }}</span><span class="tabular">{{ p.earnings | number:'1.2-2' }}</span></div>
          <div class="flex justify-between text-sm"><span>{{ 'hr.pay.deductions' | t }}</span><span class="tabular text-bad">{{ p.cappedDeductions | number:'1.2-2' }}</span></div>
          @if (p.deductions > p.cappedDeductions) {
            <div class="mt-1 text-xs text-warn">{{ 'hr.pay.capped' | t }}</div>
          }
          <div class="mt-2 flex justify-between text-lg font-bold"><span>{{ 'hr.pay.net' | t }}</span><span class="tabular">{{ p.netPay | number:'1.2-2' }}</span></div>
        </div>
      </div></div>
    }`,
})
export class HrPayrollPage implements OnInit {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly hmin = hmin;
  readonly cycles = signal<Cycle[]>([]);
  readonly payslips = signal<Payslip[]>([]);
  readonly blockers = signal<Blocker[]>([]);
  readonly selected = signal<Cycle | null>(null);
  readonly slip = signal<Payslip | null>(null);
  readonly busy = signal(false);
  year = Number(uaeToday().substring(0, 4));
  month = Number(uaeToday().substring(5, 7));

  ngOnInit(): void { this.load(); }

  async load(): Promise<void> {
    try {
      this.cycles.set(await this.api.get<Cycle[]>('hr/payroll/cycles'));
      const current = this.selected();
      if (current) {
        const refreshed = this.cycles().find(c => c.id === current.id) ?? null;
        this.selected.set(refreshed);
        if (refreshed) await this.select(refreshed);
      }
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async select(c: Cycle): Promise<void> {
    this.selected.set(c);
    try {
      const [payslips, blockers] = await Promise.all([
        this.api.get<Payslip[]>(`hr/payroll/cycles/${c.id}/payslips`),
        this.api.get<Blocker[]>(`hr/payroll/cycles/${c.id}/blockers`),
      ]);
      this.payslips.set(payslips);
      this.blockers.set(blockers);
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async open(): Promise<void> {
    this.busy.set(true);
    try {
      const cycle = await this.api.post<Cycle>('hr/payroll/cycles', { year: +this.year, month: +this.month });
      await this.load();
      await this.select(cycle);
      this.ui.ok(this.i18n.t('common.saved'));
    } catch (e) { this.ui.error(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async act(action: 'calculate' | 'approve' | 'close' | 'reopen'): Promise<void> {
    const c = this.selected();
    if (!c) return;
    if (action === 'reopen' && !(await this.ui.confirm(this.i18n.t('hr.pay.reopen'), this.i18n.t('hr.pay.reopenWarn'), true))) return;

    this.busy.set(true);
    try {
      await this.api.post(`hr/payroll/cycles/${c.id}/${action}`);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.ui.error(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  total(key: keyof Payslip): number { return this.payslips().reduce((sum, p) => sum + (p[key] as number), 0); }

  /** Downloads the finance report through the API so the file matches what the server holds. */
  async exportCsv(c: Cycle): Promise<void> {
    const session = JSON.parse(localStorage.getItem('session') ?? '{}');
    const response = await fetch(`/api/hr/payroll/cycles/${c.id}/export`, { headers: { Authorization: `Bearer ${session.token}` } });
    if (!response.ok) { this.ui.error(this.i18n.t('err.generic')); return; }
    const blob = await response.blob();
    const link = document.createElement('a');
    link.href = URL.createObjectURL(blob);
    link.download = `payroll_${c.year}-${String(c.month).padStart(2, '0')}.csv`;
    link.click();
    URL.revokeObjectURL(link.href);
  }
}
