import { DestroyRef, Injectable, InjectionToken, OnDestroy, effect, inject, signal, untracked } from '@angular/core';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';
import { CRISIS_BASE_URL } from '../api';
import type { OpenCrisisEvent } from '../api/types';
import { AuthService } from '../auth/auth.service';
import type { Language } from '../i18n/language';
import { LanguageService } from '../i18n/language.service';

/** The part of a SignalR HubConnection the portal uses; tests provide a fake. */
export interface CrisisHubConnection {
  on(method: string, handler: (...args: unknown[]) => void): void;
  start(): Promise<void>;
  stop(): Promise<void>;
  onclose(callback: (error?: Error) => void): void;
  onreconnecting(callback: (error?: Error) => void): void;
  onreconnected(callback: (connectionId?: string) => void): void;
}

export type CrisisHubConnectionFactory = (url: string, accessTokenFactory: () => Promise<string>) => CrisisHubConnection;

export const CRISIS_HUB_CONNECTION_FACTORY = new InjectionToken<CrisisHubConnectionFactory>('CRISIS_HUB_CONNECTION_FACTORY', {
  providedIn: 'root',
  factory: () => (url, accessTokenFactory) =>
    new HubConnectionBuilder().withUrl(url, { accessTokenFactory }).withAutomaticReconnect().configureLogging(LogLevel.Warning).build(),
});

export type HubStatus = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/** Wait before trying again when the first connection attempt fails (automatic reconnect covers later drops). */
export const HUB_RETRY_MS = 10_000;

/**
 * Live crisis alerts from Tamaneen.Crisis.Worker (/hubs/crisis, "crisisAlert"). Screens acquire the
 * connection while they are shown; it is opened in the portal language so pushed titles and labels are
 * localized, and reopened when the language changes.
 */
@Injectable({ providedIn: 'root' })
export class CrisisHubService implements OnDestroy {
  private readonly factory = inject(CRISIS_HUB_CONNECTION_FACTORY);
  private readonly baseUrl = inject(CRISIS_BASE_URL);
  private readonly auth = inject(AuthService);
  private readonly language = inject(LanguageService);

  private readonly alertSubject = new Subject<OpenCrisisEvent>();
  private readonly reconnectedSubject = new Subject<void>();
  private readonly state = signal<HubStatus>('disconnected');
  private connection: CrisisHubConnection | null = null;
  private connectedLanguage: Language | null = null;
  private users = 0;
  private retryTimer: ReturnType<typeof setTimeout> | undefined;
  private generation = 0;

  /** Every pushed alert or update (opened, escalated, acknowledged, closed…). */
  readonly alerts: Observable<OpenCrisisEvent> = this.alertSubject.asObservable();
  /** Fires after the connection came back, so screens can reload what they may have missed. */
  readonly reconnected: Observable<void> = this.reconnectedSubject.asObservable();
  readonly status = this.state.asReadonly();

  constructor() {
    effect(() => {
      const language = this.language.language();
      untracked(() => {
        if (this.users > 0 && language !== this.connectedLanguage) {
          void this.open(language);
        }
      });
    });
  }

  /** Keeps the connection open until the returned release function is called (or the caller's DestroyRef fires). */
  acquire(destroyRef?: DestroyRef): () => void {
    this.users++;
    if (this.users === 1) {
      void this.open(this.language.language());
    }
    let released = false;
    const release = () => {
      if (released) {
        return;
      }
      released = true;
      this.users--;
      if (this.users === 0) {
        void this.close();
      }
    };
    destroyRef?.onDestroy(release);
    return release;
  }

  ngOnDestroy(): void {
    this.users = 0;
    void this.close();
  }

  private async open(language: Language): Promise<void> {
    const generation = ++this.generation;
    await this.close();
    if (this.users === 0 || generation !== this.generation) {
      return;
    }
    const url = `${this.baseUrl}/hubs/crisis?lang=${encodeURIComponent(language)}`;
    const connection = this.factory(url, () => this.auth.validAccessToken());
    connection.on('crisisAlert', (message) => this.alertSubject.next(message as OpenCrisisEvent));
    connection.onreconnecting(() => this.isCurrent(connection) && this.state.set('reconnecting'));
    connection.onreconnected(() => {
      if (this.isCurrent(connection)) {
        this.state.set('connected');
        this.reconnectedSubject.next();
      }
    });
    connection.onclose(() => {
      if (this.isCurrent(connection)) {
        this.state.set('disconnected');
        this.scheduleRetry();
      }
    });
    this.connection = connection;
    this.connectedLanguage = language;
    this.state.set('connecting');
    try {
      await connection.start();
      if (this.isCurrent(connection)) {
        this.state.set('connected');
      }
    } catch {
      if (this.isCurrent(connection)) {
        this.state.set('disconnected');
        this.scheduleRetry();
      }
    }
  }

  private scheduleRetry(): void {
    clearTimeout(this.retryTimer);
    if (this.users === 0) {
      return;
    }
    this.retryTimer = setTimeout(() => {
      if (this.users > 0 && this.state() === 'disconnected') {
        void this.open(this.language.language()).then(() => this.state() === 'connected' && this.reconnectedSubject.next());
      }
    }, HUB_RETRY_MS);
  }

  private async close(): Promise<void> {
    clearTimeout(this.retryTimer);
    const connection = this.connection;
    this.connection = null;
    this.connectedLanguage = null;
    this.state.set('disconnected');
    if (connection) {
      try {
        await connection.stop();
      } catch {
        // Already closed.
      }
    }
  }

  private isCurrent(connection: CrisisHubConnection): boolean {
    return this.connection === connection;
  }
}
