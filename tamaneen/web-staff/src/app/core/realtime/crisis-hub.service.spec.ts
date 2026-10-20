import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { CRISIS, openEvent, settle } from '../../testing/fixtures';
import { provideTestTranslations } from '../../testing/translate-testing';
import { CRISIS_BASE_URL } from '../api';
import type { OpenCrisisEvent } from '../api/types';
import { AuthService } from '../auth/auth.service';
import { LanguageService } from '../i18n/language.service';
import { CRISIS_HUB_CONNECTION_FACTORY, type CrisisHubConnection, CrisisHubService, HUB_RETRY_MS } from './crisis-hub.service';

class FakeConnection implements CrisisHubConnection {
  readonly handlers = new Map<string, (...args: unknown[]) => void>();
  started = false;
  stopped = false;
  failStart = false;
  closeCallback?: (error?: Error) => void;
  reconnectingCallback?: (error?: Error) => void;
  reconnectedCallback?: (id?: string) => void;

  constructor(
    readonly url: string,
    readonly accessTokenFactory: () => Promise<string>,
  ) {}

  on(method: string, handler: (...args: unknown[]) => void): void {
    this.handlers.set(method, handler);
  }
  start(): Promise<void> {
    if (this.failStart) {
      return Promise.reject(new Error('offline'));
    }
    this.started = true;
    return Promise.resolve();
  }
  stop(): Promise<void> {
    this.stopped = true;
    return Promise.resolve();
  }
  onclose(callback: (error?: Error) => void): void {
    this.closeCallback = callback;
  }
  onreconnecting(callback: (error?: Error) => void): void {
    this.reconnectingCallback = callback;
  }
  onreconnected(callback: (id?: string) => void): void {
    this.reconnectedCallback = callback;
  }
  push(method: string, payload: unknown): void {
    this.handlers.get(method)?.(payload);
  }
}

describe('CrisisHubService', () => {
  let connections: FakeConnection[];

  async function setup() {
    connections = [];
    TestBed.configureTestingModule({
      providers: [
        provideTestTranslations(),
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: CRISIS_BASE_URL, useValue: CRISIS },
        {
          provide: CRISIS_HUB_CONNECTION_FACTORY,
          useValue: (url: string, factory: () => Promise<string>) => {
            const connection = new FakeConnection(url, factory);
            connections.push(connection);
            return connection;
          },
        },
      ],
    });
    const language = TestBed.inject(LanguageService);
    await language.use('ar');
    const hub = TestBed.inject(CrisisHubService);
    TestBed.tick();
    return { hub, language };
  }

  afterEach(() => localStorage.clear());

  it('connects in the portal language when a screen needs it, and passes the access token', async () => {
    const { hub } = await setup();
    expect(connections.length).toBe(0);
    const auth = TestBed.inject(AuthService);
    spyOn(auth, 'validAccessToken').and.resolveTo('access-1');

    hub.acquire();
    await settle();
    expect(connections.length).toBe(1);
    expect(connections[0].url).toBe(`${CRISIS}/hubs/crisis?lang=ar`);
    expect(connections[0].started).toBeTrue();
    expect(hub.status()).toBe('connected');
    expect(await connections[0].accessTokenFactory()).toBe('access-1');
  });

  it('emits every crisisAlert pushed by the worker', async () => {
    const { hub } = await setup();
    const received: OpenCrisisEvent[] = [];
    hub.alerts.subscribe((e) => received.push(e));
    hub.acquire();
    await settle();
    connections[0].push('crisisAlert', openEvent({ eventId: 'ev-9', kind: 'escalated' }));
    expect(received.map((e) => e.eventId)).toEqual(['ev-9']);
  });

  it('reconnects with the new language after a switch', async () => {
    const { hub, language } = await setup();
    hub.acquire();
    await settle();
    await language.use('en');
    TestBed.tick();
    await settle();
    expect(connections.length).toBe(2);
    expect(connections[0].stopped).toBeTrue();
    expect(connections[1].url).toBe(`${CRISIS}/hubs/crisis?lang=en`);
    expect(connections[1].started).toBeTrue();
  });

  it('keeps one connection for several screens and closes it when the last one leaves', async () => {
    const { hub } = await setup();
    const releaseA = hub.acquire();
    const releaseB = hub.acquire();
    await settle();
    expect(connections.length).toBe(1);
    releaseA();
    releaseA();
    await settle();
    expect(connections[0].stopped).toBeFalse();
    releaseB();
    await settle();
    expect(connections[0].stopped).toBeTrue();
    expect(hub.status()).toBe('disconnected');
  });

  it('reports reconnecting and tells screens to reload after a reconnect', async () => {
    const { hub } = await setup();
    let reloads = 0;
    hub.reconnected.subscribe(() => reloads++);
    hub.acquire();
    await settle();
    connections[0].reconnectingCallback?.();
    expect(hub.status()).toBe('reconnecting');
    connections[0].reconnectedCallback?.('id');
    expect(hub.status()).toBe('connected');
    expect(reloads).toBe(1);
  });

  it('stays usable when the worker cannot be reached, and retries later', async () => {
    const { hub } = await setup();
    spyOn(FakeConnection.prototype, 'start').and.rejectWith(new Error('offline'));
    jasmine.clock().install();
    try {
      const release = hub.acquire();
      await Promise.resolve();
      await Promise.resolve();
      await Promise.resolve();
      expect(hub.status()).toBe('disconnected');
      expect(connections.length).toBe(1);
      jasmine.clock().tick(HUB_RETRY_MS);
      await Promise.resolve();
      await Promise.resolve();
      expect(connections.length).toBe(2);
      release();
    } finally {
      jasmine.clock().uninstall();
    }
  });
});
