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
import type { CallOutcome, CloseCrisisRequest, CrisisEventDetail, HandoverRequest, OpenCrisisEvent, Responder } from '../../core/api/types';
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
  /** Local emergency procedure: the rule-pack script and the number to call (SB-41). */
  script?: string;
  emergencyNumber?: string;
}

/** Same limit as the worker (SB-42). */
export const NOTE_MAX_LENGTH = 2000;

/** Confirmation with a note: contact made, local emergency procedure, a free note, a referral. Closes with the trimmed text. */
@Component({
  selector: 'tm-note-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ data.titleKey | translate }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content>
        @if (data.bodyKey) { <p>{{ data.bodyKey | translate }}</p> }
        @if (data.script) {
          <div class="script" data-testid="emergency-script">
            @if (data.emergencyNumber) {
              <p class="number">{{ 'crisis.emergencyDialog.number' | translate }} <a [attr.href]="'tel:' + data.emergencyNumber" dir="ltr" data-testid="emergency-number">{{ data.emergencyNumber }}</a></p>
            }
            <p>{{ data.script }}</p>
          </div>
        }
        <mat-form-field appearance="outline" class="tm-full">
          <mat-label>{{ data.labelKey | translate }}</mat-label>
          <textarea matInput cdkFocusInitial formControlName="text" rows="4" [attr.maxlength]="max" data-testid="note-text"></textarea>
          <mat-hint align="end" data-testid="note-count">{{ form.controls.text.value.length }} / {{ max }}</mat-hint>
          @if (form.controls.text.hasError('maxlength')) {
            <mat-error>{{ 'crisis.noteDialog.tooLong' | translate: { max: max } }}</mat-error>
          } @else if (form.controls.text.invalid) {
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
  styles: `
    .script { background: var(--tm-danger-tint); border: 1px solid var(--tm-danger-line); border-radius: 10px; padding: 10px 14px; margin-block-end: 16px; }
    .script p { margin: 4px 0; }
    .number { font-weight: 700; }
  `,
})
export class NoteDialogComponent {
  protected readonly data = inject<NoteDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<NoteDialogComponent, string>>(MatDialogRef);
  protected readonly max = NOTE_MAX_LENGTH;
  protected readonly form = inject(NonNullableFormBuilder).group({
    text: ['', this.data.required ? [Validators.required, Validators.pattern(/\S/), Validators.maxLength(NOTE_MAX_LENGTH)] : [Validators.maxLength(NOTE_MAX_LENGTH)]],
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
  /** Resolves with the event: closed, or still open while an "unreachable" close waits for the clinical lead. */
  submit: (request: CloseCrisisRequest) => Observable<OpenCrisisEvent>;
  /** The clinical lead's own "unreachable" close needs no second signature. */
  isLead: boolean;
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
          @if (form.controls.outcome.value === 'unreachable_referred') {
            <p class="tm-muted hint" data-testid="cosign-hint">{{ (data.isLead ? 'crisis.closeDialog.unreachableLead' : 'crisis.closeDialog.unreachableCoSign') | translate }}</p>
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
    .hint { margin: 8px 0 0; font-size: 13px; }
  `,
})
export class CloseDialogComponent {
  protected readonly data = inject<CloseDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<CloseDialogComponent, OpenCrisisEvent>>(MatDialogRef);
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
      next: (event) => this.ref.close(event),
      error: (error: unknown) => {
        this.error.set(shownError(error, 'common.error'));
        this.busy.set(false);
      },
    });
  }
}

export const CALL_OUTCOMES: readonly CallOutcome[] = ['answered', 'no_answer', 'voicemail', 'wrong_number'];

/** After a call attempt (SB-39): how did it go? Closes with the outcome; cancelling records nothing more. */
@Component({
  selector: 'tm-call-outcome-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatRadioModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'crisis.callDialog.title' | translate }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content>
        <p>{{ 'crisis.callDialog.body' | translate }}</p>
        <mat-radio-group formControlName="outcome" [attr.aria-label]="'crisis.callDialog.title' | translate" class="radios" data-testid="call-outcome">
          @for (o of outcomes; track o) {
            <mat-radio-button [value]="o" [attr.data-testid]="'call-' + o">{{ 'crisis.callDialog.outcomes.' + o | translate }}</mat-radio-button>
          }
        </mat-radio-group>
        @if (submitted() && form.invalid) {
          <p class="error" role="alert">{{ 'crisis.callDialog.required' | translate }}</p>
        }
      </mat-dialog-content>
      <mat-dialog-actions class="tm-dialog-actions">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button type="submit" data-testid="call-outcome-submit">{{ 'crisis.callDialog.confirm' | translate }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .radios { display: flex; flex-direction: column; align-items: flex-start; }
    .error { color: var(--tm-danger); margin: 4px 0 0; font-size: 13px; }
  `,
})
export class CallOutcomeDialogComponent {
  private readonly ref = inject<MatDialogRef<CallOutcomeDialogComponent, CallOutcome>>(MatDialogRef);
  protected readonly outcomes = CALL_OUTCOMES;
  protected readonly submitted = signal(false);
  protected readonly form = inject(NonNullableFormBuilder).group({ outcome: ['' as CallOutcome | '', Validators.required] });

  protected submit(): void {
    this.submitted.set(true);
    if (this.form.valid) {
      this.ref.close(this.form.getRawValue().outcome as CallOutcome);
    }
  }
}

export interface HandoverDialogData {
  responders: Observable<Responder[]>;
  submit: (request: HandoverRequest) => Observable<CrisisEventDetail>;
}

/** Hand over to a chosen colleague with a note (SB-43). The case stays with the owner until the colleague accepts. */
@Component({
  selector: 'tm-handover-dialog',
  imports: [ReactiveFormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatRadioModule, TranslateModule, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'crisis.handoverDialog.title' | translate }}</h2>
    <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <mat-dialog-content>
        <p>{{ 'crisis.handoverDialog.body' | translate }}</p>
        <fieldset class="people">
          <legend id="handover-to-label">{{ 'crisis.handoverDialog.to' | translate }}</legend>
          @if (responders(); as list) {
            @if (list.length) {
              <mat-radio-group formControlName="toUserId" aria-labelledby="handover-to-label" class="radios" data-testid="handover-to">
                @for (r of list; track r.userId) {
                  <mat-radio-button [value]="r.userId" [attr.data-testid]="'handover-' + r.userId">
                    {{ r.displayName }} · {{ 'roles.' + r.role | translate }}@if (r.onCall) { · {{ 'crisis.handoverDialog.onCall' | translate }} }
                  </mat-radio-button>
                }
              </mat-radio-group>
            } @else {
              <p class="tm-muted" data-testid="no-responders">{{ 'crisis.handoverDialog.nobody' | translate }}</p>
            }
          } @else if (!loadError()) {
            <p class="tm-muted">{{ 'common.loading' | translate }}</p>
          }
          @if (submitted() && form.controls.toUserId.invalid) {
            <p class="error" role="alert" data-testid="handover-to-error">{{ 'crisis.handoverDialog.toRequired' | translate }}</p>
          }
        </fieldset>
        <mat-form-field appearance="outline" class="tm-full">
          <mat-label>{{ 'crisis.handoverDialog.label' | translate }}</mat-label>
          <textarea matInput formControlName="note" rows="3" [attr.maxlength]="max" data-testid="handover-note"></textarea>
          <mat-hint align="end">{{ form.controls.note.value.length }} / {{ max }}</mat-hint>
        </mat-form-field>
        <tm-problem-message [error]="loadError() ?? error()" />
      </mat-dialog-content>
      <mat-dialog-actions class="tm-dialog-actions">
        <button mat-button type="button" mat-dialog-close>{{ 'common.cancel' | translate }}</button>
        <button mat-flat-button type="submit" [disabled]="busy()" data-testid="handover-submit">{{ 'crisis.handoverDialog.confirm' | translate }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `
    .people { border: 0; padding: 0; margin: 0 0 16px; }
    legend { font-weight: 600; margin-block-end: 8px; }
    .radios { display: flex; flex-direction: column; align-items: flex-start; }
    .error { color: var(--tm-danger); margin: 4px 0 0; font-size: 13px; }
  `,
})
export class HandoverDialogComponent {
  protected readonly data = inject<HandoverDialogData>(MAT_DIALOG_DATA);
  private readonly ref = inject<MatDialogRef<HandoverDialogComponent, CrisisEventDetail>>(MatDialogRef);
  protected readonly max = NOTE_MAX_LENGTH;
  protected readonly responders = signal<Responder[] | null>(null);
  protected readonly loadError = signal<ShownError | null>(null);
  protected readonly busy = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly submitted = signal(false);
  protected readonly form = inject(NonNullableFormBuilder).group({
    toUserId: ['', Validators.required],
    note: ['', Validators.maxLength(NOTE_MAX_LENGTH)],
  });

  constructor() {
    this.data.responders.subscribe({
      next: (list) => this.responders.set(list),
      error: (error: unknown) => this.loadError.set(shownError(error, 'common.error')),
    });
  }

  protected submit(): void {
    this.submitted.set(true);
    if (this.form.invalid || this.busy()) {
      this.form.markAllAsTouched();
      return;
    }
    const { toUserId, note } = this.form.getRawValue();
    this.busy.set(true);
    this.error.set(null);
    this.data.submit(note.trim() ? { toUserId, note: note.trim() } : { toUserId }).subscribe({
      next: (detail) => this.ref.close(detail),
      error: (error: unknown) => {
        this.error.set(shownError(error, 'common.error'));
        this.busy.set(false);
      },
    });
  }
}
