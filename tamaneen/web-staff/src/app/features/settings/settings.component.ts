import { ChangeDetectionStrategy, Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { ActivatedRoute } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import type { Observable } from 'rxjs';
import { AccountApiService } from '../../core/api/account-api.service';
import { type ShownError, shownError } from '../../core/api/problem';
import type { StaffNotificationPrefs, StaffSecurity, TotpMove } from '../../core/api/types';
import type { Language } from '../../core/i18n/language';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';

/** The pilot's zone (Platform:TimeZone on the API); staff times are shown in it. Read only for now. */
export const PILOT_TIME_ZONE = 'Asia/Riyadh';

export type SecurityPanel = 'password' | 'move' | 'codes' | null;
export type PrefKey = 'bookings' | 'weeklyReport' | 'dailyDigest';

/**
 * S32 «إعدادات الحساب». Security changes need the password and a current authenticator code and are recorded.
 * Two-step verification cannot be turned off, only moved: the old device keeps working until a code from the new
 * one is checked. Sessions end after 15 minutes idle. Risk alerts are locked on. The three other switches save at
 * once and go back if the save fails. The language switch flips the direction at once. Passkeys (WebAuthn) are not
 * built yet, so they are not offered.
 */
@Component({
  selector: 'tm-settings',
  imports: [FormsModule, MatButtonModule, MatButtonToggleModule, MatFormFieldModule, MatInputModule, MatSlideToggleModule, TranslateModule, LocalDatePipe, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <h1 class="title">{{ 'account.title' | translate }}</h1>
    <tm-problem-message [error]="error()" />
    @if (notice(); as n) { <p class="notice" role="status" data-testid="notice">{{ n | translate }}</p> }

    <section class="tm-card" aria-labelledby="sec-title">
      <h2 id="sec-title" class="tm-card-title">{{ 'account.security' | translate }}</h2>
      @if (security(); as s) {
        <div class="row">
          <div><strong>{{ 'account.password' | translate }}</strong><p class="tm-muted">{{ 'account.passwordChanged' | translate: { date: (s.passwordChangedAt | tmDate: 'longDate' : language.language()) } }}</p></div>
          <button mat-stroked-button type="button" (click)="openPanel('password')" data-testid="change-password">{{ 'account.change' | translate }}</button>
        </div>
        @if (panel() === 'password') {
          <form class="panel" (ngSubmit)="changePassword()" data-testid="password-panel">
            <mat-form-field appearance="outline"><mat-label>{{ 'account.currentPassword' | translate }}</mat-label><input matInput type="password" name="current" [(ngModel)]="current" autocomplete="current-password" required /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>{{ 'account.newPassword' | translate }}</mat-label><input matInput type="password" name="next" [(ngModel)]="next" autocomplete="new-password" required /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>{{ 'account.confirmPassword' | translate }}</mat-label><input matInput type="password" name="confirm" [(ngModel)]="confirm" autocomplete="new-password" required /></mat-form-field>
            <mat-form-field appearance="outline"><mat-label>{{ 'account.code' | translate }}</mat-label><input matInput name="code" [(ngModel)]="code" inputmode="numeric" autocomplete="one-time-code" dir="ltr" required /></mat-form-field>
            @if (mismatch()) { <p class="field-error" role="alert" data-testid="mismatch">{{ 'account.mismatch' | translate }}</p> }
            <div class="actions"><button mat-flat-button color="primary" type="submit" [disabled]="busy()">{{ 'account.save' | translate }}</button><button mat-button type="button" (click)="openPanel(null)">{{ 'common.cancel' | translate }}</button></div>
          </form>
        }

        <div class="row">
          <div><strong>{{ 'account.authenticator' | translate }}</strong><p class="tm-muted">{{ 'account.authenticatorOn' | translate }}</p></div>
          <button mat-stroked-button type="button" (click)="openPanel('move')" data-testid="move-totp">{{ 'account.move' | translate }}</button>
        </div>
        @if (panel() === 'move') {
          @if (move(); as m) {
            <form class="panel" (ngSubmit)="confirmMove(m)" data-testid="move-confirm">
              <p>{{ 'account.moveScan' | translate }}</p>
              <p class="secret" dir="ltr" data-testid="move-secret">{{ m.secret }}</p>
              <a class="tm-muted" [href]="m.otpauthUri">{{ 'account.moveLink' | translate }}</a>
              <mat-form-field appearance="outline"><mat-label>{{ 'account.newCode' | translate }}</mat-label><input matInput name="newCode" [(ngModel)]="code" inputmode="numeric" dir="ltr" required /></mat-form-field>
              <div class="actions"><button mat-flat-button color="primary" type="submit" [disabled]="busy()">{{ 'account.confirmMove' | translate }}</button><button mat-button type="button" (click)="openPanel(null)">{{ 'common.cancel' | translate }}</button></div>
            </form>
          } @else {
            <form class="panel" (ngSubmit)="startMove()" data-testid="move-stepup">
              <p class="tm-muted">{{ 'account.stepUp' | translate }}</p>
              <mat-form-field appearance="outline"><mat-label>{{ 'account.currentPassword' | translate }}</mat-label><input matInput type="password" name="pw" [(ngModel)]="current" required /></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>{{ 'account.code' | translate }}</mat-label><input matInput name="c" [(ngModel)]="code" inputmode="numeric" dir="ltr" required /></mat-form-field>
              <div class="actions"><button mat-flat-button color="primary" type="submit" [disabled]="busy()">{{ 'account.continue' | translate }}</button><button mat-button type="button" (click)="openPanel(null)">{{ 'common.cancel' | translate }}</button></div>
            </form>
          }
        }

        <div class="row">
          <div><strong>{{ 'account.recoveryCodes' | translate }}</strong><p class="tm-muted">{{ 'account.codesLeft' | translate: { count: s.recoveryCodesRemaining } }}</p></div>
          <button mat-stroked-button type="button" (click)="openPanel('codes')" data-testid="new-codes">{{ 'account.newCodes' | translate }}</button>
        </div>
        @if (panel() === 'codes') {
          @if (codes().length) {
            <div class="panel"><p>{{ 'account.codesOnce' | translate }}</p><ul class="codes" dir="ltr" data-testid="codes">@for (c of codes(); track c) { <li>{{ c }}</li> }</ul></div>
          } @else {
            <form class="panel" (ngSubmit)="newCodes()">
              <p class="tm-muted">{{ 'account.stepUp' | translate }}</p>
              <mat-form-field appearance="outline"><mat-label>{{ 'account.currentPassword' | translate }}</mat-label><input matInput type="password" name="pw2" [(ngModel)]="current" required /></mat-form-field>
              <mat-form-field appearance="outline"><mat-label>{{ 'account.code' | translate }}</mat-label><input matInput name="c2" [(ngModel)]="code" inputmode="numeric" dir="ltr" required /></mat-form-field>
              <div class="actions"><button mat-flat-button color="primary" type="submit" [disabled]="busy()">{{ 'account.continue' | translate }}</button><button mat-button type="button" (click)="openPanel(null)">{{ 'common.cancel' | translate }}</button></div>
            </form>
          }
        }

        <div class="row">
          <div><strong>{{ 'account.sessions' | translate }}</strong><p class="tm-muted">{{ 'account.sessionsInfo' | translate: { count: s.sessions.length, minutes: s.idleTimeoutMinutes } }}</p></div>
          <button mat-stroked-button type="button" (click)="endOthers()" [disabled]="busy() || s.sessions.length < 2" data-testid="end-others">{{ 'account.endOthers' | translate }}</button>
        </div>
        <ul class="sessions">
          @for (x of s.sessions; track x.id) {
            <li class="session" [class.flagged]="x.id === flagged()" data-testid="session">
              <span>{{ x.deviceLabel }}</span>
              <span class="tm-muted">@if (x.current) { {{ 'account.thisDevice' | translate }} } @else { {{ x.lastActiveAt | tmDate: 'dayTime' : language.language() }} }</span>
              @if (!x.current) { <button mat-button type="button" (click)="endSession(x.id)" [attr.data-testid]="'end-' + x.id">{{ 'account.end' | translate }}</button> }
            </li>
          }
        </ul>
      }
    </section>

    <section class="tm-card" aria-labelledby="notif-title">
      <h2 id="notif-title" class="tm-card-title">{{ 'account.notifications' | translate }}</h2>
      <mat-slide-toggle [checked]="true" [disabled]="true" data-testid="risk-alerts">{{ 'account.riskAlerts' | translate }}</mat-slide-toggle>
      @if (prefs(); as p) {
        @for (key of prefKeys; track key) {
          <mat-slide-toggle [checked]="p[key]" (change)="togglePref(key, $event.checked, $event.source)" [attr.data-testid]="'pref-' + key">{{ 'account.pref.' + key | translate }}</mat-slide-toggle>
        }
      }
      <p class="tm-muted hint">{{ 'account.riskLocked' | translate }}</p>
    </section>

    <section class="tm-card" aria-labelledby="lang-title">
      <h2 id="lang-title" class="tm-card-title">{{ 'account.display' | translate }}</h2>
      <p><strong>{{ 'account.language' | translate }}</strong></p>
      <mat-button-toggle-group [value]="language.language()" (change)="setLanguage($event.value)" [attr.aria-label]="'account.language' | translate">
        <mat-button-toggle value="ar" lang="ar" data-testid="lang-ar">العربية</mat-button-toggle>
        <mat-button-toggle value="en" lang="en" data-testid="lang-en">English</mat-button-toggle>
      </mat-button-toggle-group>
      <p><strong>{{ 'account.timeZone' | translate }}</strong>: <span dir="ltr">{{ timeZone }}</span></p>
      <p class="tm-muted hint">{{ 'account.timeZoneNote' | translate }}</p>
    </section>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 16px; }
    .title { margin: 0; font-size: 24px; font-weight: 700; }
    .row { display: flex; justify-content: space-between; align-items: center; gap: 12px; padding-block: 12px; border-block-end: 1px solid var(--tm-line); }
    .row p { margin: 4px 0 0; font-size: 14px; }
    .panel { display: flex; flex-direction: column; gap: 4px; padding: 12px; margin-block: 8px; background: var(--tm-soft); border-radius: 10px; max-width: 420px; }
    .actions { display: flex; gap: 8px; }
    .secret { font-family: monospace; font-size: 16px; letter-spacing: 2px; word-break: break-all; }
    .codes { columns: 2; font-family: monospace; }
    .sessions { list-style: none; padding: 0; margin: 8px 0 0; }
    .session { display: flex; gap: 12px; align-items: center; padding-block: 6px; }
    .session.flagged { background: var(--tm-warn-tint); border-radius: 8px; padding-inline: 8px; }
    .field-error { color: var(--tm-danger-ink); margin: 0; }
    .notice { margin: 0; padding: 10px 14px; border-radius: 10px; background: var(--tm-ok-tint); color: var(--tm-ok-ink); }
    .hint { font-size: 13px; }
    mat-slide-toggle { display: block; margin-block: 8px; }
  `,
})
export class SettingsComponent {
  private readonly api = inject(AccountApiService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly language = inject(LanguageService);
  protected readonly prefKeys: PrefKey[] = ['bookings', 'weeklyReport', 'dailyDigest'];
  protected readonly timeZone = PILOT_TIME_ZONE;
  protected readonly security = signal<StaffSecurity | null>(null);
  protected readonly prefs = signal<StaffNotificationPrefs | null>(null);
  protected readonly panel = signal<SecurityPanel>(null);
  protected readonly move = signal<TotpMove | null>(null);
  protected readonly codes = signal<string[]>([]);
  protected readonly busy = signal(false);
  protected readonly mismatch = signal(false);
  protected readonly notice = signal<string | null>(null);
  protected readonly error = signal<ShownError | null>(null);
  /** A session opened from S41's «ليس أنت؟». */
  protected readonly flagged = signal<string | null>(inject(ActivatedRoute).snapshot.queryParamMap.get('session'));
  protected current = '';
  protected next = '';
  protected confirm = '';
  protected code = '';

  constructor() {
    this.loadSecurity();
    this.api
      .notificationPrefs()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (p) => this.prefs.set(p), error: (e: unknown) => this.error.set(shownError(e, 'account.loadError')) });
  }

  openPanel(panel: SecurityPanel): void {
    this.panel.set(panel);
    this.move.set(null);
    this.codes.set([]);
    this.mismatch.set(false);
    this.current = this.next = this.confirm = this.code = '';
  }

  changePassword(): void {
    if (this.next !== this.confirm) {
      this.mismatch.set(true);
      return;
    }
    this.run(this.api.changePassword({ currentPassword: this.current, newPassword: this.next, code: this.code }), () => {
      this.openPanel(null);
      this.notice.set('account.passwordDone');
      this.loadSecurity();
    });
  }

  startMove(): void {
    this.run(this.api.startTotpMove({ password: this.current, code: this.code }), (m) => {
      this.move.set(m);
      this.code = '';
    });
  }

  confirmMove(m: TotpMove): void {
    this.run(this.api.confirmTotpMove(m.moveToken, this.code), () => {
      this.openPanel(null);
      this.notice.set('account.moveDone');
    });
  }

  newCodes(): void {
    this.run(this.api.recoveryCodes({ password: this.current, code: this.code }), (r) => {
      this.codes.set(r.recoveryCodes);
      this.loadSecurity();
    });
  }

  endSession(id: string): void {
    this.run(this.api.revokeSession(id), () => {
      this.notice.set('account.sessionEnded');
      this.loadSecurity();
    });
  }

  endOthers(): void {
    this.run(this.api.revokeOtherSessions(), () => {
      this.notice.set('account.othersEnded');
      this.loadSecurity();
    });
  }

  /** Saves at once; on failure the switch goes back. */
  togglePref(key: PrefKey, value: boolean, source?: { checked: boolean }): void {
    const before = this.prefs();
    if (!before) return;
    const changed = { ...before, [key]: value };
    this.prefs.set(changed);
    this.api.saveNotificationPrefs(changed).subscribe({
      next: (saved) => {
        this.prefs.set(saved);
        this.notice.set('account.prefSaved');
      },
      error: (e: unknown) => {
        this.prefs.set({ ...before });
        if (source) source.checked = before[key];
        this.error.set(shownError(e, 'account.saveError'));
      },
    });
  }

  async setLanguage(language: Language): Promise<void> {
    await this.language.use(language);
    this.api.saveLanguage(language).subscribe({ error: () => undefined });
  }

  private loadSecurity(): void {
    this.api
      .security()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: (s) => this.security.set(s), error: (e: unknown) => this.error.set(shownError(e, 'account.loadError')) });
  }

  private run<T>(request: Observable<T>, done: (value: T) => void): void {
    this.busy.set(true);
    this.error.set(null);
    this.notice.set(null);
    request.subscribe({
      next: (value) => {
        this.busy.set(false);
        done(value);
      },
      error: (e: unknown) => {
        this.busy.set(false);
        this.error.set(shownError(e, 'account.saveError'));
      },
    });
  }
}
