import { Component, OnDestroy, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../core/auth';
import { I18n, TPipe } from '../core/i18n';
import { PendingRequests } from '../core/pending';
import { Brand } from './brand';
import { NotificationBell } from './notification-bell';
import { Icon } from './nav-icon';

@Component({
  selector: 'app-supervisor-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TPipe, Icon, Brand, NotificationBell],
  template: `
    <div class="min-h-screen md:grid md:grid-cols-[15rem_1fr]">
      <!-- Sidebar (horizontal scroller on small screens) -->
      <aside class="bg-surface border-b md:border-b-0 md:border-e border-line md:sticky md:top-0 md:h-screen
                    md:overflow-y-auto px-2 py-2 md:px-3 md:py-5">
        <div class="hidden md:block px-2 pb-5"><app-brand [size]="38" /></div>

        <nav class="flex gap-1 overflow-x-auto no-scrollbar md:block">
          @for (item of nav; track item.path) {
            <a [routerLink]="item.path" routerLinkActive="!bg-brand-soft !text-brand font-semibold"
               [routerLinkActiveOptions]="{ exact: item.exact === true }"
               class="group flex items-center gap-2.5 rounded-lg px-3 py-2.5 text-sm text-ink/90 no-underline
                      whitespace-nowrap transition-colors hover:bg-raised md:mb-0.5">
              <app-icon [name]="item.icon" class="text-muted group-hover:text-brand transition-colors" />
              <span>{{ item.label | t }}</span>
              @if (item.badge && pending.count() > 0) {
                <span class="ms-auto rounded-full bg-bad px-2 py-0.5 text-[11px] font-bold text-white tabular
                             animate-scale-in">{{ pending.count() }}</span>
              }
            </a>
          }
        </nav>
      </aside>

      <div class="min-w-0">
        <header class="sticky top-0 z-20 flex items-center gap-3 border-b border-line bg-surface/85 px-4 py-2.5 backdrop-blur-md">
          <div class="min-w-0 flex-1">
            <div class="truncate text-sm font-semibold">{{ auth.session()?.fullName }}</div>
            <div class="text-xs text-muted">{{ 'role.' + auth.role() | t }}</div>
          </div>
          <app-bell />
          <button class="btn sm ghost" (click)="i18n.toggle()">{{ i18n.lang() === 'ar' ? 'English' : 'عربي' }}</button>
          <button class="btn sm ghost text-bad" (click)="auth.logout()">
            <app-icon name="logout" /><span class="hidden sm:inline">{{ 'nav.logout' | t }}</span>
          </button>
        </header>

        <main class="mx-auto w-full max-w-[1500px] p-4 md:p-7"><router-outlet /></main>
      </div>
    </div>`,
})
export class SupervisorShell implements OnInit, OnDestroy {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  readonly pending = inject(PendingRequests);
  private timer?: ReturnType<typeof setInterval>;

  readonly nav = [
    { path: '/admin', label: 'nav.dashboard', icon: 'dashboard', exact: true },
    { path: '/admin/schedule', label: 'nav.schedule', icon: 'schedule' },
    { path: '/admin/attendance', label: 'nav.attendance', icon: 'attendance' },
    { path: '/admin/requests', label: 'nav.requests', icon: 'requests', badge: true },
    { path: '/admin/ratings', label: 'nav.ratings', icon: 'ratings' },
    { path: '/admin/feedback', label: 'nav.feedback', icon: 'feedback' },
    { path: '/admin/allowances', label: 'nav.allowances', icon: 'allowances' },
    { path: '/admin/employees', label: 'nav.employees', icon: 'people' },
    { path: '/admin/locations', label: 'nav.locations', icon: 'locations' },
    { path: '/admin/shifts', label: 'nav.shifts', icon: 'shifts' },
  ];

  ngOnInit(): void { this.pending.refresh(); this.timer = setInterval(() => this.pending.refresh(), 60_000); }
  ngOnDestroy(): void { clearInterval(this.timer); }
}
