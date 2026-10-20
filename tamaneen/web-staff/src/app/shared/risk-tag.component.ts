import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import type { RiskLevel } from '../core/api/types';

/** "2 · elevated": the risk level number and its name, coloured by level (1 routine … 4 critical). */
@Component({
  selector: 'tm-risk-tag',
  imports: [TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (level(); as value) {
      <span class="tm-tag" [class]="'tm-tag tm-risk-' + value" data-testid="risk-tag">{{ value }} · {{ 'risk.' + value | translate }}</span>
    }
  `,
})
export class RiskTagComponent {
  readonly level = input<RiskLevel | null>(null);
}
