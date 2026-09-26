import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { Backdrop } from '../../core/backdrop';
import { addDays, uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface AllowanceType { id: string; nameAr: string; nameEn: string; dailyAmount: number | null; notes: string | null; isActive: boolean; rowVersion: string; }
interface Allowance { id: string; employeeId: string; employeeName: string; employeeNumber: string | null; allowanceTypeId: string; allowanceTypeName: string; dailyAmount: number | null; fromDate: string; toDate: string; days: number; notes: string | null; }

@Component({
  selector: 'app-allowances',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop],
  template: `
    <div class="toolbar"><h1 class="m-0">{{ 'allow.title' | t }}</h1><span class="spacer"></span>
      @if (auth.canManage()) {
        <button class="btn" (click)="openType(null)">+ {{ 'allow.newType' | t }}</button>
        <button class="btn primary" (click)="openGrant()">+ {{ 'allow.grant' | t }}</button>
      } @else { <span class="badge">{{ 'common.viewOnly' | t }}</span> }
    </div>

    <div class="tabs">
      <button [class.active]="tab() === 'g'" (click)="tab.set('g')">{{ 'allow.granted' | t }} ({{ grants().length }})</button>
      <button [class.active]="tab() === 't'" (click)="tab.set('t')">{{ 'allow.types' | t }} ({{ types().length }})</button>
    </div>

    @if (tab() === 'g') {
      <div class="card-pad mb-3"><div class="row">
        <div class="field"><label>{{ 'common.from' | t }}</label><input type="date" [(ngModel)]="from"></div>
        <div class="field"><label>{{ 'common.to' | t }}</label><input type="date" [(ngModel)]="to"></div>
        <div class="field"><label>{{ 'att.employee' | t }}</label>
          <select [(ngModel)]="employeeId"><option value="">{{ 'common.all' | t }}</option>
            @for (e of employees(); track e.id) { <option [value]="e.id">{{ e.fullName }}</option> }</select></div>
        <button class="btn primary mb-3" (click)="load()">{{ 'common.search' | t }}</button>
      </div></div>

      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'emp.number' | t }}</th><th>{{ 'att.employee' | t }}</th><th>{{ 'allow.type' | t }}</th>
          <th>{{ 'common.from' | t }}</th><th>{{ 'common.to' | t }}</th><th>{{ 'allow.days' | t }}</th>
          <th>{{ 'allow.amount' | t }}</th><th>{{ 'sch.notes' | t }}</th><th></th></tr></thead>
        <tbody>
          @for (a of grants(); track a.id) {
            <tr><td dir="ltr">{{ a.employeeNumber }}</td><td>{{ a.employeeName }}</td><td>{{ a.allowanceTypeName }}</td>
              <td dir="ltr">{{ a.fromDate }}</td><td dir="ltr">{{ a.toDate }}</td>
              <td class="tabular">{{ a.days }}</td>
              <td class="tabular">{{ a.dailyAmount ? (a.dailyAmount * a.days) : '—' }}</td>
              <td class="whitespace-normal">{{ a.notes }}</td>
              <td>@if (auth.canManage()) { <button class="btn sm danger" (click)="removeGrant(a)">{{ 'common.delete' | t }}</button> }</td></tr>
          } @empty { <tr><td colspan="9" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    } @else {
      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'common.name' | t }}</th><th>{{ 'allow.daily' | t }}</th><th>{{ 'sch.notes' | t }}</th><th></th></tr></thead>
        <tbody>
          @for (t of types(); track t.id) {
            <tr><td>{{ i18n.pick(t.nameAr, t.nameEn) }}</td><td class="tabular">{{ t.dailyAmount ?? '—' }}</td>
              <td class="whitespace-normal">{{ t.notes }}</td>
              <td>@if (auth.canManage()) {
                <button class="btn sm" (click)="openType(t)">{{ 'common.edit' | t }}</button>
                <button class="btn sm danger" (click)="removeType(t)">{{ 'common.delete' | t }}</button> }</td></tr>
          } @empty { <tr><td colspan="4" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    }

    @if (typeForm(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="typeForm.set(null)"><div class="modal">
        <div class="modal-head"><h2>{{ f.id ? f.nameAr : ('allow.newType' | t) }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'loc.nameAr' | t }} *</label><input [(ngModel)]="f.nameAr"></div>
          <div class="field"><label>{{ 'loc.nameEn' | t }} *</label><input dir="ltr" [(ngModel)]="f.nameEn"></div>
        </div>
        <div class="field"><label>{{ 'allow.daily' | t }} <span class="muted">({{ 'common.optional' | t }})</span></label>
          <input type="number" min="0" step="0.5" dir="ltr" [(ngModel)]="f.dailyAmount"></div>
        <div class="field"><label>{{ 'sch.notes' | t }}</label><input [(ngModel)]="f.notes"></div>
        <div class="modal-foot">
          <button class="btn" (click)="typeForm.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !f.nameAr?.trim() || !f.nameEn?.trim()" (click)="saveType(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }

    @if (grantForm(); as g) {
      <div class="modal-back" appBackdrop (dismiss)="grantForm.set(null)"><div class="modal wide">
        <div class="modal-head"><h2>{{ 'allow.grant' | t }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }

        <div class="field"><label>{{ 'allow.type' | t }} *</label>
          <select [(ngModel)]="g.allowanceTypeId">@for (t of types(); track t.id) { <option [value]="t.id">{{ i18n.pick(t.nameAr, t.nameEn) }}</option> }</select></div>

        <div class="field"><label>{{ 'allow.employees' | t }} *</label>
          <div class="max-h-52 overflow-auto rounded-lg border border-line p-2">
            @for (e of employees(); track e.id) {
              <label class="flex items-center gap-2 px-1 py-1 text-ink">
                <input type="checkbox" [checked]="g.employeeIds.includes(e.id)" (change)="toggleEmployee(g, e.id)">
                <span>{{ e.fullName }} <span class="muted small" dir="ltr">{{ e.employeeNumber }}</span></span>
              </label>
            }
          </div>
          <div class="muted small mt-1">{{ g.employeeIds.length }}</div>
        </div>

        <div class="field"><label>{{ 'allow.period' | t }} *</label>
          <div class="flex flex-wrap gap-2">
            @for (m of modes; track m.key) {
              <button class="btn sm" [class.primary]="g.mode === m.key" (click)="g.mode = m.key">{{ m.label | t }}</button>
            }
          </div>
        </div>

        <div class="row">
          <div class="field"><label>{{ 'common.from' | t }} *</label><input type="date" [(ngModel)]="g.fromDate"></div>
          @if (g.mode === 'range') { <div class="field"><label>{{ 'common.to' | t }} *</label><input type="date" [(ngModel)]="g.toDate" [min]="g.fromDate"></div> }
          @if (g.mode === 'days') { <div class="field"><label>{{ 'allow.daysCount' | t }} *</label><input type="number" min="1" max="366" [(ngModel)]="g.days"></div> }
          @if (g.mode === 'weeks') { <div class="field"><label>{{ 'allow.weeksCount' | t }} *</label><input type="number" min="1" max="52" [(ngModel)]="g.weeks"></div> }
        </div>
        <div class="field"><label>{{ 'sch.notes' | t }}</label><input [(ngModel)]="g.notes"></div>

        <div class="modal-foot">
          <button class="btn" (click)="grantForm.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !grantValid(g)" (click)="saveGrant(g)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }`,
})
export class AllowancesPage implements OnInit {
  readonly i18n = inject(I18n);
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly tab = signal<'g' | 't'>('g');
  readonly types = signal<AllowanceType[]>([]);
  readonly grants = signal<Allowance[]>([]);
  readonly employees = signal<{ id: string; fullName: string; employeeNumber: string }[]>([]);
  readonly typeForm = signal<any>(null);
  readonly grantForm = signal<any>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly modes = [{ key: 'range', label: 'allow.byRange' }, { key: 'days', label: 'allow.byDays' }, { key: 'weeks', label: 'allow.byWeeks' }];
  from = uaeToday();
  to = addDays(uaeToday(), 30);
  employeeId = '';

