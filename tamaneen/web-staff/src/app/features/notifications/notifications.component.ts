import { ChangeDetectionStrategy, Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { Router } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { AccountApiService } from '../../core/api/account-api.service';
import { type ShownError, shownError } from '../../core/api/problem';
import type { InboxFilter, InboxItem } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { UnreadCountService } from '../../shared/unread-count.service';

export const INBOX_FILTERS: readonly InboxFilter[] = ['all', 'risk', 'appointments', 'messages', 'system'];

/** The letter badge of each entry, as in the design (خ, م, ر, ت, س, ن). */
export function badgeKey(item: InboxItem): string {
  if (item.category === 'risk') return 'risk';
  if (item.category === 'security') return 'security';
  return item.category;
}

/**
 * S41 staff inbox. Unacknowledged risk alerts come first in the danger colour and stay until someone acknowledges
 * the event (opening the entry goes to /alerts, it is not marked read). Filters: all, risk, appointments, messages,
 * system. Only pseudonyms, never names. «تحديد الكل كمقروء» leaves the risk alerts alone.
 */
@Component({
  selector: 'tm-notifications',
  imports: [MatButtonModule, MatButtonToggleModule, MatProgressBarModule, TranslateModule, LocalDatePipe, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" [attr.aria-label]="'common.loading' | translate" />
    }
    <header class="head">
      <h1 class="title">{{ 'inbox.title' | translate }}</h1>
      <button mat-button type="button" (click)="markAll()" [disabled]="!hasUnreadStored()" data-testid="read-all">{{ 'inbox.readAll' | translate }}</button>
    </header>
    <mat-button-toggle-group [value]="filter()" (change)="setFilter($event.value)" [attr.aria-label]="'inbox.filters' | translate" class="filters">
      @for (f of filters; track f) {
        <mat-button-toggle [value]="f" [attr.data-testid]="'filter-' + f">
          {{ 'inbox.filter.' + f | translate }}@if (f === 'all') { · {{ counts().all ?? 0 }} }
        </mat-button-toggle>
      }
    </mat-button-toggle-group>
    <tm-problem-message [error]="error()" />
    @if (error()) {
      <button mat-stroked-button type="button" (click)="load()" data-testid="retry">{{ 'inbox.retry' | translate }}</button>
    }
    <section class="tm-card">
      @if (items().length) {
        <ul class="list">
          @for (item of items(); track item.id) {
            <li class="row" [class.risk]="item.pinned" [class.unread]="!item.read" data-testid="inbox-item">
              <span class="badge" [class.badge-risk]="item.category === 'risk'" aria-hidden="true">{{ 'inbox.badge.' + badgeKey(item) | translate }}</span>
              <button type="button" class="open" (click)="open(item)">
                <span class="text">{{ item.text }}</span>
                <span class="tm-muted when">
                  {{ item.createdAt | tmDate: 'dayTime' : language.language() }}
                  @if (item.pinned) { · <strong class="pending">{{ 'inbox.notAcknowledged' | translate }}</strong> }
                  @if (!item.read && !item.pinned) { <span class="cdk-visually-hidden"> · {{ 'inbox.unread' | translate }}</span> }
                </span>
              </button>
              @if (item.kind === 'security.new_sign_in') {
                <button mat-button type="button" class="not-you" (click)="notYou(item)">{{ 'inbox.notYou' | translate }}</button>
              }
            </li>
          }
        </ul>
      } @else if (!loading() && !error()) {
        <p class="tm-muted" data-testid="inbox-empty">{{ (filter() === 'all' ? 'inbox.empty' : 'inbox.emptyFiltered') | translate }}</p>
      }
    </section>
    <p class="tm-muted note">{{ 'inbox.offlineNote' | translate }}</p>
  `,
  styles: `
    .head { display: flex; align-items: center; justify-content: space-between; gap: 12px; }
    .title { margin: 0; font-size: 24px; font-weight: 700; }
    .filters { margin-block: 12px 16px; flex-wrap: wrap; }
    .list { list-style: none; margin: 0; padding: 0; }
    .row { display: flex; gap: 12px; align-items: flex-start; padding: 12px; border-block-end: 1px solid var(--tm-line); border-radius: 10px; }
    .row.unread { background: var(--tm-tint); }
    .row.risk { background: var(--tm-danger-tint); border: 1px solid var(--tm-danger-line); }
    .badge { flex: 0 0 32px; height: 32px; border-radius: 50%; display: inline-flex; align-items: center; justify-content: center; background: var(--tm-soft); font-weight: 700; }
    .badge-risk { background: var(--tm-danger-strong); color: #fff; }
    .open { flex: 1; display: flex; flex-direction: column; gap: 4px; text-align: start; background: none; border: 0; padding: 0; font: inherit; color: inherit; cursor: pointer; min-height: 44px; }
    .text { font-weight: 600; }
    .when { font-size: 13px; }
    .pending { color: var(--tm-danger-ink); }
    .note { font-size: 13px; margin-top: 12px; }
  `,
})
export class NotificationsComponent {
  private readonly api = inject(AccountApiService);
  private readonly router = inject(Router);
  private readonly unread = inject(UnreadCountService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly language = inject(LanguageService);
  protected readonly filters = INBOX_FILTERS;
  protected readonly badgeKey = badgeKey;
  protected readonly filter = signal<InboxFilter>('all');
  protected readonly items = signal<InboxItem[]>([]);
  protected readonly counts = signal<Partial<Record<InboxFilter, number>>>({});
  protected readonly loading = signal(true);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly hasUnreadStored = computed(() => this.items().some((i) => !i.read && !i.pinned));

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.api
      .inbox(this.filter())
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (inbox) => {
          this.items.set(inbox.items);
          this.counts.set(inbox.counts);
          this.loading.set(false);
          this.unread.set(inbox.unreadCount);
        },
        error: (error: unknown) => {
          // The entries already shown stay on screen.
          this.error.set(shownError(error, 'inbox.loadError'));
          this.loading.set(false);
        },
      });
  }

  setFilter(filter: InboxFilter): void {
    this.filter.set(filter);
    this.load();
  }

  open(item: InboxItem): void {
    if (!item.pinned && !item.read) {
      this.items.update((list) => list.map((i) => (i.id === item.id ? { ...i, read: true } : i)));
      this.api.markRead(item.id).subscribe({ next: () => this.unread.refresh(), error: () => undefined });
    }
    if (item.link) {
      void this.router.navigateByUrl(item.link);
    }
  }

  notYou(item: InboxItem): void {
    if (!item.read) {
      this.api.markRead(item.id).subscribe({ error: () => undefined });
    }
    void this.router.navigate(['/settings'], { queryParams: item.data['sessionId'] ? { session: item.data['sessionId'] } : {} });
  }

  markAll(): void {
    this.api.markAllRead().subscribe({
      next: () => {
        this.items.update((list) => list.map((i) => (i.pinned ? i : { ...i, read: true })));
        this.unread.refresh();
      },
      error: (error: unknown) => this.error.set(shownError(error, 'inbox.loadError')),
    });
  }
}
