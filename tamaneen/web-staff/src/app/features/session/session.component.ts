import { ChangeDetectionStrategy, Component, DestroyRef, ElementRef, computed, effect, inject, input, signal, viewChild } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { DomSanitizer, type SafeResourceUrl } from '@angular/platform-browser';
import { RouterLink } from '@angular/router';
import { TranslateModule } from '@ngx-translate/core';
import { type ShownError, shownError, toProblem } from '../../core/api/problem';
import { StaffApiService } from '../../core/api/staff-api.service';
import type { JoinDescriptor, SessionState, TherapistSessionPage } from '../../core/api/types';
import { LanguageService } from '../../core/i18n/language.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { RiskTagComponent } from '../../shared/risk-tag.component';

const TOKEN_RENEW_MS = 60_000;

/** "06:42"; negative counts as zero. */
export function clockText(ms: number): string {
  const total = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(total / 3600);
  const pad = (n: number) => String(n).padStart(2, '0');
  const rest = `${pad(Math.floor((total % 3600) / 60))}:${pad(total % 60)}`;
  return h > 0 ? `${h}:${rest}` : rest;
}

/** Only an https room address may be framed. */
export function safeRoomUrl(url: string | null): string | null {
  return url && /^https:\/\//i.test(url) ? url : null;
}

/**
 * The therapist's session page (/sessions/:appointmentId): lobby, then live, with the same provider as the patient.
 * The lobby shows the patient's presence, their pre-session note, a local camera check and the join button (open from
 * ten minutes before). Live, the page sends a heartbeat with the session pass; with the dev provider it is a labelled
 * test room (own camera, the patient's presence, no call). An open crisis event for the patient is flagged at once.
 */
