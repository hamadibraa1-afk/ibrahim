import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { hm, uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';
import { LiveMap, MapSite } from '../../layout/live-map';
import { Auth } from '../../core/auth';
import { Backdrop } from '../../core/backdrop';

interface BoardEmployee { employeeId: string; name: string; phone: string; email: string; employeeNumber: string | null; state: string; scheduledStart: string; scheduledEnd: string; checkInAt: string | null; lateMinutes: number; }
interface BoardLocation { locationId: string; nameAr: string; nameEn: string; color: string; latitude: number; longitude: number; radiusMeters: number; employees: BoardEmployee[]; }
interface Dashboard { cards: Record<string, number | null>; board: BoardLocation[]; }
interface Shift { id: string; nameAr: string; nameEn: string; startTime: string; endTime: string; }

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [RouterLink, FormsModule, TPipe, Backdrop, LiveMap],
  styles: [`
    .dot { box-shadow: 0 0 0 4px rgb(var(--c-surface)), 0 0 0 6px currentColor; }
  `],
  template: `
    <div class="mb-6 flex flex-wrap items-center gap-3">
      <div class="flex-1 min-w-[12rem]">
        <h1>{{ 'nav.dashboard' | t }}</h1>
        <p class="mt-1 flex items-center gap-2 text-xs text-muted">
          <span class="relative flex h-2 w-2"><span class="absolute inline-flex h-full w-full animate-ping rounded-full bg-ok opacity-60"></span>
            <span class="relative inline-flex h-2 w-2 rounded-full bg-ok"></span></span>
          {{ 'dash.autoRefresh' | t }}
        </p>
      </div>
      <button class="btn sm" (click)="load()">{{ 'common.refresh' | t }}</button>
    </div>

    @if (data(); as d) {
      <div class="grid-cards">
        <a class="card stat" routerLink="/admin/attendance" [queryParams]="{ view: 'today' }"><div class="num">{{ d.cards['scheduled'] }}</div><div class="lbl">{{ 'dash.scheduled' | t }}</div></a>
        <a class="card stat" routerLink="/admin/attendance" [queryParams]="{ view: 'present' }"><div class="num" style="color:var(--green)">{{ d.cards['presentNow'] }}</div><div class="lbl">{{ 'dash.present' | t }}</div></a>
        <a class="card stat" routerLink="/admin/attendance" [queryParams]="{ view: 'late' }"><div class="num" style="color:var(--yellow)">{{ d.cards['late'] }}</div><div class="lbl">{{ 'dash.late' | t }}</div></a>
        <a class="card stat" routerLink="/admin/attendance" [queryParams]="{ view: 'absent' }"><div class="num" style="color:var(--red)">{{ d.cards['absent'] }}</div><div class="lbl">{{ 'dash.absent' | t }}</div></a>
        <a class="card stat" routerLink="/admin/attendance" [queryParams]="{ view: 'leave' }"><div class="num">{{ d.cards['onLeave'] }}</div><div class="lbl">{{ 'dash.onLeave' | t }}</div></a>
        <a class="card stat" routerLink="/admin/attendance" [queryParams]="{ view: 'exit' }"><div class="num">{{ d.cards['onExit'] }}</div><div class="lbl">{{ 'dash.onExit' | t }}</div></a>
        <a class="card stat" routerLink="/admin/schedule"><div class="num" style="color:var(--red)">{{ d.cards['uncoveredLocations'] }}</div><div class="lbl">{{ 'dash.uncovered' | t }}</div></a>
        <a class="card stat" routerLink="/admin/requests"><div class="num">{{ d.cards['pendingRequests'] }}</div><div class="lbl">{{ 'dash.pending' | t }}</div></a>
        <a class="card stat" routerLink="/admin/feedback"><div class="num" [class.text-bad]="(d.cards['openFeedback'] ?? 0) > 0">{{ d.cards['openFeedback'] }}</div><div class="lbl">{{ 'dash.feedback' | t }}</div></a>
        <a class="card stat" routerLink="/admin/ratings"><div class="num">{{ d.cards['averageRating'] ?? '—' }}</div><div class="lbl">{{ 'dash.rating' | t }} ({{ d.cards['ratingCount'] }})</div></a>
      </div>

      <div class="mb-6"><app-live-map [sites]="sites()" (selected)="focus.set($event)" /></div>

      <h2 class="mb-3">{{ 'dash.liveBoard' | t }}</h2>
      <div class="grid gap-3 [grid-template-columns:repeat(auto-fill,minmax(19rem,1fr))]">
        @for (l of d.board; track l.locationId) {
          <section class="card overflow-hidden transition-shadow hover:shadow-lift"
                   [class.ring-2]="focus() === l.locationId" [class.ring-brand]="focus() === l.locationId">
            <header class="flex items-center gap-2.5 border-b border-line px-4 py-3">
              <span class="dot h-2 w-2 rounded-full" [class]="dotClass(l.color)"></span>
              <h2 class="flex-1 truncate">{{ i18n.pick(l.nameAr, l.nameEn) }}</h2>
              @if (l.color === 'red' && auth.canManage()) {
                <button class="btn sm primary" (click)="openCover(l)">{{ 'dash.cover' | t }}</button>
              }
            </header>

            <div class="divide-y divide-line">
              @for (e of l.employees; track e.employeeId + e.scheduledStart) {
                <div class="flex flex-wrap items-center gap-x-2.5 gap-y-1.5 px-4 py-3 transition-colors hover:bg-raised/60">
                  <span class="min-w-[7rem] flex-1 truncate font-semibold">{{ e.name }}</span>
                  <span class="badge {{ badge(e.state) }}">{{ 'state.' + e.state | t }}</span>
                  <span class="text-xs text-muted tabular" dir="ltr">{{ hm(e.scheduledStart) }}–{{ hm(e.scheduledEnd) }}</span>
                  @if (e.checkInAt) { <span class="text-xs font-medium text-ok tabular">✓ {{ hm(e.checkInAt) }}</span> }
                  @if (e.lateMinutes > 0) { <span class="text-xs font-semibold text-bad tabular">+{{ e.lateMinutes }}′</span> }
                  <span class="basis-full sm:basis-auto"></span>
                  <button class="btn sm ghost" (click)="details.set(e)">{{ 'common.details' | t }}</button>
                  <a class="btn sm ghost" [href]="'tel:' + e.phone">{{ 'common.call' | t }}</a>
                </div>
              } @empty {
                <p class="px-4 py-6 text-center text-sm text-muted">{{ 'dash.noOne' | t }}</p>
              }
            </div>
          </section>
        }
      </div>
    } @else {
      <div class="grid-cards">
        @for (i of [1,2,3,4,5,6,7,8,9]; track i) { <div class="skeleton h-[5.5rem]"></div> }
      </div>
      <div class="grid gap-3 [grid-template-columns:repeat(auto-fill,minmax(19rem,1fr))]">
        @for (i of [1,2,3]; track i) { <div class="skeleton h-40"></div> }
      </div>
    }

    @if (details(); as e) {
      <div class="modal-back" appBackdrop (dismiss)="details.set(null)"><div class="modal" (click)="$event.stopPropagation()">
        <div class="modal-head"><h2>{{ e.name }}</h2><button class="btn sm" (click)="details.set(null)">{{ 'common.close' | t }}</button></div>
        <dl class="grid grid-cols-[auto_1fr] gap-x-4 gap-y-2.5 text-sm">
          <dt class="text-muted">{{ 'emp.number' | t }}</dt><dd dir="ltr">{{ e.employeeNumber }}</dd>
          <dt class="text-muted">{{ 'emp.email' | t }}</dt><dd dir="ltr"><a [href]="'mailto:' + e.email">{{ e.email }}</a></dd>
          <dt class="text-muted">{{ 'emp.phone' | t }}</dt><dd dir="ltr"><a [href]="'tel:' + e.phone">{{ e.phone }}</a></dd>
          <dt class="text-muted">{{ 'att.shift' | t }}</dt><dd dir="ltr" class="tabular">{{ hm(e.scheduledStart) }}–{{ hm(e.scheduledEnd) }}</dd>
          <dt class="text-muted">{{ 'att.in' | t }}</dt>
          <dd class="flex items-center gap-2"><span class="tabular">{{ hm(e.checkInAt) }}</span>
            <span class="badge {{ badge(e.state) }}">{{ 'state.' + e.state | t }}</span></dd>
        </dl>
      </div></div>
    }

    @if (cover(); as c) {
      <div class="modal-back" appBackdrop (dismiss)="cover.set(null)"><div class="modal" (click)="$event.stopPropagation()">
        <div class="modal-head"><h2>{{ 'dash.cover' | t }} — {{ i18n.pick(c.nameAr, c.nameEn) }}</h2></div>
        @if (!available().length) { <div class="alert">{{ 'sch.noAvailable' | t }}</div> }
        <div class="field"><label>{{ 'sch.replacement' | t }}</label>
          <select [(ngModel)]="coverEmployee">@for (a of available(); track a.id) { <option [value]="a.id">{{ a.fullName }}</option> }</select></div>
        <div class="field"><label>{{ 'sch.shift' | t }}</label>
          <select [(ngModel)]="coverShift">@for (s of shifts(); track s.id) { <option [value]="s.id">{{ i18n.pick(s.nameAr, s.nameEn) }} ({{ s.startTime.substring(0,5) }}–{{ s.endTime.substring(0,5) }})</option> }</select></div>
        <div class="modal-foot">
          <button class="btn" (click)="cover.set(null)">{{ 'common.cancel' | t }}</button>
          <button class="btn primary" [disabled]="!coverEmployee || !coverShift" (click)="applyCover(c)">{{ 'sch.execute' | t }}</button>
        </div>
      </div></div>
    }`,
})
export class DashboardPage implements OnInit, OnDestroy {
  readonly i18n = inject(I18n);
  readonly auth = inject(Auth);
  private readonly api = inject(Api);
  private readonly ui = inject(Ui);
  readonly hm = hm;
  readonly data = signal<Dashboard | null>(null);
  readonly details = signal<BoardEmployee | null>(null);
  readonly cover = signal<BoardLocation | null>(null);
  readonly focus = signal<string | null>(null);

