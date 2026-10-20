import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatRadioModule } from '@angular/material/radio';
import { TranslateModule } from '@ngx-translate/core';
import type { Observable } from 'rxjs';
import { type ShownError, shownError } from '../../core/api/problem';
import type { CloseCrisisRequest } from '../../core/api/types';
import { ProblemMessageComponent } from '../../shared/problem-message.component';

const TOTP = /^\d{6}$/;

export interface StepUpDialogData<T> {
  titleKey: string;
  bodyKey: string;
  confirmKey: string;
  /** Contact reveal also needs a reason, recorded in the audit log. */
  requireReason: boolean;
  submit: (totpCode: string, reason: string) => Observable<T>;
}

/** Extra verification with the authenticator code before sensitive data is shown. The request runs here so errors stay in the dialog. */
@Component({
  selector: 'tm-step-up-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslateModule, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.titleKey | translate }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content>
        <p>{{ data.bodyKey | translate }}</p>
        <mat-form-field appearance="outline" class="tm-full">
          <mat-label>{{ 'crisis.stepUp.code' | translate }}</mat-label>
          <input #code matInput cdkFocusInitial formControlName="totpCode" inputmode="numeric" autocomplete="one-time-code" maxlength="6" dir="ltr" data-testid="step-up-code" />
          @if (form.controls.totpCode.hasError('required')) {
            <mat-error>{{ 'common.required' | translate }}</mat-error>
          } @else if (form.controls.totpCode.invalid) {
            <mat-error>{{ 'auth.codeInvalid' | translate }}</mat-error>
          }
        </mat-form-field>
        @if (data.requireReason) {
          <mat-form-field appearance="outline" class="tm-full">
            <mat-label>{{ 'crisis.stepUp.reason' | translate }}</mat-label>
            <textarea matInput formControlName="reason" rows="2" data-testid="step-up-reason"></textarea>
            <mat-hint>{{ 'crisis.stepUp.reasonHint' | translate }}</mat-hint>
            @if (form.controls.reason.invalid) {
              <mat-error>{{ 'common.required' | translate }}</mat-error>
            }
          </mat-form-field>
        }
        <tm-problem-message [error]="error()" />
      </mat-dialog-content>
      <mat-dialog-actions class="tm-dialog-actions">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button type="submit" [disabled]="busy()" data-testid="step-up-submit">{{ data.confirmKey | translate }}</button>
      </mat-dialog-actions>
    </form>
  `,
})
export class StepUpDialogComponent<T> {
  protected readonly data = inject<StepUpDialogData<T>>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<StepUpDialogComponent<T>, T>>(MatDialogRef);
  private readonly injector = inject(Injector);
  private readonly codeInput = viewChild.required<ElementRef<HTMLInputElement>>('code');
  protected readonly busy = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    totpCode: ['', [Validators.required, Validators.pattern(TOTP)]],
    reason: ['', this.data.requireReason ? [Validators.required, Validators.pattern(/\S/)] : []],
  });

  protected submit(): void {
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { totpCode, reason } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.data.submit(totpCode, reason.trim()).subscribe({
      next: (result) => this.ref.close(result),
      error: (error: unknown) => {
        this.error.set(shownError(error, 'common.error'));
        this.busy.set(false);
        this.form.controls.totpCode.reset('');
        afterNextRender(() => this.codeInput().nativeElement.focus(), { injector: this.injector });
      },
    });
  }
}

export interface NoteDialogData {
  titleKey: string;
  bodyKey: string | null;
  labelKey: string;
  confirmKey: string;
  required: boolean;
  danger: boolean;
}

/** Confirmation with a note: local emergency procedure, a free note, or a handover. Closes with the trimmed text. */
@Component({
  selector: 'tm-note-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.titleKey | translate }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content>
        @if (data.bodyKey) { <p>{{ data.bodyKey | translate }}</p> }
        <mat-form-field appearance="outline" class="tm-full">
          <mat-label>{{ data.labelKey | translate }}</mat-label>
          <textarea matInput cdkFocusInitial formControlName="text" rows="4" data-testid="note-text"></textarea>
          @if (form.controls.text.invalid) {
            <mat-error>{{ 'common.required' | translate }}</mat-error>
          }
        </mat-form-field>
      </mat-dialog-content>
      <mat-dialog-actions class="tm-dialog-actions">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button type="submit" [class.tm-danger-button]="data.danger" data-testid="note-submit">{{ data.confirmKey | translate }}</button>
      </mat-dialog-actions>
    </form>
  `,
})
export class NoteDialogComponent {
  protected readonly data = inject<NoteDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<NoteDialogComponent, string>>(MatDialogRef);
  protected readonly form = inject(NonNullableFormBuilder).group({
    text: ['', this.data.required ? [Validators.required, Validators.pattern(/\S/)] : []],
  });

