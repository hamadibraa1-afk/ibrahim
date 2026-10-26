import { ChangeDetectionStrategy, Component, DestroyRef, NgZone, inject } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { NavigationEnd, Router, RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { filter, interval } from 'rxjs';
import { UnreadCountService } from './unread-count.service';

/** Polling period of the bell. Risk alerts never depend on it: they also arrive on /hubs/crisis and by phone. */
export const BELL_POLL_MS = 60_000;

/** Toolbar bell (S41 entry): the unread count, refreshed on navigation and every minute. */
@Component({
  selector: 'tm-notification-bell',
  imports: [MatButtonModule, RouterLink, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a mat-icon-button routerLink="/notifications" class="bell" data-testid="bell"
       [attr.aria-label]="unread.count() ? ('inbox.bellUnread' | translate: { count: unread.count() }) : ('inbox.bell' | translate)">
      <svg aria-hidden="true" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <path d="M18 8a6 6 0 10-12 0c0 7-3 9-3 9h18s-3-2-3-9M13.7 21a2 2 0 01-3.4 0" />
      </svg>
      @if (unread.count() > 0) {
        <span class="count" aria-hidden="true" data-testid="bell-count">{{ unread.count() > 99 ? '99+' : unread.count() }}</span>
      }
    </a>
  `,
  styles: `
    .bell { position: relative; color: inherit; }
    .count { position: absolute; inset-block-start: 2px; inset-inline-end: 2px; min-width: 18px; height: 18px; border-radius: 9px; background: var(--tm-danger-strong); color: #fff; font-size: 11px; font-weight: 700; line-height: 18px; text-align: center; padding-inline: 3px; }
  `,
})
export class NotificationBellComponent {
  protected readonly unread = inject(UnreadCountService);

  constructor() {
    const destroyRef = inject(DestroyRef);
    this.unread.refresh();
    inject(Router).events.pipe(filter((e) => e instanceof NavigationEnd), takeUntilDestroyed(destroyRef)).subscribe(() => this.unread.refresh());
    // Outside Angular's zone, so a waiting timer never keeps the page "busy" (tests, whenStable).
    const zone = inject(NgZone);
    zone.runOutsideAngular(() => interval(BELL_POLL_MS).pipe(takeUntilDestroyed(destroyRef)).subscribe(() => zone.run(() => this.unread.refresh())));
  }
}
