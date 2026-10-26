import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { RouterLink } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import type { Observable } from 'rxjs';
import { type ShownError, shownError } from '../../core/api/problem';
import { SchedulingApiService } from '../../core/api/scheduling-api.service';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { Schedule, StaffAppointment, TherapistCancelReason } from '../../core/api/types';
import { intlLocale } from '../../core/i18n/format';
import { LanguageService } from '../../core/i18n/language.service';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { ReasonDialogComponent, type ReasonDialogData, type ReasonDialogResult } from '../../shared/reason-dialog.component';
import { DEFAULT_TIME_ZONE, addDays, dayOfWeek, today, weekStart, zonedParts } from './zoned-time';

export const CANCEL_REASONS: readonly TherapistCancelReason[] = ['emergency', 'illness', 'schedule_conflict', 'technical', 'other'];
export const LATE_MINUTES: readonly number[] = [5, 10, 15, 20, 30];
export type ScheduleView = 'week' | 'day';

/** The badge an appointment shows in the grid: what needs attention first. */
export function badgeOf(a: StaffAppointment): 'cancelled' | 'missed' | 'outcome' | 'risk' | 'file' | 'late' | null {
  if (a.status === 'cancelled_by_patient' || a.status === 'cancelled_by_therapist') return 'cancelled';
  if (a.missed) return 'missed';
  if (a.canRecordOutcome && !a.outcomeRecorded && a.status === 'confirmed' && !a.canReportLate) return 'outcome';
  if (a.riskLevel >= 3) return 'risk';
  if (a.filePending) return 'file';
  if (a.therapistLateMinutes !== null && a.status === 'confirmed') return 'late';
  return null;
}

/**
 * S18 schedule: the therapist's sessions by week (Sunday to Thursday, plus a weekend day that has one) or by day,
 * with the session number and any alert. The chosen session shows its state and actions: record the outcome after
 * it starts (SB-25), say you are running late, cancel with a reason (SB-26), or start it.
 */
