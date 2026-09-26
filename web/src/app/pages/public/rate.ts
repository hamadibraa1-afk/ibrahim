import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Api } from '../../core/api';
import { RouterLink } from '@angular/router';
import { I18n, TPipe } from '../../core/i18n';

interface Info { nameAr: string; nameEn: string; chooseEmployee: { id: string; firstName: string }[]; }

@Component({
  selector: 'app-rate',
  standalone: true,
  imports: [FormsModule, TPipe, RouterLink],
  template: `
    <div class="min-h-screen px-4 py-8
                bg-[radial-gradient(900px_420px_at_50%_-10%,rgb(var(--c-brand)/0.2),transparent_65%)]">
      <div class="mx-auto w-full max-w-sm">
        <div class="mb-5 flex justify-center">
          <button class="btn sm ghost" (click)="i18n.toggle()">{{ i18n.lang() === 'ar' ? 'English' : 'عربي' }}</button>
        </div>

        @if (invalid()) {
          <div class="card-pad text-center animate-scale-in"><h2>{{ 'pub.invalid' | t }}</h2></div>
        } @else if (done()) {
          <div class="card-pad text-center animate-scale-in">
            <div class="mx-auto mb-4 grid h-16 w-16 place-items-center rounded-full bg-ok-soft text-ok">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round"
                   stroke-linejoin="round" class="h-8 w-8"><path d="m5 13 4.5 4.5L19 7" /></svg>
            </div>
            <h1 class="text-xl">{{ 'pub.thanks' | t }}</h1>
            <p class="mt-2 text-sm text-muted">{{ 'pub.thanksLead' | t }}</p>
            <div class="mt-5 flex gap-2">
              <a class="btn flex-1" [routerLink]="['/r', token(), 'feedback']" [queryParams]="{ kind: 'Complaint' }">{{ 'pub.complaint' | t }}</a>
              <a class="btn flex-1" [routerLink]="['/r', token(), 'feedback']" [queryParams]="{ kind: 'Suggestion' }">{{ 'pub.suggestion' | t }}</a>
            </div>
          </div>
        } @else {
          @if (info(); as i) {
            <div class="card-pad rounded-2xl text-center animate-slide-up">
              <img src="/logo.png" alt="" class="mx-auto mb-3 h-20 w-20 rounded-xl bg-white object-contain p-1">
              <div class="text-sm font-semibold text-brand">{{ 'org.name' | t }}</div>
              <h1 class="mt-1 text-xl">{{ i18n.pick(i.nameAr, i.nameEn) }}</h1>
              <p class="mt-2 text-sm text-muted">{{ 'pub.lead' | t }}</p>

              @if (i.chooseEmployee.length) {
                <p class="mt-5 text-sm font-medium">{{ 'pub.chooseEmployee' | t }}</p>
                <div class="mt-2 flex flex-wrap justify-center gap-2">
                  @for (e of i.chooseEmployee; track e.id) {
                    <button class="btn" [class.primary]="employeeId === e.id" (click)="employeeId = e.id">{{ e.firstName }}</button>
                  }
                </div>
              }

              <div class="my-6 flex justify-center gap-1.5" dir="ltr">
                @for (n of [1,2,3,4,5]; track n) {
                  <button class="text-4xl leading-none transition-all duration-150 hover:scale-110 active:scale-95"
                          [class]="n <= stars() ? 'text-[#f5b301] drop-shadow' : 'text-line'"
                          (click)="stars.set(n)" [attr.aria-label]="n">★</button>
                }
              </div>

              <textarea rows="3" maxlength="500" class="resize-none text-start" [placeholder]="'pub.comment' | t" [(ngModel)]="comment"></textarea>

              @if (error()) { <div class="alert red mt-3">{{ error() }}</div> }

              <button class="btn primary big mt-4"
                      [disabled]="!stars() || busy() || (i.chooseEmployee.length > 0 && !employeeId)" (click)="submit()">
                {{ 'pub.submit' | t }}
              </button>

              <div class="mt-6 border-t border-line pt-4">
                <p class="text-xs text-muted">{{ 'pub.feedbackLead' | t }}</p>
                <div class="mt-2 flex gap-2">
                  <a class="btn flex-1" [routerLink]="['/r', token(), 'feedback']" [queryParams]="{ kind: 'Complaint' }">{{ 'pub.complaint' | t }}</a>
                  <a class="btn flex-1" [routerLink]="['/r', token(), 'feedback']" [queryParams]="{ kind: 'Suggestion' }">{{ 'pub.suggestion' | t }}</a>
                </div>
              </div>
            </div>
          } @else { <div class="skeleton h-80 rounded-2xl"></div> }
        }
      </div>
    </div>`,
})
export class RatePage implements OnInit {
  readonly i18n = inject(I18n);
  private readonly api = inject(Api);
  readonly token = input.required<string>();
  readonly info = signal<Info | null>(null);
  readonly invalid = signal(false);
  readonly done = signal(false);
  readonly busy = signal(false);
  readonly error = signal<string | null>(null);
  readonly stars = signal(0);
  employeeId = '';
  comment = '';

  async ngOnInit(): Promise<void> {
    try { this.info.set(await this.api.get<Info>(`public/r/${this.token()}`)); }
    catch { this.invalid.set(true); }
  }

  private device(): string {
    let id = localStorage.getItem('device');
    if (!id) { id = crypto.randomUUID(); localStorage.setItem('device', id); }
    return id;
  }

  async submit(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      await this.api.post(`public/r/${this.token()}`, { stars: this.stars(), comment: this.comment || null, employeeId: this.employeeId || null, deviceToken: this.device() });
      this.done.set(true);
    } catch (e) {
      const err = this.api.error(e);
      if (err.code === 'rating.already_submitted') this.done.set(true);
      else this.error.set(err.message);
    } finally { this.busy.set(false); }
  }
}
