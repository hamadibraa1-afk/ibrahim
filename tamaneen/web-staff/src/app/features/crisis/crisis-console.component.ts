import { ChangeDetectionStrategy, Component, DestroyRef, InjectionToken, NgZone, computed, effect, inject, signal, untracked } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog, type MatDialogConfig } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router } from '@angular/router';
import { TranslateModule, TranslateService } from '@ngx-translate/core';
import type { Observable } from 'rxjs';
import { CrisisApiService, upsertEvent } from '../../core/api/crisis-api.service';
import { type ShownError, shownError } from '../../core/api/problem';
import type { CallOutcome, CrisisActionKind, CrisisEventDetail, CrisisHealth, Excerpt, OpenCrisisEvent, RevealedContact } from '../../core/api/types';
import { formatCountdown } from '../../core/i18n/format';
import { LanguageService } from '../../core/i18n/language.service';
import { CrisisHubService } from '../../core/realtime/crisis-hub.service';
import { LocalDatePipe } from '../../shared/local-date.pipe';
import { ProblemMessageComponent } from '../../shared/problem-message.component';
import { SessionService } from '../../core/session/session.service';
import {
  CallOutcomeDialogComponent,
  CloseDialogComponent,
  type CloseDialogData,
  HandoverDialogComponent,
  type HandoverDialogData,
  NoteDialogComponent,
  type NoteDialogData,
  StepUpDialogComponent,
  type StepUpDialogData,
} from './crisis-dialogs';

/** Below this the SLA countdown turns red and is announced once. */
export const URGENT_MS = 60_000;
export const HEALTH_POLL_MS = 30_000;

/** Shown sensitive data, bound to the event it belongs to. */
interface Shown<T> {
  eventId: string;
  value: T;
}

export function telHref(phone: string): string {
  return `tel:${phone.replace(/[^\d+]/g, '')}`;
}

/** Opens the phone app for a tel: link; tests replace it. */
export const TEL_OPENER = new InjectionToken<(href: string) => void>('TEL_OPENER', {
  providedIn: 'root',
  factory: () => (href) => window.location.assign(href),
});

