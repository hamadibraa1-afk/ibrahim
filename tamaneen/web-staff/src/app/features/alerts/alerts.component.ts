import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { interval } from 'rxjs';
import { AccountApiService } from '../../core/api/account-api.service';
import { CrisisApiService } from '../../core/api/crisis-api.service';
import { type ShownError, shownError } from '../../core/api/problem';
import type { Alert, AlertStatusFilter, CrisisEventDetail } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { ReasonDialogComponent, type ReasonDialogData, type ReasonDialogResult } from '../../shared/reason-dialog.component';
import { RiskTagComponent } from '../../shared/risk-tag.component';

/** S20: «انقطاع لحظي: استطلاع كل 5 ثوانٍ». */
export const ALERTS_POLL_MS = 5_000;
export const REFER_MAX_LENGTH = 1000;

/** History label of a closed event: false alarm, contacted, emergency services, unreachable. */
export function outcomeKey(alert: Alert): string {
  return alert.closeOutcome ? `alerts.outcome.${alert.closeOutcome}` : 'alerts.outcome.closed';
}

/**
 * S20 «التنبيهات»: crisis alerts for my patients, open now and the last 30 days. Acknowledging (SB-10) stops the
 * escalation only if it is the first acknowledgement; «إحالة للمناوب مع ملاحظة» (SB-11) needs a note up to 1000
 * characters and leaves the event with the on-call. «فتح الحدث» opens the console. Risk alerts cannot be muted.
 * The list refreshes every 5 seconds; a failed refresh keeps what is shown and offers a retry.
 */
