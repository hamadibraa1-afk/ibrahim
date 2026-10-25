import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, RouterLink } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import { type Observable, interval } from 'rxjs';
import { type ShownError, shownError, statusOf } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { ReferralCode, RiskLevel, SuggestedTherapist, TriageCaseDetail, TriageDecisionRequest, TriageDecisionResult, TriageQueueItem } from '../../core/api/types';
import { formatCountdown } from '../../core/i18n/format';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { type ReasonDialogData, type ReasonDialogResult, ReasonDialogComponent } from '../../shared/reason-dialog.component';
import { RiskTagComponent } from '../../shared/risk-tag.component';

export const REFERRALS: readonly ReferralCode[] = ['crisis_line', 'primary_care', 'psychiatry', 'community_support', 'emergency_services'];
export const MAX_REASON = 1000;
/** The claim warning shows two minutes before it ends (S03 states). */
export const CLAIM_WARNING_MS = 2 * 60_000;

/** "2:40" hours:minutes left, or minutes only under an hour; past the deadline "0:00". */
export function timeLeft(dueAt: string, now: number): { hours: number; minutes: number; overdue: boolean } {
  const ms = new Date(dueAt).getTime() - now;
  const total = Math.max(0, Math.floor(ms / 60_000));
  return { hours: Math.floor(total / 60), minutes: total % 60, overdue: ms <= 0 };
}

/**
 * S03 triage review (/triage, /triage/:id), for supervisors and the clinical lead. The side queue is sorted by risk
 * then deadline and shows who holds a case. Opening a case claims it for 15 minutes of server time (released on
 * leaving); a case held by someone else is read-only with their name and time. Every decision except approval needs
 * the reason written on the page. Lowering from high or critical goes to the clinical lead, who co-signs here.
 */
@Component({
  selector: 'tm-triage-review',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent, RiskTagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './triage-review.component.html',
  styleUrl: './triage-review.component.scss',
})
export class TriageReviewComponent {
  /** Route parameter :id of the open case, if any. */
  readonly id = input<string | undefined>(undefined);

  private readonly api = inject(StaffApiService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly language = inject(LanguageService);

  protected readonly queue = signal<TriageQueueItem[]>([]);
  protected readonly queueError = signal<ShownError | null>(null);
  protected readonly detail = signal<TriageCaseDetail | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<ShownError | null>(null);
  protected readonly decisionError = signal<ShownError | null>(null);
  protected readonly busy = signal(false);
  /** Who holds the case when the claim was refused (409). */
  protected readonly heldBy = signal<{ by: string | null; until: string | null } | null>(null);
  protected readonly reasonMissing = signal(false);
  protected reason = '';

  /** Server-clock offset, so the claim countdown uses server time. */
  private skew = 0;
  protected readonly now = signal(Date.now());
  private claimedId: string | null = null;

  protected readonly maxReason = MAX_REASON;
  protected readonly claimMine = computed(() => {
    const d = this.detail();
    return !!d?.claim.mine && !!d.claim.until && new Date(d.claim.until).getTime() > this.now();
  });
  protected readonly claimMsLeft = computed(() => {
    const until = this.detail()?.claim.until;
    return until ? new Date(until).getTime() - this.now() : 0;
  });
  protected readonly claimExpired = computed(() => !!this.detail()?.claim.mine && this.claimMsLeft() <= 0);
  protected readonly claimWarning = computed(() => this.claimMine() && this.claimMsLeft() <= CLAIM_WARNING_MS);
  protected readonly claimCountdown = computed(() => formatCountdown(this.claimMsLeft()));
  protected readonly canDecide = computed(() => this.claimMine() && this.detail()?.status === 'pending' && !this.busy());
  protected readonly lowerLevels = computed(() => ([1, 2, 3, 4] as const).filter((l) => l !== this.detail()?.riskLevel && l >= (this.detail()?.crisisFloor ?? 0)));

  constructor() {
    this.loadQueue();
    interval(1000).pipe(takeUntilDestroyed()).subscribe(() => this.now.set(Date.now() + this.skew));
    interval(60_000).pipe(takeUntilDestroyed()).subscribe(() => this.loadQueue());
    effect(() => {
      const id = this.id();
      this.language.language();
      untracked(() => this.open(id));
    });
    // Leaving the page releases the claim (SB-31).
    this.destroyRef.onDestroy(() => this.releaseCurrent());
  }

  protected loadQueue(): void {
    this.api.triageQueue().subscribe({
      next: (items) => {
        this.queue.set(items);
        this.queueError.set(null);
      },
      error: (error: unknown) => this.queueError.set(shownError(error, 'triage.queueError')),
    });
  }

  protected sourceKey(item: { path: string; contactRequested: boolean }): string {
    return item.contactRequested ? 'triage.source.contact' : `triage.source.${item.path}`;
  }

  protected left(dueAt: string) {
    return timeLeft(dueAt, this.now());
  }

  protected heldByOther(item: TriageQueueItem): boolean {
    return !!item.claim.by && !item.claim.mine && !!item.claim.until && new Date(item.claim.until).getTime() > this.now();
  }

  /** Claims the case; when someone else holds it, shows it read-only. */
  private open(id: string | undefined): void {
    if (this.claimedId && this.claimedId !== id) {
      this.releaseCurrent();
    }
    this.detail.set(null);
    this.heldBy.set(null);
    this.loadError.set(null);
    this.decisionError.set(null);
    this.reasonMissing.set(false);
    if (!id) {
      return;
    }
    this.loading.set(true);
    this.api.claim(id).subscribe({
      next: (detail) => {
        this.show(detail);
        this.claimedId = id;
        this.loadQueue();
      },
      error: (error: unknown) => {
        if (statusOf(error) === 409 && error instanceof HttpErrorResponse && (error.error as { code?: string } | null)?.code === 'error.triage.claimed') {
          const body = error.error as { claimedBy?: string; claimedUntil?: string };
          this.heldBy.set({ by: body.claimedBy ?? null, until: body.claimedUntil ?? null });
          this.readOnly(id);
        } else if (statusOf(error) === 409) {
          // Already decided: show it read-only.
          this.readOnly(id);
        } else {
          this.loading.set(false);
          this.loadError.set(shownError(error, statusOf(error) === 404 ? 'triage.notFound' : 'triage.loadError'));
        }
      },
    });
  }

  protected reclaim(): void {
    const id = this.id();
    if (id) {
      this.claimedId = null;
      this.open(id);
    }
  }

  protected release(): void {
    this.releaseCurrent();
    void this.router.navigate(['/triage']);
  }

  private readOnly(id: string): void {
    this.api.triageCaseDetail(id).subscribe({
      next: (detail) => this.show(detail),
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(shownError(error, 'triage.loadError'));
      },
    });
  }

