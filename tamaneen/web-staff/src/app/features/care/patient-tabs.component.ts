import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';

export type CareTab = 'overview' | 'file' | 'plan' | 'homework' | 'safety-plan';
export const CARE_TABS: readonly CareTab[] = ['overview', 'file', 'plan', 'homework', 'safety-plan'];

/** Breadcrumb to "My patients" and the patient file's tabs (S14, S02, S15, S16, S35); the S17 reports tab has no screen yet. */
@Component({
  selector: 'tm-patient-tabs',
  imports: [RouterLink, TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <a routerLink="/patients" class="back" data-testid="breadcrumb">{{ 'file.back' | translate }}</a>
    <nav class="tabs" [attr.aria-label]="'care.tabs.label' | translate" data-testid="care-tabs">
      @for (tab of tabs; track tab) {
        <a
          class="tab"
          [routerLink]="link(tab)"
          [class.active]="tab === active()"
          [attr.aria-current]="tab === active() ? 'page' : null"
          [attr.data-testid]="'tab-' + tab"
          >{{ 'care.tabs.' + tab | translate }}</a
        >
      }
    </nav>
  `,
  styles: `
    :host { display: block; margin-block-end: 16px; }
    .back { color: var(--tm-primary); font-size: 14px; }
    .tabs { display: flex; flex-wrap: wrap; gap: 4px; margin-block-start: 12px; border-block-end: 1px solid var(--tm-line); }
    .tab { padding: 10px 14px; color: var(--tm-muted); text-decoration: none; border-block-end: 3px solid transparent; font-weight: 600; }
    .tab.active { color: var(--tm-primary-strong); border-block-end-color: var(--tm-primary); }
  `,
})
export class PatientTabsComponent {
  readonly id = input.required<string>();
  readonly active = input.required<CareTab>();
  protected readonly tabs = CARE_TABS;

  protected link(tab: CareTab): string[] {
    return tab === 'overview' ? ['/patients', this.id()] : ['/patients', this.id(), tab];
  }
}