  async ngOnInit(): Promise<void> {
    this.employees.set(await this.api.get('employees', { role: 'Collector' }));
    await this.load();
  }

  async load(): Promise<void> {
    try {
      const [types, grants] = await Promise.all([
        this.api.get<AllowanceType[]>('allowance-types'),
        this.api.get<Allowance[]>('allowances', { from: this.from, to: this.to, employeeId: this.employeeId }),
      ]);
      this.types.set(types);
      this.grants.set(grants);
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  openType(t: AllowanceType | null): void {
    this.error.set(null);
    this.typeForm.set(t ? { ...t } : { id: '', nameAr: '', nameEn: '', dailyAmount: null, notes: '', rowVersion: '' });
  }

  async saveType(f: any): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const body = { nameAr: f.nameAr, nameEn: f.nameEn, dailyAmount: f.dailyAmount === '' || f.dailyAmount === null ? null : +f.dailyAmount, notes: f.notes || null };
    try {
      if (f.id) await this.api.put(`allowance-types/${f.id}`, { ...body, rowVersion: f.rowVersion });
      else await this.api.post('allowance-types', body);
      this.typeForm.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async removeType(t: AllowanceType): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete') + ': ' + t.nameAr, this.i18n.t('common.deleteConfirm'), true))) return;
    try { await this.api.delete(`allowance-types/${t.id}`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  openGrant(): void {
    this.error.set(null);
    this.grantForm.set({
      allowanceTypeId: this.types()[0]?.id ?? '', employeeIds: [] as string[], mode: 'range',
      fromDate: uaeToday(), toDate: addDays(uaeToday(), 6), days: 7, weeks: 1, notes: '',
    });
  }

  toggleEmployee(g: any, id: string): void {
    g.employeeIds = g.employeeIds.includes(id) ? g.employeeIds.filter((x: string) => x !== id) : [...g.employeeIds, id];
  }

  grantValid(g: any): boolean {
    if (!g.allowanceTypeId || !g.employeeIds.length || !g.fromDate) return false;
    if (g.mode === 'range') return !!g.toDate && g.toDate >= g.fromDate;
    return g.mode === 'days' ? g.days > 0 : g.weeks > 0;
  }

  async saveGrant(g: any): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.post('allowances', {
        employeeIds: g.employeeIds, allowanceTypeId: g.allowanceTypeId, fromDate: g.fromDate,
        toDate: g.mode === 'range' ? g.toDate : null,
        days: g.mode === 'days' ? +g.days : null,
        weeks: g.mode === 'weeks' ? +g.weeks : null,
        notes: g.notes || null,
      });
      this.grantForm.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async removeGrant(a: Allowance): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete'), a.employeeName + ' — ' + a.allowanceTypeName, true))) return;
    try { await this.api.delete(`allowances/${a.id}`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
