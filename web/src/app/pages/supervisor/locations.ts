import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import QRCode from 'qrcode';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { getPosition, toTimeOnly, uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';
import { MapPicker } from '../../layout/map-picker';
import { Backdrop } from '../../core/backdrop';

interface Loc { kind: string; id: string; nameAr: string; nameEn: string; address: string | null; latitude: number; longitude: number; radiusMeters: number; capacity: number; qrToken: string; isActive: boolean; overlapsWith: string[]; rowVersion: string; }

@Component({
  selector: 'app-locations',
  standalone: true,
  imports: [FormsModule, TPipe, MapPicker, Backdrop],
  styles: [`
    .qr-card { text-align: center; } .qr-card img { width: 250px; max-width: 100%; }
    .print-only .sheet { width: 105mm; margin: 10mm auto; text-align: center; border: 1px solid #000; padding: 8mm; border-radius: 4mm; }
    .print-only img { width: 70mm; } .print-only h2 { margin: 4mm 0 0; }
    .coords { display: grid; grid-template-columns: 1fr 1fr auto; gap: 10px; align-items: end; }
    .staff-row { display: grid; grid-template-columns: 2fr 1fr 1fr auto; gap: 8px; margin-bottom: 8px; align-items: center; }
    .days { display: flex; gap: 8px; flex-wrap: wrap; } .days label { display: flex; gap: 4px; align-items: center; color: var(--text); margin: 0; }
    @media (max-width: 620px) { .staff-row { grid-template-columns: 1fr 1fr; } }
    @media (max-width: 520px) { .coords { grid-template-columns: 1fr 1fr; } }
  `],
  template: `
    <div class="toolbar"><h1 style="margin:0">{{ 'loc.title' | t }}</h1><span class="spacer"></span>
      <label style="display:flex;gap:6px;align-items:center;margin:0"><input type="checkbox" [(ngModel)]="showDeleted" (change)="load()">{{ 'common.showDeleted' | t }}</label>
      @if (auth.canManage()) { <button class="btn primary" (click)="open(null)">+ {{ 'loc.new' | t }}</button> }
      @else { <span class="badge">{{ 'common.viewOnly' | t }}</span> }</div>

    <div class="table-wrap"><table>
      <thead><tr><th>{{ 'common.name' | t }}</th><th>{{ 'loc.address' | t }}</th><th>{{ 'loc.radius' | t }}</th>
        <th>{{ 'loc.capacity' | t }}</th><th>{{ 'common.actions' | t }}</th></tr></thead>
      <tbody>
        @for (l of items(); track l.id) {
          <tr [class.inactive]="!l.isActive">
            <td>{{ i18n.pick(l.nameAr, l.nameEn) }}
              @if (!l.isActive) { <span class="badge">{{ 'common.deleted' | t }}</span> }
              @if (l.overlapsWith.length) { <div class="badge yellow">{{ 'loc.overlap' | t }}: {{ l.overlapsWith.join('، ') }}</div> }</td>
            <td>{{ l.address }}</td><td>{{ l.radiusMeters }}</td><td>{{ l.capacity }}</td>
            <td>
              @if (l.kind !== 'Office') { <button class="btn sm" (click)="showQr(l)">{{ 'loc.qr' | t }}</button> }
              @if (auth.canManage()) { <button class="btn sm" (click)="openStaff(l)">{{ 'loc.staff' | t }}</button> }
              <a class="btn sm" target="_blank" [href]="'https://www.google.com/maps?q=' + l.latitude + ',' + l.longitude">{{ 'common.map' | t }}</a>
              @if (auth.canManage()) {
                <button class="btn sm" (click)="open(l)">{{ 'common.edit' | t }}</button>
                @if (l.isActive) { <button class="btn sm danger" (click)="remove(l)">{{ 'common.delete' | t }}</button> }
                @else { <button class="btn sm" (click)="restore(l)">{{ 'common.restore' | t }}</button> }
              }
            </td>
          </tr>
        } @empty { <tr><td colspan="5" class="muted">{{ 'common.empty' | t }}</td></tr> }
      </tbody></table></div>

    @if (editing(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="editing.set(null)"><div class="modal wide" (click)="$event.stopPropagation()">
        <div class="modal-head"><h2>{{ f.id ? f.nameAr : ('loc.new' | t) }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'loc.nameAr' | t }} *</label><input [(ngModel)]="f.nameAr"></div>
          <div class="field"><label>{{ 'loc.nameEn' | t }} *</label><input dir="ltr" [(ngModel)]="f.nameEn"></div>
        </div>
        <div class="field"><label>{{ 'loc.address' | t }}</label><input [(ngModel)]="f.address"></div>

        <app-map-picker [latitude]="f.latitude" [longitude]="f.longitude" [radius]="f.radiusMeters"
          (picked)="f.latitude = $event.latitude; f.longitude = $event.longitude" />

        <div class="coords" style="margin-top:12px">
          <div class="field" style="margin:0"><label>{{ 'loc.lat' | t }} *</label><input type="number" step="0.000001" dir="ltr" [(ngModel)]="f.latitude"></div>
          <div class="field" style="margin:0"><label>{{ 'loc.lng' | t }} *</label><input type="number" step="0.000001" dir="ltr" [(ngModel)]="f.longitude"></div>
          <button class="btn" (click)="useMyLocation(f)">{{ 'loc.useMyLocation' | t }}</button>
        </div>

        <div class="row" style="margin-top:12px">
          <div class="field"><label>{{ 'loc.radius' | t }} *</label><input type="number" min="20" max="2000" [(ngModel)]="f.radiusMeters"></div>
          <div class="field"><label>{{ 'loc.capacity' | t }} *</label><input type="number" min="1" max="50" [(ngModel)]="f.capacity"></div>
        </div>

        <div class="modal-foot">
          <button class="btn" (click)="editing.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !valid(f)" (click)="save(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }

    @if (qr(); as q) {
      <div class="modal-back" appBackdrop (dismiss)="qr.set(null)"><div class="modal qr-card" (click)="$event.stopPropagation()">
        <div class="modal-head"><h2>{{ i18n.pick(q.loc.nameAr, q.loc.nameEn) }}</h2><button class="btn sm" (click)="qr.set(null)">{{ 'common.close' | t }}</button></div>
        <img [src]="q.dataUrl" alt="QR"><div class="small muted" dir="ltr" style="word-break:break-all">{{ q.url }}</div>
        <div class="modal-foot" style="justify-content:center">
          <a class="btn" [href]="q.dataUrl" [download]="'QR-' + q.loc.nameEn + '.png'">{{ 'loc.download' | t }}</a>
          <button class="btn" (click)="print()">{{ 'loc.print' | t }}</button>
          @if (auth.canManage()) { <button class="btn danger" (click)="regenerate(q.loc)">{{ 'loc.regenerate' | t }}</button> }
        </div>
      </div></div>
      <div class="print-only"><div class="sheet">
        <img src="/logo.png" alt="" style="width:26mm;height:26mm;object-fit:contain">
        <h2>{{ q.loc.nameAr }}</h2><div>{{ q.loc.nameEn }}</div>
        <img [src]="q.dataUrl" alt="QR"><h2>قيّم خدمتنا</h2><div>Rate our service</div></div></div>
    }

    @if (staff(); as st) {
      <div class="modal-back" appBackdrop (dismiss)="staff.set(null)"><div class="modal wide" (click)="$event.stopPropagation()">
        <div class="modal-head"><h2>{{ 'loc.staff' | t }} — {{ i18n.pick(st.loc.nameAr, st.loc.nameEn) }}</h2>
          <button class="btn sm" (click)="staff.set(null)">{{ 'common.close' | t }}</button></div>
        <div class="alert blue">{{ 'loc.staffHint' | t }}</div>
        @if (staffError()) { <div class="alert red">{{ staffError() }}</div> }

        <h2>{{ 'loc.currentStaff' | t }}</h2>
        <div class="table-wrap" style="margin-bottom:16px"><table>
          <thead><tr><th>{{ 'att.employee' | t }}</th><th>{{ 'sch.shift' | t }}</th><th>{{ 'common.from' | t }}</th><th>{{ 'common.to' | t }}</th><th></th></tr></thead>
          <tbody>
            @for (a of st.assignments; track a.id) {
              <tr><td>{{ a.employeeName }}</td><td>{{ a.shiftName }}</td>
                <td dir="ltr">{{ a.startDate }}</td><td dir="ltr">{{ a.endDate ?? '—' }}</td>
                <td><button class="btn sm danger" (click)="endAssignment(st.loc, a)">{{ 'loc.endAssignment' | t }}</button></td></tr>
            } @empty { <tr><td colspan="5" class="muted">{{ 'loc.noStaff' | t }}</td></tr> }
          </tbody></table></div>

        <h2>{{ 'loc.period' | t }}</h2>
        <div class="row">
          <div class="field"><label>{{ 'common.from' | t }} *</label><input type="date" [(ngModel)]="st.fromDate" [min]="today"></div>
          <div class="field"><label>{{ 'sch.endDate' | t }}</label><input type="date" [(ngModel)]="st.toDate" [min]="st.fromDate"></div>
        </div>
        <div class="field"><label>{{ 'sch.days' | t }}</label><div class="days">
          @for (d of [0,1,2,3,4,5,6]; track d) { <label><input type="checkbox" [checked]="hasDay(st, d)" (change)="toggleDay(st, d)">{{ ('day.' + d) | t }}</label> }
        </div></div>

        <h2>{{ 'loc.workTime' | t }}</h2>
        @for (row of st.rows; track $index) {
          <div class="staff-row">
            <select [(ngModel)]="row.employeeId">
              <option value="">—</option>
              @for (e of employees(); track e.id) { <option [value]="e.id">{{ e.fullName }}</option> }
            </select>
            <input type="time" [(ngModel)]="row.start">
            <input type="time" [(ngModel)]="row.end">
            <button class="btn sm danger" [disabled]="st.rows.length === 1" (click)="st.rows.splice($index, 1)">{{ 'loc.removeRow' | t }}</button>
          </div>
        }
        <button class="btn sm" (click)="st.rows.push({ employeeId: '', start: '08:00', end: '16:00' })">+ {{ 'loc.addRow' | t }}</button>

        <div class="modal-foot">
          <button class="btn" (click)="staff.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !staffValid(st)" (click)="saveStaff(st)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }`,
})
export class LocationsPage implements OnInit {
  readonly i18n = inject(I18n);
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly items = signal<Loc[]>([]);
  readonly editing = signal<any>(null);
  readonly qr = signal<{ loc: Loc; url: string; dataUrl: string } | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly staff = signal<any>(null);
  readonly staffError = signal<string | null>(null);
  readonly employees = signal<{ id: string; fullName: string }[]>([]);
  readonly today = uaeToday();
  showDeleted = false;

  ngOnInit(): void { this.load(); }

  async load(): Promise<void> {
    try { this.items.set(await this.api.get<Loc[]>('locations', { includeInactive: this.showDeleted, kind: 'Field' })); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  open(l: Loc | null): void {
    this.error.set(null);
    this.editing.set(l ? { ...l } : { id: '', nameAr: '', nameEn: '', address: '', latitude: null, longitude: null, radiusMeters: 100, capacity: 1, rowVersion: '' });
  }

  valid(f: any): boolean {
    return !!f.nameAr?.trim() && !!f.nameEn?.trim() && f.latitude !== null && f.longitude !== null && f.radiusMeters >= 20 && f.capacity >= 1;
  }

  async useMyLocation(f: any): Promise<void> {
    try {
      const p = await getPosition();
      f.latitude = +p.coords.latitude.toFixed(6);
      f.longitude = +p.coords.longitude.toFixed(6);
    } catch { this.ui.error(this.i18n.t('me.geoDenied')); }
  }

  async save(f: any): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const body = { nameAr: f.nameAr, nameEn: f.nameEn, address: f.address || null, latitude: +f.latitude, longitude: +f.longitude, radiusMeters: +f.radiusMeters, capacity: +f.capacity };
    try {
      if (f.id) await this.api.put(`locations/${f.id}`, { ...body, rowVersion: f.rowVersion });
      else await this.api.post('locations', body);
      this.editing.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) {
      this.error.set(this.api.error(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  async showQr(l: Loc): Promise<void> {
    const url = `${location.origin}/r/${l.qrToken}`;
    this.qr.set({ loc: l, url, dataUrl: await QRCode.toDataURL(url, { width: 600, margin: 1, errorCorrectionLevel: 'M' }) });
  }

  print(): void { window.print(); }

  async regenerate(l: Loc): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('loc.regenerate'), this.i18n.t('loc.regenerateConfirm'), true))) return;
    try {
      const updated = await this.api.post<Loc>(`locations/${l.id}/regenerate-qr`);
      await this.load();
      await this.showQr(updated);
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async remove(l: Loc): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete') + ': ' + l.nameAr, this.i18n.t('common.deleteConfirm'), true))) return;
    try { await this.api.delete(`locations/${l.id}`); this.ui.ok(this.i18n.t('common.saved')); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async restore(l: Loc): Promise<void> {
    try { await this.api.post(`locations/${l.id}/restore`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  /** Staffing straight from the location: who works here, on which days, and at what times. */
  async openStaff(l: Loc): Promise<void> {
    this.staffError.set(null);
    const [employees, assignments] = await Promise.all([
      this.api.get<{ id: string; fullName: string }[]>('employees', { role: 'Collector' }),
      this.api.get<any[]>('schedule/assignments', { locationId: l.id }),
    ]);
    this.employees.set(employees);
    this.staff.set({
      loc: l, assignments, fromDate: this.today, toDate: '', days: 31,
      rows: [{ employeeId: '', start: '08:00', end: '16:00' }],
    });
  }

  hasDay(st: any, d: number): boolean { return (st.days & (1 << d)) !== 0; }
  toggleDay(st: any, d: number): void { st.days ^= 1 << d; }

  staffValid(st: any): boolean {
    return st.days > 0 && !!st.fromDate
      && st.rows.length > 0
      && st.rows.every((r: any) => r.employeeId && r.start && r.end && r.start !== r.end);
  }

  async saveStaff(st: any): Promise<void> {
    this.busy.set(true);
    this.staffError.set(null);
    try {
      await this.api.post('schedule/assign-to-location', {
        locationId: st.loc.id,
        fromDate: st.fromDate,
        toDate: st.toDate || null,
        days: st.days,
        entries: st.rows.map((r: any) => ({ employeeId: r.employeeId, startTime: toTimeOnly(r.start), endTime: toTimeOnly(r.end) })),
      });
      this.ui.ok(this.i18n.t('common.saved'));
      await this.openStaff(st.loc);
    } catch (e) {
      this.staffError.set(this.api.error(e).message);
    } finally {
      this.busy.set(false);
    }
  }

  /** Ends the assignment from today; past days keep their attendance records. */
  async endAssignment(l: Loc, a: any): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('loc.endAssignment') + ': ' + a.employeeName, this.i18n.t('common.deleteConfirm'), true))) return;
    try {
      await this.api.post(`schedule/assignments/${a.id}/end`, { effectiveDate: this.today });
      this.ui.ok(this.i18n.t('common.saved'));
      await this.openStaff(l);
    } catch (e) { this.staffError.set(this.api.error(e).message); }
  }
}
