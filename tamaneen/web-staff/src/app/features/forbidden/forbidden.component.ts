import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { StaffApiService } from '../../core/api/staff-api.service';
import { SessionService } from '../../core/session/session.service';

/** Only portal paths are reported; anything else in the query string is ignored. */
export function refusedPath(from: string | null): string | null {
  return from && from.startsWith('/') && !from.startsWith('//') ? from.slice(0, 500) : null;
}

/**
 * S06 /forbidden: a route guard refused the page. Says so, records the attempt in the audit ledger and links
 * back to the home of the active role.
 */
@Component({
  selector: 'tm-forbidden',
  imports: [MatButtonModule, RouterLink, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="tm-card box" aria-labelledby="forbidden-title" data-testid="forbidden">
      <h1 id="forbidden-title" class="title">{{ 'forbidden.title' | translate }}</h1>
      <p>{{ 'forbidden.body' | translate }}</p>
      @if (logged() === 'logged') {
        <p class="tm-muted small" data-testid="forbidden-logged">{{ 'forbidden.logged' | translate }}</p>
      } @else if (logged() === 'failed') {
        <p class="tm-muted small" data-testid="forbidden-log-failed">{{ 'forbidden.logFailed' | translate }}</p>
      }
      <a mat-flat-button [routerLink]="session.home()" data-testid="back-home">{{ 'forbidden.back' | translate }}</a>
    </section>
  `,
  styles: `
    .box { max-width: 560px; padding: 28px; }
    .title { margin: 0 0 12px; font-size: 24px; font-weight: 700; }
    .small { font-size: 13px; margin-block-end: 20px; }
  `,
})
export class ForbiddenComponent {
  protected readonly session = inject(SessionService);
  protected readonly logged = signal<'pending' | 'logged' | 'failed'>('pending');

  constructor() {
    const path = refusedPath(inject(ActivatedRoute).snapshot.queryParamMap.get('from'));
    if (!path) {
      return;
    }
    inject(StaffApiService)
      .reportAccessDenied(path)
      .subscribe({ next: () => this.logged.set('logged'), error: () => this.logged.set('failed') });
  }
}
