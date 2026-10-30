import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslateModule } from '@ngx-translate/core';
import { AccountApiService } from '../../core/api/account-api.service';
import { type ShownError, shownError } from '../../core/api/problem';
import type { Lang, StaffProfile } from '../../core/api/types';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { LanguageService } from '../../core/i18n/language.service';

export const MAX_DISPLAY_NAME = 60;
export const MAX_TITLE = 80;
export const MAX_BIO = 600;

/** Field errors of a 400 ProblemDetails ({ errors: { field: [code] } }). */
export function fieldErrors(error: unknown): Record<string, string[]> {
  if (error instanceof HttpErrorResponse && error.error && typeof error.error === 'object' && 'errors' in error.error) {
    return (error.error as { errors: Record<string, string[]> }).errors ?? {};
  }
  return {};
}

/**
 * S21 «الملف المهني»: what patients see on the directory and what matching uses. Name, title and bio in Arabic and
 * English, specialties from the catalog, session languages, capacity (never below the current caseload, SB-29) and
 * "accepting new patients". No phone numbers or links in the text. The licence card is read only: changing it
 * needs a new verification (S09). These changes are not safety content, so they save directly (guide, two-tier approval).
 */
@Component({
  selector: 'tm-profile',
  imports: [ReactiveFormsModule, MatButtonModule, MatCheckboxModule, MatChipsModule, MatFormFieldModule, MatInputModule, MatProgressBarModule, MatSlideToggleModule, TranslateModule, ProblemMessageComponent, LocalDatePipe],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" [attr.aria-label]="'common.loading' | translate" />
    }
    <h1 class="title">{{ 'profile.title' | translate }}</h1>
    <tm-problem-message [error]="error()" />
    @if (profile(); as p) {
      <form class="layout" [formGroup]="form" (ngSubmit)="save()" novalidate>
        <section class="tm-card fields">
          <div class="pair">
            <mat-form-field appearance="outline">
              <mat-label>{{ 'profile.displayNameAr' | translate }}</mat-label>
              <input matInput formControlName="displayNameAr" dir="rtl" lang="ar" [maxlength]="maxName" data-testid="name-ar" />
              <mat-error>{{ 'common.required' | translate }}</mat-error>
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'profile.displayNameEn' | translate }}</mat-label>
              <input matInput formControlName="displayNameEn" dir="ltr" lang="en" [maxlength]="maxName" data-testid="name-en" />
              <mat-error>{{ 'common.required' | translate }}</mat-error>
            </mat-form-field>
          </div>
          <div class="pair">
            <mat-form-field appearance="outline">
              <mat-label>{{ 'profile.titleAr' | translate }}</mat-label>
              <input matInput formControlName="titleAr" dir="rtl" lang="ar" [maxlength]="maxTitle" />
            </mat-form-field>
            <mat-form-field appearance="outline">
              <mat-label>{{ 'profile.titleEn' | translate }}</mat-label>
              <input matInput formControlName="titleEn" dir="ltr" lang="en" [maxlength]="maxTitle" />
            </mat-form-field>
          </div>
          <mat-form-field appearance="outline">
            <mat-label>{{ 'profile.capacity' | translate }}</mat-label>
            <input matInput type="number" formControlName="capacity" min="1" max="200" data-testid="capacity" />
            <mat-hint>{{ 'profile.current' | translate: { count: p.currentPatients } }}</mat-hint>
          </mat-form-field>
          @if (capacityError() || serverErrors()['capacity']) {
            <p class="field-error" role="alert" data-testid="capacity-error">{{ 'profile.capacityTooLow' | translate }}</p>
          }

          <fieldset class="group">
            <legend>{{ 'profile.specialties' | translate }}</legend>
            @for (code of p.specialtyCatalog; track code) {
              <mat-checkbox [checked]="specialties().includes(code)" (change)="toggleSpecialty(code, $event.checked)" [attr.data-testid]="'specialty-' + code">{{ 'specialties.' + code | translate }}</mat-checkbox>
            }
            @if (specialtyError()) { <p class="field-error" role="alert" data-testid="specialty-error">{{ 'common.chooseOne' | translate }}</p> }
          </fieldset>

          <fieldset class="group">
            <legend>{{ 'profile.languages' | translate }}</legend>
            @for (l of allLanguages; track l) {
              <mat-checkbox [checked]="languages().includes(l)" (change)="toggleLanguage(l, $event.checked)" [attr.data-testid]="'lang-' + l">{{ 'profile.lang.' + l | translate }}</mat-checkbox>
            }
          </fieldset>

          <mat-form-field appearance="outline">
            <mat-label>{{ 'profile.bioAr' | translate }}</mat-label>
            <textarea matInput formControlName="bioAr" rows="4" dir="rtl" lang="ar" [maxlength]="maxBio" data-testid="bio-ar"></textarea>
            <mat-hint align="end">{{ form.controls.bioAr.value.length }} / {{ maxBio }}</mat-hint>
          </mat-form-field>
          <mat-form-field appearance="outline">
            <mat-label>{{ 'profile.bioEn' | translate }}</mat-label>
            <textarea matInput formControlName="bioEn" rows="4" dir="ltr" lang="en" [maxlength]="maxBio" data-testid="bio-en"></textarea>
            <mat-hint align="end">{{ form.controls.bioEn.value.length }} / {{ maxBio }}</mat-hint>
          </mat-form-field>
          <p class="tm-muted hint">{{ 'profile.noContact' | translate }}</p>

          <mat-slide-toggle formControlName="acceptingNewPatients" data-testid="accepting">{{ 'profile.accepting' | translate }}</mat-slide-toggle>
          <p class="tm-muted hint">{{ 'profile.acceptingHint' | translate }}</p>
          <p class="tm-muted hint">{{ 'profile.session' | translate: { minutes: p.sessionMinutes } }}</p>

          <div class="actions">
            <button mat-flat-button color="primary" type="submit" [disabled]="saving()" data-testid="save">{{ 'profile.save' | translate }}</button>
            @if (saved()) { <span role="status" class="saved" data-testid="saved">{{ 'profile.saved' | translate }}</span> }
          </div>
        </section>

        <aside class="tm-card" data-testid="license-card">
          <h2 class="tm-card-title">{{ 'profile.license' | translate }}</h2>
          @if (p.licenseExpiringSoon) {
            <p class="warn" role="alert" data-testid="license-expiring">{{ 'profile.licenseExpiring' | translate: { date: (p.licenseExpiresOn | tmDate: 'longDate' : lang.language()) } }}</p>
          }
          <p>{{ 'profile.status' | translate }}: <strong data-testid="license-status">{{ (p.licenseVerified ? 'profile.verified' : 'profile.unverified') | translate }}</strong></p>
          @if (p.licenseExpiresOn) {
            <p data-testid="license-expires">{{ 'profile.licenseExpires' | translate: { date: (p.licenseExpiresOn | tmDate: 'longDate' : lang.language()) } }}</p>
          }
          @if (p.licenseVerifiedBy && p.licenseVerifiedAt) {
            <p class="tm-muted" data-testid="license-verified-by">{{ 'profile.licenseVerifiedBy' | translate: { name: p.licenseVerifiedBy, date: (p.licenseVerifiedAt | tmDate: 'longDate' : lang.language()) } }}</p>
          }
          <p class="tm-muted hint">{{ 'profile.licenseNote' | translate }}</p>
        </aside>
      </form>
    }
  `,
  styles: `
    .title { margin: 0 0 16px; font-size: 24px; font-weight: 700; }
    .warn { background: var(--tm-danger-tint); border: 1px solid var(--tm-danger-line); color: var(--tm-danger-ink); border-radius: 10px; padding: 8px 12px; }
    .layout { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 1fr); gap: 16px; align-items: start; }
    @media (max-width: 900px) { .layout { grid-template-columns: 1fr; } }
    .fields { display: flex; flex-direction: column; gap: 8px; }
    .pair { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
    @media (max-width: 600px) { .pair { grid-template-columns: 1fr; } }
    .group { border: 0; padding: 0; margin: 8px 0; display: flex; flex-wrap: wrap; gap: 4px 12px; }
    .group legend { font-weight: 700; margin-block-end: 8px; }
    .hint { font-size: 13px; margin: 0; }
    .field-error { color: var(--tm-danger-ink); margin: 0; font-size: 13px; width: 100%; }
    .actions { display: flex; align-items: center; gap: 12px; margin-top: 8px; }
    .saved { color: var(--tm-ok-ink); }
  `,
})
export class ProfileComponent {
  private readonly api = inject(AccountApiService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly maxName = MAX_DISPLAY_NAME;
  protected readonly maxTitle = MAX_TITLE;
  protected readonly maxBio = MAX_BIO;
  protected readonly allLanguages: Lang[] = ['ar', 'en'];
  protected readonly profile = signal<StaffProfile | null>(null);
  protected readonly specialties = signal<string[]>([]);
  protected readonly languages = signal<Lang[]>([]);
  protected readonly lang = inject(LanguageService);
  protected readonly loading = signal(true);
  protected readonly saving = signal(false);
  protected readonly saved = signal(false);
  protected readonly capacityError = signal(false);
  protected readonly specialtyError = signal(false);
  protected readonly serverErrors = signal<Record<string, string[]>>({});
  protected readonly error = signal<ShownError | null>(null);
  protected readonly form = inject(NonNullableFormBuilder).group({
    displayNameAr: ['', [Validators.required, Validators.maxLength(MAX_DISPLAY_NAME)]],
    displayNameEn: ['', [Validators.required, Validators.maxLength(MAX_DISPLAY_NAME)]],
    titleAr: ['', Validators.maxLength(MAX_TITLE)],
    titleEn: ['', Validators.maxLength(MAX_TITLE)],
    bioAr: ['', Validators.maxLength(MAX_BIO)],
    bioEn: ['', Validators.maxLength(MAX_BIO)],
    capacity: [1, [Validators.required, Validators.min(1), Validators.max(200)]],
    acceptingNewPatients: [true],
  });

  constructor() {
    this.api
      .profile()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (p) => this.apply(p),
        error: (error: unknown) => {
          this.error.set(shownError(error, 'profile.loadError'));
          this.loading.set(false);
        },
      });
  }

  toggleSpecialty(code: string, on: boolean): void {
    this.specialties.update((list) => (on ? [...new Set([...list, code])] : list.filter((c) => c !== code)));
  }

  toggleLanguage(language: Lang, on: boolean): void {
    this.languages.update((list) => (on ? [...new Set([...list, language])] : list.filter((l) => l !== language)));
  }

  save(): void {
    const p = this.profile();
    this.saved.set(false);
    this.serverErrors.set({});
    const value = this.form.getRawValue();
    this.capacityError.set(p !== null && value.capacity < p.currentPatients);
    this.specialtyError.set(this.specialties().length === 0);
    if (this.form.invalid || this.capacityError() || this.specialtyError() || this.languages().length === 0) {
      this.form.markAllAsTouched();
      return;
    }
    this.saving.set(true);
    this.error.set(null);
    this.api.saveProfile({ ...value, specialties: this.specialties(), languages: this.languages() }).subscribe({
      next: (saved) => {
        this.apply(saved);
        this.saving.set(false);
        this.saved.set(true);
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.serverErrors.set(fieldErrors(error));
        this.error.set(shownError(error, 'profile.saveError'));
      },
    });
  }

  private apply(p: StaffProfile): void {
    this.profile.set(p);
    this.specialties.set(p.specialties);
    this.languages.set(p.languages);
    this.form.setValue({
      displayNameAr: p.displayNameAr,
      displayNameEn: p.displayNameEn,
      titleAr: p.titleAr,
      titleEn: p.titleEn,
      bioAr: p.bioAr,
      bioEn: p.bioEn,
      capacity: p.capacity,
      acceptingNewPatients: p.acceptingNewPatients,
    });
    this.loading.set(false);
  }
}
