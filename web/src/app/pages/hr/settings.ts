import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { Backdrop } from '../../core/backdrop';
import { addDays, uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Lookup { id: string; nameAr: string; nameEn: string; notes: string | null; sortOrder: number; isActive: boolean; rowVersion: string; }
interface Holiday { id: string; nameAr: string; nameEn: string; fromDate: string; toDate: string; days: number; }

/** Every configurable list in one place: HR adds and renames values without a code change. */
@Component({
  selector: 'app-hr-settings',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop],
  template: `
    <div class="toolbar"><h1 class="m-0">{{ 'hr.nav.settings' | t }}</h1><span class="spacer"></span>
      @if (!auth.canManageHr()) { <span class="badge">{{ 'common.viewOnly' | t }}</span> }</div>

    <div class="tabs">
      @for (s of sets; track s.key) { <button [class.active]="set() === s.key" (click)="setSet(s.key)">{{ s.label | t }}</button> }
    </div>

    @if (set() === 'holidays') {
      <div class="toolbar">
        @if (auth.canManageHr()) { <button class="btn primary" (click)="openHoliday()">+ {{ 'hr.set.newHoliday' | t }}</button> }
      </div>
      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'common.name' | t }}</th><th>{{ 'common.from' | t }}</th><th>{{ 'common.to' | t }}</th><th>{{ 'allow.days' | t }}</th><th></th></tr></thead>
        <tbody>
          @for (h of holidays(); track h.id) {
            <tr><td>{{ h.nameAr }}</td><td dir="ltr">{{ h.fromDate }}</td><td dir="ltr">{{ h.toDate }}</td><td class="tabular">{{ h.days }}</td>
              <td>@if (auth.canManageHr()) { <button class="btn sm danger" (click)="removeHoliday(h)">{{ 'common.delete' | t }}</button> }</td></tr>
          } @empty { <tr><td colspan="5" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    } @else {
      <div class="toolbar">
        @if (auth.canManageHr()) { <button class="btn primary" (click)="openLookup(null)">+ {{ 'hr.set.newItem' | t }}</button> }
        <span class="spacer"></span>
        <label class="flex items-center gap-2 m-0"><input type="checkbox" [(ngModel)]="includeInactive" (change)="load()">{{ 'common.showDeleted' | t }}</label>
      </div>
      <div class="table-wrap"><table>
        <thead><tr><th>{{ 'loc.nameAr' | t }}</th><th>{{ 'loc.nameEn' | t }}</th><th>{{ 'sch.notes' | t }}</th><th></th></tr></thead>
        <tbody>
          @for (x of items(); track x.id) {
            <tr [class.inactive]="!x.isActive">
              <td>{{ x.nameAr }} @if (!x.isActive) { <span class="badge">{{ 'common.deleted' | t }}</span> }</td>
              <td dir="ltr">{{ x.nameEn }}</td><td class="whitespace-normal">{{ x.notes }}</td>
              <td>@if (auth.canManageHr()) {
                <button class="btn sm" (click)="openLookup(x)">{{ 'common.edit' | t }}</button>
                @if (x.isActive) { <button class="btn sm danger" (click)="removeLookup(x)">{{ 'common.delete' | t }}</button> }
                @else { <button class="btn sm" (click)="restoreLookup(x)">{{ 'common.restore' | t }}</button> } }</td></tr>
          } @empty { <tr><td colspan="4" class="muted">{{ 'common.empty' | t }}</td></tr> }
        </tbody></table></div>
    }

    @if (editing(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="editing.set(null)"><div class="modal">
        <div class="modal-head"><h2>{{ f.id ? f.nameAr : ('hr.set.newItem' | t) }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'loc.nameAr' | t }} *</label><input [(ngModel)]="f.nameAr"></div>
          <div class="field"><label>{{ 'loc.nameEn' | t }} *</label><input dir="ltr" [(ngModel)]="f.nameEn"></div>
        </div>
        <div class="field"><label>{{ 'sch.notes' | t }}</label><input [(ngModel)]="f.notes"></div>
        <div class="modal-foot">
          <button class="btn" (click)="editing.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !f.nameAr?.trim() || !f.nameEn?.trim()" (click)="saveLookup(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }

    @if (holiday(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="holiday.set(null)"><div class="modal">
        <div class="modal-head"><h2>{{ 'hr.set.newHoliday' | t }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'loc.nameAr' | t }} *</label><input [(ngModel)]="f.nameAr"></div>
          <div class="field"><label>{{ 'loc.nameEn' | t }} *</label><input dir="ltr" [(ngModel)]="f.nameEn"></div>
        </div>
        <div class="row">
          <div class="field"><label>{{ 'common.from' | t }} *</label><input type="date" [(ngModel)]="f.fromDate"></div>
          <div class="field"><label>{{ 'common.to' | t }} *</label><input type="date" [(ngModel)]="f.toDate" [min]="f.fromDate"></div>
        </div>
        <div class="modal-foot">
          <button class="btn" (click)="holiday.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !f.nameAr?.trim() || !f.nameEn?.trim()" (click)="saveHoliday(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }`,
})
export class HrSettingsPage implements OnInit {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly sets = [
    { key: 'job-titles', label: 'hr.set.jobTitles' },
    { key: 'grades', label: 'hr.set.grades' },
    { key: 'contract-types', label: 'hr.set.contracts' },
    { key: 'holidays', label: 'hr.set.holidays' },
  ];
  readonly set = signal('job-titles');
  readonly items = signal<Lookup[]>([]);
  readonly holidays = signal<Holiday[]>([]);
  readonly editing = signal<any>(null);
  readonly holiday = signal<any>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  includeInactive = false;

  ngOnInit(): void { this.load(); }

  setSet(key: string): void { this.set.set(key); this.load(); }

  async load(): Promise<void> {
    try {
      if (this.set() === 'holidays') this.holidays.set(await this.api.get<Holiday[]>('hr/org/holidays'));
      else this.items.set(await this.api.get<Lookup[]>(`hr/lookups/${this.set()}`, { includeInactive: this.includeInactive }));
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  openLookup(x: Lookup | null): void {
    this.error.set(null);
    this.editing.set(x ? { ...x } : { id: '', nameAr: '', nameEn: '', notes: '', sortOrder: 0, rowVersion: '' });
  }

  async saveLookup(f: any): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const body = { nameAr: f.nameAr, nameEn: f.nameEn, notes: f.notes || null, sortOrder: +f.sortOrder || 0, rowVersion: f.rowVersion };
    try {
      if (f.id) await this.api.put(`hr/lookups/${this.set()}/${f.id}`, body);
      else await this.api.post(`hr/lookups/${this.set()}`, body);
      this.editing.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async removeLookup(x: Lookup): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete') + ': ' + x.nameAr, this.i18n.t('common.deleteConfirm'), true))) return;
    try { await this.api.delete(`hr/lookups/${this.set()}/${x.id}`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async restoreLookup(x: Lookup): Promise<void> {
    try { await this.api.post(`hr/lookups/${this.set()}/${x.id}/restore`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  openHoliday(): void {
    this.error.set(null);
    this.holiday.set({ nameAr: '', nameEn: '', fromDate: uaeToday(), toDate: addDays(uaeToday(), 0) });
  }

  async saveHoliday(f: any): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.post('hr/org/holidays', f);
      this.holiday.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async removeHoliday(h: Holiday): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete') + ': ' + h.nameAr, this.i18n.t('common.deleteConfirm'), true))) return;
    try { await this.api.delete(`hr/org/holidays/${h.id}`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
