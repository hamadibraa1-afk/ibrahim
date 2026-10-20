import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { TranslateModule } from '@ngx-translate/core';
import type { ShownError } from '../core/api/problem';

/** Shows an API problem (already localized by the server) or the translated fallback, announced to screen readers. */
@Component({
  selector: 'tm-problem-message',
  imports: [TranslateModule],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (error(); as e) {
      <div class="tm-problem" role="alert" data-testid="problem">
        @if (e.title) {
          <strong>{{ e.title }}</strong>
          @if (e.detail) { <span> {{ e.detail }}</span> }
        } @else {
          <span>{{ e.fallbackKey | translate }}</span>
        }
      </div>
    }
  `,
})
export class ProblemMessageComponent {
  readonly error = input<ShownError | null | undefined>(null);
}
