import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { type ShownError, shownError } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { CaseloadItem } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { RiskTagComponent } from '../../shared/risk-tag.component';

/** "My patients": the caseload, highest risk first, each linking to the clinical file (S02). */
@Component({
  selector: 'tm-patients',
  imports: [MatProgressBarModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent, RiskTagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" [attr.aria-label]="'common.loading' | translate" />
    }
    <h1 class="title">{{ 'patients.title' | translate }}</h1>
    <tm-problem-message [error]="error()" />
    <section class="tm-card">
      @if (items().length) {
        <ul class="list">
          @for (p of items(); track p.pseudonymId) {
            <li class="row" data-testid="caseload-row">
              <a [routerLink]="['/patients', p.pseudonymId]" class="pseudonym" dir="ltr">{{ p.pseudonymId }}</a>
              <tm-risk-tag [level]="p.riskLevel" />
              <span class="tm-muted">{{ (p.fileApproved ? 'patients.approved' : 'patients.awaiting') | translate }}</span>
              <span class="tm-muted">
                @if (p.nextSessionAt) { {{ 'patients.next' | translate: { time: (p.nextSessionAt | tmDate: 'dayTime' : language.language()) } }} } @else { {{ 'patients.noSession' | translate }} }
              </span>
            </li>
          }
        </ul>
      } @else if (!loading()) {
        <p class="tm-muted">{{ 'patients.empty' | translate }}</p>
      }
    </section>
  `,
  styles: `
    .title { margin: 0 0 16px; font-size: 24px; font-weight: 700; }
    .list { list-style: none; margin: 0; padding: 0; }
    .row { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; padding-block: 12px; border-block-end: 1px solid var(--tm-line); }
    .row:last-child { border-block-end: 0; }
    .pseudonym { font-weight: 700; unicode-bidi: isolate; min-width: 130px; color: var(--tm-primary); }
  `,
})
export class PatientsComponent {
  private readonly api = inject(StaffApiService);
  protected readonly language = inject(LanguageService);
  protected readonly items = signal<CaseloadItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<ShownError | null>(null);

  constructor() {
    this.api
      .caseload()
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe({
        next: (items) => {
          this.items.set(items);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.error.set(shownError(error, 'patients.loadError'));
          this.loading.set(false);
        },
      });
  }
}
