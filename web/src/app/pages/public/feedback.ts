import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Api } from '../../core/api';
import { I18n, TPipe } from '../../core/i18n';

interface Info { nameAr: string; nameEn: string; }

@Component({
  selector: 'app-feedback',
  standalone: true,
  imports: [FormsModule, TPipe, RouterLink],
  template: `
    <div class="min-h-screen px-4 py-8
                bg-[radial-gradient(900px_420px_at_50%_-10%,rgb(var(--c-brand)/0.2),transparent_65%)]">
      <div class="mx-auto w-full max-w-sm">
        <div class="mb-5 flex items-center">
          <a class="btn sm ghost" [routerLink]="['/r', token()]">‹ {{ 'common.back' | t }}</a>
          <span class="flex-1"></span>
          <button class="btn sm ghost" (click)="i18n.toggle()">{{ i18n.lang() === 'ar' ? 'English' : 'عربي' }}</button>
        </div>

        @if (reference(); as ref) {
          <div class="card-pad rounded-2xl text-center animate-scale-in">
            <div class="mx-auto mb-4 grid h-16 w-16 place-items-center rounded-full bg-ok-soft text-ok">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"
                   stroke-linejoin="round" class="h-8 w-8"><path d="m5 13 4.5 4.5L19 7" /></svg>
            </div>
            <h1 class="text-xl">{{ 'fb.received' | t }}</h1>
            <p class="mt-2 text-sm text-muted">{{ 'fb.referenceHint' | t }}</p>
            <div class="mt-3 rounded-xl bg-raised px-4 py-3 text-lg font-bold tracking-wider" dir="ltr">{{ ref }}</div>
            <a class="btn big mt-5" [routerLink]="['/r', token()]">{{ 'common.close' | t }}</a>
          </div>
        } @else {
          <div class="card-pad rounded-2xl animate-slide-up">
            <div class="text-center">
              <img src="/logo.png" alt="" class="mx-auto mb-3 h-16 w-16 rounded-xl bg-white object-contain p-1">
              <h1 class="text-xl">{{ (isSuggestion() ? 'fb.suggestionTitle' : 'fb.complaintTitle') | t }}</h1>
              @if (info(); as i) { <p class="mt-1 text-sm text-muted">{{ i18n.pick(i.nameAr, i.nameEn) }}</p> }
              <p class="mt-2 text-sm text-muted">{{ (isSuggestion() ? 'fb.suggestionLead' : 'fb.complaintLead') | t }}</p>
            </div>

            @if (error()) { <div class="alert red mt-4">{{ error() }}</div> }

            <div class="mt-5">
              <div class="field"><label>{{ 'fb.name' | t }} *</label><input [(ngModel)]="name" maxlength="120"></div>
              <div class="field"><label>{{ 'fb.phone' | t }} *</label>
                <input dir="ltr" inputmode="tel" placeholder="05XXXXXXXX" [(ngModel)]="phone" maxlength="20"></div>
              <div class="field"><label>{{ 'fb.email' | t }} <span class="muted">({{ 'common.optional' | t }})</span></label>
                <input type="email" dir="ltr" [(ngModel)]="email" maxlength="254"></div>
              <div class="field"><label>{{ (isSuggestion() ? 'fb.suggestionText' : 'fb.complaintText') | t }} *</label>
                <textarea rows="4" class="resize-none" maxlength="1000" [(ngModel)]="message"></textarea>
                <div class="mt-1 text-end text-xs text-muted tabular">{{ message.length }}/1000</div></div>

              <button class="btn primary big" [disabled]="busy() || !valid()" (click)="submit()">{{ 'fb.send' | t }}</button>
              <p class="mt-3 text-center text-xs text-muted">{{ 'fb.privacy' | t }}</p>
            </div>
          </div>
        }
      </div>
    </div>`,
})
export class FeedbackPage implements OnInit {
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  readonly token = input.required<string>();
  readonly kind = input<string>('Complaint');
  readonly info = signal<Info | null>(null);
  readonly reference = signal<string | null>(null);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  name = '';
  phone = '';
  email = '';
  message = '';

  isSuggestion(): boolean { return this.kind() === 'Suggestion'; }

  async ngOnInit(): Promise<void> {
    try { this.info.set(await this.api.get<Info>(`public/r/${this.token()}`)); } catch { /* the form still works */ }
  }

  valid(): boolean { return this.name.trim().length > 1 && this.phone.trim().length >= 9 && this.message.trim().length > 4; }

  async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const receipt = await this.api.post<{ reference: string }>(`public/r/${this.token()}/feedback`, {
        kind: this.isSuggestion() ? 'Suggestion' : 'Complaint',
        name: this.name.trim(), phone: this.phone.trim(), email: this.email.trim() || null, message: this.message.trim(),
      });
      this.reference.set(receipt.reference);
    } catch (e) {
      this.error.set(this.api.error(e).message);
    } finally {
      this.busy.set(false);
    }
  }
}