/** S04 crisis console: open events with SLA countdowns, the selected event, step-up reveals and case actions. */
@Component({
  selector: 'tm-crisis-console',
  imports: [MatButtonModule, MatProgressBarModule, TranslateModule, LocalDatePipe, ProblemMessageComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './crisis-console.component.html',
  styleUrl: './crisis-console.component.scss',
})
export class CrisisConsoleComponent {
  private readonly api = inject(CrisisApiService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly translate = inject(TranslateService);
  private readonly destroyRef = inject(DestroyRef);
  protected readonly language = inject(LanguageService);
  protected readonly hub = inject(CrisisHubService);
  private readonly router = inject(Router);
  private readonly session = inject(SessionService);
  private readonly openTel = inject(TEL_OPENER);
  /** The event in the URL (/crisis/:eventId, the link in every page): kept selected even before it is in the list. */
  private readonly routeEventId = signal<string | null>(null);

  protected readonly events = signal<OpenCrisisEvent[]>([]);
  protected readonly loading = signal(true);
  protected readonly listError = signal<ShownError | null>(null);
  protected readonly selectedId = signal<string | null>(null);
  protected readonly detail = signal<CrisisEventDetail | null>(null);
  protected readonly detailError = signal<ShownError | null>(null);
  protected readonly health = signal<CrisisHealth | null>(null);
  protected readonly healthError = signal(false);
  protected readonly now = signal(Date.now());
  protected readonly excerpt = signal<Shown<Excerpt> | null>(null);
  protected readonly contact = signal<Shown<RevealedContact> | null>(null);
  protected readonly busy = signal(false);
  protected readonly actionError = signal<ShownError | null>(null);
  /** Polite live region: "one minute left" announcements. */
  protected readonly announcement = signal('');
  private readonly announced = new Set<string>();
  private started = false;

  protected readonly sortedEvents = computed(() =>
    [...this.events()].sort((a, b) => Number(a.status !== 'Open') - Number(b.status !== 'Open') || Date.parse(a.ackDeadlineUtc) - Date.parse(b.ackDeadlineUtc)),
  );
  protected readonly visibleExcerpt = computed(() => {
    const shown = this.excerpt();
    return shown && shown.eventId === this.selectedId() && Date.parse(shown.value.hideAt) > this.now() ? shown.value : null;
  });
  protected readonly visibleContact = computed(() => {
    const shown = this.contact();
    return shown && shown.eventId === this.selectedId() && Date.parse(shown.value.hideAt) > this.now() ? shown.value : null;
  });
  protected readonly healthParts = ['gate', 'classifier', 'comms'] as const;
  protected readonly telHref = telHref;

  constructor() {
    inject(ActivatedRoute)
      .paramMap.pipe(takeUntilDestroyed())
      .subscribe((params) => {
        const id = params.get('eventId');
        this.routeEventId.set(id);
        if (id && this.started) {
          this.select(id);
        } else if (id) {
          // The first load (below) fetches the selected event's detail.
          this.selectedId.set(id);
        }
      });
    this.hub.acquire(this.destroyRef);
    this.hub.alerts.pipe(takeUntilDestroyed()).subscribe((event) => this.applyEvent(event));
    this.hub.reconnected.pipe(takeUntilDestroyed()).subscribe(() => this.loadAll());

    // Clocks run outside the zone so the app can become stable; signal updates still refresh the view.
    const zone = inject(NgZone);
    const tick = zone.runOutsideAngular(() => setInterval(() => this.tick(), 1000));
    const poll = zone.runOutsideAngular(() => setInterval(() => zone.run(() => this.loadHealth()), HEALTH_POLL_MS));
    this.destroyRef.onDestroy(() => {
      clearInterval(tick);
      clearInterval(poll);
    });

    // Reload in the new language so server-localized labels, titles and the timeline switch too.
    effect(() => {
      this.language.language();
      untracked(() => this.loadAll());
    });
  }

  protected remainingMs(event: OpenCrisisEvent): number {
    return Date.parse(event.ackDeadlineUtc) - this.now();
  }

  protected countdown(event: OpenCrisisEvent): string {
    return formatCountdown(this.remainingMs(event));
  }

  protected urgent(event: OpenCrisisEvent): boolean {
    return event.status === 'Open' && this.remainingMs(event) < URGENT_MS;
  }

  protected hidesIn(hideAt: string): string {
    return formatCountdown(Date.parse(hideAt) - this.now());
  }

  /** A click in the list: the URL follows, so the event can be shared or reopened. */
  protected open(id: string): void {
    void this.router.navigate(['/crisis', id], { replaceUrl: true });
  }

  protected select(id: string): void {
    if (this.selectedId() === id) {
      return;
    }
    this.selectedId.set(id);
    this.detail.set(null);
    this.actionError.set(null);
    this.loadDetail(id);
  }

  protected acknowledge(): void {
    const id = this.selectedId();
    if (id) {
      this.run(this.api.acknowledge(id), (event) => {
        this.applyEvent(event, false);
        this.loadDetail(id);
        this.notify('crisis.done.acknowledged');
      });
    }
  }

  protected revealExcerpt(): void {
    const id = this.selectedId();
    if (!id) {
      return;
    }
    this.openStepUp<Excerpt>({
      titleKey: 'crisis.stepUp.excerptTitle',
      bodyKey: 'crisis.stepUp.excerptBody',
      confirmKey: 'crisis.stepUp.confirm',
      requireReason: false,
      submit: (code) => this.api.excerpt(id, code),
    }).subscribe((value) => value && this.excerpt.set({ eventId: id, value }));
  }

  protected revealContact(): void {
    const id = this.selectedId();
    if (!id) {
      return;
    }
    this.openStepUp<RevealedContact>({
      titleKey: 'crisis.stepUp.contactTitle',
      bodyKey: 'crisis.stepUp.contactBody',
      confirmKey: 'crisis.stepUp.confirm',
      requireReason: true,
      submit: (totpCode, reason) => this.api.revealContact(id, { totpCode, reason }),
    }).subscribe((value) => value && this.contact.set({ eventId: id, value }));
  }

  /** SB-39: logs the attempt, opens the phone app, then asks how the call went. */
  protected call(event: Event, phone: string): void {
    event.preventDefault();
    this.act('call_attempt', undefined, () => {
      this.openTel(telHref(phone));
      this.dialog
        .open<CallOutcomeDialogComponent, unknown, CallOutcome>(CallOutcomeDialogComponent, this.dialogConfig({}))
        .afterClosed()
        .subscribe((outcome) => {
          if (outcome) {
            this.act('call_outcome', undefined, () => outcome === 'answered' && this.markContacted(), outcome);
          }
        });
    });
  }

  /** SB-40: with a short note; the event moves to InContact. */
  protected markContacted(): void {
    this.askNote({ titleKey: 'crisis.contactedDialog.title', bodyKey: 'crisis.contactedDialog.body', labelKey: 'crisis.contactedDialog.label', confirmKey: 'crisis.contactedDialog.confirm', required: true, danger: false }, (text) =>
      this.act('contacted', text),
    );
  }

  /** SB-41: shows the rule-pack script and the number; without a location the note says why (EC-34). */
  protected localEmergency(): void {
    const d = this.detail();
    if (!d) {
      return;
    }
    this.askNote(
      {
        titleKey: 'crisis.emergencyDialog.title',
        bodyKey: d.locationConsent ? 'crisis.emergencyDialog.body' : 'crisis.emergencyDialog.noLocation',
        labelKey: d.locationConsent ? 'crisis.emergencyDialog.label' : 'crisis.emergencyDialog.noLocationLabel',
        confirmKey: 'crisis.emergencyDialog.confirm',
        required: true,
        danger: true,
        script: d.emergencyScript,
        emergencyNumber: d.emergencyNumber,
      },
      (text) => this.act('local_emergency', text),
    );
  }

  protected addNote(): void {
    this.askNote({ titleKey: 'crisis.noteDialog.title', bodyKey: null, labelKey: 'crisis.noteDialog.label', confirmKey: 'crisis.noteDialog.confirm', required: true, danger: false }, (text) => this.act('note', text));
  }

  /** SB-43: to a chosen colleague with a note; the case stays here until they accept. */
  protected handover(): void {
    const id = this.selectedId();
    if (!id) {
      return;
    }
    this.dialog
      .open<HandoverDialogComponent, HandoverDialogData, CrisisEventDetail>(
        HandoverDialogComponent,
        this.dialogConfig({ responders: this.api.responders(), submit: (request) => this.api.handover(id, request) }),
      )
      .afterClosed()
      .subscribe((detail) => {
        if (detail) {
          this.showDetail(id, detail);
          this.notify('crisis.done.handover_requested');
        }
      });
  }

  protected answerHandover(accept: boolean): void {
    const id = this.selectedId();
    if (id) {
      this.run(accept ? this.api.acceptHandover(id) : this.api.declineHandover(id), (detail) => {
        this.showDetail(id, detail);
        this.notify(accept ? 'crisis.done.handover_accepted' : 'crisis.done.handover_declined');
      });
    }
  }

  protected closeCase(): void {
    const id = this.selectedId();
    if (!id) {
      return;
    }
    this.dialog
      .open<CloseDialogComponent, CloseDialogData, OpenCrisisEvent>(
        CloseDialogComponent,
        this.dialogConfig({ submit: (request) => this.api.close(id, request), isLead: this.session.role() === 'lead' }),
      )
      .afterClosed()
      .subscribe((event) => this.afterClose(id, event));
  }

  /** Clinical lead: signs a supervisor's "unreachable" close (SB-44). */
  protected coSign(): void {
    const id = this.selectedId();
    if (id) {
      this.run(this.api.coSignClose(id), (event) => this.afterClose(id, event));
    }
  }

  private afterClose(id: string, event: OpenCrisisEvent | undefined): void {
    if (!event) {
      return;
    }
    if (event.status === 'Closed') {
      this.removeEvent(id);
      this.notify('crisis.closeDialog.closed');
    } else {
      this.loadDetail(id);
      this.notify('crisis.closeDialog.waitingForLead');
    }
  }

  private act(kind: CrisisActionKind, text?: string, then?: () => void, outcome?: CallOutcome): void {
    const id = this.selectedId();
    if (!id) {
      return;
    }
    this.run(this.api.action(id, { kind, ...(text ? { text } : {}), ...(outcome ? { outcome } : {}) }), (detail) => {
      this.showDetail(id, detail);
      if (then) {
        then();
      } else {
        this.notify(`crisis.done.${kind}`);
      }
    });
  }

  private showDetail(id: string, detail: CrisisEventDetail): void {
    if (this.selectedId() === id) {
      this.detail.set(detail);
    }
    this.applyEvent(detail, false);
  }

  private run<T>(request: Observable<T>, onSuccess: (value: T) => void): void {
    this.busy.set(true);
    this.actionError.set(null);
    request.subscribe({
      next: (value) => {
        this.busy.set(false);
        onSuccess(value);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(shownError(error, 'common.error'));
      },
    });
  }

  private loadAll(): void {
    this.started = true;
    this.loadEvents();
    this.loadHealth();
    const id = this.selectedId();
    if (id) {
      this.loadDetail(id);
    }
  }

  private loadEvents(): void {
    this.loading.set(true);
    this.api
      .openEvents()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (events) => {
          this.events.set(events.filter((e) => e.status !== 'Closed'));
          this.listError.set(null);
          this.loading.set(false);
          this.ensureSelection();
        },
        error: (error: unknown) => {
          this.listError.set(shownError(error, 'crisis.loadError'));
          this.loading.set(false);
        },
      });
  }

  private loadDetail(id: string): void {
    this.api
      .detail(id)
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (detail) => {
          if (this.selectedId() === id) {
            this.detail.set(detail);
            this.detailError.set(null);
          }
        },
        error: (error: unknown) => {
          if (this.selectedId() === id) {
            this.detailError.set(shownError(error, 'crisis.detailError'));
          }
        },
      });
  }

  private loadHealth(): void {
    this.api
      .health()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (health) => {
          this.health.set(health);
          this.healthError.set(false);
        },
        error: () => this.healthError.set(true),
      });
  }

  private applyEvent(event: OpenCrisisEvent, reloadSelected = true): void {
    const closed = event.status === 'Closed' || event.kind === 'closed';
    if (closed) {
      this.removeEvent(event.eventId);
      return;
    }
    this.events.update((events) => upsertEvent(events, event));
    if (reloadSelected && event.eventId === this.selectedId()) {
      this.loadDetail(event.eventId);
    }
    this.ensureSelection();
  }

  private removeEvent(id: string): void {
    this.events.update((events) => events.filter((e) => e.eventId !== id));
    if (this.routeEventId() === id) {
      this.routeEventId.set(null);
      void this.router.navigate(['/crisis'], { replaceUrl: true });
    }
    if (this.selectedId() === id) {
      this.selectedId.set(null);
      this.detail.set(null);
      this.ensureSelection();
    }
  }

  private ensureSelection(): void {
    const id = this.selectedId();
    const list = this.sortedEvents();
    // The event in the URL stays selected even when the list does not have it (yet).
    if (id && (list.some((e) => e.eventId === id) || id === this.routeEventId())) {
      return;
    }
    if (list.length) {
      this.selectedId.set(null);
      this.select(list[0].eventId);
    } else {
      this.selectedId.set(null);
      this.detail.set(null);
    }
  }

  private tick(): void {
    const now = Date.now();
    this.now.set(now);
    for (const event of this.events()) {
      const remaining = Date.parse(event.ackDeadlineUtc) - now;
      if (event.status === 'Open' && remaining > 0 && remaining <= URGENT_MS && !this.announced.has(event.eventId)) {
        this.announced.add(event.eventId);
        const name = event.number || event.pseudonymId;
        this.announcement.set(this.translate.instant('crisis.ack.minuteLeft', { event: name }));
      }
    }
    // Drop revealed data from memory once its time is up.
    const excerpt = this.excerpt();
    if (excerpt && Date.parse(excerpt.value.hideAt) <= now) {
      this.excerpt.set(null);
    }
    const contact = this.contact();
    if (contact && Date.parse(contact.value.hideAt) <= now) {
      this.contact.set(null);
    }
  }

  private openStepUp<T>(data: StepUpDialogData<T>): Observable<T | undefined> {
    return this.dialog.open<StepUpDialogComponent<T>, StepUpDialogData<T>, T>(StepUpDialogComponent<T>, this.dialogConfig(data)).afterClosed();
  }

  private askNote(data: NoteDialogData, then: (text: string) => void): void {
    this.dialog
      .open<NoteDialogComponent, NoteDialogData, string>(NoteDialogComponent, this.dialogConfig(data))
      .afterClosed()
      .subscribe((text) => text !== undefined && then(text));
  }

  private dialogConfig<D>(data: D): MatDialogConfig<D> {
    return { data, direction: this.language.direction(), width: '520px', maxWidth: '95vw', restoreFocus: true };
  }

  private notify(key: string): void {
    this.snackBar.open(this.translate.instant(key), undefined, { duration: 4000, direction: this.language.direction(), politeness: 'polite' });
  }
}
