import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { addDays, uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Cycle { id: string; year: number; month: number; status: string; }

/**
 * Reports open as a right-to-left page the browser prints or saves as PDF, or download
 * as a Word file. Both come from the same renderer, so the two always agree.
 */
@Component({
  selector: 'app-hr-reports',
  standalone: true,
  imports: [FormsModule, TPipe],
  template: `
    <h1 class="mb-1">{{ 'hr.nav.reports' | t }}</h1>
    <p class="mb-5 text-xs text-muted">{{ 'hr.rep.lead' | t }}</p>

    <div class="grid gap-4 lg:grid-cols-2">
      <section class="card-pad">
        <h2 class="mb-3">{{ 'hr.rep.attendance' | t }}</h2>
        <div class="row">
          <div class="field"><label>{{ 'common.from' | t }}</label><input type="date" [(ngModel)]="from"></div>
          <div class="field"><label>{{ 'common.to' | t }}</label><input type="date" [(ngModel)]="to"></div>
          <div class="field"><label>{{ 'hr.org.department' | t }}</label>
            <select [(ngModel)]="departmentId"><option value="">{{ 'common.all' | t }}</option>
              @for (d of departments(); track d.id) { <option [value]="d.id">{{ d.nameAr }}</option> }</select></div>
        </div>
        <div class="flex gap-2">
          <button class="btn primary" (click)="open('attendance', 'print')">{{ 'hr.rep.print' | t }}</button>
          <button class="btn" (click)="open('attendance', 'word')">{{ 'hr.rep.word' | t }}</button>
        </div>
      </section>

      <section class="card-pad">
        <h2 class="mb-3">{{ 'hr.rep.deductions' | t }}</h2>
        <div class="row">
          <div class="field"><label>{{ 'common.from' | t }}</label><input type="date" [(ngModel)]="from"></div>
          <div class="field"><label>{{ 'common.to' | t }}</label><input type="date" [(ngModel)]="to"></div>
        </div>
        <div class="flex gap-2">
          <button class="btn primary" (click)="open('deductions', 'print')">{{ 'hr.rep.print' | t }}</button>
          <button class="btn" (click)="open('deductions', 'word')">{{ 'hr.rep.word' | t }}</button>
        </div>
      </section>

      @if (auth.canManageHr()) {
      <section class="card-pad lg:col-span-2">
        <h2 class="mb-3">{{ 'hr.rep.payroll' | t }}</h2>
        @if (cycles().length) {
          <div class="row">
            <div class="field"><label>{{ 'hr.pay.cycle' | t }}</label>
              <select [(ngModel)]="cycleId">
                @for (c of cycles(); track c.id) {
                  <option [value]="c.id">{{ c.year }}-{{ c.month }} · {{ 'hr.pay.status.' + c.status | t }}</option>
                }</select></div>
          </div>
          <div class="flex gap-2">
            <button class="btn primary" [disabled]="!cycleId" (click)="openPayroll('print')">{{ 'hr.rep.print' | t }}</button>
            <button class="btn" [disabled]="!cycleId" (click)="openPayroll('word')">{{ 'hr.rep.word' | t }}</button>
          </div>
        } @else { <p class="muted">{{ 'hr.pay.none' | t }}</p> }
      </section>
      }
    </div>`,
})
export class HrReportsPage implements OnInit {
  readonly i18n = inject(I18n);
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly departments = signal<{ id: string; nameAr: string }[]>([]);
  readonly cycles = signal<Cycle[]>([]);
  from = addDays(uaeToday(), -30);
  to = uaeToday();
  departmentId = '';
  cycleId = '';

  async ngOnInit(): Promise<void> {
    try {
      this.departments.set(await this.api.get('hr/org/departments'));
      // Payroll figures are for the HR manager and the administrator; nobody else asks for them.
      const cycles = this.auth.canManageHr() ? await this.api.get<Cycle[]>('hr/payroll/cycles').catch(() => []) : [];
      this.cycles.set(cycles);
      this.cycleId = cycles[0]?.id ?? '';
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  open(report: 'attendance' | 'deductions', format: 'print' | 'word'): void {
    const params = new URLSearchParams({ from: this.from, to: this.to, format });
    if (report === 'attendance' && this.departmentId) params.set('departmentId', this.departmentId);
    this.download(`hr/reports/${report}?${params}`, `${report}_${this.from}_${this.to}`, format);
  }

  openPayroll(format: 'print' | 'word'): void {
    this.download(`hr/reports/payroll/${this.cycleId}?format=${format}`, 'payroll', format);
  }

  /** The report needs the bearer token, so it is fetched and then shown or saved. */
  private async download(path: string, name: string, format: 'print' | 'word'): Promise<void> {
    const session = JSON.parse(localStorage.getItem('session') ?? '{}');
    const response = await fetch(`/api/${path}`, { headers: { Authorization: `Bearer ${session.token}` } });
    if (!response.ok) { this.ui.error(this.i18n.t('err.generic')); return; }

    const blob = await response.blob();
    const url = URL.createObjectURL(blob);

    if (format === 'word') {
      const link = document.createElement('a');
      link.href = url;
      link.download = `${name}.doc`;
      link.click();
    } else {
      window.open(url, '_blank');
    }
    setTimeout(() => URL.revokeObjectURL(url), 30_000);
  }
}
