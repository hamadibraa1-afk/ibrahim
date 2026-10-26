import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { type ShownError, shownError, statusOf } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { ManualCrisisResult, PatientOverview, RiskLevel } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/session/session.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { RiskTagComponent } from '../../shared/risk-tag.component';
import { careUi } from './care-dialogs';
import { PatientTabsComponent } from './patient-tabs.component';

/**
 * S14 patient overview (/patients/:id), the first page of the patient's file: risk, time in care, the next and past
 * sessions with their notes, the mood trend (with a table for screen readers), the plan's goals, this week's homework,
 * the safety plan and alerts. The header raises the risk (SB-17), raises a crisis (SB-18) or opens the end of care
 * (S57). Every opening is audited on the server; a patient who is not assigned is "not found" (EC-42).
 */
@Component({
  selector: 'tm-patient-overview',
  imports: [DecimalPipe, MatButtonModule, MatProgressBarModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent, RiskTagComponent, PatientTabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './patient-overview.component.html',
  styleUrl: './care.scss',
})
export class PatientOverviewComponent {
  readonly id = input.required<string>();

  private readonly api = inject(StaffApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ui = careUi();
  protected readonly language = inject(LanguageService);
  protected readonly session = inject(SessionService);

  protected readonly data = signal<PatientOverview | null>(null);
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly weeksInCare = computed(() => {
    const since = this.data()?.careStartedAt;
    return since ? Math.max(0, Math.floor((Date.now() - new Date(since).getTime()) / (7 * 86_400_000))) : null;
  });
  /** Points of the mood sparkline in a 0..100 × 0..40 box (score 0–10). */
  protected readonly moodPath = computed(() => {
    const mood = this.data()?.mood ?? [];
    if (mood.length < 2) {
      return '';
    }
    return mood.map((m, i) => `${((i / (mood.length - 1)) * 100).toFixed(1)},${(40 - m.score * 4).toFixed(1)}`).join(' ');
  });

  constructor() {
    effect(() => {
      const id = this.id();
      this.language.language();
      untracked(() => this.load(id));
    });
  }

  protected load(id = this.id()): void {
    this.loading.set(true);
    this.api
      .overview(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (o) => {
          this.data.set(o);
          this.loading.set(false);
          this.error.set(null);
        },
        error: (e: unknown) => {
          this.loading.set(false);
          if (statusOf(e) === 404) {
            this.notFound.set(true);
          } else {
            this.error.set(shownError(e, 'care.overview.loadError'));
          }
        },
      });
  }

  /** The session note can be written from the start of a session. */
  protected started(startsAt: string): boolean {
    return new Date(startsAt).getTime() <= Date.now();
  }

  protected raiseRisk(): void {
    const o = this.data();
    if (!o) {
      return;
    }
    this.ui
      .open<{ riskLevel: RiskLevel }>({
        titleKey: 'file.riskDialog.title',
        bodyKey: 'file.riskDialog.body',
        labelKey: 'file.riskDialog.reason',
        confirmKey: 'file.riskDialog.confirm',
        danger: false,
        maxLength: 1000,
        levels: ([1, 2, 3, 4] as const).filter((l) => l > o.riskLevel),
        levelLabelKey: 'file.riskDialog.level',
        submit: (level, _c, reason) => this.api.raiseRisk(o.pseudonymId, level as RiskLevel, reason),
      })
      .subscribe((r) => {
        if (r) {
          this.ui.notify('file.riskRaised');
          this.load();
        }
      });
  }

  protected manualCrisis(): void {
    const o = this.data();
    if (!o) {
      return;
    }
    this.ui
      .open<ManualCrisisResult>({
        titleKey: 'file.crisisDialog.title',
        bodyKey: 'file.crisisDialog.body',
        labelKey: 'file.crisisDialog.note',
        confirmKey: 'file.crisisDialog.confirm',
        danger: true,
        maxLength: 2000,
        levels: [3, 4],
        levelLabelKey: 'file.crisisDialog.level',
        submit: (level, _c, note) => this.api.manualCrisis(o.pseudonymId, level as 3 | 4, note),
      })
      .subscribe((r) => {
        if (r) {
          this.ui.notify('file.crisisRaised');
          void this.router.navigate(['/crisis', r.value.crisisEventId]);
        }
      });
  }
}
