import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterOutlet } from '@angular/router';
import { TPipe } from './core/i18n';
import { Ui } from './core/ui';
import { Backdrop } from './core/backdrop';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [RouterOutlet, FormsModule, TPipe, Backdrop],
  template: `
    <router-outlet />
    <div class="fixed bottom-4 start-4 z-[60] flex flex-col gap-2" aria-live="polite">
      @for (t of ui.toasts(); track t.id) {
        <div class="animate-slide-up rounded-xl px-4 py-2.5 text-sm shadow-pop max-w-sm"
             [class]="t.kind === 'error' ? 'bg-bad text-white' : 'bg-ink text-surface'">{{ t.text }}</div>
      }
    </div>
    @if (ui.dialog(); as d) {
      <div class="modal-back" appBackdrop (dismiss)="ui.close(d.input ? null : false)">
        <div class="modal max-w-md" role="dialog" aria-modal="true" (click)="$event.stopPropagation()">
          <div class="modal-head"><h2>{{ d.title }}</h2></div>
          @if (d.message) { <p class="text-sm text-muted">{{ d.message }}</p> }
          @if (d.input) {
            <div class="field"><label>{{ d.input.label }}</label>
              <textarea rows="3" [(ngModel)]="d.input.value" autofocus></textarea></div>
          }
          <div class="modal-foot">
            <button class="btn" (click)="ui.close(d.input ? null : false)">{{ 'common.cancel' | t }}</button>
            <button class="btn primary" [class.!bg-bad]="d.danger" [class.!border-bad]="d.danger"
              [disabled]="d.input?.required && !d.input?.value?.trim()"
              (click)="ui.close(d.input ? d.input.value.trim() : true)">{{ 'common.confirm' | t }}</button>
          </div>
        </div>
      </div>
    }
  `,
})
export class AppComponent {
  readonly ui = inject(Ui);
}
