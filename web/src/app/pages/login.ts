import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Api } from '../core/api';
import { Auth, Session } from '../core/auth';
import { I18n, TPipe } from '../core/i18n';
import { Brand } from '../layout/brand';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [FormsModule, TPipe, Brand],
  template: `
    <div class="min-h-screen grid place-items-center p-4
                bg-[radial-gradient(1100px_520px_at_50%_-15%,rgb(var(--c-brand)/0.18),transparent_65%)]">
      <form (ngSubmit)="submit()" class="w-full max-w-sm animate-slide-up">
        <div class="flex justify-center mb-6">
          <button type="button" class="btn sm ghost" (click)="i18n.toggle()">{{ i18n.lang() === 'ar' ? 'English' : 'عربي' }}</button>
        </div>

        <div class="mb-7 flex flex-col items-center text-center">
          <app-brand [size]="64" [stacked]="true" />
          <p class="mt-3 text-sm text-muted">{{ 'login.subtitle' | t }}</p>
        </div>

        <div class="card-pad rounded-2xl">
          @if (error()) { <div class="alert red">{{ error() }}</div> }

          <div class="field">
            <label for="no">{{ 'login.number' | t }}</label>
            <input id="no" name="employeeNumber" inputmode="numeric" dir="ltr" autocomplete="username"
                   class="text-center text-lg tracking-[.2em] tabular" [(ngModel)]="employeeNumber" required>
          </div>

          <div class="field">
            <label for="pw">{{ 'login.password' | t }}</label>
            <div class="flex gap-2">
              <input id="pw" name="password" [type]="show() ? 'text' : 'password'" dir="ltr" autocomplete="current-password"
                     [(ngModel)]="password" required>
              <button type="button" class="btn shrink-0" (click)="show.set(!show())">{{ (show() ? 'login.hide' : 'login.show') | t }}</button>
            </div>
          </div>

          <button class="btn primary big mt-2" [disabled]="busy() || !employeeNumber || !password">
            @if (busy()) {
              <span class="h-4 w-4 animate-spin rounded-full border-2 border-brand-ink/40 border-t-brand-ink"></span>
            }
            {{ 'login.submit' | t }}
          </button>
        </div>

        <p class="mt-5 text-center text-xs text-muted">{{ 'emp.loginNote' | t }}</p>
      </form>
    </div>`,
})
export class LoginPage implements OnInit {
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);
  employeeNumber = '';
  password = '';
  readonly show = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);

  ngOnInit(): void { if (this.auth.session()) this.router.navigateByUrl(this.auth.home()); }

  async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const s = await this.api.post<Session>('auth/login', { employeeNumber: this.employeeNumber.trim(), password: this.password });
      this.auth.set(s);
      this.i18n.lang.set(s.language);
      await this.router.navigateByUrl(this.auth.home());
    } catch (e) {
      this.error.set(this.api.error(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
