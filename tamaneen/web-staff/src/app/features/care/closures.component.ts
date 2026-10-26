import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { type ShownError, shownError } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { ClosureQueueItem } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';

/** End-of-care summaries waiting for a supervisor's sign-off (S57), oldest first; each opens /patients/:id/close. */
@Component({
  selector: 'tm-closures',
  imports: [MatProgressBarModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" [attr.aria-label]="'common.loading' | translate" />
    }
    <h1 class="title">{{ 'care.closures.title' | translate }}</h1>
    <tm-problem-message [error]="error()" />
    <section class="tm-card">
      @if (items().length) {
        <ul class="list">
          @for (c of items(); track c.id) {
            <li data-testid="closure-row">
              <a [routerLink]="['/patients', c.pseudonymId, 'close']" dir="ltr" class="pseudonym">{{ c.pseudonymId }}</a>
              <span>{{ c.therapistName }}</span>
              <span class="tm-muted">{{ 'care.close.reasons.' + c.reasonCode | translate }} · {{ c.submittedAt | tmDate: 'dayTime' : language.language() }}</span>
              @if (c.blocked) { <span class="tm-tag tm-risk-3" data-testid="closure-blocked">{{ 'care.closures.blocked' | translate }}</span> }
            </li>
          }
        </ul>
      } @else if (!loading()) {
        <p class="tm-muted" data-testid="closures-empty">{{ 'care.closures.empty' | translate }}</p>
      }
    </section>
  `,
  styles: `
    .title { margin: 0 0 16px; font-size: 24px; font-weight: 700; }
    .list { list-style: none; margin: 0; padding: 0; }
    .list > li { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; padding-block: 12px; border-block-end: 1px solid var(--tm-line); }
    .pseudonym { font-weight: 700; unicode-bidi: isolate; color: var(--tm-primary); }
  `,
})
export class ClosuresComponent {
  private readonly api = inject(StaffApiService);
  protected readonly language = inject(LanguageService);
  protected readonly items = signal<ClosureQueueItem[]>([]);
  protected readonly loading = signal(true);
  protected readonly error = signal<ShownError | null>(null);

  constructor() {
    this.api
      .closureQueue()
      .pipe(takeUntilDestroyed(inject(DestroyRef)))
      .subscribe({
        next: (items) => {
          this.items.set(items);
          this.loading.set(false);
        },
        error: (e: unknown) => {
          this.error.set(shownError(e, 'care.closures.loadError'));
          this.loading.set(false);
        },
      });
  }
}
