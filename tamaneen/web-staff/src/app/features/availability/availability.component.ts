import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar } from '@angular/material/snack-bar';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { type ShownError, shownError } from '../../core/api/problem';
import { SchedulingApiService } from '../../core/api/scheduling-api.service';
import type { Availability, AvailabilityBreak, AvailabilitySlot, SaveAvailabilityRequest } from '../../core/api/types';
import { intlLocale } from '../../core/i18n/format';
import { LanguageService } from '../../core/i18n/language.service';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { DEFAULT_TIME_ZONE, addDays, minutesOf, today, zonedParts, zonedToUtc } from '../schedule/zoned-time';

export interface Period { start: string; end: string }
export interface DayPlan { day: number; enabled: boolean; periods: Period[] }
export type PeriodError = 'overlap' | 'end_before_start' | null;

/** Sunday first, as the working week runs. */
export const DAYS: readonly number[] = [0, 1, 2, 3, 4, 5, 6];
export const NEW_PERIOD: Period = { start: '10:00', end: '13:00' };

/** The first problem among a day's periods: an end not after its start, or two periods that overlap (touching is fine). */
export function dayError(periods: readonly Period[]): PeriodError {
  if (periods.some((p) => !p.start || !p.end || minutesOf(p.end) <= minutesOf(p.start))) return 'end_before_start';
  const sorted = [...periods].sort((a, b) => minutesOf(a.start) - minutesOf(b.start));
  return sorted.some((p, i) => i > 0 && minutesOf(p.start) < minutesOf(sorted[i - 1].end)) ? 'overlap' : null;
}

export function toPlans(a: Availability): DayPlan[] {
  return DAYS.map((day) => {
    const periods = a.weekly.filter((p) => p.day === day).map((p) => ({ start: p.start, end: p.end }));
    return { day, enabled: periods.length > 0, periods: periods.length > 0 ? periods : [{ ...NEW_PERIOD }] };
  });
}

export function toRequest(plans: readonly DayPlan[], sessionMinutes: number, bufferMinutes: number, exceptions: readonly AvailabilityBreak[]): SaveAvailabilityRequest {
  return {
    sessionMinutes,
    bufferMinutes,
    weekly: plans.filter((d) => d.enabled).flatMap((d) => d.periods.map((p) => ({ day: d.day, start: p.start, end: p.end }))),
    exceptions: [...exceptions],
  };
}

/**
 * S19 availability: the usual week (a switch and periods per day), the session length and gap, leave and absences,
 * and one-off times. Saving generates the bookable times of the next eight weeks; booked times are never removed and
 * show locked, and the ones the new pattern no longer covers are listed first (SB-28). A failed save keeps the edits.
 */
@Component({
  selector: 'tm-availability',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatProgressBarModule, MatSelectModule, MatSlideToggleModule, TranslateModule, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './availability.component.html',
  styleUrl: './availability.component.scss',
})
export class AvailabilityComponent {
  private readonly api = inject(SchedulingApiService);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly language = inject(LanguageService);

  protected readonly data = signal<Availability | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<ShownError | null>(null);
  protected readonly saveError = signal<ShownError | null>(null);
  protected readonly slotError = signal<ShownError | null>(null);
  protected readonly saving = signal(false);

  protected readonly plans = signal<DayPlan[]>([]);
  protected readonly sessionMinutes = signal(50);
  protected readonly bufferMinutes = signal(10);
  protected readonly exceptions = signal<AvailabilityBreak[]>([]);
  /** Booked times the change would leave outside the pattern: shown before saving, then "Save anyway". */
  protected readonly pendingAffected = signal<AvailabilitySlot[] | null>(null);

  protected readonly addingException = signal(false);
  protected readonly exceptionKind = signal<'leave' | 'absence'>('leave');
  protected readonly exceptionFrom = signal('');
  protected readonly exceptionTo = signal('');
  protected readonly absenceStart = signal('09:00');
  protected readonly absenceEnd = signal('12:00');

  protected readonly oneOffDate = signal('');
  protected readonly oneOffTime = signal('');