@Component({
  selector: 'tm-schedule',
  imports: [MatButtonModule, MatButtonToggleModule, MatProgressBarModule, RouterLink, TranslateModule, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './schedule.component.html',
  styleUrl: './schedule.component.scss',
})
export class ScheduleComponent {
  private readonly api = inject(SchedulingApiService);
  private readonly staff = inject(StaffApiService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly language = inject(LanguageService);

  protected readonly timeZone = signal(DEFAULT_TIME_ZONE);
  protected readonly view = signal<ScheduleView>('week');
  protected readonly anchor = signal(today(DEFAULT_TIME_ZONE));
  protected readonly data = signal<Schedule | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<ShownError | null>(null);
  protected readonly actionError = signal<ShownError | null>(null);
  protected readonly busy = signal(false);
  protected readonly selectedId = signal<string | null>(null);

  protected readonly from = computed(() => (this.view() === 'week' ? weekStart(this.anchor()) : this.anchor()));
  protected readonly todayDate = computed(() => today(this.timeZone()));

  /** Sunday to Thursday always; Friday and Saturday only when a session falls on them. */
  protected readonly days = computed(() => {
    if (this.view() === 'day') return [this.from()];
    const items = this.data()?.items ?? [];
    const used = new Set(items.map((a) => zonedParts(a.startsAt, this.timeZone()).date));
    return Array.from({ length: 7 }, (_, i) => addDays(this.from(), i)).filter((d) => dayOfWeek(d) < 5 || used.has(d));
  });

  /** One row per hour that has a session, as in the design. */
  protected readonly hours = computed(() => {
    const items = this.data()?.items ?? [];
    return [...new Set(items.map((a) => zonedParts(a.startsAt, this.timeZone()).hour))].sort((a, b) => a - b);
  });

  protected readonly selected = computed(() => this.data()?.items.find((a) => a.id === this.selectedId()) ?? null);

  protected readonly title = computed(() => {
    const days = this.days();
    const first = this.dayLabel(days[0], 'short');
    return days.length === 1 ? first : `${first} – ${this.dayLabel(days[days.length - 1], 'short')}`;
  });

  constructor() {
    effect(() => {
      this.language.language();
      this.from();
      untracked(() => this.load());
    });
  }

  protected load(): void {
    this.loading.set(true);
    this.api
      .schedule(this.from(), this.view() === 'week' ? 7 : 1)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (schedule) => {
          this.timeZone.set(schedule.timeZone || DEFAULT_TIME_ZONE);
          this.data.set(schedule);
          this.loadError.set(null);
          this.loading.set(false);
          if (this.selectedId() && !schedule.items.some((a) => a.id === this.selectedId())) this.selectedId.set(null);
        },
        error: (error: unknown) => {
          this.loadError.set(shownError(error, 'schedule.loadError'));
          this.loading.set(false);
        },
      });
  }

  protected setView(view: ScheduleView): void {
    this.view.set(view);
  }

  protected previous(): void {
    this.anchor.set(addDays(this.anchor(), this.view() === 'week' ? -7 : -1));
  }

  protected next(): void {
    this.anchor.set(addDays(this.anchor(), this.view() === 'week' ? 7 : 1));
  }

  protected goToday(): void {
    this.anchor.set(this.todayDate());
  }

  protected at(day: string, hour: number): StaffAppointment[] {
    return (this.data()?.items ?? []).filter((a) => {
      const p = zonedParts(a.startsAt, this.timeZone());
      return p.date === day && p.hour === hour;
    });
  }

  protected select(a: StaffAppointment): void {
    this.selectedId.set(a.id);
    this.actionError.set(null);
  }

  protected day(a: StaffAppointment): string {
    return zonedParts(a.startsAt, this.timeZone()).date;
  }

  protected badge(a: StaffAppointment): string | null {
    return badgeOf(a);
  }

  protected sessionLabel(a: StaffAppointment): string {
    return a.sequenceNumber === 1 ? this.translate.instant('schedule.first') : this.translate.instant('schedule.session', { n: a.sequenceNumber });
  }

  /** What the panel says about the chosen session. */
  protected stateKey(a: StaffAppointment): string {
    if (a.status === 'cancelled_by_therapist') return 'schedule.state.cancelledByYou';
    if (a.status === 'cancelled_by_patient') return 'schedule.state.cancelledByPatient';
    if (a.status === 'completed') return 'schedule.state.completed';
    if (a.missed) return 'schedule.state.missed';
    if (a.canRecordOutcome && !a.canReportLate) return 'schedule.state.ended';
    if (a.canReportLate) return 'schedule.state.now';
    return 'schedule.state.upcoming';
  }

  protected dayLabel(date: string, style: 'short' | 'long'): string {
    const [y, m, d] = date.split('-').map(Number);
    return new Intl.DateTimeFormat(intlLocale(this.language.language()), {
      timeZone: 'UTC',
      weekday: 'long',
      day: 'numeric',
      ...(style === 'long' ? { month: 'long' } : {}),
    }).format(new Date(Date.UTC(y, m - 1, d)));
  }

  protected time(iso: string): string {
    return new Intl.DateTimeFormat(intlLocale(this.language.language()), { timeZone: this.timeZone(), hour: 'numeric', minute: '2-digit' }).format(new Date(iso));
  }

  protected hourLabel(hour: number): string {
    return new Intl.DateTimeFormat(intlLocale(this.language.language()), { timeZone: 'UTC', hour: 'numeric', minute: '2-digit' }).format(new Date(Date.UTC(2000, 0, 1, hour)));
  }

  protected record(a: StaffAppointment, outcome: 'attended' | 'no_show'): void {
    this.run(this.staff.recordOutcome(a.id, outcome), outcome === 'attended' ? 'schedule.done.attended' : 'schedule.done.noShow');
  }

  protected runningLate(a: StaffAppointment): void {
    const data: ReasonDialogData<StaffAppointment> = {
      titleKey: 'schedule.late.title',
      bodyKey: 'schedule.late.body',
      labelKey: 'schedule.late.title',
      confirmKey: 'schedule.late.confirm',
      danger: false,
      hideText: true,
      maxLength: 0,
      levels: LATE_MINUTES,
      levelLabelKey: 'schedule.late.minutes',
      levelOptionKey: () => 'schedule.late.unit',
      submit: (minutes) => this.api.runningLate(a.id, minutes ?? LATE_MINUTES[0]),
    };
    this.afterDialog(data, 'schedule.done.late');
  }

  protected cancel(a: StaffAppointment): void {
    const data: ReasonDialogData<StaffAppointment> = {
      titleKey: 'schedule.cancel.title',
      bodyKey: 'schedule.cancel.body',
      labelKey: 'schedule.cancel.note',
      confirmKey: 'schedule.cancel.confirm',
      danger: true,
      required: false,
      maxLength: 500,
      choices: CANCEL_REASONS,
      choicesLabelKey: 'schedule.cancel.reason',
      choiceOptionKey: (c) => `schedule.reasons.${c}`,
      maxChoices: 1,
      submit: (_level, choices, text) => this.api.cancel(a.id, choices[0] as TherapistCancelReason, text.trim()),
    };
    this.afterDialog(data, 'schedule.done.cancelled');
  }

  private afterDialog(data: ReasonDialogData<StaffAppointment>, doneKey: string): void {
    this.dialog
      .open<ReasonDialogComponent<StaffAppointment>, ReasonDialogData<StaffAppointment>, ReasonDialogResult<StaffAppointment>>(ReasonDialogComponent, { data, width: '460px', direction: this.language.direction() })
      .afterClosed()
      .subscribe((result) => {
        if (result) {
          this.snackBar.open(this.translate.instant(doneKey), undefined, { duration: 4000 });
          this.load();
        }
      });
  }

  private run(call: Observable<unknown>, doneKey: string): void {
    this.busy.set(true);
    this.actionError.set(null);
    call.pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.busy.set(false);
        this.snackBar.open(this.translate.instant(doneKey), undefined, { duration: 4000 });
        this.load();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        // EC-51: something changed meanwhile; show why and refresh.
        this.actionError.set(shownError(error, 'schedule.actionError'));
        this.load();
      },
    });
  }
}