  private show(detail: TriageCaseDetail): void {
    this.skew = new Date(detail.serverNow).getTime() - Date.now();
    this.now.set(Date.now() + this.skew);
    this.detail.set(detail);
    this.loading.set(false);
  }

  private releaseCurrent(): void {
    const id = this.claimedId;
    this.claimedId = null;
    if (id) {
      this.api.release(id).subscribe({ error: () => undefined });
    }
  }

  private requireReason(): string | null {
    const reason = this.reason.trim();
    if (!reason) {
      this.reasonMissing.set(true);
      document.getElementById('decision-reason')?.focus();
      return null;
    }
    this.reasonMissing.set(false);
    return reason;
  }

  /** SB-32: approve the level and the suggestions shown; the patient is told the suggestions are ready. */
  protected approve(): void {
    const d = this.detail();
    if (!d) {
      return;
    }
    this.run({ kind: 'approve', reason: this.reason.trim() || undefined, therapistIds: d.suggestions.map((s) => s.id) }, 'triage.done.approve', true);
  }

  /** SB-33: change the level with a reason; lowering from high or critical asks the clinical lead to co-sign. */
  protected overrideRisk(): void {
    const d = this.detail();
    const reason = this.requireReason();
    if (!d || !reason) {
      return;
    }
    const highNow = d.riskLevel >= 3;
    this.confirm<TriageDecisionResult>({
      titleKey: 'triage.overrideDialog.title',
      bodyKey: highNow ? 'triage.overrideDialog.bodyHigh' : 'triage.overrideDialog.body',
      bodyParams: { floor: d.crisisFloor },
      confirmKey: 'triage.overrideDialog.confirm',
      danger: false,
      levels: this.lowerLevels(),
      levelLabelKey: 'triage.overrideDialog.level',
      submit: (level) => this.api.decide(d.id, { kind: 'override_risk', riskLevel: level as RiskLevel, reason }),
    }).subscribe((result) => {
      if (result) {
        this.notify(result.value.coSignRequested ? 'triage.done.coSignRequested' : 'triage.done.override');
        this.refresh(d.id);
      }
    });
  }

  /** SB-34: replace the suggestions with approved therapists that have room. */
  protected changeSuggestions(): void {
    const d = this.detail();
    const reason = this.requireReason();
    if (!d || !reason) {
      return;
    }
    this.api.availableTherapists(d.id).subscribe({
      next: (therapists) => {
        if (!therapists.length) {
          this.decisionError.set({ title: null, detail: null, fallbackKey: 'triage.noTherapist' });
          return;
        }
        const byId = new Map(therapists.map((t) => [t.id, t]));
        this.confirm<TriageDecisionResult>({
          titleKey: 'triage.reassignDialog.title',
          bodyKey: 'triage.reassignDialog.body',
          confirmKey: 'triage.reassignDialog.confirm',
          danger: false,
          choices: therapists.map((t) => t.id),
          choicesLabelKey: 'triage.reassignDialog.choose',
          choiceOptionKey: (id) => this.therapistLine(byId.get(id)!),
          maxChoices: 3,
          submit: (_level, ids) => this.api.decide(d.id, { kind: 'reassign', therapistIds: ids, reason }),
        }).subscribe((result) => {
          if (result) {
            this.notify('triage.done.reassign');
            this.refresh(d.id);
          }
        });
      },
      error: (error: unknown) => this.decisionError.set(shownError(error, 'common.error')),
    });
  }

