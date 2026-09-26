import { Component, input } from '@angular/core';
import { I18n, TPipe } from '../core/i18n';
import { inject } from '@angular/core';

/**
 * Organisation mark. The image is served from /logo.png, so replacing that one file
 * swaps the logo everywhere (sidebar, sign-in, collector app, printed QR card).
 */
@Component({
  selector: 'app-brand',
  standalone: true,
  imports: [TPipe],
  template: `
    <div class="flex items-center gap-2.5" [class.flex-col]="stacked()" [class.gap-3]="stacked()">
      <img src="/logo.png" [alt]="'org.name' | t" [style.height.px]="size()" [style.width.px]="size()"
           class="rounded-xl bg-white object-contain p-1 shadow-card">
      @if (showText()) {
        <div [class.text-center]="stacked()">
          <div class="font-bold leading-tight" [class.text-lg]="stacked()">{{ 'org.name' | t }}</div>
          <div class="text-xs text-muted leading-tight">{{ 'app.title' | t }}</div>
        </div>
      }
    </div>`,
})
export class Brand {
  readonly i18n = inject(I18n);
  readonly size = input(40);
  readonly showText = input(true);
  readonly stacked = input(false);
}
