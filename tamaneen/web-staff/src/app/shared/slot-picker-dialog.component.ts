import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSelectModule } from '@angular/material/select';
import { TranslateModule } from '@ngx-translate/core';
import type { Observable } from 'rxjs';
import { type ShownError, shownError } from '../core/api/problem';
import { SchedulingApiService } from '../core/api/scheduling-api.service';
import type { FreeSlot } from '../core/api/types';
import { intlLocale } from '../core/i18n/format';
import { LanguageService } from '../core/i18n/language.service';
import { ProblemMessageComponent } from './problem-message.component';

export interface SlotPickerDialogData<T> {
  titleKey: string;
  bodyKey: string | null;
  bodyParams?: Record<string, unknown>;
  confirmKey: string;
  /** Times are shown in this zone (the schedule's). */
  timeZone: string;
  /** A required reason from this list (S18 reschedule, SB-26); none for S33 "book the next session". */
  reasons?: readonly string[];
  reasonLabelKey?: string;
  reasonOptionKey?: (reason: string) => string;
  /** Runs inside the dialog, so a taken time shows the problem and the list reloads. */
  submit: (slotId: string, reason: string | null) => Observable<T>;
}

/**
 * Picks one of the therapist's own free times (GET /v1/staff/free-slots), plus a reason when asked.
 * Closes with the server's answer.
 */
@Component({
  selector: 'tm-slot-picker-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatSelectModule, MatProgressBarModule, TranslateModule, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.titleKey | translate }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content>
        @if (data.bodyKey) { <p>{{ data.bodyKey | translate: data.bodyParams }}</p> }
        @if (loading()) {
          <mat-progress-bar mode="indeterminate" [attr.aria-label]="'common.loading' | translate" />
        } @else if (slots().length === 0) {
          <p class="tm-muted" data-testid="slots-empty">{{ 'slotPicker.empty' | translate }}</p>
        } @else {
          <mat-form-field appearance="outline" class="tm-full">
            <mat-label>{{ 'slotPicker.time' | translate }}</mat-label>
            <mat-select formControlName="slotId" data-testid="slot-select">
              @for (s of slots(); track s.slotId) {
                <mat-option [value]="s.slotId" [attr.data-testid]="'slot-' + s.slotId">{{ label(s) }}</mat-option>
              }
            </mat-select>
            @if (form.controls.slotId.touched && form.controls.slotId.invalid) {
              <mat-error>{{ 'common.chooseOne' | translate }}</mat-error>
            }
          </mat-form-field>
        }
        @if (data.reasons?.length) {
          <mat-form-field appearance="outline" class="tm-full">
            <mat-label>{{ (data.reasonLabelKey ?? 'common.choose') | translate }}</mat-label>
            <mat-select formControlName="reason" data-testid="slot-reason">
              @for (r of data.reasons; track r) {
                <mat-option [value]="r" [attr.data-testid]="'reason-' + r">{{ reasonLabel(r) | translate }}</mat-option>
              }
            </mat-select>
            @if (form.controls.reason.touched && form.controls.reason.invalid) {
              <mat-error>{{ 'common.chooseOne' | translate }}</mat-error>
            }
          </mat-form-field>
        }
        <tm-problem-message [error]="error()" />
      </mat-dialog-content>
      <mat-dialog-actions class="tm-dialog-actions">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button type="submit" [disabled]="busy() || slots().length === 0" data-testid="slot-submit">{{ data.confirmKey | translate }}</button>
      </mat-dialog-actions>
    </form>
  `,
})
export class SlotPickerDialogComponent<T> {
  protected readonly data = inject<SlotPickerDialogData<T>>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<SlotPickerDialogComponent<T>, T>>(MatDialogRef);
  private readonly api = inject(SchedulingApiService);
  private readonly language = inject(LanguageService);
  protected readonly slots = signal<FreeSlot[]>([]);
  protected readonly loading = signal(true);
  protected readonly busy = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    slotId: ['', [Validators.required]],
    reason: ['', this.data.reasons?.length ? [Validators.required] : []],
  });

  constructor() {
    this.load();
  }

  protected label(s: FreeSlot): string {
    return new Intl.DateTimeFormat(intlLocale(this.language.language()), {
      timeZone: this.data.timeZone,
      weekday: 'long',
      day: 'numeric',
      month: 'long',
      hour: 'numeric',
      minute: '2-digit',
    }).format(new Date(s.startsAt));
  }

  protected reasonLabel(reason: string): string {
    return this.data.reasonOptionKey ? this.data.reasonOptionKey(reason) : reason;
  }

  protected submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { slotId, reason } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.data.submit(slotId, reason || null).subscribe({
      next: (value) => this.ref.close(value),
      error: (error: unknown) => {
        this.error.set(shownError(error, 'common.error'));
        this.busy.set(false);
        // The time may have been taken meanwhile: offer the current list.
        this.form.controls.slotId.setValue('');
        this.load();
      },
    });
  }

  private load(): void {
    this.loading.set(true);
    this.api.freeSlots().subscribe({
      next: (slots) => {
        this.slots.set(slots);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.error.set(shownError(error, 'common.error'));
        this.loading.set(false);
      },
    });
  }
}
