import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslateModule } from '@ngx-translate/core';
import { type ShownError, shownError } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { StaffIntakeView } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { RiskTagComponent } from '../../shared/risk-tag.component';

export interface IntakeDialogData {
  pseudonymId: string;
}
export type IntakeDialogResult = 'approved' | undefined;

const SECTIONS = ['concern', 'goals', 'supports', 'priorCare', 'extra'] as const;

/** The intake summary of one patient (audited on the server), with "approve file". */
@Component({
  selector: 'tm-intake-dialog',
  imports: [MatDialogModule, MatButtonModule, MatProgressBarModule, TranslateModule, LocalDatePipe, ProblemMessageComponent, RiskTagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h2 mat-dialog-title>{{ 'intake.title' | translate: { id: data.pseudonymId } }}</h2>
    <mat-dialog-content>
      @if (loading()) {
        <mat-progress-bar mode="indeterminate" [attr.aria-label]="'common.loading' | translate" />
      }
      <tm-problem-message [error]="error()" />
      @if (intake(); as view) {
        <p class="tm-muted audit">{{ 'intake.audited' | translate }}</p>
        <dl class="scores" data-testid="intake-scores">
          <div><dt>{{ 'intake.risk' | translate }}</dt><dd><tm-risk-tag [level]="view.riskLevel" /></dd></div>
          <div><dt>PHQ-9</dt><dd>{{ view.phq9 ?? ('common.none' | translate) }}</dd></div>
          <div><dt>GAD-7</dt><dd>{{ view.gad7 ?? ('common.none' | translate) }}</dd></div>
          <div>
            <dt>{{ 'intake.cssrs' | translate }}</dt>
            <dd data-testid="cssrs">
              @switch (view.cssrsPositive) {
                @case (true) { <span class="tm-tag tm-risk-4">{{ 'intake.cssrsPositive' | translate }}</span> }
                @case (false) { <span class="tm-tag tm-risk-1">{{ 'intake.cssrsNegative' | translate }}</span> }
                @default { <span class="tm-muted">{{ 'intake.cssrsMissing' | translate }}</span> }
              }
            </dd>
          </div>
        </dl>
        @if (view.submittedAt) {
          <p class="tm-muted">{{ 'intake.submittedAt' | translate: { date: (view.submittedAt | tmDate: 'longDate' : language.language()) } }}</p>
        } @else {
          <p class="tm-muted">{{ 'intake.notSubmitted' | translate }}</p>
        }
        @for (key of sections; track key) {
          <section class="section">
            <h3>{{ 'intake.sections.' + key | translate }}</h3>
            @if (view.sections[key]) {
              <p class="text">{{ view.sections[key] }}</p>
            } @else {
              <p class="tm-muted">{{ 'intake.empty' | translate }}</p>
            }
          </section>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions class="tm-dialog-actions">
      <button mat-button type="button" mat-dialog-close>{{ 'common.close' | translate }}</button>
      @if (intake(); as view) {
        @if (view.approved) {
          <span class="tm-tag tm-risk-1" data-testid="approved">{{ 'intake.approved' | translate }}</span>
        } @else {
          <button mat-flat-button type="button" (click)="approve()" [disabled]="approving()" data-testid="approve">{{ 'intake.approve' | translate }}</button>
        }
      }
    </mat-dialog-actions>
  `,
  styles: `
    .audit { margin-block-start: 0; font-size: 13px; }
    .scores { display: grid; grid-template-columns: repeat(auto-fit, minmax(120px, 1fr)); gap: 12px; margin: 0 0 12px; }
    .scores dt { font-size: 13px; color: var(--tm-muted); }
    .scores dd { margin: 4px 0 0; font-weight: 600; }
    .section h3 { font-size: 15px; margin: 16px 0 4px; }
    .text { white-space: pre-line; margin: 0; }
  `,
})
export class IntakeDialogComponent {
  protected readonly data = inject<IntakeDialogData>(MAT_DIALOG_DATA);
  private readonly api = inject(StaffApiService);
  private readonly ref = inject<MatDialogRef<IntakeDialogComponent, IntakeDialogResult>>(MatDialogRef);
  protected readonly language = inject(LanguageService);
  protected readonly sections = SECTIONS;

  protected readonly intake = signal<StaffIntakeView | null>(null);
  protected readonly loading = signal(true);
  protected readonly approving = signal(false);
  protected readonly error = signal<ShownError | null>(null);

  constructor() {
    this.api.intake(this.data.pseudonymId).subscribe({
      next: (view) => {
        this.intake.set(view);
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.error.set(shownError(error, 'intake.loadError'));
        this.loading.set(false);
      },
    });
  }

  protected approve(): void {
    this.approving.set(true);
    this.error.set(null);
    this.api.approveIntake(this.data.pseudonymId).subscribe({
      next: () => this.ref.close('approved'),
      error: (error: unknown) => {
        this.error.set(shownError(error, 'common.error'));
        this.approving.set(false);
      },
    });
  }
}
