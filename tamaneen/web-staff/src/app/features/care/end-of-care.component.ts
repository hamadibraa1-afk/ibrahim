import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { type ShownError, shownError, statusOf } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { Closure, ClosureFollowUp, ClosureReason, ClosureView, SaveClosureRequest } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { careUi } from './care-dialogs';

export const CLOSURE_REASONS: readonly ClosureReason[] = ['goals_met', 'patient_choice', 'transferred', 'no_contact', 'other'];
export const FOLLOW_UPS: readonly ClosureFollowUp[] = ['phq9_1m_3m', 'none'];
/** The patient summary must not carry raw scores (P53): numbers are flagged before sending. */
export const HAS_NUMBERS = /[0-9٠-٩]/;

/**
 * S57 end of care (/patients/:id/close). The therapist writes the care summary (reason from the list, sessions and
 * weeks filled from the appointments), a plain-language summary for the patient (no raw scores; numbers are flagged),
 * the relapse-prevention plan and the follow-up, saves a draft or sends it to a supervisor. Closing is blocked while
 * the risk was 3–4 in the last 14 days (the API refuses it too). While waiting it is read-only with "withdraw"; a
 * refusal shows the reason. A supervisor opening the page signs it off or sends it back with a reason.
 */
@Component({
  selector: 'tm-end-of-care',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, MatSelectModule, MatSlideToggleModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './end-of-care.component.html',
  styleUrl: './care.scss',
})
export class EndOfCareComponent {
  readonly id = input.required<string>();

  private readonly api = inject(StaffApiService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ui = careUi();
  protected readonly language = inject(LanguageService);

  protected readonly reasons = CLOSURE_REASONS;
  protected readonly followUps = FOLLOW_UPS;
  protected readonly view = signal<ClosureView | null>(null);
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly saveError = signal<ShownError | null>(null);
  protected readonly saving = signal(false);
  protected readonly status = computed(() => this.view()?.closure?.status ?? null);
  protected readonly locked = computed(() => this.status() === 'submitted' || this.status() === 'signed_off');
  protected draft: SaveClosureRequest = this.empty(null);
  private version = 0;

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
  }

  protected load(id = this.id()): void {
    this.loading.set(true);
    this.api
      .closure(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: ({ view, version }) => this.apply(view, version),
        error: (e: unknown) => {
          this.loading.set(false);
          if (statusOf(e) === 404) {
            this.notFound.set(true);
          } else {
            this.error.set(shownError(e, 'care.close.loadError'));
          }
        },
      });
  }

  protected summaryHasNumbers(): boolean {
    return HAS_NUMBERS.test(this.draft.patientSummary);
  }

  protected complete(): boolean {
    const d = this.draft;
    return !!d.reasonCode && d.patientSummary.trim().length > 0 && d.patientSummary.length <= 1500 && d.warningSigns.trim().length > 0 && d.warningSigns.length <= 300 && d.whatToDo.trim().length > 0 && d.whatToDo.length <= 300;
  }

  protected save(submit: boolean): void {
    const v = this.view();
    if (!v || this.saving() || (submit && (v.blocked || !this.complete()))) {
      return;
    }
    this.saving.set(true);
    this.saveError.set(null);
    this.api.saveClosure(v.pseudonymId, this.version, { ...this.draft, submit }).subscribe({
      next: ({ view, version }) => {
        this.saving.set(false);
        this.apply(view, version);
        this.ui.notify(submit ? 'care.close.submitted' : 'care.close.saved');
      },
      error: (e: unknown) => {
        this.saving.set(false);
        this.saveError.set(shownError(e, 'care.close.saveError'));
        if (statusOf(e) === 412) {
          this.load();
        }
      },
    });
  }

  protected withdraw(): void {
    const v = this.view();
    if (v) {
      this.api.withdrawClosure(v.pseudonymId).subscribe({ next: ({ view, version }) => this.apply(view, version), error: (e: unknown) => this.saveError.set(shownError(e, 'common.error')) });
    }
  }

  /** Supervisor: sign off (ends the care assignment) or send back with a reason. */
  protected decide(approve: boolean): void {
    const c = this.view()?.closure;
    if (!c) {
      return;
    }
    const dialog = approve
      ? this.ui.confirm<Closure>('care.close.signOffTitle', 'care.close.signOffBody', 'care.close.signOff', false, () => this.api.signOffClosure(c.id, true))
      : this.ui.open<Closure>({ titleKey: 'care.close.rejectTitle', bodyKey: 'care.close.rejectBody', labelKey: 'care.close.rejectReason', confirmKey: 'care.close.reject', danger: false, maxLength: 1000, submit: (_l, _c, reason) => this.api.signOffClosure(c.id, false, reason) });
    dialog.subscribe((r) => {
      if (r) {
        this.ui.notify(approve ? 'care.close.signedOff' : 'care.close.rejected');
        void this.router.navigate(['/closures']);
      }
    });
  }

  private apply(view: ClosureView, version: number): void {
    this.view.set(view);
    this.version = version;
    this.draft = this.empty(view);
    this.loading.set(false);
  }

  private empty(view: ClosureView | null): SaveClosureRequest {
    const c = view?.closure && view.closure.status !== 'signed_off' ? view.closure : null;
    return {
      reasonCode: c?.reasonCode ?? '',
      sessionCount: c?.sessionCount ?? view?.prefill.sessionCount ?? 0,
      durationWeeks: c?.durationWeeks ?? view?.prefill.durationWeeks ?? 0,
      patientSummary: c?.patientSummary ?? '',
      warningSigns: c?.warningSigns ?? '',
      whatToDo: c?.whatToDo ?? '',
      followUp: c?.followUp ?? 'phq9_1m_3m',
      keepLibrary: c?.keepLibrary ?? true,
      submit: false,
    };
  }
}
