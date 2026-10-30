import { ChangeDetectionStrategy, Component, DestroyRef, effect, inject, input, signal, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { type ShownError, shownError, statusOf } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { HomeworkFrequency, HomeworkTemplate, StaffCheckin, StaffHomework, StaffHomeworkList } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { careUi } from './care-dialogs';
import { PatientTabsComponent } from './patient-tabs.component';

export const HOMEWORK_FREQUENCIES: readonly HomeworkFrequency[] = ['daily', 'twice_weekly', 'weekly', 'once'];

interface HomeworkDraft {
  title: string;
  instructions: string;
  frequency: HomeworkFrequency;
  endsOn: string;
  minutes: number | null;
  templateCode: string | null;
}

/** Today in the portal user's local calendar, as yyyy-mm-dd. */
export function localDate(offsetDays = 0, now = new Date()): string {
  const d = new Date(now.getFullYear(), now.getMonth(), now.getDate() + offsetDays);
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/**
 * S16 patient homework (/patients/:id/homework): the current exercises with this week's progress and the patient's
 * latest note (a note the crisis gate rated 3–4 gets a risk badge and a link to the console), a reply with a preview
 * (SB-23), edit and cancel with a confirmation (SB-22), and a new exercise from the library or own text with a
 * preview as the patient sees it (SB-21). The patient gets it in P29/P10.
 */
@Component({
  selector: 'tm-patient-homework',
  imports: [DecimalPipe, FormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, MatSelectModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent, PatientTabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './patient-homework.component.html',
  styleUrl: './care.scss',
})
export class PatientHomeworkComponent {
  readonly id = input.required<string>();

  private readonly api = inject(StaffApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ui = careUi();
  protected readonly language = inject(LanguageService);

  protected readonly frequencies = HOMEWORK_FREQUENCIES;
  protected readonly data = signal<StaffHomeworkList | null>(null);
  protected readonly templates = signal<HomeworkTemplate[]>([]);
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly formError = signal<ShownError | null>(null);
  protected readonly saving = signal(false);
  protected readonly preview = signal(false);
  /** The exercise being edited (null: the form assigns a new one). */
  protected readonly editing = signal<string | null>(null);
  /** Check-in being answered, with the reply text and whether its preview is shown. */
  protected readonly replyTo = signal<string | null>(null);
  protected readonly replyPreview = signal(false);
  protected replyText = '';
  protected draft: HomeworkDraft = this.emptyDraft();
  protected readonly minDate = localDate();

  constructor() {
    effect(() => {
      const id = this.id();
      this.language.language();
      untracked(() => {
        this.load(id);
        this.api.homeworkTemplates().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ next: (t) => this.templates.set(t), error: () => this.templates.set([]) });
      });
    });
  }

  protected load(id = this.id()): void {
    this.loading.set(true);
    this.api
      .homework(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (d) => {
          this.data.set(d);
          this.loading.set(false);
          this.error.set(null);
        },
        error: (e: unknown) => {
          this.loading.set(false);
          if (statusOf(e) === 404) {
            this.notFound.set(true);
          } else {
            this.error.set(shownError(e, 'care.homework.loadError'));
          }
        },
      });
  }

  /** Fills the form from a library template, or clears it for the therapist's own text. */
  protected useTemplate(code: string | null): void {
    const t = this.templates().find((x) => x.code === code);
    this.editing.set(null);
    this.draft = t
      ? { title: t.title, instructions: t.instructions.join('\n'), frequency: t.frequency, endsOn: this.draft.endsOn, minutes: t.minutes, templateCode: t.code }
      : { ...this.emptyDraft(), endsOn: this.draft.endsOn };
  }

  protected edit(h: StaffHomework): void {
    this.editing.set(h.id);
    this.preview.set(false);
    this.draft = { title: h.title, instructions: h.instructions.join('\n'), frequency: h.frequency, endsOn: h.endsOn, minutes: h.minutes, templateCode: h.templateCode };
  }

  protected cancelEdit(): void {
    this.editing.set(null);
    this.draft = this.emptyDraft();
  }

  protected lines(text: string): string[] {
    return text
      .split('\n')
      .map((l) => l.trim())
      .filter((l) => l.length > 0);
  }

  protected valid(): boolean {
    const d = this.draft;
    return d.title.trim().length > 0 && d.title.length <= 120 && this.lines(d.instructions).length > 0 && d.instructions.length <= 2000 && !!d.endsOn && d.endsOn >= this.minDate;
  }

  protected save(): void {
    const data = this.data();
    if (!data || !this.valid() || this.saving()) {
      return;
    }
    const d = this.draft;
    const body = { title: d.title.trim(), instructions: this.lines(d.instructions), minutes: d.minutes, frequency: d.frequency, endsOn: d.endsOn };
    const editing = this.editing();
    const request = editing ? this.api.updateHomework(editing, body) : this.api.assignHomework(data.pseudonymId, { ...body, templateCode: d.templateCode });
    this.saving.set(true);
    this.formError.set(null);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.ui.notify(editing ? 'care.homework.updated' : 'care.homework.assigned');
        this.editing.set(null);
        this.preview.set(false);
        this.draft = this.emptyDraft();
        this.load();
      },
      error: (e: unknown) => {
        // "Assign failed": the form keeps what was typed.
        this.saving.set(false);
        this.formError.set(shownError(e, 'care.homework.saveError'));
      },
    });
  }

  protected cancel(h: StaffHomework): void {
    this.ui.confirm('care.homework.cancelTitle', 'care.homework.cancelBody', 'care.homework.cancelConfirm', true, () => this.api.cancelHomework(h.id)).subscribe((r) => {
      if (r) {
        this.ui.notify('care.homework.cancelled');
        this.load();
      }
    });
  }

  protected startReply(c: StaffCheckin): void {
    this.replyTo.set(c.id);
    this.replyPreview.set(false);
    this.replyText = '';
  }

  protected sendReply(): void {
    const id = this.replyTo();
    const text = this.replyText.trim();
    if (!id || !text) {
      return;
    }
    this.saving.set(true);
    this.api.checkinFeedback(id, text).subscribe({
      next: () => {
        this.saving.set(false);
        this.replyTo.set(null);
        this.ui.notify('care.homework.replied');
        this.load();
      },
      error: (e: unknown) => {
        this.saving.set(false);
        this.formError.set(shownError(e, 'care.homework.saveError'));
      },
    });
  }

  private emptyDraft(): HomeworkDraft {
    return { title: '', instructions: '', frequency: 'daily', endsOn: localDate(7), minutes: null, templateCode: null };
  }
}
