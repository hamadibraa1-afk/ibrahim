import { Injectable, inject, signal } from '@angular/core';
import { Api } from './api';

/**
 * Shared badge state for requests awaiting a decision.
 * Pages that act on requests call refresh() (or drop()) so the badge reacts immediately
 * instead of waiting for the next poll.
 */
@Injectable({ providedIn: 'root' })
export class PendingRequests {
  private readonly api = inject(Api);
  readonly count = signal(0);

  async refresh(): Promise<void> {
    try {
      const c = await this.api.get<{ permissions: number; exceptions: number; leaves: number }>('requests/pending-counts');
      this.count.set(c.permissions + c.exceptions + c.leaves);
    } catch { /* the badge is best-effort; a failed poll must not break the page */ }
  }

  /** Optimistic decrement right after a decision, before the server round-trip returns. */
  drop(n = 1): void { this.count.update(c => Math.max(0, c - n)); }
}
