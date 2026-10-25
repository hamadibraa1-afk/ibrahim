import { ChangeDetectionStrategy, Component, DestroyRef, Injector, afterNextRender, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
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
import { type ShownError, shownError, statusOf } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { FileStatement, ManualCrisisResult, PatientFile, RiskLevel, StatementKey } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/session/session.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { type ReasonDialogData, type ReasonDialogResult, ReasonDialogComponent } from '../../shared/reason-dialog.component';
import { RiskTagComponent } from '../../shared/risk-tag.component';

export type FileTab = 'summary' | 'transcript' | 'scores' | 'cssrs' | 'corrections';
export const FILE_TABS: readonly FileTab[] = ['summary', 'transcript', 'scores', 'cssrs', 'corrections'];

/**
 * S02 clinical file and evidence inspector (/patients/:id). The AI's intake summary sits next to the intake chat:
 * each statement shows how many messages it came from, and "source" highlights them in the chat. The therapist edits
 * statements in place (saved with the approval), dismisses them with a reason, rates the file and approves it with the
 * version they read (412 shows the latest). Raising the risk and a manual crisis alert are in the header. Every read
 * is audited on the server; anyone the patient is not assigned to gets "not found".
 */
@Component({
  selector: 'tm-patient-file',
  imports: [NgTemplateOutlet, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent, RiskTagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './patient-file.component.html',
  styleUrl: './patient-file.component.scss',
})
export class PatientFileComponent {
  /** Route parameter :id, the patient's pseudonym. */
  readonly id = input.required<string>();
  /** ?tab= keeps the open tab in the address. */
  readonly tabParam = input<string | undefined>(undefined, { alias: 'tab' });

  private readonly api = inject(StaffApiService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly language = inject(LanguageService);
  protected readonly session = inject(SessionService);

  protected readonly tabs = FILE_TABS;
  protected readonly file = signal<PatientFile | null>(null);
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly loadError = signal<ShownError | null>(null);
  protected readonly actionError = signal<ShownError | null>(null);
  /** 412: someone changed the file; the latest version is shown and the therapist approves again. */
  protected readonly conflict = signal(false);
  protected readonly approving = signal(false);
  protected readonly tab = signal<FileTab>('summary');

  /** Statement being edited, and the local wording of edited statements (sent with the approval). */
  protected readonly editing = signal<StatementKey | null>(null);
  protected readonly edits = signal<Partial<Record<StatementKey, string>>>({});
  protected draft = '';

  /** Statement whose sources are highlighted, and the highlighted message ids. */
  protected readonly selected = signal<StatementKey | null>(null);
  protected readonly highlighted = signal<ReadonlySet<string>>(new Set());
  protected readonly sourceRemoved = signal(false);

  protected readonly canEdit = computed(() => this.file()?.canEdit ?? false);
  protected readonly isTherapist = computed(() => this.session.role() === 'therapist');
  protected readonly statements = computed(() => this.file()?.statements ?? []);

  constructor() {
    effect(() => {
      const tab = this.tabParam();
      untracked(() => this.tab.set(FILE_TABS.includes(tab as FileTab) ? (tab as FileTab) : 'summary'));
    });
    // Reload when the patient or the language changes (server-localized item texts).
    effect(() => {
      const id = this.id();
      this.language.language();
      untracked(() => this.load(id));
    });
  }

  protected load(id = this.id()): void {
    this.loading.set(true);
    this.api
      .file(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (file) => {
          this.file.set(file);
          this.loading.set(false);
          this.loadError.set(null);
          this.notFound.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          if (statusOf(error) === 404) {
            this.notFound.set(true);
          } else {
            this.loadError.set(shownError(error, 'file.loadError'));
          }
        },
      });
  }

  protected selectTab(tab: FileTab): void {
    this.tab.set(tab);
    void this.router.navigate([], { queryParams: { tab: tab === 'summary' ? null : tab }, queryParamsHandling: 'merge', replaceUrl: true });
  }

  protected statementText(s: FileStatement): string {
    return this.edits()[s.key] ?? s.text;
  }

  protected isEdited(s: FileStatement): boolean {
    return s.editedByTherapist || this.edits()[s.key] !== undefined;
  }

  /** SB-12: highlight the sources in the chat and scroll to the first one. */
  protected showSources(s: FileStatement): void {
    const file = this.file();
    if (!file) {
      return;
    }
    this.selected.set(s.key);
    this.api.evidence(file.pseudonymId, s.key).subscribe({
      next: (evidence) => {
        this.highlighted.set(new Set(evidence.messageIds));
        this.sourceRemoved.set(evidence.removed);
        if (this.tab() !== 'summary' && this.tab() !== 'transcript') {
          this.selectTab('summary');
        }
        const first = evidence.messageIds[0];
        if (first) {
          afterNextRender(() => document.getElementById(`turn-${first}`)?.scrollIntoView?.({ block: 'nearest', behavior: 'smooth' }), { injector: this.injector });
        }
      },
      error: (error: unknown) => this.actionError.set(shownError(error, 'common.error')),
    });
  }

  /** SB-13: edit in place; nothing is saved until the file is approved. */
  protected startEdit(s: FileStatement): void {
    this.draft = this.statementText(s);
    this.editing.set(s.key);
  }

  protected saveEdit(key: StatementKey): void {
    const text = this.draft.trim();
    if (text) {
      this.edits.update((e) => ({ ...e, [key]: text }));
    }
    this.editing.set(null);
  }

  protected cancelEdit(): void {
    this.editing.set(null);
  }

  /** SB-14: dismiss with a reason; the statement leaves the file, the reason stays in the audit log. */
  protected dismiss(s: FileStatement): void {
    const file = this.file();
    if (!file) {
      return;
    }
    this.openReason<PatientFile>({
      titleKey: 'file.dismissDialog.title',
      bodyKey: 'file.dismissDialog.body',
      labelKey: 'file.dismissDialog.reason',
      confirmKey: 'file.dismissDialog.confirm',
      danger: false,
      maxLength: 1000,
      submit: (_level, _choices, reason) => this.api.dismissStatement(file.pseudonymId, s.key, reason),
    }).subscribe((result) => {
      if (result) {
        this.edits.update(({ [s.key]: _, ...rest }) => rest);
        this.file.set(result.value);
        this.notify('file.dismissed');
      }
    });
  }

  /** SB-15: approve as is or with the local edits, with the version read (If-Match). */
  protected approve(): void {
    const file = this.file();
    if (!file || this.approving()) {
      return;
    }
    this.approving.set(true);
    this.actionError.set(null);
    this.conflict.set(false);
    this.api.approveFile(file.pseudonymId, file.version, this.edits()).subscribe({
      next: (approved) => {
        this.file.set(approved);
        this.edits.set({});
        this.approving.set(false);
        this.notify('file.approvedDone');
      },
      error: (error: unknown) => {
        this.approving.set(false);
        if (statusOf(error) === 412) {
          // Show the latest version; the therapist's local edits are kept and can be approved again.
          this.conflict.set(true);
          this.load();
        } else {
          this.actionError.set(shownError(error, 'common.error'));
        }
      },
    });
  }

  /** SB-16: usefulness 1–5. */
  protected rate(rating: number): void {
    const file = this.file();
    if (!file) {
      return;
    }
    const previous = file.feedback.rating;
    this.file.set({ ...file, feedback: { ...file.feedback, rating } });
    this.api.rateFile(file.pseudonymId, rating).subscribe({
      next: () => this.notify('file.rated'),
      error: (error: unknown) => {
        this.file.update((f) => f && { ...f, feedback: { ...f.feedback, rating: previous } });
        this.actionError.set(shownError(error, 'common.error'));
      },
    });
  }

  /** SB-17: only levels above the current one; lowering goes through a supervisor. */
  protected raiseRisk(): void {
    const file = this.file();
    if (!file) {
      return;
    }
    const levels = ([1, 2, 3, 4] as const).filter((l) => l > file.riskLevel);
    this.openReason<{ riskLevel: RiskLevel }>({
      titleKey: 'file.riskDialog.title',
      bodyKey: 'file.riskDialog.body',
      labelKey: 'file.riskDialog.reason',
      confirmKey: 'file.riskDialog.confirm',
      danger: false,
      maxLength: 1000,
      levels,
      levelLabelKey: 'file.riskDialog.level',
      submit: (level, _choices, reason) => this.api.raiseRisk(file.pseudonymId, level as RiskLevel, reason),
    }).subscribe((result) => {
      if (result) {
        this.notify('file.riskRaised');
        this.load();
      }
    });
  }

  /** SB-18: a manual crisis alert (high or critical) with a note; then the crisis console opens on the event. */
  protected manualCrisis(): void {
    const file = this.file();
    if (!file) {
      return;
    }
    this.openReason<ManualCrisisResult>({
      titleKey: 'file.crisisDialog.title',
      bodyKey: 'file.crisisDialog.body',
      labelKey: 'file.crisisDialog.note',
      confirmKey: 'file.crisisDialog.confirm',
      danger: true,
      maxLength: 2000,
      levels: [3, 4],
      levelLabelKey: 'file.crisisDialog.level',
      submit: (level, _choices, note) => this.api.manualCrisis(file.pseudonymId, level as 3 | 4, note),
    }).subscribe((result) => {
      if (result) {
        this.notify('file.crisisRaised');
        void this.router.navigate(['/crisis', result.value.crisisEventId]);
      }
    });
  }

  protected isSource(id: string): boolean {
    return this.highlighted().has(id);
  }

  private openReason<T>(data: ReasonDialogData<T>) {
    return this.dialog
      .open<ReasonDialogComponent<T>, ReasonDialogData<T>, ReasonDialogResult<T>>(ReasonDialogComponent, { data, direction: this.language.direction(), width: '520px', maxWidth: '95vw', restoreFocus: true })
      .afterClosed();
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000, direction: this.language.direction(), politeness: 'polite' });
  }
}
