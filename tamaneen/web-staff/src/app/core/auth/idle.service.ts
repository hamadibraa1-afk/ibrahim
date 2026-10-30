import { DOCUMENT } from '@angular/common';
import { Injectable, NgZone, OnDestroy, effect, inject } from '@angular/core';
import { Router } from '@angular/router';
import { AuthService } from './auth.service';

/** S32 rule «مهلة الخمول 15 دقيقة لحسابات الفريق». */
export const IDLE_TIMEOUT_MS = 15 * 60_000;
const CHECK_EVERY_MS = 30_000;
const ACTIVITY_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart', 'scroll'] as const;

/**
 * Signs a staff member out after 15 minutes without any input, keeping the page to come back to (returnUrl).
 * A therapist in a live session (/sessions/…) is never signed out mid-call; the clock starts again when they
 * leave the page. Timers are throttled in background tabs, so the check also runs when the tab is shown again.
 */
@Injectable({ providedIn: 'root' })
export class IdleService implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly zone = inject(NgZone);
  private readonly document = inject(DOCUMENT);
  private lastActivity = Date.now();
  private timer: ReturnType<typeof setInterval> | undefined;
  private readonly onActivity = () => {
    this.lastActivity = Date.now();
  };
  private readonly onVisible = () => {
    if (this.document.visibilityState === 'visible') this.check();
  };

  constructor() {
    effect(() => (this.auth.signedIn() ? this.start() : this.stop()));
  }

  ngOnDestroy(): void {
    this.stop();
  }

  /** Runs the check now (also used by tests). */
  check(now = Date.now()): void {
    if (!this.auth.signedIn()) return;
    if (this.router.url.startsWith('/sessions/')) {
      this.lastActivity = now;
      return;
    }
    if (now - this.lastActivity >= IDLE_TIMEOUT_MS) {
      this.zone.run(() => void this.auth.idleSignOut());
    }
  }

  private start(): void {
    if (this.timer) return;
    this.lastActivity = Date.now();
    this.zone.runOutsideAngular(() => {
      for (const name of ACTIVITY_EVENTS) this.document.addEventListener(name, this.onActivity, { passive: true, capture: true });
      this.document.addEventListener('visibilitychange', this.onVisible);
      this.timer = setInterval(() => this.check(), CHECK_EVERY_MS);
    });
  }

  private stop(): void {
    if (!this.timer) return;
    clearInterval(this.timer);
    this.timer = undefined;
    for (const name of ACTIVITY_EVENTS) this.document.removeEventListener(name, this.onActivity, { capture: true });
    this.document.removeEventListener('visibilitychange', this.onVisible);
  }
}
