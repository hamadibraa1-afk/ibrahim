import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from '../../core/api';
import { getPosition, hm, hmin, hmShift, nowUaeHm, timeInput } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Perm { id: string; type: string; fromTime: string | null; toTime: string | null; status: string; }
interface TodayShift {
  recordId: string; shiftDate: string; locationNameAr: string; locationNameEn: string; locationLatitude: number; locationLongitude: number; locationRadius: number;
  shiftNameAr: string; shiftNameEn: string; scheduledStart: string; scheduledEnd: string; status: string; checkInAt: string | null; checkOutAt: string | null;
  checkOutType: string | null; openExitAt: string | null; openExitReturnBy: string | null; lateUnexcused: number; earlyUnexcused: number;
  netWorkMinutes: number; overtimeMinutes: number; hasPendingException: boolean; permissions: Perm[];
  flexMinutes: number; expectedCheckOutAt: string | null;
}
type Kind = 'CheckIn' | 'CheckOut';

@Component({
  selector: 'app-today',
  standalone: true,
  imports: [TPipe],
  template: `
    @if (loading()) {
      <div class="space-y-3">@for (i of [1,2]; track i) { <div class="skeleton h-56"></div> }</div>
    } @else if (!shifts().length) {
      <div class="card-pad text-center animate-slide-up">
        <div class="mx-auto mb-3 grid h-12 w-12 place-items-center rounded-2xl bg-raised text-muted">☾</div>
        <h2>{{ 'me.noShift' | t }}</h2>
      </div>
    }

    @for (s of shifts(); track s.recordId) {
      <section class="card mb-4 overflow-hidden animate-slide-up">
        <header class="bg-gradient-to-b from-brand-soft to-transparent px-5 pt-5 pb-4">
          <h2 class="text-lg">{{ i18n.pick(s.locationNameAr, s.locationNameEn) }}</h2>
          <div class="mt-1 flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted">
            <span>{{ i18n.pick(s.shiftNameAr, s.shiftNameEn) }}</span>
            <span class="tabular" dir="ltr">{{ hm(s.scheduledStart) }}–{{ hm(s.scheduledEnd) }}</span>
            @if (s.flexMinutes) { <span class="badge">{{ 'me.flex' | t }}</span> }
            <a class="underline underline-offset-2" target="_blank"
               [href]="'https://www.google.com/maps?q=' + s.locationLatitude + ',' + s.locationLongitude">{{ 'common.map' | t }}</a>
          </div>
        </header>

        <div class="space-y-3 px-5 pb-5">
          @if (problem()?.recordId === s.recordId) { <div class="alert red">{{ problem()!.text }}</div> }

          @switch (stateOf(s)) {
            @case ('leave') { <div class="alert blue mb-0">{{ 'me.onLeave' | t }}</div> }
            @case ('pending') { <div class="alert blue mb-0">{{ 'me.pendingApproval' | t }}</div> }

            @case ('before') {
              @if (s.flexMinutes) {
                <p class="text-sm text-muted">{{ 'me.arriveBetween' | t }}
                  <span class="tabular font-semibold text-ink" dir="ltr">{{ hmShift(s.scheduledStart, -s.flexMinutes) }}–{{ hmShift(s.scheduledStart, s.flexMinutes) }}</span></p>
              }
              <button class="btn primary big" [disabled]="busy()" (click)="checkIn(s)">
                @if (busy()) { <span class="h-4 w-4 animate-spin rounded-full border-2 border-brand-ink/40 border-t-brand-ink"></span> }
                {{ busy() ? ('me.locating' | t) : ('me.checkIn' | t) }}
              </button>
              @if (problem()?.recordId === s.recordId && problem()!.canException) {
                <button class="btn big" (click)="requestException(s, 'CheckIn')">{{ 'me.exception' | t }}</button>
              }
            }

            @case ('present') {
              <div class="rounded-xl bg-ok-soft px-4 py-3 text-ok">
                <div class="text-xs opacity-80">{{ 'me.checkedInAt' | t }}</div>
                <div class="text-2xl font-bold tabular" dir="ltr">{{ hm(s.checkInAt) }}</div>
                @if (s.lateUnexcused) {
                  <div class="mt-1 text-xs font-semibold text-bad">{{ 'att.late' | t }}: {{ hmin(s.lateUnexcused) }}</div>
                }
                @if (s.expectedCheckOutAt) {
                  <div class="mt-2 border-t border-ok/20 pt-2 text-sm">{{ 'me.leaveAt' | t }}
                    <span class="text-lg font-bold tabular" dir="ltr">{{ hm(s.expectedCheckOutAt) }}</span></div>
                }
              </div>
              @if (activeExitPermission(s); as p) {
                <button class="btn big" [disabled]="busy()" (click)="startExit(p)">
                  {{ 'me.exit' | t }} <span class="text-xs text-muted tabular" dir="ltr">{{ timeInput(p.fromTime) }}–{{ timeInput(p.toTime) }}</span>
                </button>
              }
              <button class="btn primary big" [disabled]="busy()" (click)="checkOut(s)">
                @if (busy()) { <span class="h-4 w-4 animate-spin rounded-full border-2 border-brand-ink/40 border-t-brand-ink"></span> }
                {{ busy() ? ('me.locating' | t) : ('me.checkOut' | t) }}
              </button>
              @if (problem()?.recordId === s.recordId && problem()!.canException) {
                <button class="btn big" (click)="requestException(s, 'CheckOut')">{{ 'me.exceptionOut' | t }}</button>
              }
            }

            @case ('exit') {
              <div class="rounded-xl bg-warn-soft px-4 py-3 text-warn">
                <div class="text-xs opacity-80">{{ 'att.exitOut' | t }} <span class="tabular" dir="ltr">{{ hm(s.openExitAt) }}</span></div>
                <div class="text-xs opacity-80 mt-1">{{ 'me.returnBy' | t }}</div>
                <div class="text-2xl font-bold tabular" dir="ltr">{{ hm(s.openExitReturnBy) }}</div>
              </div>
              <button class="btn primary big" [disabled]="busy()" (click)="doReturn(s)">
                {{ busy() ? ('me.locating' | t) : ('me.return' | t) }}
              </button>
            }

            @case ('done') {
              <h2>{{ 'me.daySummary' | t }}</h2>
              <div class="grid grid-cols-2 gap-2">
                <div class="rounded-xl bg-raised px-3 py-2.5">
                  <div class="text-[11px] text-muted">{{ 'att.in' | t }} / {{ 'att.out' | t }}</div>
                  <div class="font-semibold tabular" dir="ltr">{{ hm(s.checkInAt) }} – {{ hm(s.checkOutAt) }}</div></div>
                <div class="rounded-xl bg-raised px-3 py-2.5">
                  <div class="text-[11px] text-muted">{{ 'att.net' | t }}</div>
                  <div class="font-semibold tabular">{{ hmin(s.netWorkMinutes) }}</div></div>
                <div class="rounded-xl bg-raised px-3 py-2.5">
                  <div class="text-[11px] text-muted">{{ 'att.late' | t }}</div>
                  <div class="font-semibold tabular" [class.text-bad]="s.lateUnexcused > 0">{{ hmin(s.lateUnexcused) }}</div></div>
                <div class="rounded-xl bg-raised px-3 py-2.5">
                  <div class="text-[11px] text-muted">{{ 'att.ot' | t }}</div>
                  <div class="font-semibold tabular">{{ hmin(s.overtimeMinutes) }}</div></div>
              </div>
            }

            @case ('absent') { <div class="alert red mb-0">{{ 'state.Absent' | t }}</div> }
          }
        </div>
      </section>
    }`,
})
export class TodayPage implements OnInit, OnDestroy {
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  private readonly router = inject(Router);
  readonly hm = hm;
  readonly hmShift = hmShift;
  readonly hmin = hmin;
  readonly timeInput = timeInput;
  readonly shifts = signal<TodayShift[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly problem = signal<{ recordId: string; text: string; canException: boolean; fix?: GeolocationCoordinates } | null>(null);
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void { this.load(); this.timer = setInterval(() => this.load(), 30_000); }
  ngOnDestroy(): void { clearInterval(this.timer); }

  async load(): Promise<void> {
    try { this.shifts.set(await this.api.get<TodayShift[]>('me/today')); }
    catch (e) { this.ui.error(this.api.error(e).message); }
    finally { this.loading.set(false); }
  }

  stateOf(s: TodayShift): string {
    if (s.status === 'OnLeave') return 'leave';
    if (s.status === 'Absent') return s.hasPendingException ? 'pending' : 'absent';
    if (s.hasPendingException) return 'pending';
    if (!s.checkInAt) return 'before';
    if (s.checkOutAt) return 'done';
    return s.openExitAt ? 'exit' : 'present';
  }

  activeExitPermission(s: TodayShift): Perm | null {
    const now = nowUaeHm();
    return s.permissions.find(p => p.type === 'TemporaryExit' && p.status === 'Approved'
      && timeInput(p.fromTime) <= now && now < timeInput(p.toTime)) ?? null;
  }

  checkIn(s: TodayShift): Promise<void> { return this.withGps(s, 'me/check-in'); }
  doReturn(s: TodayShift): Promise<void> { return this.withGps(s, 'me/return'); }

  async checkOut(s: TodayShift): Promise<void> {
    const now = nowUaeHm();
    const endsLater = hm(s.scheduledEnd) > now && s.shiftDate === s.scheduledEnd.substring(0, 10);
    const excused = s.permissions.some(p => p.type === 'EarlyDeparture' && p.status === 'Approved' && timeInput(p.fromTime) <= now);
    if (endsLater && !excused) {
      const go = await this.ui.confirm(this.i18n.t('me.checkOut'), this.i18n.t('me.earlyWarn'), true);
      if (!go) { this.router.navigate(['/me/requests'], { queryParams: { type: 'EarlyDeparture' } }); return; }
    }
    await this.withGps(s, 'me/check-out');
  }

  async startExit(p: Perm): Promise<void> {
    this.busy.set(true);
    try { await this.api.post('me/exit', { permissionId: p.id }); await this.load(); }
    catch (e) { this.ui.error(this.api.error(e).message); }
    finally { this.busy.set(false); }
  }

  async requestException(s: TodayShift, kind: Kind): Promise<void> {
    const fix = this.problem()?.fix;
    const reason = await this.ui.prompt(this.i18n.t(kind === 'CheckIn' ? 'me.exception' : 'me.exceptionOut'), this.i18n.t('common.reason'));
    if (!reason) return;
    try {
      await this.api.post('me/exception', { kind, latitude: fix?.latitude ?? s.locationLatitude, longitude: fix?.longitude ?? s.locationLongitude, accuracy: fix?.accuracy ?? 0, reason });
      this.problem.set(null);
      await this.load();
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  private async withGps(s: TodayShift, url: string): Promise<void> {
    this.busy.set(true);
    this.problem.set(null);
    let coords: GeolocationCoordinates;
    try {
      coords = (await getPosition()).coords;
    } catch {
      this.problem.set({ recordId: s.recordId, text: this.i18n.t('me.geoDenied'), canException: false });
      this.busy.set(false);
      return;
    }
    try {
      await this.api.post(url, { latitude: coords.latitude, longitude: coords.longitude, accuracy: Math.round(coords.accuracy) });
      await this.load();
    } catch (e) {
      const err = this.api.error(e);
      const text = err.code === 'geofence.outside' ? this.i18n.t('me.outside', Math.round(err.data?.distance ?? 0), err.data?.radius ?? '')
        : err.code === 'geofence.low_accuracy' ? this.i18n.t('me.lowAccuracy', Math.round(err.data?.accuracy ?? 0))
        : err.message;
      this.problem.set({ recordId: s.recordId, text, canException: err.code.startsWith('geofence.'), fix: coords });
    } finally {
      this.busy.set(false);
    }
  }
}
