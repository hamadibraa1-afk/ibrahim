import { Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';
import { Auth } from '../core/auth';
import { TPipe } from '../core/i18n';

/**
 * The same switcher in every module's header. Everyone is an employee, so «حسابي» (check-in,
 * requests, profile) is always there; the office and field modules appear only for those whose
 * permissions open them. It used to exist in the HR module alone, so a supervisor or a manager in
 * the field module had no way to their own check-in.
 */
@Component({
  selector: 'app-module-switch',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, TPipe],
  template: `
    @if (items().length > 1) {
      <nav class="flex shrink-0 rounded-lg border border-line p-0.5 text-xs" [attr.aria-label]="'switch.label' | t">
        @for (m of items(); track m.path) {
          <a [routerLink]="m.path" routerLinkActive="!bg-brand !text-brand-ink font-semibold" ariaCurrentWhenActive="page"
             class="whitespace-nowrap rounded-md px-2.5 py-1 text-muted no-underline transition-colors hover:text-ink">{{ m.label | t }}</a>
        }
      </nav>
    }`,
})
export class ModuleSwitch {
  private readonly auth = inject(Auth);
  readonly items = computed(() => [
    { path: '/my', label: 'my.portal' },
    ...(this.auth.canHr() ? [{ path: '/hr', label: 'switch.office' }] : []),
    ...(this.auth.canField() ? [{ path: '/admin', label: 'switch.field' }] : []),
  ]);
}
