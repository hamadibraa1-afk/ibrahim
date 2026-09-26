import { Component, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { Auth } from '../core/auth';
import { I18n, TPipe } from '../core/i18n';
import { Brand } from './brand';
import { Icon } from './nav-icon';

@Component({
  selector: 'app-collector-shell',
  standalone: true,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, TPipe, Icon, Brand],
  template: `
    <header class="sticky top-0 z-20 flex items-center gap-2 bg-brand px-4 pb-3 text-brand-ink shadow-lift
                   pt-[calc(0.75rem+env(safe-area-inset-top))]">
      <img src="/logo.png" alt="" class="h-9 w-9 rounded-lg bg-white object-contain p-0.5">
      <div class="min-w-0 flex-1">
        <div class="truncate text-[11px] text-brand-ink/75">{{ 'org.name' | t }}</div>
        <div class="truncate font-semibold">{{ auth.session()?.fullName }}</div>
      </div>
      <button class="btn sm border-brand-ink/30 bg-brand-ink/10 text-brand-ink hover:bg-brand-ink/20"
              (click)="i18n.toggle()">{{ i18n.lang() === 'ar' ? 'EN' : 'ع' }}</button>
      <button class="btn sm border-brand-ink/30 bg-brand-ink/10 text-brand-ink hover:bg-brand-ink/20"
              (click)="auth.logout()"><app-icon name="logout" /></button>
    </header>

    <main class="mx-auto w-full max-w-xl px-4 pt-4 pb-[calc(6rem+env(safe-area-inset-bottom))]"><router-outlet /></main>

    <nav class="fixed inset-x-0 bottom-0 z-20 flex border-t border-line bg-surface/90 backdrop-blur-md
                pb-[env(safe-area-inset-bottom)]">
      @for (item of nav; track item.path) {
        <a [routerLink]="item.path" routerLinkActive="!text-brand" [routerLinkActiveOptions]="{ exact: item.exact === true }"
           class="flex flex-1 flex-col items-center gap-1 py-3 text-xs text-muted no-underline transition-colors active:scale-95">
          <app-icon [name]="item.icon" /><span>{{ item.label | t }}</span>
        </a>
      }
    </nav>`,
})
export class CollectorShell {
  readonly auth = inject(Auth);
  readonly i18n = inject(I18n);
  /** Office staff check in here too; they need a way back to the portal they came from. */
  readonly nav = [
    { path: '/me', label: 'nav.today', icon: 'today', exact: true },
    { path: '/me/schedule', label: 'nav.mySchedule', icon: 'schedule' },
    { path: '/me/requests', label: 'nav.myRequests', icon: 'requests' },
    ...(this.auth.role() === 'Collector' ? [] : [{ path: '/my', label: 'my.portal', icon: 'people', exact: false }]),
  ];
}
