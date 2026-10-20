import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatMenuModule } from '@angular/material/menu';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { CrisisApiService, upsertEvent } from '../../core/api/crisis-api.service';
import { type ShownError, shownError } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { OpenCrisisEvent, SessionOutcome, TherapistDashboard, TodaySession } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { CrisisHubService } from '../../core/realtime/crisis-hub.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { RiskTagComponent } from '../../shared/risk-tag.component';
import { IntakeDialogComponent, type IntakeDialogData, type IntakeDialogResult } from './intake-dialog.component';

export const OUTCOMES: readonly SessionOutcome[] = ['attended', 'no_show', 'cancelled'];

/** "morning" before 12:00, "afternoon" before 17:00, then "evening" (local time). */
export function partOfDay(date: Date): 'morning' | 'afternoon' | 'evening' {
  const hour = date.getHours();
  return hour < 12 ? 'morning' : hour < 17 ? 'afternoon' : 'evening';
}

/** S01 therapist dashboard: live high-risk alerts, the day at a glance, files to approve and today's sessions. */
@Component({
  selector: 'tm-dashboard',
  imports: [MatButtonModule, MatMenuModule, MatProgressBarModule, MatTableModule, TranslateModule, LocalDatePipe, ProblemMessageComponent, RiskTagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss',
})
export class DashboardComponent {
  private readonly staff = inject(StaffApiService);
  private readonly crisis = inject(CrisisApiService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly language = inject(LanguageService);

  protected readonly data = signal<TherapistDashboard | null>(null);
  protected readonly loading = signal(true);
  protected readonly loadError = signal<ShownError | null>(null);
  /** Per alert: the last problem from acknowledge/refer (e.g. 409, someone else took it). */
  protected readonly alertErrors = signal<Record<string, ShownError>>({});
  protected readonly busyAlerts = signal<ReadonlySet<string>>(new Set());
  protected readonly busySessions = signal<ReadonlySet<string>>(new Set());
  protected readonly sessionErrors = signal<Record<string, ShownError>>({});

  protected readonly outcomes = OUTCOMES;
  protected readonly pendingColumns = ['pseudonym', 'risk', 'phq9', 'gad7', 'firstSession', 'action'];
  protected readonly greetingKey = computed(() => `dashboard.greeting.${partOfDay(new Date())}`);
  protected readonly alerts = computed(() => this.data()?.alerts ?? []);

  constructor() {
    const hub = inject(CrisisHubService);
    hub.acquire(this.destroyRef);
    hub.alerts.pipe(takeUntilDestroyed()).subscribe((event) => this.applyAlert(event));
    hub.reconnected.pipe(takeUntilDestroyed()).subscribe(() => this.load());
    // Reload in the new language so server-localized titles and labels switch too.
    effect(() => {
      this.language.language();
      untracked(() => this.load());
    });
  }

  protected load(): void {
    this.loading.set(true);
    this.staff
      .dashboard()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (data) => {
          this.data.set(data);
          this.loadError.set(null);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loadError.set(shownError(error, 'dashboard.loadError'));
          this.loading.set(false);
        },
      });
  }

  protected acknowledge(alert: OpenCrisisEvent): void {
    this.runAlert(alert.eventId, this.crisis.acknowledge(alert.eventId), 'dashboard.alerts.acknowledgedDone');
  }

  protected refer(alert: OpenCrisisEvent): void {
    this.runAlert(alert.eventId, this.crisis.action(alert.eventId, { kind: 'handover' }), 'dashboard.alerts.referred');
  }

  protected recordOutcome(session: TodaySession, outcome: SessionOutcome): void {
    const id = session.appointmentId;
    this.busySessions.update((s) => new Set(s).add(id));
    this.sessionErrors.update(({ [id]: _, ...rest }) => rest);
    this.staff.recordOutcome(id, outcome).subscribe({
      next: () => {
        this.data.update((d) => d && { ...d, todaySessions: d.todaySessions.map((s) => (s.appointmentId === id ? { ...s, outcomeRecorded: true } : s)) });
        this.done(id, this.busySessions);
        this.notify(this.translate.instant('dashboard.today.outcomeSaved', { outcome: this.translate.instant(`dashboard.outcome.${outcome}`) }));
      },
      error: (error: unknown) => {
        this.sessionErrors.update((e) => ({ ...e, [id]: shownError(error, 'common.error') }));
        this.done(id, this.busySessions);
      },
    });
  }

  protected openIntake(pseudonymId: string): void {
    const ref = this.dialog.open<IntakeDialogComponent, IntakeDialogData, IntakeDialogResult>(IntakeDialogComponent, {
      data: { pseudonymId },
      direction: this.language.direction(),
      width: '640px',
      maxWidth: '95vw',
      autoFocus: 'dialog',
      restoreFocus: true,
    });
    ref.afterClosed().subscribe((result) => {
      if (result === 'approved') {
        this.data.update((d) => {
          if (!d) {
            return d;
          }
          const pendingProfiles = d.pendingProfiles.filter((p) => p.pseudonymId !== pseudonymId);
          const removed = d.pendingProfiles.length - pendingProfiles.length;
          return { ...d, pendingProfiles, stats: { ...d.stats, pendingProfiles: Math.max(0, d.stats.pendingProfiles - removed) } };
        });
        this.notify(this.translate.instant('intake.approvedDone'));
      }
    });
  }

  private applyAlert(event: OpenCrisisEvent): void {
    this.data.update((d) => d && { ...d, alerts: upsertEvent(d.alerts, event) });
  }

  private runAlert(id: string, request: ReturnType<CrisisApiService['acknowledge']>, doneKey: string): void {
    this.busyAlerts.update((s) => new Set(s).add(id));
    this.alertErrors.update(({ [id]: _, ...rest }) => rest);
    request.subscribe({
      next: (event) => {
        this.applyAlert(event);
        this.done(id, this.busyAlerts);
        this.notify(this.translate.instant(doneKey));
      },
      error: (error: unknown) => {
        this.alertErrors.update((e) => ({ ...e, [id]: shownError(error, 'common.error') }));
        this.done(id, this.busyAlerts);
      },
    });
  }

  private done(id: string, set: typeof this.busyAlerts): void {
    set.update((s) => {
      const next = new Set(s);
      next.delete(id);
      return next;
    });
  }

  private notify(message: string): void {
    this.snackBar.open(message, undefined, { duration: 4000, direction: this.language.direction(), politeness: 'polite' });
  }
}
