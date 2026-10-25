import { ChangeDetectionStrategy, Component, ElementRef, afterNextRender, inject, Injector, signal, viewChild } from '@angular/core';
import { BidiModule } from '@angular/cdk/bidi';
import { NgTemplateOutlet } from '@angular/common';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { toProblem } from '../../core/api/problem';
import { AuthService } from '../../core/auth/auth.service';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/session/session.service';
import { LanguageToggleComponent } from '../../shared/language-toggle.component';

type Step = 'credentials' | 'mfa';

/** RFC 5321 limit for an email address; the design caps the work email field at 254 characters. */
export const CONTACT_MAX_LENGTH = 254;
type Method = 'totp' | 'recovery';

/** What went wrong, ready to render: the server's localized problem plus attempts/lock hints. */
export interface LoginError {
  title: string | null;
  detail: string | null;
  remainingAttempts: number | null;
  lockedMinutes: number | null;
  fallbackKey: string | null;
}

export function loginError(error: unknown): LoginError {
  const problem = toProblem(error);
  if (!problem) {
    return { title: null, detail: null, remainingAttempts: null, lockedMinutes: null, fallbackKey: 'common.offline' };
  }
  return {
    title: problem.title,
    detail: problem.detail ?? null,
    remainingAttempts: problem.remainingAttempts ?? null,
    lockedMinutes: problem.retryAfterSeconds != null ? Math.max(1, Math.ceil(problem.retryAfterSeconds / 60)) : null,
    fallbackKey: null,
  };
}

/** S06 staff sign-in: work email and password, then the authenticator code (or a recovery code). */
@Component({
  selector: 'tm-login',
  imports: [BidiModule, NgTemplateOutlet, ReactiveFormsModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, RouterLink, TranslateModule, LanguageToggleComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly session = inject(SessionService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly injector = inject(Injector);
  private readonly fb = inject(NonNullableFormBuilder);
  protected readonly language = inject(LanguageService);

  protected readonly step = signal<Step>('credentials');
  protected readonly method = signal<Method>('totp');
  protected readonly methods = signal<Method[]>(['totp', 'recovery']);
  protected readonly busy = signal(false);
  protected readonly error = signal<LoginError | null>(null);
  private mfaToken = '';

  private readonly codeInput = viewChild<ElementRef<HTMLInputElement>>('codeInput');
  private readonly contactInput = viewChild<ElementRef<HTMLInputElement>>('contactInput');
  private readonly heading = viewChild<ElementRef<HTMLElement>>('heading');

  protected readonly credentials = this.fb.group({
    contact: ['', [Validators.required, Validators.email, Validators.maxLength(CONTACT_MAX_LENGTH)]],
    password: ['', Validators.required],
  });
  protected readonly mfa = this.fb.group({ code: ['', [Validators.required, Validators.pattern(/^\d{6}$/)]] });

  protected async submitCredentials(): Promise<void> {
    if (this.credentials.invalid || this.busy()) {
      this.credentials.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    const { contact, password } = this.credentials.getRawValue();
    try {
      const challenge = await this.auth.login(contact.trim(), password);
      this.mfaToken = challenge.mfaToken;
      const offered = (challenge.methods?.length ? challenge.methods : ['totp']) as Method[];
      this.methods.set(offered);
      this.setMethod(offered.includes('totp') ? 'totp' : offered[0]);
      this.credentials.controls.password.reset('');
      this.step.set('mfa');
      this.focusAfterRender(() => this.codeInput());
    } catch (error) {
      this.error.set(loginError(error));
    } finally {
      this.busy.set(false);
    }
  }

  protected async submitCode(): Promise<void> {
    if (this.mfa.invalid || this.busy()) {
      this.mfa.markAllAsTouched();
      return;
    }
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.auth.completeMfa(this.mfaToken, this.method(), this.mfa.getRawValue().code);
      const returnUrl = this.route.snapshot.queryParamMap.get('returnUrl');
      const target = returnUrl && returnUrl.startsWith('/') && !returnUrl.startsWith('//') && !returnUrl.startsWith('/login') ? returnUrl : this.session.home();
      await this.router.navigateByUrl(target);
    } catch (error) {
      this.error.set(loginError(error));
      this.mfa.controls.code.reset('');
      this.focusAfterRender(() => this.codeInput());
    } finally {
      this.busy.set(false);
    }
  }

  protected switchMethod(): void {
    this.setMethod(this.method() === 'totp' ? 'recovery' : 'totp');
    this.error.set(null);
    this.focusAfterRender(() => this.codeInput());
  }

  protected startOver(): void {
    this.mfaToken = '';
    this.mfa.reset();
    this.error.set(null);
    this.step.set('credentials');
    this.focusAfterRender(() => this.contactInput());
  }

  private setMethod(method: Method): void {
    this.method.set(method);
    const code = this.mfa.controls.code;
    code.reset('');
    code.setValidators(method === 'totp' ? [Validators.required, Validators.pattern(/^\d{6}$/)] : [Validators.required, Validators.minLength(6)]);
    code.updateValueAndValidity();
  }

  private focusAfterRender(target: () => ElementRef<HTMLElement> | undefined): void {
    afterNextRender(() => (target() ?? this.heading())?.nativeElement.focus(), { injector: this.injector });
  }
}