  protected readonly days = DAYS;
  protected readonly timeZone = computed(() => this.data()?.timeZone ?? DEFAULT_TIME_ZONE);
  protected readonly errors = computed(() => this.plans().map((d) => (d.enabled ? dayError(d.periods) : null)));
  protected readonly invalid = computed(() => this.errors().some((e) => e !== null));
  protected readonly affected = computed(() => this.pendingAffected() ?? this.data()?.affected ?? []);
  protected readonly exceptionValid = computed(() => {
    if (!this.exceptionFrom()) return false;
    if (this.exceptionKind() === 'leave') return !!this.exceptionTo() && this.exceptionTo() >= this.exceptionFrom();
    return minutesOf(this.absenceEnd()) > minutesOf(this.absenceStart());
  });

  /** Upcoming times by local date, booked ones included (locked). */
  protected readonly slotDays = computed(() => {
    const tz = this.timeZone();
    const groups = new Map<string, AvailabilitySlot[]>();
    for (const s of this.data()?.slots ?? []) {
      if (s.status === 'blocked') continue;
      const date = zonedParts(s.startsAt, tz).date;
      groups.set(date, [...(groups.get(date) ?? []), s]);
    }
    return [...groups.entries()].map(([date, slots]) => ({ date, slots }));
  });

  constructor() {
    effect(() => {
      this.language.language();
      untracked(() => this.load());
    });
  }