@Component({
  selector: 'tm-alerts',
  imports: [MatButtonModule, MatButtonToggleModule, MatProgressBarModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent, RiskTagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" [attr.aria-label]="'common.loading' | translate" />
    }
    <h1 class="title">{{ 'alerts.title' | translate }}</h1>
    <mat-button-toggle-group [value]="status()" (change)="setStatus($event.value)" [attr.aria-label]="'alerts.filters' | translate" class="filters">
      <mat-button-toggle value="open" data-testid="status-open">{{ 'alerts.open' | translate: { count: openCount() } }}</mat-button-toggle>
      <mat-button-toggle value="handled" data-testid="status-handled">{{ 'alerts.handled' | translate }}</mat-button-toggle>
      <mat-button-toggle value="all" data-testid="status-all">{{ 'alerts.all' | translate }}</mat-button-toggle>
    </mat-button-toggle-group>
    <tm-problem-message [error]="error()" />
    @if (error()) {
      <button mat-stroked-button type="button" (click)="load()" data-testid="retry">{{ 'alerts.retry' | translate }}</button>
    }

    @if (status() !== 'handled') {
      <section class="block" aria-labelledby="open-title">
        <h2 id="open-title" class="cdk-visually-hidden">{{ 'alerts.openTitle' | translate }}</h2>
        @for (a of open(); track a.eventId) {
          <article class="tm-card alert" data-testid="open-alert">
            <div class="line">
              <tm-risk-tag [level]="a.level" />
              <strong dir="ltr" class="pseudonym">{{ a.pseudonymId }}</strong>
              @if (a.ruleIds.length) { <span class="tm-muted">· {{ 'alerts.rules' | translate: { rules: a.ruleIds.join(', ') } }}</span> }
            </div>
            <p class="tm-muted meta">
              {{ 'alerts.source.' + a.source | translate }} · {{ a.openedAt | tmDate: 'dayTime' : language.language() }} ·
              @if (a.acknowledgedBy) { {{ 'alerts.ackedBy' | translate: { name: a.acknowledgedBy } }} } @else { <strong class="pending">{{ 'alerts.notAcked' | translate }}</strong> }
            </p>
            <div class="actions">
              @if (a.status === 'Open') {
                <button mat-flat-button color="primary" type="button" (click)="acknowledge(a)" [disabled]="busy() === a.eventId" data-testid="ack">{{ 'alerts.acknowledge' | translate }}</button>
              }
              <button mat-stroked-button type="button" (click)="refer(a)" data-testid="refer">{{ 'alerts.refer' | translate }}</button>
              <a mat-button [routerLink]="['/crisis', a.eventId]" data-testid="open-event">{{ 'alerts.openEvent' | translate }}</a>
            </div>
          </article>
        } @empty {
          @if (!loading()) { <p class="tm-card tm-muted" data-testid="no-open">{{ 'alerts.noneOpen' | translate }}</p> }
        }
      </section>
    }

    @if (status() !== 'open') {
      <section class="block tm-card" aria-labelledby="history-title">
        <h2 id="history-title" class="tm-card-title">{{ 'alerts.history' | translate }}</h2>
        <ul class="list">
          @for (a of handled(); track a.eventId) {
            <li class="row" data-testid="history-alert">
              <tm-risk-tag [level]="a.level" />
              <span dir="ltr" class="pseudonym">{{ a.pseudonymId }}</span>
              <span class="tm-muted">{{ a.number }} · {{ a.closedAt | tmDate: 'longDate' : language.language() }}</span>
              <span class="final">{{ outcomeKey(a) | translate }}</span>
            </li>
          } @empty {
            <li class="tm-muted">{{ 'alerts.noHistory' | translate }}</li>
          }
        </ul>
      </section>
    }
  `,
  styles: `
    .title { margin: 0 0 12px; font-size: 24px; font-weight: 700; }
    .filters { margin-block-end: 16px; }
    .block { display: flex; flex-direction: column; gap: 12px; margin-block-end: 16px; }
    .alert { border-color: var(--tm-danger-line); background: var(--tm-danger-tint); }
    .line { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
    .pseudonym { unicode-bidi: isolate; }
    .meta { margin: 8px 0; font-size: 14px; }
    .pending { color: var(--tm-danger-ink); }
    .actions { display: flex; flex-wrap: wrap; gap: 8px; }
    .list { list-style: none; margin: 0; padding: 0; }
    .row { display: flex; flex-wrap: wrap; gap: 10px; align-items: center; padding-block: 10px; border-block-end: 1px solid var(--tm-line); }
    .final { margin-inline-start: auto; font-weight: 600; }
  `,
})
export class AlertsComponent {
  private readonly api = inject(AccountApiService);
  private readonly crisis = inject(CrisisApiService);
  private readonly dialog = inject(MatDialog);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly language = inject(LanguageService);
  protected readonly outcomeKey = outcomeKey;
  protected readonly status = signal<AlertStatusFilter>('open');
  protected readonly open = signal<Alert[]>([]);
  protected readonly handled = signal<Alert[]>([]);
  protected readonly openCount = signal(0);
  protected readonly loading = signal(true);
  protected readonly busy = signal<string | null>(null);
  protected readonly error = signal<ShownError | null>(null);

  constructor() {
    this.load();
    interval(ALERTS_POLL_MS).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.load(true));
  }

  load(quiet = false): void {
    if (!quiet) this.loading.set(true);
    this.api
      .alerts('all')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (alerts) => {
          this.open.set(alerts.items.filter((a) => a.status !== 'Closed'));
          this.handled.set(alerts.items.filter((a) => a.status === 'Closed'));
          this.openCount.set(alerts.openCount);
          this.error.set(null);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.error.set(shownError(error, 'alerts.loadError'));
          this.loading.set(false);
        },
      });
  }

  setStatus(status: AlertStatusFilter): void {
    this.status.set(status);
  }

  acknowledge(alert: Alert): void {
    this.busy.set(alert.eventId);
    this.crisis.acknowledge(alert.eventId).subscribe({
      next: () => {
        this.busy.set(null);
        this.load(true);
      },
      error: (error: unknown) => {
        // 409: someone else acknowledged first; the refresh shows who owns it.
        this.busy.set(null);
        this.error.set(shownError(error, 'alerts.ackError'));
        this.load(true);
      },
    });
  }

  refer(alert: Alert): void {
    const data: ReasonDialogData<CrisisEventDetail> = {
      titleKey: 'alerts.referTitle',
      bodyKey: 'alerts.referBody',
      labelKey: 'alerts.referNote',
      confirmKey: 'alerts.referConfirm',
      danger: false,
      maxLength: REFER_MAX_LENGTH,
      submit: (_level, _choices, text) => this.crisis.action(alert.eventId, { kind: 'refer', text }),
    };
    this.dialog
      .open<ReasonDialogComponent<CrisisEventDetail>, ReasonDialogData<CrisisEventDetail>, ReasonDialogResult<CrisisEventDetail>>(ReasonDialogComponent, {
        data,
        direction: this.language.direction(),
        width: '520px',
        maxWidth: '95vw',
        restoreFocus: true,
      })
      .afterClosed()
      .subscribe((result) => {
        if (result) this.load(true);
      });
  }
}
