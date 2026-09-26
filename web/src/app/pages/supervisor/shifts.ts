import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { hmin, timeInput, toTimeOnly } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';
import { Backdrop } from '../../core/backdrop';

interface Shift { id: string; nameAr: string; nameEn: string; startTime: string; endTime: string; durationMinutes: number; breakMinutes: number; graceMinutes: number; countEarlyArrivalAsOvertime: boolean; earlyCheckInMinutes: number; crossesMidnight: boolean; isActive: boolean; rowVersion: string; }

@Component({
  selector: 'app-shifts',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop],
  template: `
    <div class="toolbar"><h1 style="margin:0">{{ 'shift.title' | t }}</h1><span class="spacer"></span>
      <label style="display:flex;gap:6px;align-items:center;margin:0"><input type="checkbox" [(ngModel)]="showDeleted" (change)="load()">{{ 'common.showDeleted' | t }}</label>
      @if (auth.canManage()) { <button class="btn primary" (click)="open(null)">+ {{ 'shift.new' | t }}</button> }
      @else { <span class="badge">{{ 'common.viewOnly' | t }}</span> }</div>

    <div class="table-wrap"><table>
      <thead><tr><th>{{ 'common.name' | t }}</th><th>{{ 'shift.start' | t }}</th><th>{{ 'shift.end' | t }}</th><th>{{ 'shift.duration' | t }}</th>
        <th>{{ 'shift.break' | t }}</th><th>{{ 'shift.grace' | t }}</th><th>{{ 'shift.earlyWindow' | t }}</th><th>{{ 'common.actions' | t }}</th></tr></thead>
      <tbody>
        @for (s of items(); track s.id) {
          <tr [class.inactive]="!s.isActive">
            <td>{{ i18n.pick(s.nameAr, s.nameEn) }}
              @if (s.crossesMidnight) { <span class="badge blue">{{ 'shift.midnight' | t }}</span> }
              @if (!s.isActive) { <span class="badge">{{ 'common.deleted' | t }}</span> }</td>
            <td dir="ltr">{{ s.startTime.substring(0,5) }}</td><td dir="ltr">{{ s.endTime.substring(0,5) }}</td>
            <td>{{ hmin(s.durationMinutes) }}</td><td>{{ s.breakMinutes }}</td><td>{{ s.graceMinutes }}</td><td>{{ s.earlyCheckInMinutes }}</td>
            <td>@if (auth.canManage()) {
              <button class="btn sm" (click)="open(s)">{{ 'common.edit' | t }}</button>
              @if (s.isActive) { <button class="btn sm danger" (click)="remove(s)">{{ 'common.delete' | t }}</button> }
              @else { <button class="btn sm" (click)="restore(s)">{{ 'common.restore' | t }}</button> } }</td>
          </tr>
        } @empty { <tr><td colspan="8" class="muted">{{ 'common.empty' | t }}</td></tr> }
      </tbody></table></div>

    @if (editing(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="editing.set(null)"><div class="modal" (click)="$event.stopPropagation()">
        <div class="modal-head"><h2>{{ f.id ? f.nameAr : ('shift.new' | t) }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'loc.nameAr' | t }} *</label><input [(ngModel)]="f.nameAr"></div>
          <div class="field"><label>{{ 'loc.nameEn' | t }} *</label><input dir="ltr" [(ngModel)]="f.nameEn"></div>
        </div>
        <div class="row">
          <div class="field"><label>{{ 'shift.start' | t }} *</label><input type="time" [(ngModel)]="f.start"></div>
          <div class="field"><label>{{ 'shift.end' | t }} *</label><input type="time" [(ngModel)]="f.end"></div>
        </div>
        <div class="row">
          <div class="field"><label>{{ 'shift.break' | t }}</label><input type="number" min="0" [(ngModel)]="f.breakMinutes"></div>
          <div class="field"><label>{{ 'shift.grace' | t }}</label><input type="number" min="0" max="120" [(ngModel)]="f.graceMinutes"></div>
        </div>
        <label style="display:flex;gap:8px;align-items:center;color:var(--text)">
          <input type="checkbox" [(ngModel)]="f.countEarlyArrivalAsOvertime">{{ 'shift.earlyOt' | t }}</label>
        @if (f.id) { <div class="alert blue" style="margin-top:12px">{{ 'shift.appliesNote' | t }}</div> }
        <div class="modal-foot">
          <button class="btn" (click)="editing.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !valid(f)" (click)="save(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }`,
})
export class ShiftsPage implements OnInit {
  readonly i18n = inject(I18n);
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly hmin = hmin;
  readonly items = signal<Shift[]>([]);
  readonly editing = signal<any>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  showDeleted = false;

  ngOnInit(): void { this.load(); }

  async load(): Promise<void> {
    try { this.items.set(await this.api.get<Shift[]>('shifts', { includeInactive: this.showDeleted })); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  open(s: Shift | null): void {
    this.error.set(null);
    this.editing.set(s
      ? { ...s, start: timeInput(s.startTime), end: timeInput(s.endTime) }
      : { id: '', nameAr: '', nameEn: '', start: '08:00', end: '16:00', breakMinutes: 0, graceMinutes: 10, countEarlyArrivalAsOvertime: false, earlyCheckInMinutes: 30, rowVersion: '' });
  }

  valid(f: any): boolean { return !!f.nameAr?.trim() && !!f.nameEn?.trim() && !!f.start && !!f.end && f.start !== f.end; }

  async save(f: any): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const body = { nameAr: f.nameAr, nameEn: f.nameEn, startTime: toTimeOnly(f.start), endTime: toTimeOnly(f.end), breakMinutes: +f.breakMinutes, graceMinutes: +f.graceMinutes, countEarlyArrivalAsOvertime: !!f.countEarlyArrivalAsOvertime, earlyCheckInMinutes: +f.earlyCheckInMinutes || 0 };
    try {
      if (f.id) await this.api.put(`shifts/${f.id}`, { ...body, rowVersion: f.rowVersion });
      else await this.api.post('shifts', body);
      this.editing.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) {
      this.error.set(this.api.error(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  async remove(s: Shift): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete') + ': ' + s.nameAr, this.i18n.t('common.deleteConfirm'), true))) return;
    try { await this.api.delete(`shifts/${s.id}`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async restore(s: Shift): Promise<void> {
    try { await this.api.post(`shifts/${s.id}/restore`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
