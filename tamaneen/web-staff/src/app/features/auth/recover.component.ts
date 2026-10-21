import { ChangeDetectionStrategy, Component, ElementRef, Injector, afterNextRender, inject, signal, viewChild } from '@angular/core';
import { BidiModule } from '@angular/cdk/bidi';
import { NgTemplateOutlet } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/i18n/language.service';
import { LanguageToggleComponent } from '../../shared/language-toggle.component';
import { CONTACT_MAX_LENGTH, type LoginError, loginError } from './login.component';

type Step = 'email' | 'code' | 'done';

/** Same rule as the API (PasswordService): at least 12 characters; the breached-password check is on the server. */
export const PASSWORD_MIN_LENGTH = 12;

/**
 * S06 /recover: the work email gets a code (the answer is the same whether or not the account exists), then a
 * new password is set with it. Two-step verification is not reset here; that needs two admins.
 */
@Component({
  selector: 'tm-recover',
  imports: [BidiModule, NgTemplateOutlet, ReactiveFormsModule, RouterLink, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, TranslateModule, LanguageToggleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './recover.component.html',
  styleUrl: './login.component.scss',
})
export class RecoverComponent {
  private readonly auth = inject(AuthService);
  private readonly injector = inject(Injector);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly language = inject(LanguageService);

  protected readonly step = signal<Step>('email');
  protected readonly busy = signal(false);
  protected readonly error = signal<LoginError | null>(null);
  protected readonly maskedContact = signal('');
  protected readonly passwordMin = PASSWORD_MIN_LENGTH;
  private challengeId = '';

  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  protected readonly emailForm = this.fb.group({ contact: ['', [Validators.required, Validators.email, Validators.maxLength(CONTACT_MAX_LENGTH)]] });
  protected readonly resetForm = this.fb.group({
    code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]],
    newPassword: ['', [Validators.required, Validators.minLength(PASSWORD_MIN_LENGTH)]],
  });

  protected async sendCode(): Promise<void> {
    if (this.emailForm.invalid || this.busy()) {
      this.emailForm.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      const challenge = await this.auth.startRecovery(this.emailForm.getRawValue().contact.trim());
      this.challengeId = challenge.challengeId;
      this.maskedContact.set(challenge.maskedContact);
      this.step.set('code');
      this.focusHeading();
    } catch (error) {
      this.error.set(loginError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async reset(): Promise<void> {
    if (this.resetForm.invalid || this.busy()) {
      this.resetForm.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const { code, newPassword } = this.resetForm.getRawValue();
    try {
      await this.auth.completeRecovery(this.challengeId, code, newPassword);
      this.resetForm.reset();
      this.step.set('done');
      this.focusHeading();
    } catch (error) {
      this.error.set(loginError(error));
      this.resetForm.controls.code.reset('');
    } finally {
      this.busy.set(false);
    }
  }

  protected startOver(): void {
    this.challengeId = '';
    this.resetForm.reset();
    this.error.set(null);
    this.step.set('email');
    this.focusHeading();
  }

  private focusHeading(): void {
    afterNextRender(() => this.heading()?.nativeElement.focus(), { injector: this.injector });
  }
}
