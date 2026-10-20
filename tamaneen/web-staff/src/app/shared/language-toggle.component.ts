import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { TranslateModule } from '@ngx-translate/core';
import { LanguageService } from '../core/i18n/language.service';

/** The Arabic/English switch at the top of every portal screen. Its label is written in the target language. */
@Component({
  selector: 'tm-language-toggle',
  standalone: true,
  imports: [MatButtonModule, MatIconModule, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      mat-stroked-button
      type="button"
      class="toggle"
      data-testid="language-toggle"
      [attr.aria-label]="'language.switchLabel' | translate"
      (click)="language.toggle()"
    >
      <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <circle cx="12" cy="12" r="9" />
        <path d="M3 12h18M12 3c2.5 2.7 2.5 15.3 0 18M12 3c-2.5 2.7-2.5 15.3 0 18" />
      </svg>
      <span [attr.lang]="language.other()">{{ 'language.target' | translate }}</span>
    </button>
  `,
  styles: `
    .toggle { min-height: 44px; border-radius: 999px; gap: 8px; }
    .toggle svg { margin-inline-end: 6px; vertical-align: middle; }
  `,
})
export class LanguageToggleComponent {
  protected readonly language = inject(LanguageService);
}
