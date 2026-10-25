import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { TranslateModule } from '@ngx-translate/core';
import type { Observable } from 'rxjs';
import { type ShownError, shownError } from '../core/api/problem';
import { ProblemMessageComponent } from './problem-message.component';

export interface ReasonDialogResult<T> {
  value: T;
  level: number | null;
  choices: string[];
  text: string;
}

export interface ReasonDialogData<T> {
  titleKey: string;
  /** Explains what happens (and, for a risk change, the consequences). */
  bodyKey: string | null;
  bodyParams?: Record<string, unknown>;
  labelKey: string;
  confirmKey: string;
  danger: boolean;
  /** The text is required unless false (approve-like actions). */
  required?: boolean;
  /** No text field: the reason was already written on the page (S03). */
  hideText?: boolean;
  maxLength: number;
  /** Optional single choice of a level, e.g. [3, 4] or the levels above the current one. */
  levels?: readonly number[];
  levelLabelKey?: string;
  /** Label key of each level option, given the level number ("risk.3"). */
  levelOptionKey?: (level: number) => string;
  /** Optional multiple choice (e.g. referral resources); at least one is required when present. */
  choices?: readonly string[];
  choicesLabelKey?: string;
  choiceOptionKey?: (choice: string) => string;
  /** At most this many choices. */
  maxChoices?: number;
  /** The request runs inside the dialog, so a failure keeps the typed text and shows the problem (S03 "save error"). */
  submit: (level: number | null, choices: string[], text: string) => Observable<T>;
}

/** Confirmation with a required reason, an optional level and optional choices. Closes with the server's answer. */
@Component({
  selector: 'tm-reason-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatRadioModule, MatCheckboxModule, TranslateModule, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.titleKey | translate }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content>
        @if (data.bodyKey) { <p>{{ data.bodyKey | translate: data.bodyParams }}</p> }
        @if (data.levels?.length) {
          <fieldset class="group">
            <legend>{{ (data.levelLabelKey ?? 'common.level') | translate }}</legend>
            <mat-radio-group formControlName="level" class="options" data-testid="reason-level">
              @for (level of data.levels; track level) {
                <mat-radio-button [value]="level" [attr.data-testid]="'level-' + level">{{ level }} · {{ optionLabel(level) | translate }}</mat-radio-button>
              }
            </mat-radio-group>
            @if (form.controls.level.touched && form.controls.level.invalid) {
              <mat-error>{{ 'common.chooseOne' | translate }}</mat-error>
            }
          </fieldset>
        }
        @if (data.choices?.length) {
          <fieldset class="group">
            <legend>{{ (data.choicesLabelKey ?? 'common.choose') | translate }}</legend>
            <div class="options">
              @for (choice of data.choices; track choice) {
                <mat-checkbox [checked]="picked().has(choice)" (change)="toggle(choice)" [attr.data-testid]="'choice-' + choice">{{ choiceLabel(choice) | translate }}</mat-checkbox>
              }
            </div>
            @if (choicesTouched() && picked().size === 0) {
              <mat-error>{{ 'common.chooseOne' | translate }}</mat-error>
            } @else if (tooMany()) {
              <mat-error>{{ 'common.chooseAtMost' | translate: { count: data.maxChoices } }}</mat-error>
            }
          </fieldset>
        }
        @if (!data.hideText) {
        <mat-form-field appearance="outline" class="tm-full">
          <mat-label>{{ data.labelKey | translate }}</mat-label>
          <textarea matInput cdkFocusInitial formControlName="text" rows="3" [attr.maxlength]="data.maxLength" data-testid="reason-text"></textarea>
          <mat-hint align="end">{{ form.controls.text.value.length }} / {{ data.maxLength }}</mat-hint>
          @if (form.controls.text.invalid) {
            <mat-error>{{ 'common.required' | translate }}</mat-error>
          }
        </mat-form-field>
        }
        <tm-problem-message [error]="error()" />
      </mat-dialog-content>
      <mat-dialog-actions class="tm-dialog-actions">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button type="submit" [class.tm-danger-button]="data.danger" [disabled]="busy()" data-testid="reason-submit">{{ data.confirmKey | translate }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .group { border: 0; padding: 0; margin: 0 0 12px; }
    .group legend { font-weight: 600; margin-block-end: 6px; }
    .options { display: flex; flex-direction: column; gap: 4px; }
  `,
})
export class ReasonDialogComponent<T> {
  protected readonly data = inject<ReasonDialogData<T>>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<ReasonDialogComponent<T>, ReasonDialogResult<T>>>(MatDialogRef);
  protected readonly busy = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly picked = signal<ReadonlySet<string>>(new Set());
  protected readonly choicesTouched = signal(false);
  protected readonly form = inject(NonNullableFormBuilder).group({
    level: [null as number | null, this.data.levels?.length ? [Validators.required] : []],
    text: ['', this.data.required === false || this.data.hideText ? [Validators.maxLength(this.data.maxLength)] : [Validators.required, Validators.pattern(/\S/), Validators.maxLength(this.data.maxLength)]],
  });

  protected tooMany(): boolean {
    return this.data.maxChoices !== undefined && this.picked().size > this.data.maxChoices;
  }

  protected optionLabel(level: number): string {
    return this.data.levelOptionKey ? this.data.levelOptionKey(level) : `risk.${level}`;
  }

  protected choiceLabel(choice: string): string {
    return this.data.choiceOptionKey ? this.data.choiceOptionKey(choice) : choice;
  }

  protected toggle(choice: string): void {
    this.choicesTouched.set(true);
    this.picked.update((s) => {
      const next = new Set(s);
      if (next.has(choice)) {
        next.delete(choice);
      } else {
        next.add(choice);
      }
      return next;
    });
  }

  protected submit(): void {
    const needsChoice = (this.data.choices?.length ?? 0) > 0 && this.picked().size === 0;
    if (this.form.invalid || needsChoice || this.tooMany() || this.busy()) {
      this.form.markAllAsTouched();
      this.choicesTouched.set(true);
      return;
    }
    const { level, text } = this.form.getRawValue();
    const choices = this.data.choices?.filter((c) => this.picked().has(c)) ?? [];
    this.busy.set(true);
    this.error.set(null);
    this.data.submit(level, choices, text.trim()).subscribe({
      next: (value) => this.ref.close({ value, level, choices, text: text.trim() }),
      error: (error: unknown) => {
        this.error.set(shownError(error, 'common.error'));
        this.busy.set(false);
      },
    });
  }
}
