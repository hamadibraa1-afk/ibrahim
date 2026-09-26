import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { hm, hmin, uaeToday } from '../../core/format';
import { I18n, TPipe } from '../../core/i18n';
import { Ui } from '../../core/ui';

interface Day { date: string; locationName: string; scheduledStart: string; scheduledEnd: string; checkInAt: string | null; checkOutAt: string | null; status: string; netWorkMinutes: number; lateUnexcused: number; overtimeMinutes: number; }
interface Month { year: number; month: number; scheduledDays: number; presentDays: number; absentDays: number; leaveDays: number; netWorkMinutes: number; lateMinutes: number; overtimeMinutes: number; days: Day[]; }
interface Profile { fullName: string; jobTitle: string | null; department: string | null; branch: string | null; manager: string | null; scheduleName: string | null; }
interface Return { id: string; expectedDate: string; actualDate: string | null; status: string; lateDays: number; }
interface Warning { id: string; levelName: string; reason: string; issuedAt: string; objection: string | null; }

@Component({
  selector: 'app-my-overview',
  standalone: true,
  imports: [RouterLink, TPipe],
  template: `
    @if (profile(); as p) {
      <section class="card-pad mb-4">
        <h1 class="text-lg">{{ p.fullName }}</h1>
        <div class="mt-1 flex flex-wrap gap-x-5 gap-y-1 text-sm text-muted">
          @if (p.jobTitle) { <span>{{ p.jobTitle }}</span> }
          @if (p.department) { <span>{{ p.department }}</span> }
          @if (p.branch) { <span>{{ p.branch }}</span> }
          @if (p.manager) { <span>{{ 'hr.emp.manager' | t }}: {{ p.manager }}</span> }
        </div>
      </section>
    }

    @if (today(); as t) {
      <section class="card-pad mb-4">
        <div class="text-xs text-muted">{{ 'my.today' | t }}</div>
        <div class="mt-1 flex flex-wrap items-center gap-3">
          <span class="text-2xl font-bold tabular" dir="ltr">{{ hm(t.checkInAt) }}</span>
          <span class="badge" [class.green]="t.checkInAt" [class.red]="t.status === 'Absent'">{{ 'state.' + t.status | t }}</span>
          <span class="text-sm text-muted tabular" dir="ltr">{{ hm(t.scheduledStart) }}–{{ hm(t.scheduledEnd) }}</span>
          @if (t.lateUnexcused) { <span class="text-sm font-semibold text-bad">{{ 'att.late' | t }}: {{ hmin(t.lateUnexcused) }}</span> }
        </div>
      </section>
    }

    <div class="grid-cards">
      <a class="card stat" routerLink="/my/attendance"><div class="num">{{ month()?.presentDays ?? 0 }}</div><div class="lbl">{{ 'my.presentDays' | t }}</div></a>
      <a class="card stat" routerLink="/my/attendance"><div class="num" [class.text-bad]="(month()?.absentDays ?? 0) > 0">{{ month()?.absentDays ?? 0 }}</div><div class="lbl">{{ 'dash.absent' | t }}</div></a>
      <a class="card stat" routerLink="/my/attendance"><div class="num">{{ hmin(month()?.lateMinutes ?? 0) }}</div><div class="lbl">{{ 'att.late' | t }}</div></a>
      <a class="card stat" routerLink="/my/attendance"><div class="num">{{ hmin(month()?.netWorkMinutes ?? 0) }}</div><div class="lbl">{{ 'att.net' | t }}</div></a>
      <a class="card stat" routerLink="/my/requests"><div class="num">{{ month()?.leaveDays ?? 0 }}</div><div class="lbl">{{ 'dash.onLeave' | t }}</div></a>
      <a class="card stat" routerLink="/my/payslips"><div class="num">{{ hmin(month()?.overtimeMinutes ?? 0) }}</div><div class="lbl">{{ 'att.ot' | t }}</div></a>
    </div>

    @for (r of returns(); track r.id) {
      @if (r.status !== 'Confirmed') {
        <section class="card-pad mb-4 border-warn/50">
          <h2>{{ 'my.returnTitle' | t }}</h2>
          <p class="mt-1 text-sm text-muted">{{ 'my.returnLead' | t }} <span dir="ltr" class="tabular">{{ r.expectedDate }}</span></p>
          @if (r.actualDate) {
            <p class="mt-2 text-sm">{{ 'my.returnReported' | t }} <span dir="ltr">{{ r.actualDate }}</span>
              @if (r.lateDays) { <span class="text-bad"> (+{{ r.lateDays }})</span> }</p>
          } @else {
            <button class="btn primary mt-3" (click)="reportReturn(r)">{{ 'my.returnAction' | t }}</button>
          }
        </section>
      }
    }

    @if (warnings().length) {
      <h2 class="mb-2">{{ 'my.warnings' | t }}</h2>
      @for (w of warnings(); track w.id) {
        <div class="card-pad mb-2">
          <div class="flex items-center gap-2"><strong>{{ w.levelName }}</strong>
            <span class="muted small" dir="ltr">{{ w.issuedAt.substring(0,10) }}</span></div>
          <p class="mt-1 text-sm">{{ w.reason }}</p>
          @if (!w.objection) { <button class="btn sm mt-2" (click)="object(w)">{{ 'my.object' | t }}</button> }
          @else { <div class="mt-2 text-xs text-muted">{{ 'my.objectionFiled' | t }}</div> }
        </div>
      }
    }`,
})
export class MyOverviewPage implements OnInit {
  private readonly api = inject(Api);
  readonly i18n = inject(I18n);
  private readonly ui = inject(Ui);
  readonly hm = hm;
  readonly hmin = hmin;
  readonly profile = signal<Profile | null>(null);
  readonly month = signal<Month | null>(null);
  readonly warnings = signal<Warning[]>([]);
  readonly returns = signal<Return[]>([]);
  readonly today = computed(() => this.month()?.days.find(d => d.date === uaeToday()) ?? null);

  async ngOnInit(): Promise<void> {
    try {
      const [profile, month, warnings, returns] = await Promise.all([
        this.api.get<Profile>('my/profile'),
        this.api.get<Month>('my/attendance'),
        this.api.get<Warning[]>('my/warnings'),
        this.api.get<Return[]>('my/returns'),
      ]);
      this.profile.set(profile);
      this.month.set(month);
      this.warnings.set(warnings);
      this.returns.set(returns);
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  /** Reporting back from leave; a late return is recorded, not hidden. */
  async reportReturn(r: Return): Promise<void> {
    const note = await this.ui.prompt(this.i18n.t('my.returnAction'), this.i18n.t('sch.notes'), false);
    if (note === null) return;
    try {
      await this.api.post(`my/returns/${r.id}/report`, { actualDate: uaeToday(), note: note || null });
      this.returns.set(await this.api.get<Return[]>('my/returns'));
      this.ui.ok(this.i18n.t('common.saved'));
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }

  async object(w: Warning): Promise<void> {
    const text = await this.ui.prompt('اعتراض', 'اذكر سبب اعتراضك');
    if (!text) return;
    try {
      await this.api.post(`my/warnings/${w.id}/object`, { text });
      this.warnings.set(await this.api.get<Warning[]>('my/warnings'));
    } catch (e) { this.ui.error(this.api.error(e).message); }
  }
}