@Component({
  selector: 'tm-session',
  imports: [MatButtonModule, MatProgressBarModule, RouterLink, TranslateModule, LocalDatePipe, ProblemMessageComponent, RiskTagComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (loading()) {
      <mat-progress-bar mode="indeterminate" [attr.aria-label]="'common.loading' | translate" />
    }
    <a routerLink="/dashboard" class="back">{{ 'session.back' | translate }}</a>
    <h1 class="title">{{ (joined() ? 'session.liveTitle' : 'session.lobbyTitle') | translate }}</h1>
    <tm-problem-message [error]="error()" />

    @if (page(); as p) {
      @if (state()?.patientCrisisOpen) {
        <p class="crisis" role="alert" data-testid="crisis-open">
          {{ 'session.crisisOpen' | translate }} <a routerLink="/crisis">{{ 'session.openConsole' | translate }}</a>
        </p>
      }
      <section class="tm-card head">
        <span class="pseudonym" dir="ltr" data-testid="session-pseudonym">{{ p.pseudonymId }}</span>
        @if (p.riskLevel) {
          <tm-risk-tag [level]="p.riskLevel" />
        }
        <span class="tm-muted">{{ 'session.sequence' | translate: { n: p.sequenceNumber } }}</span>
        <span class="tm-muted">{{ p.state.startsAt | tmDate: 'dayTime' : language.language() }} · {{ 'session.minutes' | translate: { n: p.state.durationMinutes } }}</span>
        @if (!joined()) {
          <span class="countdown" dir="ltr" data-testid="countdown">{{ countdown() }}</span>
        } @else {
          <span class="countdown" dir="ltr" data-testid="elapsed">{{ elapsed() }}</span>
        }
        <span class="tag">{{ 'session.noRecording' | translate }}</span>
      </section>

      <p class="peer" role="status" data-testid="patient-presence">{{ (state()?.peer?.present ? 'session.patientIn' : 'session.patientNotYet') | translate }}</p>
      @if (reconnecting()) {
        <p class="warn" role="status">{{ 'session.reconnecting' | translate }}</p>
      }

      @if (!joined()) {
        <section class="tm-card">
          <h2 class="tm-card-title">{{ 'session.noteTitle' | translate }}</h2>
          @if (p.preSessionNote) {
            <p class="note" data-testid="pre-session-note">{{ p.preSessionNote }}</p>
          } @else {
            <p class="tm-muted" data-testid="no-note">{{ 'session.noNote' | translate }}</p>
          }
        </section>
        <section class="tm-card">
          <h2 class="tm-card-title">{{ 'session.devicesTitle' | translate }}</h2>
          <p class="tm-muted">{{ 'session.devicesLocal' | translate }}</p>
          @if (mediaStatus() === 'denied' || mediaStatus() === 'unavailable') {
            <p class="warn" role="status">{{ 'session.devices.' + mediaStatus() | translate }}</p>
          } @else if (mediaStatus() === 'ok') {
            <p role="status">{{ 'session.devices.ok' | translate }}</p>
          }
          <button mat-stroked-button type="button" (click)="startMedia()" data-testid="check-devices">{{ 'session.devicesCheck' | translate }}</button>
          <video #preview class="preview" [class.hidden]="mediaStatus() !== 'ok'" muted playsinline autoplay [attr.aria-label]="'session.preview' | translate"></video>
        </section>
        <p class="tm-muted">{{ 'session.privacy' | translate }}</p>
        <button mat-flat-button type="button" [disabled]="!canJoin() || joining()" (click)="join()" data-testid="join">{{ 'session.join' | translate }}</button>
        @if (!canJoin()) {
          <p class="tm-muted" data-testid="join-hint">{{ (closed() ? 'session.closed' : 'session.opensBefore') | translate }}</p>
        }
      } @else {
        @if (descriptor()?.mode === 'test_room') {
          <section class="tm-card test-room" data-testid="test-room">
            <strong>{{ 'session.testRoom' | translate }}</strong>
            <p>{{ 'session.testRoomBody' | translate }}</p>
          </section>
        }
        @if (roomUrl(); as url) {
          <iframe class="room" [src]="url" [title]="'session.roomTitle' | translate" allow="camera; microphone; fullscreen; display-capture"></iframe>
        } @else {
          <div class="tiles">
            <div class="tile" data-testid="patient-tile">
              <span dir="ltr">{{ p.pseudonymId }}</span>
              <span>{{ (state()?.peer?.present ? 'session.patientIn' : 'session.patientNotYet') | translate }}</span>
            </div>
            <div class="tile self">
              <video #live class="preview" muted playsinline autoplay [attr.aria-label]="'session.you' | translate"></video>
              <span class="you">{{ 'session.you' | translate }}</span>
            </div>
          </div>
        }
        <div class="controls">
          @if (!roomUrl()) {
            <button mat-stroked-button type="button" (click)="toggleMic()" [attr.aria-pressed]="!micOn()">{{ (micOn() ? 'session.mute' : 'session.unmute') | translate }}</button>
            <button mat-stroked-button type="button" (click)="toggleCamera()" [attr.aria-pressed]="!cameraOn()">{{ (cameraOn() ? 'session.cameraOff' : 'session.cameraOn') | translate }}</button>
          }
          <button mat-flat-button color="warn" type="button" (click)="leave()" data-testid="leave">{{ 'session.leave' | translate }}</button>
        </div>
      }
    }
  `,
  styles: `
    .back { display: inline-block; margin-block-end: 8px; color: var(--tm-primary); }
    .title { margin: 0 0 16px; font-size: 24px; font-weight: 700; }
    .head { display: flex; flex-wrap: wrap; gap: 12px; align-items: center; margin-block-end: 12px; }
    .pseudonym { font-weight: 700; unicode-bidi: isolate; }
    .countdown { font-size: 22px; font-weight: 700; font-variant-numeric: tabular-nums; margin-inline-start: auto; }
    .tag { background: var(--tm-tint); color: var(--tm-primary-strong); border-radius: 999px; padding: 2px 10px; font-size: 12px; font-weight: 600; }
    .peer { font-weight: 600; }
    .warn { background: var(--tm-warn-tint); color: var(--tm-warn-ink); padding: 8px 12px; border-radius: 12px; }
    .crisis { background: var(--tm-danger-tint); color: var(--tm-danger-ink); border: 1px solid var(--tm-danger-line); padding: 10px 14px; border-radius: 12px; font-weight: 600; }
    .note { white-space: pre-line; }
    .tm-card { margin-block-end: 12px; }
    .preview { width: 100%; max-width: 320px; border-radius: 12px; background: #1b2a2f; transform: scaleX(-1); }
    .hidden { display: none; }
    .test-room { background: var(--tm-warn-tint); }
    .tiles { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; margin-block: 12px; }
    .tile { min-height: 200px; border-radius: 16px; background: var(--tm-tint); display: flex; flex-direction: column; align-items: center; justify-content: center; gap: 8px; padding: 12px; text-align: center; position: relative; }
    .tile.self { background: #1b2a2f; color: #fff; }
    .tile .you { position: absolute; inset-block-end: 8px; inset-inline-start: 8px; font-size: 12px; }
    .room { width: 100%; aspect-ratio: 16 / 9; border: 1px solid var(--tm-line); border-radius: 16px; }
    .controls { display: flex; flex-wrap: wrap; gap: 8px; justify-content: center; }
  `,
})
export class SessionComponent {
  private readonly api = inject(StaffApiService);
  private readonly sanitizer = inject(DomSanitizer);
  protected readonly language = inject(LanguageService);

  /** Route parameter (withComponentInputBinding). */
  readonly appointmentId = input.required<string>();

  protected readonly page = signal<TherapistSessionPage | null>(null);
  protected readonly state = signal<SessionState | null>(null);
  protected readonly descriptor = signal<JoinDescriptor | null>(null);
  protected readonly loading = signal(true);
  protected readonly joining = signal(false);
  protected readonly reconnecting = signal(false);
  protected readonly error = signal<ShownError | null>(null);
  protected readonly mediaStatus = signal<'idle' | 'ok' | 'denied' | 'unavailable'>('idle');
  protected readonly micOn = signal(true);
  protected readonly cameraOn = signal(true);
  private readonly now = signal(Date.now());
  private readonly offset = signal(0);
  private stream: MediaStream | null = null;
  private poll: ReturnType<typeof setInterval> | null = null;
  private readonly previewRef = viewChild<ElementRef<HTMLVideoElement>>('preview');
  private readonly liveRef = viewChild<ElementRef<HTMLVideoElement>>('live');

  protected readonly joined = computed(() => this.descriptor() !== null);
  protected readonly serverNow = computed(() => this.now() + this.offset());
  protected readonly closed = computed(() => {
    const s = this.state();
    return !!s && (s.status !== 'confirmed' || this.serverNow() > Date.parse(s.closesAt));
  });
  protected readonly canJoin = computed(() => {
    const s = this.state();
    return !!s && s.status === 'confirmed' && this.serverNow() >= Date.parse(s.opensAt) && this.serverNow() <= Date.parse(s.closesAt);
  });
  protected readonly countdown = computed(() => clockText(Date.parse(this.state()?.startsAt ?? '') - this.serverNow()));
  protected readonly elapsed = computed(() => {
    const s = this.state();
    return s ? clockText(this.serverNow() - Math.max(Date.parse(s.startsAt), Date.parse(this.descriptorJoinedAt ?? s.startsAt))) : '';
  });
  protected readonly roomUrl = computed<SafeResourceUrl | null>(() => {
    const d = this.descriptor();
    const url = d?.mode === 'embed' ? safeRoomUrl(d.url) : null;
    return url ? this.sanitizer.bypassSecurityTrustResourceUrl(url) : null;
  });
  private descriptorJoinedAt: string | null = null;

  constructor() {
    const destroyRef = inject(DestroyRef);
    effect(() => {
      const id = this.appointmentId();
      this.api.session(id).subscribe({
        next: (page) => {
          this.page.set(page);
          this.applyState(page.state);
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.error.set(shownError(error, 'session.loadError'));
          this.loading.set(false);
        },
      });
    });
    effect(() => {
      // Attach the local stream to whichever video element is on screen.
      const video = (this.liveRef() ?? this.previewRef())?.nativeElement;
      if (video && this.stream && this.mediaStatus() === 'ok') {
        video.srcObject = this.stream;
      }
    });
    const tick = setInterval(() => this.now.set(Date.now()), 1000);
    this.schedulePoll(15);
    destroyRef.onDestroy(() => {
      clearInterval(tick);
      if (this.poll) clearInterval(this.poll);
      this.stopMedia();
    });
  }

  protected async startMedia(): Promise<void> {
    if (!navigator.mediaDevices?.getUserMedia) {
      this.mediaStatus.set('unavailable');
      return;
    }
    try {
      this.stopMedia();
      this.stream = await navigator.mediaDevices.getUserMedia({ audio: true, video: true });
      this.mediaStatus.set('ok');
      this.micOn.set(true);
      this.cameraOn.set(true);
    } catch {
      this.mediaStatus.set('denied');
    }
  }

  protected toggleMic(): void {
    const next = !this.micOn();
    this.stream?.getAudioTracks().forEach((t) => (t.enabled = next));
    this.micOn.set(next);
  }

  protected toggleCamera(): void {
    const next = !this.cameraOn();
    this.stream?.getVideoTracks().forEach((t) => (t.enabled = next));
    this.cameraOn.set(next);
  }

  protected join(): void {
    this.joining.set(true);
    this.api.joinSession(this.appointmentId()).subscribe({
      next: (d) => {
        this.descriptor.set(d);
        this.descriptorJoinedAt = this.descriptorJoinedAt ?? new Date(this.serverNow()).toISOString();
        this.joining.set(false);
        this.error.set(null);
        this.schedulePoll(d.heartbeatSeconds);
        this.beat();
        if (d.mode === 'test_room' && this.mediaStatus() !== 'ok') void this.startMedia();
      },
      error: (error: unknown) => {
        this.joining.set(false);
        this.error.set(shownError(error, 'session.joinError'));
      },
    });
  }

  protected leave(): void {
    const d = this.descriptor();
    if (d) {
      this.api.heartbeat(this.appointmentId(), d.token, true).subscribe({ next: (s) => this.applyState(s), error: () => undefined });
    }
    this.descriptor.set(null);
    this.stopMedia();
    this.mediaStatus.set('idle');
  }

  private beat(): void {
    const d = this.descriptor();
    const id = this.appointmentId();
    if (!d) {
      this.api.presence(id).subscribe({ next: (s) => this.applyState(s), error: () => this.reconnecting.set(true) });
      return;
    }
    if (Date.parse(d.expiresAt) - Date.now() < TOKEN_RENEW_MS && Date.now() < Date.parse(d.closesAt)) {
      this.join();
      return;
    }
    this.api.heartbeat(id, d.token).subscribe({
      next: (s) => this.applyState(s),
      error: (error: unknown) => {
        if (toProblem(error)?.code === 'error.session.token_invalid') this.join();
        else this.reconnecting.set(true);
      },
    });
  }

  private schedulePoll(seconds: number): void {
    if (this.poll) clearInterval(this.poll);
    this.poll = setInterval(() => this.beat(), Math.max(5, seconds) * 1000);
  }

  private applyState(s: SessionState): void {
    this.state.set(s);
    this.offset.set(Date.parse(s.serverNow) - Date.now());
    this.reconnecting.set(false);
  }

  private stopMedia(): void {
    this.stream?.getTracks().forEach((t) => t.stop());
    this.stream = null;
  }
}