  protected submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.ref.close(this.form.getRawValue().text.trim());
  }
}

export const CLOSE_OUTCOMES: readonly CloseCrisisRequest['outcome'][] = ['safe_contacted', 'emergency_services', 'unreachable_referred', 'false_alarm'];

export interface CloseDialogData {
  submit: (request: CloseCrisisRequest) => Observable<void>;
}

/** Close with outcome: one of four outcomes and a required reason. */
@Component({
  selector: 'tm-close-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatRadioModule, TranslateModule, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'crisis.closeDialog.title' | translate }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content>
        <fieldset class="outcomes" [attr.aria-invalid]="showOutcomeError()">
          <legend id="close-outcome-label">{{ 'crisis.closeDialog.outcome' | translate }}</legend>
          <mat-radio-group formControlName="outcome" aria-labelledby="close-outcome-label" class="radios" data-testid="close-outcome">
            @for (o of outcomes; track o) {
              <mat-radio-button [value]="o" [attr.data-testid]="'close-' + o">{{ 'crisis.closeDialog.outcomes.' + o | translate }}</mat-radio-button>
            }
          </mat-radio-group>
          @if (showOutcomeError()) {
            <p class="error" role="alert" data-testid="outcome-error">{{ 'crisis.closeDialog.outcomeRequired' | translate }}</p>
          }
        </fieldset>
        <mat-form-field appearance="outline" class="tm-full">
          <mat-label>{{ 'crisis.closeDialog.reason' | translate }}</mat-label>
          <textarea matInput formControlName="reason" rows="3" data-testid="close-reason"></textarea>
          <mat-hint>{{ 'crisis.stepUp.reasonHint' | translate }}</mat-hint>
          @if (form.controls.reason.invalid) {
            <mat-error data-testid="reason-error">{{ 'common.required' | translate }}</mat-error>
          }
        </mat-form-field>
        <tm-problem-message [error]="error()" />
      </mat-dialog-content>
      <mat-dialog-actions class="tm-dialog-actions">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button type="submit" [disabled]="busy()" data-testid="close-submit">{{ 'crisis.closeDialog.confirm' | translate }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .outcomes { border: 0; padding: 0; margin: 0 0 16px; }
    legend { font-weight: 600; margin-block-end: 8px; }
    .radios { display: flex; flex-direction: column; align-items: flex-start; }
    .error { color: var(--tm-danger); margin: 4px 0 0; font-size: 13px; }
  `,
})
export class CloseDialogComponent {
  protected readonly data = inject<CloseDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<CloseDialogComponent, true>>(MatDialogRef);
  protected readonly outcomes = CLOSE_OUTCOMES;
  protected readonly busy = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly submitted = signal(false);
  protected readonly form = inject(NonNullableFormBuilder).group({
    outcome: ['' as CloseCrisisRequest['outcome'] | '', Validators.required],
    reason: ['', [Validators.required, Validators.pattern(/\S/)]],
  });

  protected showOutcomeError(): boolean {
    return this.submitted() && this.form.controls.outcome.invalid;
  }

  protected submit(): void {
    this.submitted.set(true);
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { outcome, reason } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.data.submit({ outcome: outcome as CloseCrisisRequest['outcome'], reason: reason.trim() }).subscribe({
      next: () => this.ref.close(true),
      error: (error: unknown) => {
        this.error.set(shownError(error, 'common.error'));
        this.busy.set(false);
      },
    });
  }
}