  /** Feeds the map: one entry per site with how many of its scheduled people are actually there. */
  readonly sites = computed<MapSite[]>(() => (this.data()?.board ?? []).map(l => ({
    locationId: l.locationId, nameAr: l.nameAr, nameEn: l.nameEn, color: l.color,
    latitude: l.latitude, longitude: l.longitude, radiusMeters: l.radiusMeters,
    present: l.employees.filter(e => e.state === 'Present' || e.state === 'PresentLate').length,
    scheduled: l.employees.length,
  })));
  readonly available = signal<{ id: string; fullName: string }[]>([]);
  readonly shifts = signal<Shift[]>([]);
  coverEmployee = '';
  coverShift = '';
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void { this.load(); this.timer = setInterval(() => this.load(), 30_000); }
  ngOnDestroy(): void { clearInterval(this.timer); }

  async load(): Promise<void> {
    try { this.data.set(await this.api.get<Dashboard>('dashboard')); }
    catch (e) { this.ui.error(this.api.error(e).message); }
  }

  dotClass(color: string): string {
    return ({ green: 'bg-ok text-ok', yellow: 'bg-warn text-warn', red: 'bg-bad text-bad' } as Record<string, string>)[color] ?? 'bg-muted text-muted';
  }

  badge(state: string): string {
    return ({ Present: 'green', PresentLate: 'yellow', OnExit: 'yellow', NotArrived: 'red', Absent: 'red', OnLeave: 'blue' } as Record<string, string>)[state] ?? '';
  }

  async openCover(l: BoardLocation): Promise<void> {
    const [available, shifts] = await Promise.all([
      this.api.get<{ id: string; fullName: string }[]>('schedule/available', { date: uaeToday() }),
      this.api.get<Shift[]>('shifts'),
    ]);
    this.available.set(available);
    this.shifts.set(shifts);
    this.coverEmployee = available[0]?.id ?? '';
    this.coverShift = shifts[0]?.id ?? '';
    this.cover.set(l);
  }

  async applyCover(l: BoardLocation): Promise<void> {
    const today = uaeToday();
    try {
      await this.api.post('schedule/overrides', { type: 'EmergencyCover', employeeId: this.coverEmployee, fromDate: today, toDate: today, locationId: l.locationId, shiftTemplateId: this.coverShift, reason: null });
      this.cover.set(null);
      this.ui.ok(this.i18n.t('common.saved'));
      await this.load();
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
