import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../core/auth';
import { I18n, TPipe } from '../core/i18n';
import { Brand } from './brand';
import { NotificationBell } from './notification-bell';
import { Icon } from './nav-icon';

/** Self-service shell for office employees: everything about them, nothing about anyone else. */
@Component({
  selector: 'app-my-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TPipe, Brand, Icon, NotificationBell],
  template: `
    <div class="min-h-screen">
      <header class="sticky top-0 z-20 border-b border-line bg-surface/90 backdrop-blur-md">
        <div class="mx-auto flex w-full max-w-4xl items-center gap-3 px-4 py-2.5">
          <app-brand [size]="34" [showText]="false" />
          <div class="min-w-0 flex-1">
            <div class="truncate text-sm font-semibold">{{ auth.session()?.fullName }}</div>
            <div class="text-xs text-muted">{{ 'my.portal' | t }}</div>
          </div>
          <app-bell />
          <button class="btn sm ghost" (click)="i18n.toggle()">{{ i18n.lang() === 'ar' ? 'English' : 'عربي' }}</button>
          <button class="btn sm ghost text-bad" (click)="auth.logout()"><app-icon name="logout" /></button>
        </div>
        <nav class="mx-auto flex w-full max-w-4xl gap-1 overflow-x-auto no-scrollbar px-2">
          @for (item of nav; track item.path) {
            <a [routerLink]="item.path" routerLinkActive="!text-brand !border-brand font-semibold"
               [routerLinkActiveOptions]="{ exact: item.exact === true }"
               class="whitespace-nowrap border-b-2 border-transparent px-3 py-2.5 text-sm text-muted no-underline transition-colors hover:text-ink">
              {{ item.label | t }}
            </a>
          }
        </nav>
      </header>
      <main class="mx-auto w-full max-w-4xl p-4"><router-outlet /></main>
    </div>`,
})
export class MyShell {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  readonly nav = [
    { path: '/my', label: 'my.overview', icon: 'today', exact: true },
    { path: '/my/attendance', label: 'my.attendance', icon: 'attendance' },
    { path: '/my/requests', label: 'my.requests', icon: 'requests' },
    { path: '/my/payslips', label: 'my.payslips', icon: 'allowances' },
    { path: '/my/profile', label: 'my.profile', icon: 'people' },
  ];
}
