import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../core/auth';
import { I18n, TPipe } from '../core/i18n';
import { Brand } from './brand';
import { NotificationBell } from './notification-bell';
import { Icon } from './nav-icon';

@Component({
  selector: 'app-hr-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TPipe, Brand, Icon, NotificationBell],
  template: `
    <div class="min-h-screen md:grid md:grid-cols-[15rem_1fr]">
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
            </a>
          }
        </nav>
      </aside>

      <div class="min-w-0">
        <header class="sticky top-0 z-20 flex items-center gap-3 border-b border-line bg-surface/85 px-4 py-2.5 backdrop-blur-md">
          <div class="min-w-0 flex-1">
            <div class="truncate text-sm font-semibold">{{ auth.session()?.fullName }}</div>
            <div class="text-xs text-muted">{{ 'role.' + auth.role() | t }} · {{ 'select.office' | t }}</div>
          </div>
          @if (auth.canField()) {
            <a routerLink="/select" class="btn sm ghost">{{ 'select.switch' | t }}</a>
          }
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
export class HrShell {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  readonly nav = [
    { path: '/hr', label: 'hr.nav.dashboard', icon: 'dashboard', exact: true },
    { path: '/hr/employees', label: 'hr.nav.employees', icon: 'people' },
    { path: '/hr/attendance', label: 'hr.nav.attendance', icon: 'attendance' },
    { path: '/hr/returns', label: 'hr.ret.title', icon: 'today' },
    { path: '/hr/discipline', label: 'hr.nav.discipline', icon: 'requests' },
    { path: '/hr/reports', label: 'hr.nav.reports', icon: 'ratings' },
    { path: '/hr/payroll', label: 'hr.nav.payroll', icon: 'allowances' },
    { path: '/hr/org', label: 'hr.nav.org', icon: 'locations' },
    { path: '/hr/schedules', label: 'hr.nav.schedules', icon: 'schedule' },
    { path: '/hr/settings', label: 'hr.nav.settings', icon: 'shifts' },
  ];
}