  protected load(): void {
    this.loading.set(true);
    this.api
      .availability()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (a) => {
          this.apply(a);
          this.loadError.set(null);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loadError.set(shownError(error, 'availability.loadError'));
          this.loading.set(false);
        },
      });
  }

  private apply(a: Availability): void {
    this.data.set(a);
    this.plans.set(toPlans(a));
    this.sessionMinutes.set(a.sessionMinutes);
    this.bufferMinutes.set(a.bufferMinutes);
    this.exceptions.set(a.exceptions);
    this.pendingAffected.set(null);
    const start = today(a.timeZone);
    this.oneOffDate.set(this.oneOffDate() || addDays(start, 1));
  }

  private edited(): void {
    this.pendingAffected.set(null);
    this.saveError.set(null);
  }

  protected toggleDay(day: number, enabled: boolean): void {
    this.plans.update((plans) => plans.map((d) => (d.day === day ? { ...d, enabled } : d)));
    this.edited();
  }

  protected setPeriod(day: number, index: number, field: keyof Period, value: string): void {
    this.plans.update((plans) => plans.map((d) => (d.day === day ? { ...d, periods: d.periods.map((p, i) => (i === index ? { ...p, [field]: value } : p)) } : d)));
    this.edited();
  }

  protected addPeriod(day: number): void {
    this.plans.update((plans) =>
      plans.map((d) => {
        if (d.day !== day) return d;
        const last = d.periods[d.periods.length - 1];
        const start = last ? last.end : NEW_PERIOD.start;
        const end = `${String(Math.min(23, Math.floor(minutesOf(start) / 60) + 2)).padStart(2, '0')}:${start.slice(3)}`;
        return { ...d, enabled: true, periods: [...d.periods, { start, end }] };
      }),
    );
    this.edited();
  }

  protected removePeriod(day: number, index: number): void {
    this.plans.update((plans) => plans.map((d) => (d.day === day ? { ...d, periods: d.periods.filter((_, i) => i !== index) } : d)));
    this.edited();
  }

  protected setSession(value: number): void {
    this.sessionMinutes.set(value);
    this.edited();
  }

  protected setBuffer(value: number): void {
    this.bufferMinutes.set(value);
    this.edited();
  }

  protected addException(): void {
    if (!this.exceptionValid()) return;
    const tz = this.timeZone();
    const from = this.exceptionFrom();
    const entry: AvailabilityBreak =
      this.exceptionKind() === 'leave'
        ? { kind: 'leave', startsAt: zonedToUtc(from, '00:00', tz), endsAt: zonedToUtc(addDays(this.exceptionTo(), 1), '00:00', tz) }
        : { kind: 'absence', startsAt: zonedToUtc(from, this.absenceStart(), tz), endsAt: zonedToUtc(from, this.absenceEnd(), tz) };
    this.exceptions.update((list) => [...list, entry].sort((a, b) => a.startsAt.localeCompare(b.startsAt)));
    this.addingException.set(false);
    this.exceptionFrom.set('');
    this.exceptionTo.set('');
    this.edited();
  }

  protected removeException(index: number): void {
    this.exceptions.update((list) => list.filter((_, i) => i !== index));
    this.edited();
  }

  /** Saves, unless booked times would fall outside the new pattern: then the list shows first and the next click saves. */
  protected save(): void {
    if (this.invalid() || this.saving()) return;
    const request = toRequest(this.plans(), this.sessionMinutes(), this.bufferMinutes(), this.exceptions());
    this.saving.set(true);
    this.saveError.set(null);
    if (this.pendingAffected() === null) {
      this.api
        .preview(request)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          next: (preview) => {
            if (preview.affected.length > 0) {
              this.saving.set(false);
              this.pendingAffected.set(preview.affected);
            } else {
              this.commit(request);
            }
          },
          error: (error: unknown) => this.failed(error),
        });
    } else {
      this.commit(request);
    }
  }

  private commit(request: SaveAvailabilityRequest): void {
    this.api
      .save(request)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (result) => {
          this.saving.set(false);
          this.apply(result.availability);
          this.snackBar.open(this.translate.instant('availability.saved', { added: result.added, removed: result.removed }), undefined, { duration: 5000 });
        },
        error: (error: unknown) => this.failed(error),
      });
  }

  private failed(error: unknown): void {
    this.saving.set(false);
    this.saveError.set(shownError(error, 'availability.saveError'));
  }

  protected addOneOff(): void {
    if (!this.oneOffDate() || !this.oneOffTime()) return;
    this.slotError.set(null);
    this.api
      .addSlot(zonedToUtc(this.oneOffDate(), this.oneOffTime(), this.timeZone()))
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => {
          this.oneOffTime.set('');
          this.snackBar.open(this.translate.instant('availability.oneOff.added'), undefined, { duration: 4000 });
          this.reloadSlots();
        },
        error: (error: unknown) => this.slotError.set(shownError(error, 'availability.slotError')),
      });
  }

  protected removeSlot(slot: AvailabilitySlot): void {
    this.slotError.set(null);
    this.api
      .removeSlot(slot.slotId)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: () => this.reloadSlots(),
        error: (error: unknown) => {
          this.slotError.set(shownError(error, 'availability.slotError'));
          this.reloadSlots();
        },
      });
  }

  /** Refreshes the times only, so unsaved edits to the pattern stay. */
  private reloadSlots(): void {
    this.api
      .availability()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (a) => this.data.update((d) => (d ? { ...d, slots: a.slots, affected: a.affected } : a)) });
  }

  protected zonedDate(slot: AvailabilitySlot): string {
    return zonedParts(slot.startsAt, this.timeZone()).date;
  }

  protected locked(slot: AvailabilitySlot): boolean {
    return slot.status === 'booked' || slot.status === 'held';
  }

  protected dayName(day: number): string {
    // 2023-01-01 was a Sunday.
    return new Intl.DateTimeFormat(intlLocale(this.language.language()), { timeZone: 'UTC', weekday: 'long' }).format(new Date(Date.UTC(2023, 0, 1 + day)));
  }

  protected dateLabel(date: string): string {
    const [y, m, d] = date.split('-').map(Number);
    return new Intl.DateTimeFormat(intlLocale(this.language.language()), { timeZone: 'UTC', weekday: 'short', day: 'numeric', month: 'short' }).format(new Date(Date.UTC(y, m - 1, d)));
  }

  protected time(iso: string): string {
    return new Intl.DateTimeFormat(intlLocale(this.language.language()), { timeZone: this.timeZone(), hour: 'numeric', minute: '2-digit' }).format(new Date(iso));
  }

  protected range(b: AvailabilityBreak): string {
    const tz = this.timeZone();
    const from = zonedParts(b.startsAt, tz);
    const to = zonedParts(b.endsAt, tz);
    if (b.kind === 'leave') {
      const last = to.time === '00:00' ? addDays(to.date, -1) : to.date;
      return last === from.date ? this.dateLabel(from.date) : `${this.dateLabel(from.date)} – ${this.dateLabel(last)}`;
    }
    return `${this.dateLabel(from.date)} ${this.time(b.startsAt)} – ${this.time(b.endsAt)}`;
  }
}
