import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { Auth } from '../core/auth';
import { I18n, TPipe } from '../core/i18n';
import { Brand } from '../layout/brand';

/**
 * Landing screen when a user can reach both modules. Anyone with access to only one
 * never sees it: Auth.home() sends them straight in.
 */
@Component({
  selector: 'app-select-module',
  standalone: true,
  imports: [RouterLink, TPipe, Brand],
  template: `
    <div class="min-h-screen px-4 py-10
                bg-[radial-gradient(1100px_520px_at_50%_-15%,rgb(var(--c-brand)/0.18),transparent_65%)]">
      <div class="mx-auto w-full max-w-3xl">
        <div class="mb-10 flex flex-col items-center text-center">
          <app-brand [size]="64" [stacked]="true" />
          <p class="mt-3 text-sm text-muted">{{ 'select.lead' | t }}</p>
        </div>

        <div class="grid gap-4 sm:grid-cols-2">
          @if (auth.canHr()) {
            <a routerLink="/hr" class="card group p-7 no-underline text-ink transition-all hover:-translate-y-1 hover:shadow-lift hover:border-brand/50">
              <div class="mb-4 grid h-12 w-12 place-items-center rounded-2xl bg-brand-soft text-brand">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" class="h-6 w-6">
                  <circle cx="9" cy="8" r="3.2" /><path d="M3.5 19a5.5 5.5 0 0 1 11 0M17 11.5a2.7 2.7 0 1 0 0-5.4M18 19a4.6 4.6 0 0 0-2.3-4" />
                </svg>
              </div>
              <h2 class="text-lg">{{ 'select.office' | t }}</h2>
              <p class="mt-2 text-sm text-muted">{{ 'select.officeLead' | t }}</p>
              <span class="mt-4 inline-flex text-sm font-semibold text-brand">{{ 'select.enter' | t }} ←</span>
            </a>
          }

          @if (auth.canField()) {
            <a routerLink="/admin" class="card group p-7 no-underline text-ink transition-all hover:-translate-y-1 hover:shadow-lift hover:border-brand/50">
              <div class="mb-4 grid h-12 w-12 place-items-center rounded-2xl bg-brand-soft text-brand">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" class="h-6 w-6">
                  <path d="M12 21s7-5.3 7-11a7 7 0 1 0-14 0c0 5.7 7 11 7 11Z" /><circle cx="12" cy="10" r="2.6" />
                </svg>
              </div>
              <h2 class="text-lg">{{ 'select.field' | t }}</h2>
              <p class="mt-2 text-sm text-muted">{{ 'select.fieldLead' | t }}</p>
              <span class="mt-4 inline-flex text-sm font-semibold text-brand">{{ 'select.enter' | t }} ←</span>
            </a>
          }
        </div>

        <div class="mt-10 flex items-center justify-center gap-3 text-sm">
          <span class="text-muted">{{ auth.session()?.fullName }}</span>
          <button class="btn sm ghost" (click)="i18n.toggle()">{{ i18n.lang() === 'ar' ? 'English' : 'عربي' }}</button>
          <button class="btn sm ghost text-bad" (click)="auth.logout()">{{ 'nav.logout' | t }}</button>
        </div>
      </div>
    </div>`,
})
export class SelectModulePage {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  private readonly router = inject(Router);

  constructor() {
    // Someone with a single module should never be parked here.
    if (!(this.auth.canHr() && this.auth.canField())) this.router.navigateByUrl(this.auth.home());
  }
}
