import { ChangeDetectionStrategy, Component, DestroyRef, OnDestroy, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { DecimalPipe } from '@angular/common';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { of } from 'rxjs';
import { TranslateModule } from '@ngx-translate/core';
import { type ShownError, shownError, statusOf } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { NoteTemplate, SessionNoteView } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { SchedulingApiService } from '../../core/api/scheduling-api.service';
import { getDisplayTimeZone } from '../../core/i18n/format';
import { MatDialog } from '@angular/material/dialog';
import type { StaffAppointment } from '../../core/api/types';
import { SlotPickerDialogComponent, type SlotPickerDialogData } from '../../shared/slot-picker-dialog.component';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { careUi } from './care-dialogs';
import { PatientTabsComponent } from './patient-tabs.component';

export const NOTE_TEMPLATES: readonly NoteTemplate[] = ['soap', 'dap', 'free'];
/** S33: the draft is saved automatically every 25 seconds while it has unsaved changes. */
export const AUTOSAVE_MS = 25_000;

/**
 * S33 session notes (/patients/:id/sessions/:appointmentId/notes). SOAP, DAP or free text; the draft saves on demand
 * and every 25 s with the revision it started from (If-Match). A failed save says «not saved» and the text stays on
 * the page (kept in memory only, never in the browser's storage). A 412 shows the saved version next to the draft.
 * Signing (after a confirmation) locks the note; then only addenda. Up to three plain-language points can be sent
 * to the patient when signing. Notes are never visible to the patient.
 */
@Component({
  selector: 'tm-session-notes',
  imports: [DecimalPipe, FormsModule, MatButtonModule, MatCheckboxModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent, PatientTabsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './session-notes.component.html',
  styleUrl: './care.scss',
})
export class SessionNotesComponent implements OnDestroy {
  readonly id = input.required<string>();
  readonly appointmentId = input.required<string>();

  private readonly api = inject(StaffApiService);
  private readonly destroyRef = inject(DestroyRef);
  private readonly ui = careUi();
  private readonly scheduling = inject(SchedulingApiService);
  private readonly dialog = inject(MatDialog);
  protected readonly language = inject(LanguageService);

  protected readonly templates = NOTE_TEMPLATES;
  protected readonly view = signal<SessionNoteView | null>(null);
  protected readonly loading = signal(true);
  protected readonly notFound = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly saveError = signal<ShownError | null>(null);
  protected readonly saving = signal(false);
  protected readonly savedAt = signal<Date | null>(null);
  protected readonly dirty = signal(false);
  protected readonly conflict = signal<SessionNoteView | null>(null);
  protected readonly signed = computed(() => this.view()?.note?.status === 'signed');
  protected template: NoteTemplate = 'soap';
  protected fields: Record<string, string> = {};
  protected points: string[] = ['', '', ''];
  protected share = false;
  protected addendum = '';
  private revision = 0;
  private readonly timer = setInterval(() => this.autosave(), AUTOSAVE_MS);

  constructor() {
    effect(() => {
      const id = this.id();
      const appointment = this.appointmentId();
      untracked(() => this.load(id, appointment));
    });
  }

  ngOnDestroy(): void {
    clearInterval(this.timer);
  }

  /** «حجز الجلسة القادمة»: one of the therapist's own free times; the patient is told. */
  protected bookNext(): void {
    const v = this.view();
    if (!v) {
      return;
    }
    const data: SlotPickerDialogData<StaffAppointment> = {
      titleKey: 'care.notes.bookNextTitle',
      bodyKey: 'care.notes.bookNextBody',
      confirmKey: 'care.notes.bookNextConfirm',
      timeZone: getDisplayTimeZone() ?? Intl.DateTimeFormat().resolvedOptions().timeZone,
      submit: (slotId) => this.scheduling.bookNext(v.pseudonymId, slotId),
    };
    this.dialog
      .open<SlotPickerDialogComponent<StaffAppointment>, SlotPickerDialogData<StaffAppointment>, StaffAppointment>(SlotPickerDialogComponent, { data, width: '460px', direction: this.language.direction() })
      .afterClosed()
      .subscribe((booked) => {
        if (booked) {
          this.view.update((current) => (current ? { ...current, nextAppointment: { id: booked.id, startsAt: booked.startsAt, durationMinutes: booked.durationMinutes, sequenceNumber: booked.sequenceNumber, status: booked.status } } : current));
          this.ui.notify('care.notes.bookNextDone');
        }
      });
  }

  protected keys(): string[] {
    return this.view()?.templates[this.template] ?? [];
  }

  protected load(id = this.id(), appointment = this.appointmentId()): void {
    this.loading.set(true);
    this.api
      .sessionNote(id, appointment)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (v) => {
          this.apply(v);
          this.loading.set(false);
        },
        error: (e: unknown) => {
          this.loading.set(false);
          if (statusOf(e) === 404) {
            this.notFound.set(true);
          } else {
            this.error.set(shownError(e, 'care.notes.loadError'));
          }
        },
      });
  }

  protected changed(): void {
    this.dirty.set(true);
  }

  /** Switching template asks first when text would move; the text goes into the first field of the new template. */
  protected switchTemplate(next: NoteTemplate): void {
    if (next === this.template) {
      return;
    }
    const text = Object.values(this.fields).filter((v) => v.trim()).join('\n\n');
    const apply = () => {
      this.template = next;
      const keys = this.keys();
      this.fields = Object.fromEntries(keys.map((k, i) => [k, i === 0 ? text : '']));
      this.dirty.set(true);
    };
    if (!text) {
      apply();
      return;
    }
    this.ui.confirm('care.notes.switchTitle', 'care.notes.switchBody', 'care.notes.switchConfirm', false, () => of(true)).subscribe((r) => r && apply());
  }

  protected save(): void {
    const v = this.view();
    if (!v || !v.canEdit || this.signed() || this.saving()) {
      return;
    }
    this.saving.set(true);
    this.saveError.set(null);
    const body = { template: this.template, fields: this.fields, summaryPoints: this.points.map((p) => p.trim()).filter((p) => p), shareSummary: this.share };
    this.api.saveNote(v.pseudonymId, v.appointment.id, this.revision, body).subscribe({
      next: (saved) => {
        this.saving.set(false);
        this.dirty.set(false);
        this.savedAt.set(new Date());
        this.conflict.set(null);
        this.view.set(saved);
        this.revision = saved.note?.revision ?? this.revision;
      },
      error: (e: unknown) => {
        this.saving.set(false);
        if (statusOf(e) === 412) {
          this.api.sessionNote(v.pseudonymId, v.appointment.id).subscribe((latest) => {
            this.conflict.set(latest);
            this.revision = latest.note?.revision ?? 0;
          });
        } else {
          this.saveError.set(shownError(e, 'care.notes.notSaved'));
        }
      },
    });
  }

  protected sign(): void {
    const v = this.view();
    if (!v || this.signed()) {
      return;
    }
    if (this.dirty() || !v.note) {
      this.save();
      return;
    }
    this.ui.confirm('care.notes.signTitle', this.share ? 'care.notes.signBodyShare' : 'care.notes.signBody', 'care.notes.signConfirm', false, () => this.api.signNote(v.pseudonymId, v.appointment.id, this.revision)).subscribe((r) => {
      if (r) {
        this.apply(r.value);
        this.ui.notify('care.notes.signed');
      }
    });
  }

  protected addAddendum(): void {
    const v = this.view();
    const text = this.addendum.trim();
    if (!v || !text) {
      return;
    }
    this.api.addAddendum(v.pseudonymId, v.appointment.id, text).subscribe({
      next: (saved) => {
        this.addendum = '';
        this.apply(saved);
        this.ui.notify('care.notes.addendumAdded');
      },
      error: (e: unknown) => this.saveError.set(shownError(e, 'common.error')),
    });
  }

  protected fieldsOf(v: SessionNoteView): string {
    return Object.values(v.note?.fields ?? {}).filter((x) => x).join('\n\n');
  }

  protected mineText(): string {
    return Object.values(this.fields).filter((x) => x).join('\n\n');
  }

  private autosave(): void {
    if (this.dirty() && !this.saving() && !this.conflict()) {
      this.save();
    }
  }

  private apply(v: SessionNoteView): void {
    this.view.set(v);
    this.revision = v.note?.revision ?? 0;
    this.template = v.note?.template ?? 'soap';
    const keys = v.templates[this.template] ?? [];
    this.fields = Object.fromEntries(keys.map((k) => [k, v.note?.fields[k] ?? '']));
    const pts = v.note?.summaryPoints ?? [];
    this.points = [pts[0] ?? '', pts[1] ?? '', pts[2] ?? ''];
    this.share = v.note?.shareSummary ?? false;
    this.dirty.set(false);
    if (v.note) {
      this.savedAt.set(new Date(v.note.updatedAt));
    }
  }
}
