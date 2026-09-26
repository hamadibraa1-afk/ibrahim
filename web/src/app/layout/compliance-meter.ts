import { Component, computed, input } from '@angular/core';
import { hmin } from '../core/format';
import { TPipe } from '../core/i18n';

export interface Compliance { year: number; month: number; countedDays: number; requiredMinutes: number; compliantMinutes: number; percent: number | null; }

/**
 * Month-to-date attendance compliance. One component for the employee's own page and the HR
 * profile, so both always show the figure the same way. The number itself comes from the server.
 */
@Component({
  selector: 'app-compliance-meter',
  standalone: true,
  imports: [TPipe],
  template: `
    <section class="card-pad flex items-center gap-4">
      <div class="relative h-20 w-20 shrink-0" role="img" [attr.aria-label]="('comp.title' | t) + ': ' + label()">
        <svg viewBox="0 0 36 36" class="h-20 w-20 -rotate-90" aria-hidden="true">
          <circle cx="18" cy="18" r="15.9155" fill="none" class="stroke-line" stroke-width="3.5" />
          @if (data().percent !== null) {
            <circle cx="18" cy="18" r="15.9155" fill="none" stroke-width="3.5" stroke-linecap="round"
                    [attr.class]="tone()" [attr.stroke-dasharray]="data().percent + ' 100'" />
          }
        </svg>
        <div class="absolute inset-0 grid place-items-center text-base font-bold tabular" dir="ltr" aria-hidden="true">{{ label() }}</div>
      </div>
      <div class="min-w-0">
        <h2 class="text-base">{{ 'comp.title' | t }}</h2>
        @if (data().percent === null) {
          <p class="text-sm text-muted">{{ 'comp.none' | t }}</p>
        } @else {
          <p class="text-sm text-muted">{{ 'comp.detail' | t }}:
            <span class="tabular font-semibold text-ink" dir="ltr">{{ hmin(data().compliantMinutes) }} / {{ hmin(data().requiredMinutes) }}</span></p>
          <p class="text-sm text-muted">{{ 'comp.days' | t }}: <span class="tabular font-semibold text-ink">{{ data().countedDays }}</span></p>
        }
        <p class="mt-1 text-xs text-muted">{{ 'comp.how' | t }}</p>
      </div>
    </section>`,
})
export class ComplianceMeter {
  readonly data = input.required<Compliance>();
  readonly hmin = hmin;
  readonly label = computed(() => this.data().percent === null ? '—' : `${this.data().percent}%`);
  /** Bands: 95 and above is on track, from 85 it needs attention, below that it is a problem. */
  readonly tone = computed(() => {
    const p = this.data().percent ?? 0;
    return p >= 95 ? 'stroke-ok' : p >= 85 ? 'stroke-warn' : 'stroke-bad';
  });
}