  /** SB-35: open a crisis event and go to it on the console. */
  protected escalate(): void {
    const d = this.detail();
    const reason = this.requireReason();
    if (!d || !reason) {
      return;
    }
    this.confirm<TriageDecisionResult>({
      titleKey: 'triage.escalateDialog.title',
      bodyKey: 'triage.escalateDialog.body',
      confirmKey: 'triage.escalateDialog.confirm',
      danger: true,
      levels: [3, 4],
      levelLabelKey: 'triage.escalateDialog.level',
      submit: (level) => this.api.decide(d.id, { kind: 'escalate', riskLevel: level as RiskLevel, reason }),
    }).subscribe((result) => {
      if (result) {
        this.claimedId = null;
        this.notify('triage.done.escalate');
        void this.router.navigate(result.value.crisisEventId ? ['/crisis', result.value.crisisEventId] : ['/triage']);
      }
    });
  }

  /** SB-36: close without a match; the patient sees the referral resources chosen. */
  protected close(): void {
    const d = this.detail();
    const reason = this.requireReason();
    if (!d || !reason) {
      return;
    }
    this.confirm<TriageDecisionResult>({
      titleKey: 'triage.closeDialog.title',
      bodyKey: 'triage.closeDialog.body',
      confirmKey: 'triage.closeDialog.confirm',
      danger: false,
      choices: REFERRALS,
      choicesLabelKey: 'triage.closeDialog.referrals',
      choiceOptionKey: (c) => `triage.referral.${c}`,
      submit: (_level, referrals) => this.api.decide(d.id, { kind: 'close', referrals: referrals as ReferralCode[], reason }),
    }).subscribe((result) => {
      if (result) {
        this.finished('triage.done.close');
      }
    });
  }

  /** The clinical lead confirms or refuses a pending lowering (EC-48). */
  protected coSign(approve: boolean): void {
    const d = this.detail();
    const reason = this.requireReason();
    if (!d || !reason) {
      return;
    }
    this.busy.set(true);
    this.decisionError.set(null);
    this.api.decide(d.id, { kind: 'cosign', approve, reason }).subscribe({
      next: () => {
        this.busy.set(false);
        this.reason = '';
        this.notify(approve ? 'triage.done.coSigned' : 'triage.done.coSignRefused');
        this.refresh(d.id);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.decisionError.set(shownError(error, 'common.error'));
      },
    });
  }

  protected therapistLine(t: SuggestedTherapist): string {
    const parts = [t.displayName, t.languages.map((l) => this.translate.instant(`triage.language.${l}`)).join('، '), this.translate.instant('triage.capacity', { load: t.load, capacity: t.capacity })];
    return parts.filter(Boolean).join(' · ');
  }

  private run(request: TriageDecisionRequest, doneKey: string, finished: boolean): void {
    const d = this.detail();
    if (!d || this.busy()) {
      return;
    }
    this.busy.set(true);
    this.decisionError.set(null);
    this.api.decide(d.id, request).subscribe({
      next: () => {
        this.busy.set(false);
        if (finished) {
          this.finished(doneKey);
        } else {
          this.notify(doneKey);
          this.refresh(d.id);
        }
      },
      // The reason stays in the field so the supervisor can retry (S03 "save error").
      error: (error: unknown) => {
        this.busy.set(false);
        this.decisionError.set(shownError(error, 'common.error'));
      },
    });
  }

  private refresh(id: string): void {
    this.api.triageCaseDetail(id).subscribe({ next: (detail) => this.show(detail) });
    this.loadQueue();
  }

  /** The case left the queue: back to the list. */
  private finished(doneKey: string): void {
    this.claimedId = null;
    this.reason = '';
    this.notify(doneKey);
    this.loadQueue();
    void this.router.navigate(['/triage']);
  }

  private confirm<T>(data: Omit<ReasonDialogData<T>, 'labelKey' | 'maxLength' | 'hideText'>): Observable<ReasonDialogResult<T> | undefined> {
    this.decisionError.set(null);
    return this.dialog
      .open<ReasonDialogComponent<T>, ReasonDialogData<T>, ReasonDialogResult<T>>(ReasonDialogComponent, {
        data: { ...data, labelKey: 'triage.reason', maxLength: MAX_REASON, hideText: true },
        direction: this.language.direction(),
        width: '520px',
        maxWidth: '95vw',
        restoreFocus: true,
      })
      .afterClosed();
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000, direction: this.language.direction(), politeness: 'polite' });
  }
}
