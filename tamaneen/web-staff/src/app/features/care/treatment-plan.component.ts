import { ChangeDetectionStrategy, Component, DestroyRef, HostListener, effect, inject, input, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { type ShownError, shownError, statusOf } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { GoalStatus, PlanFrequency, PlanHistory, PlanVersion, SavePlanRequest } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/session/session.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { careUi } from './care-dialogs';
import { localDate } from './patient-homework.component';
import { PatientTabsComponent } from './patient-tabs.component';

export const GOAL_STATUSES: readonly GoalStatus[] = ['not_started', 'ongoing', 'improving'];
export const PLAN_FREQUENCIES: readonly PlanFrequency[] = ['weekly', 'biweekly', 'monthly'];

/** What is kept while the therapist edits: the draft, the version it is based on, and whether a save is waiting for the connection. */
export interface KeptPlanDraft {
  draft: PlanDraft;
  basedOn: number;
  waiting: boolean;
}

export const planDraftKey = (pseudonymId: string) => `plan:${pseudonymId.toUpperCase()}`;

interface PlanDraft {
  goals: string;
  statuses: GoalStatus[];
  interventions: string;
  frequency: PlanFrequency;
  reviewOn: string;
  followUpNotes: string;
}

/**
 * S15 treatment plan (/patients/:id/plan). The therapist writes the goals (one per line, each with its status),
 * interventions, session frequency, review date and follow-up notes. "Save as a new version" sends the version the
 * draft started from (If-Match); a 412 shows the newer version beside the draft to merge (EC-50). Saving is disabled
 * until the clinical file is approved (SB-15). Older versions open read-only (SB-20) and are never deleted.
 *
 * Lost connection («المسودة محفوظة محليًا ويُعاد الحفظ»): every edit is kept in memory for the session
 * (SessionService.keepDraft), so leaving the page or a failed save loses nothing; a save that failed for the
 * connection is sent again when the browser is back online. Clinical text is never written to browser storage
 * (see SessionService); closing the tab with an unsent draft asks first.
 */
@Component({
  selector: 'tm-treatment-plan',
  imports: [FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, MatSelectModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent, PatientTabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './treatment-plan.component.html',
  styleUrl: './care.scss',
})
export class TreatmentPlanComponent {
  readonly id = input.required<string>();

  private readonly api = inject(StaffApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ui = careUi();
  private readonly session = inject(SessionService);
  protected readonly language = inject(LanguageService);

  protected readonly statuses = GOAL_STATUSES;
  protected readonly frequencies = PLAN_FREQUENCIES;
  protected readonly history = signal<PlanHistory | null>(null);
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly saveError = signal<ShownError | null>(null);
  protected readonly saving = signal(false);
  /** 412: the version someone else saved, shown next to the draft. */
  protected readonly conflict = signal<PlanVersion | null>(null);
  /** An older version opened read-only. */
  protected readonly viewing = signal<PlanVersion | null>(null);
  /** The version the draft is based on (If-Match). */
  protected basedOn = 0;
  protected draft: PlanDraft = this.fromVersion(null);
  protected readonly minDate = localDate(1);
  /** A save failed for the connection and is sent again when it returns. */
  protected readonly waiting = signal(false);
  /** The draft shown was kept from earlier in this session, not loaded from the server. */
  protected readonly restored = signal(false);
  private dirty = false;

  constructor() {
    effect(() => {
      const id = this.id();
      untracked(() => this.load(id));
    });
  }

  protected load(id = this.id(), keepDraft = false): void {
    this.loading.set(true);
    this.api
      .plans(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (h) => {
          this.history.set(h);
          this.loading.set(false);
          if (!keepDraft) {
            const kept = this.session.draft<KeptPlanDraft>(planDraftKey(h.pseudonymId));
            if (kept) {
              this.basedOn = kept.basedOn;
              this.draft = { ...kept.draft, statuses: [...kept.draft.statuses] };
              this.dirty = true;
              this.restored.set(true);
              this.waiting.set(kept.waiting);
              if (kept.waiting && this.online()) {
                this.save();
              }
            } else {
              this.basedOn = h.current?.version ?? 0;
              this.draft = this.fromVersion(h.current);
              this.dirty = false;
              this.restored.set(false);
            }
          }
        },
        error: (e: unknown) => {
          this.loading.set(false);
          if (statusOf(e) === 404) {
            this.notFound.set(true);
          } else {
            this.error.set(shownError(e, 'care.plan.loadError'));
          }
        },
      });
  }

  protected goalLines(): string[] {
    return this.draft.goals.split('\n').map((l) => l.trim()).filter((l) => l.length > 0);
  }

  protected statusAt(i: number): GoalStatus {
    return this.draft.statuses[i] ?? 'not_started';
  }

  protected setStatus(i: number, status: GoalStatus): void {
    const next = [...this.draft.statuses];
    next[i] = status;
    this.draft.statuses = next;
    this.remember();
  }

  /** Keeps the draft in memory after every edit. */
  protected remember(): void {
    const h = this.history();
    if (!h?.canEdit) {
      return;
    }
    this.dirty = true;
    this.session.keepDraft(planDraftKey(h.pseudonymId), { draft: { ...this.draft, statuses: [...this.draft.statuses] }, basedOn: this.basedOn, waiting: this.waiting() } satisfies KeptPlanDraft);
  }

  /** Back online: send the save that was waiting. */
  @HostListener('window:online')
  protected reconnected(): void {
    if (this.waiting() && !this.saving()) {
      this.save();
    }
  }

  /** Closing the tab loses the in-memory draft: ask first. */
  @HostListener('window:beforeunload', ['$event'])
  protected beforeUnload(event: BeforeUnloadEvent): void {
    if (this.dirty) {
      event.preventDefault();
    }
  }

  protected valid(): boolean {
    const goals = this.goalLines();
    return goals.length > 0 && this.draft.goals.length <= 4000 && this.draft.interventions.trim().length > 0 && this.draft.interventions.length <= 4000 && this.draft.followUpNotes.length <= 4000 && this.draft.reviewOn >= this.minDate;
  }

  protected save(): void {
    const h = this.history();
    if (!h || !h.fileApproved || !this.valid() || this.saving()) {
      return;
    }
    const body: SavePlanRequest = {
      goals: this.goalLines().map((text, i) => ({ text, status: this.statusAt(i) })),
      interventions: this.draft.interventions.trim(),
      frequency: this.draft.frequency,
      reviewOn: this.draft.reviewOn,
      followUpNotes: this.draft.followUpNotes.trim(),
    };
    this.saving.set(true);
    this.saveError.set(null);
    this.api.savePlan(h.pseudonymId, this.basedOn, body).subscribe({
      next: () => {
        this.saving.set(false);
        this.conflict.set(null);
        this.forget(h.pseudonymId);
        this.ui.notify('care.plan.saved');
        this.load();
      },
      error: (e: unknown) => {
        this.saving.set(false);
        if (statusOf(e) === 0 || !this.online()) {
          // Lost connection: the draft stays in memory and is sent again when the connection returns.
          this.waiting.set(true);
          this.remember();
        } else if (statusOf(e) === 412) {
          // Keep the draft; show the newer version and base the next save on it.
          this.api.plans(h.pseudonymId).subscribe((latest) => {
            this.history.set(latest);
            this.conflict.set(latest.current);
            this.basedOn = latest.current?.version ?? 0;
            this.remember();
          });
        } else {
          this.saveError.set(shownError(e, 'care.plan.saveError'));
        }
      },
    });
  }

  /** "Discard the draft": after a confirmation, back to the latest version. */
  protected discard(): void {
    this.ui.confirm('care.plan.discardTitle', 'care.plan.discardBody', 'care.plan.discardConfirm', false, () => of(true)).subscribe((r) => {
      if (r) {
        this.conflict.set(null);
        this.forget(this.history()?.pseudonymId ?? this.id());
        this.load();
      }
    });
  }

  protected view(version: number): void {
    const h = this.history();
    if (h) {
      this.api.planVersion(h.pseudonymId, version).subscribe({ next: (v) => this.viewing.set(v), error: (e: unknown) => this.saveError.set(shownError(e, 'common.error')) });
    }
  }

  protected asText(v: PlanVersion): string {
    return v.goals.map((g) => g.text).join('\n');
  }

  private forget(pseudonymId: string): void {
    this.session.dropDraft(planDraftKey(pseudonymId));
    this.dirty = false;
    this.waiting.set(false);
    this.restored.set(false);
  }

  private online(): boolean {
    return typeof navigator === 'undefined' || navigator.onLine !== false;
  }

  private fromVersion(v: PlanVersion | null): PlanDraft {
    return {
      goals: v ? this.asText(v) : '',
      statuses: v ? v.goals.map((g) => g.status) : [],
      interventions: v?.interventions ?? '',
      frequency: v?.frequency ?? 'weekly',
      reviewOn: v && v.reviewOn >= localDate(1) ? v.reviewOn : localDate(28),
      followUpNotes: v?.followUpNotes ?? '',
    };
  }
}
