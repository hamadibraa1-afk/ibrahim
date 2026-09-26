import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { Auth } from '../../core/auth';
import { Backdrop } from '../../core/backdrop';
import { hmin, timeInput, toTimeOnly } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Day { day: number; startTime: string; endTime: string; breakMinutes: number; }
interface Schedule { id: string; nameAr: string; nameEn: string; graceMinutes: number; earlyCheckInMinutes: number; countEarlyArrivalAsOvertime: boolean; weeklyMinutes: number; employeeCount: number; days: Day[]; rowVersion: string; }

@Component({
  selector: 'app-hr-schedules',
  standalone: true,
  imports: [FormsModule, TPipe, Backdrop],
  template: `
    <div class="toolbar"><h1 class="m-0">{{ 'hr.nav.schedules' | t }}</h1><span class="spacer"></span>
      @if (auth.canManageHr()) { <button class="btn primary" (click)="open(null)">+ {{ 'hr.sch.new' | t }}</button> }
      @else { <span class="badge">{{ 'common.viewOnly' | t }}</span> }
    </div>

    <div class="grid gap-4 lg:grid-cols-2">
      @for (s of items(); track s.id) {
        <section class="card overflow-hidden">
          <header class="flex items-center gap-2 border-b border-line px-4 py-3">
            <h2 class="flex-1">{{ s.nameAr }}</h2>
            <span class="badge">{{ s.employeeCount }}</span>
            @if (auth.canManageHr()) {
              <button class="btn sm" (click)="open(s)">{{ 'common.edit' | t }}</button>
              <button class="btn sm danger" (click)="remove(s)">{{ 'common.delete' | t }}</button>
            }
          </header>
          <div class="divide-y divide-line px-4">
            @for (d of s.days; track d.day) {
              <div class="flex items-center gap-3 py-2 text-sm">
                <span class="w-20 font-semibold">{{ ('day.' + d.day) | t }}</span>
                <span class="tabular" dir="ltr">{{ d.startTime.substring(0,5) }}–{{ d.endTime.substring(0,5) }}</span>
                @if (d.breakMinutes) { <span class="muted small">{{ 'shift.break' | t }}: {{ d.breakMinutes }}</span> }
              </div>
            }
          </div>
          <footer class="flex flex-wrap gap-4 border-t border-line px-4 py-3 text-xs text-muted">
            <span>{{ 'hr.sch.weekly' | t }}: {{ hmin(s.weeklyMinutes) }}</span>
            <span>{{ 'shift.grace' | t }}: {{ s.graceMinutes }}</span>
            <span>{{ 'shift.earlyWindow' | t }}: {{ s.earlyCheckInMinutes }}</span>
          </footer>
        </section>
      } @empty { <div class="card-pad muted">{{ 'common.empty' | t }}</div> }
    </div>

    @if (editing(); as f) {
      <div class="modal-back" appBackdrop (dismiss)="editing.set(null)"><div class="modal wide">
        <div class="modal-head"><h2>{{ f.id ? f.nameAr : ('hr.sch.new' | t) }}</h2></div>
        @if (error()) { <div class="alert red">{{ error() }}</div> }
        <div class="row">
          <div class="field"><label>{{ 'loc.nameAr' | t }} *</label><input [(ngModel)]="f.nameAr"></div>
          <div class="field"><label>{{ 'loc.nameEn' | t }} *</label><input dir="ltr" [(ngModel)]="f.nameEn"></div>
        </div>
        <div class="row">
          <div class="field"><label>{{ 'shift.grace' | t }}</label><input type="number" min="0" max="120" [(ngModel)]="f.graceMinutes"></div>
          <div class="field"><label>{{ 'shift.earlyWindow' | t }}</label><input type="number" min="0" max="240" [(ngModel)]="f.earlyCheckInMinutes"></div>
        </div>
        <label class="flex items-center gap-2 text-ink mb-4">
          <input type="checkbox" [(ngModel)]="f.countEarlyArrivalAsOvertime">{{ 'shift.earlyOt' | t }}</label>

        <h2 class="mb-2">{{ 'hr.sch.days' | t }}</h2>
        <div class="divide-y divide-line">
          @for (d of f.days; track d.day) {
            <div class="flex flex-wrap items-center gap-3 py-2">
              <label class="flex w-24 items-center gap-2 text-ink m-0">
                <input type="checkbox" [(ngModel)]="d.on">{{ ('day.' + d.day) | t }}</label>
              <input type="time" class="w-32" [(ngModel)]="d.start" [disabled]="!d.on">
              <input type="time" class="w-32" [(ngModel)]="d.end" [disabled]="!d.on">
              <input type="number" class="w-24" min="0" [(ngModel)]="d.breakMinutes" [disabled]="!d.on"
                     [attr.aria-label]="'shift.break' | t">
              <span class="muted small">{{ 'shift.break' | t }}</span>
            </div>
          }
        </div>
        <p class="mt-3 text-xs text-muted">{{ 'hr.sch.hint' | t }}</p>

        <div class="modal-foot">
          <button class="btn" (click)="editing.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="busy() || !valid(f)" (click)="save(f)">{{ 'common.save' | t }}</button>
        </div>
      </div></div>
    }`,
})
export class HrSchedulesPage implements OnInit {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly hmin = hmin;
  readonly items = signal<Schedule[]>([]);
  readonly editing = signal<any>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void { this.load(); }

  async load(): Promise<void> {
    try { this.items.set(await this.api.get<Schedule[]>('hr/work-schedules')); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  open(s: Schedule | null): void {
    this.error.set(null);
    const days = [0, 1, 2, 3, 4, 5, 6].map(day => {
      const existing = s?.days.find(d => d.day === day);
      return {
        day, on: !!existing,
        start: existing ? timeInput(existing.startTime) : '08:00',
        end: existing ? timeInput(existing.endTime) : '16:00',
        breakMinutes: existing?.breakMinutes ?? 0,
      };
    });
    this.editing.set(s
      ? { ...s, days }
      : { id: '', nameAr: '', nameEn: '', graceMinutes: 10, earlyCheckInMinutes: 30, countEarlyArrivalAsOvertime: false, days, rowVersion: '' });
  }

  valid(f: any): boolean {
    const on = f.days.filter((d: any) => d.on);
    return !!f.nameAr?.trim() && !!f.nameEn?.trim() && on.length > 0 && on.every((d: any) => d.start && d.end && d.start !== d.end);
  }

  async save(f: any): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    const body = {
      nameAr: f.nameAr, nameEn: f.nameEn, graceMinutes: +f.graceMinutes || 0,
      earlyCheckInMinutes: +f.earlyCheckInMinutes || 0, countEarlyArrivalAsOvertime: !!f.countEarlyArrivalAsOvertime,
      days: f.days.filter((d: any) => d.on).map((d: any) => ({
        day: d.day, startTime: toTimeOnly(d.start), endTime: toTimeOnly(d.end), breakMinutes: +d.breakMinutes || 0,
      })),
      rowVersion: f.rowVersion,
    };
    try {
      if (f.id) await this.api.put(`hr/work-schedules/${f.id}`, body);
      else await this.api.post('hr/work-schedules', body);
      this.editing.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.error.set(this.api.error(e).message); } finally { this.busy.set(false); }
  }

  async remove(s: Schedule): Promise<void> {
    if (!(await this.ui.confirm(this.i18n.t('common.delete') + ': ' + s.nameAr, this.i18n.t('common.deleteConfirm'), true))) return;
    try { await this.api.delete(`hr/work-schedules/${s.id}`); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
