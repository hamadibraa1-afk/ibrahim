import { Injectable, inject, signal } from '@angular/core';
import { AccountApiService } from '../core/api/account-api.service';

/** The shell bell's count (S41): stored unread entries plus unacknowledged risk alerts. */
@Injectable({ providedIn: 'root' })
export class UnreadCountService {
  private readonly api = inject(AccountApiService);
  private readonly value = signal(0);
  readonly count = this.value.asReadonly();

  set(count: number): void {
    this.value.set(count);
  }

  refresh(): void {
    this.api.unreadCount().subscribe({ next: (r) => this.value.set(r.unreadCount), error: () => undefined });
  }
}
