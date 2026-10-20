import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { MatCardModule } from '@angular/material/card';
import { TranslateModule } from '@ngx-translate/core';
import { API_BASE_URL, type LocaleResponse } from '../../core/api';
import { LanguageService } from '../../core/i18n/language.service';
import { SessionService } from '../../core/session/session.service';

type ApiState = { kind: 'loading' } | { kind: 'ok'; data: LocaleResponse } | { kind: 'error' };

/**
 * Portal start page. Shows the active role and a live check that Tamaneen.Api is reachable and
 * answering in the language selected here (the API reply is localized from Accept-Language).
 */
@Component({
    selector: 'tm-home',
    imports: [MatCardModule, TranslateModule],
    changeDetection: ChangeDetectionStrategy.OnPush,
    template: `
    <h1 class="title">{{ 'home.title' | translate }}</h1>
    @if (session.role(); as role) {
      <p class="muted">{{ 'home.subtitle' | translate: { role: ('roles.' + role | translate) } }}</p>
    }
    <mat-card class="card" appearance="outlined">
      <mat-card-header>
        <mat-card-title>{{ 'home.api.title' | translate }}</mat-card-title>
      </mat-card-header>
      <mat-card-content>
        @switch (api().kind) {
          @case ('loading') { <p>{{ 'home.api.loading' | translate }}</p> }
          @case ('error') { <p class="error" role="alert">{{ 'home.api.error' | translate }}</p> }
          @case ('ok') {
            @if (api(); as state) {
              @if (state.kind === 'ok') {
                <p data-testid="api-message">{{ state.data.message }}</p>
                <p class="muted">{{ 'home.api.language' | translate: { language: state.data.language, dir: state.data.dir } }}</p>
              }
            }
          }
        }
      </mat-card-content>
    </mat-card>
  `,
    styles: `
    .title { margin: 0 0 4px; font-size: 24px; font-weight: 700; }
    .muted { color: var(--tm-muted); }
    .card { margin-top: 16px; max-width: 560px; border-radius: 14px; }
    .error { color: var(--tm-danger); }
  `
})
export class HomeComponent {
  private readonly http = inject(HttpClient);
  private readonly apiBase = inject(API_BASE_URL);
  protected readonly session = inject(SessionService);
  protected readonly api = signal<ApiState>({ kind: 'loading' });

  constructor() {
    const language = inject(LanguageService);
    // Re-ask the API whenever the language changes, to show its reply in the new language.
    effect(() => {
      language.language();
      this.load();
    });
  }

  private load(): void {
    this.api.set({ kind: 'loading' });
    this.http.get<LocaleResponse>(`${this.apiBase}/v1/meta/locale`).subscribe({
      next: (data) => this.api.set({ kind: 'ok', data }),
      error: () => this.api.set({ kind: 'error' }),
    });
  }
}
