import { Component, OnDestroy, OnInit, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { Api } from '../core/api';
import { I18n, TPipe } from '../core/i18n';

interface Item { id: string; kind: string; subject: string | null; value: string | null; link: string | null; createdAt: string; isUnread: boolean; }

/**
 * The bell. Text is built here from the kind and its values, so the same alert reads
 * correctly in Arabic or English depending on who is looking at it.
 */
@Component({
  selector: 'app-bell',
  standalone: true,
  imports: [TPipe],
  template: `
    <div class="relative">
      <button class="btn sm ghost relative" (click)="toggle()" [attr.aria-label]="'notif.title' | t">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" class="h-[18px] w-[18px]">
          <path d="M18 9a6 6 0 1 0-12 0c0 5-2 6-2 6h16s-2-1-2-6" stroke-linecap="round" stroke-linejoin="round" />
          <path d="M10.3 20a2 2 0 0 0 3.4 0" stroke-linecap="round" />
        </svg>
        @if (unread() > 0) {
          <span class="absolute -top-1 -end-1 min-w-[18px] rounded-full bg-bad px-1 text-[10px] font-bold
                       leading-[18px] text-white tabular animate-scale-in">{{ unread() }}</span>
        }
      </button>

      @if (open()) {
        <div class="fixed inset-0 z-40" (click)="open.set(false)"></div>
        <div class="absolute end-0 z-50 mt-2 w-[22rem] max-w-[90vw] card overflow-hidden animate-scale-in">
          <header class="flex items-center gap-2 border-b border-line px-4 py-2.5">
            <h2 class="flex-1">{{ 'notif.title' | t }}</h2>
            @if (unread() > 0) { <button class="btn sm ghost" (click)="readAll()">{{ 'notif.readAll' | t }}</button> }
          </header>
          <div class="max-h-[60vh] overflow-auto divide-y divide-line">
            @for (n of items(); track n.id) {
              <button class="block w-full px-4 py-3 text-start transition-colors hover:bg-raised"
                      [class.bg-brand-soft]="n.isUnread" (click)="go(n)">
                <div class="text-sm">{{ text(n) }}</div>
                <div class="mt-0.5 text-xs text-muted tabular" dir="ltr">{{ n.createdAt.substring(0, 16).replace('T', ' ') }}</div>
              </button>
            } @empty { <p class="px-4 py-8 text-center text-sm text-muted">{{ 'notif.empty' | t }}</p> }
          </div>
        </div>
      }
    </div>`,
})
export class NotificationBell implements OnInit, OnDestroy {
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  readonly i18n = inject(I18n);
  readonly items = signal<Item[]>([]);
  readonly unread = signal(0);
  readonly open = signal(false);
  private timer?: ReturnType<typeof setInterval>;

  ngOnInit(): void {
    this.refresh();
    this.timer = setInterval(() => this.refresh(), 60_000);
  }

  ngOnDestroy(): void { clearInterval(this.timer); }

  async refresh(): Promise<void> {
    try {
      const { unread } = await this.api.get<{ unread: number }>('notifications/count');
      this.unread.set(unread);
    } catch { /* the bell is best-effort and must never break a page */ }
  }

  async toggle(): Promise<void> {
    const opening = !this.open();
    this.open.set(opening);
    if (!opening) return;
    try { this.items.set(await this.api.get<Item[]>('notifications')); }
    catch { this.items.set([]); }
  }

  /** Builds the sentence from the kind plus whatever the server attached to it. */
  text(n: Item): string {
    const base = this.i18n.t('notif.kind.' + n.kind);
    const subject = n.subject ? ` — ${n.subject}` : '';
    const value = n.value && !Number.isNaN(Number(n.value)) ? ` (${n.value})` : '';
    return base + subject + value;
  }

  async go(n: Item): Promise<void> {
    this.open.set(false);
    try {
      await this.api.post('notifications/read', { ids: [n.id] });
      await this.refresh();
    } catch { /* navigation matters more than the read flag */ }
    if (n.link) await this.router.navigateByUrl(n.link);
  }

  async readAll(): Promise<void> {
    try {
      await this.api.post('notifications/read', { ids: null });
      this.items.update(list => list.map(n => ({ ...n, isUnread: false })));
      await this.refresh();
    } catch { /* ignored */ }
  }
}
