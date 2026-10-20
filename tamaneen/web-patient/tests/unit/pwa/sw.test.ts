import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { runInNewContext } from 'node:vm';

const source = readFileSync(join(__dirname, '../../../public/sw.js'), 'utf8');
const ORIGIN = 'https://app.tamaneen.example';

type Req = { url: string; method: string; mode?: string };
interface SwHelpers {
  routeFor(request: Req, origin: string): string;
  localeOf(pathname: string): string;
  offlinePageFor(pathname: string): string;
  isOfflineCapable(pathname: string): boolean;
  extractStaticAssets(html: string): string[];
}

/** Loads sw.js without a service-worker global, exposing its pure helpers. */
function helpers(): SwHelpers {
  const context: Record<string, unknown> = { URL, setTimeout, clearTimeout };
  runInNewContext(source, context);
  return context as unknown as SwHelpers;
}

/** Loads sw.js as a service worker with an in-memory cache and a scripted network. */
function worker(network: (url: string) => Promise<Response>) {
  const stores = new Map<string, Map<string, Response>>();
  const keyOf = (r: string | Request | Req) => new URL(typeof r === 'string' ? r : r.url, ORIGIN).pathname;
  const open = async (name: string) => {
    if (!stores.has(name)) stores.set(name, new Map());
    const store = stores.get(name)!;
    return {
      put: async (r: string | Req, response: Response) => void store.set(keyOf(r), response),
      add: async (r: string) => void store.set(keyOf(r), await network(r)),
    };
  };
  const caches = {
    open,
    keys: async () => [...stores.keys()],
    delete: async (n: string) => stores.delete(n),
    match: async (r: string | Req) => {
      for (const store of stores.values()) {
        const hit = store.get(keyOf(r));
        if (hit) return hit.clone();
      }
      return undefined;
    },
  };
  const listeners: Record<string, (event: unknown) => void> = {};
  const self = {
    location: { origin: ORIGIN },
    addEventListener: (type: string, fn: (event: unknown) => void) => (listeners[type] = fn),
    skipWaiting: async () => undefined,
    clients: { claim: async () => undefined },
  };
  const fetch = (input: string | Req) => network(typeof input === 'string' ? input : input.url);
  runInNewContext(source, { self, caches, fetch, URL, Response, setTimeout, clearTimeout, Promise });

  return {
    stores,
    async install() {
      let done: Promise<unknown> = Promise.resolve();
      listeners.install!({ waitUntil: (p: Promise<unknown>) => (done = p) });
      await done;
    },
    async request(path: string, mode = 'navigate', method = 'GET'): Promise<Response | null> {
      let answer: Promise<Response> | null = null;
      listeners.fetch!({ request: { url: `${ORIGIN}${path}`, method, mode }, respondWith: (p: Promise<Response>) => (answer = p) });
      return answer ? await answer : null;
    },
  };
}

/** Responses built in tests have type "default"; a same-origin fetch has type "basic". */
function basic(body: string, init: ResponseInit = {}): Response {
  const response = new Response(body, { status: 200, headers: { 'content-type': 'text/html' }, ...init });
  Object.defineProperty(response, 'type', { value: 'basic' });
  return response;
}

describe('service worker routing', () => {
  const sw = helpers();
  const req = (path: string, mode = 'no-cors', method = 'GET', origin = ORIGIN) => ({ url: `${origin}${path}`, method, mode });

  it('never handles the API, other origins or non-GET requests', () => {
    expect(sw.routeFor(req('/api/v1/intake', 'cors'), ORIGIN)).toBe('bypass');
    expect(sw.routeFor(req('/api/auth/login', 'navigate'), ORIGIN)).toBe('bypass');
    expect(sw.routeFor(req('/ar/intake', 'navigate', 'POST'), ORIGIN)).toBe('bypass');
    expect(sw.routeFor(req('/x.js', 'no-cors', 'GET', 'https://cdn.example'), ORIGIN)).toBe('bypass');
  });

  it('serves hashed static assets cache-first and routes navigations', () => {
    expect(sw.routeFor(req('/_next/static/chunks/app-1a2b.js'), ORIGIN)).toBe('static');
    expect(sw.routeFor(req('/icon.svg'), ORIGIN)).toBe('static');
    expect(sw.routeFor(req('/ar/crisis-resources', 'navigate'), ORIGIN)).toBe('crisis');
    expect(sw.routeFor(req('/en/crisis-resources/', 'navigate'), ORIGIN)).toBe('crisis');
    expect(sw.routeFor(req('/en/home', 'navigate'), ORIGIN)).toBe('navigate');
  });

  it('picks the offline page and offline-capable pages by locale', () => {
    expect(sw.offlinePageFor('/en/appointments/12')).toBe('/en/offline');
    expect(sw.offlinePageFor('/somewhere')).toBe('/ar/offline');
    expect(sw.isOfflineCapable('/ar/safety-plan')).toBe(true);
    expect(sw.isOfflineCapable('/ar/intake')).toBe(false);
  });

  it('finds the static assets a page references', () => {
    const html = '<script src="/_next/static/chunks/main-1.js"></script><link href="/_next/static/css/a.css" rel="stylesheet"><link rel="preload" href="/_next/static/media/font.woff2"><script src="/_next/static/chunks/main-1.js"></script>';
    expect(sw.extractStaticAssets(html).sort()).toEqual(['/_next/static/chunks/main-1.js', '/_next/static/css/a.css', '/_next/static/media/font.woff2']);
  });
});

describe('service worker offline behaviour', () => {
  let online = true;
  const network = async (url: string) => {
    if (!online) throw new TypeError('Failed to fetch');
    const path = new URL(url, ORIGIN).pathname;
    if (path.endsWith('/crisis-resources')) return basic(`<p>crisis ${path}</p><script src="/_next/static/chunks/c.js"></script>`);
    if (path.endsWith('/offline')) return basic(`<p>offline ${path}</p>`);
    return basic(`asset ${path}`);
  };

  beforeEach(() => {
    online = true;
  });

  it('precaches crisis resources and offline pages in both languages with their assets', async () => {
    const sw = worker(network);
    await sw.install();
    const keys = [...[...sw.stores.values()][0]!.keys()].sort();
    expect(keys).toEqual(['/_next/static/chunks/c.js', '/ar/crisis-resources', '/ar/offline', '/en/crisis-resources', '/en/offline', '/icon.svg', '/manifest.webmanifest']);
  });

  it('serves crisis resources from the cache and other pages with the offline page when offline', async () => {
    const sw = worker(network);
    await sw.install();
    online = false;
    expect(await (await sw.request('/en/crisis-resources'))!.text()).toContain('crisis /en/crisis-resources');
    expect(await (await sw.request('/en/appointments'))!.text()).toContain('offline /en/offline');
    expect(await (await sw.request('/ar/home'))!.text()).toContain('offline /ar/offline');
    expect(await sw.request('/api/v1/home', 'cors')).toBeNull();
  });

  it('does not cache API responses or pages with personal data', async () => {
    const sw = worker(network);
    await sw.install();
    await sw.request('/ar/intake');
    await sw.request('/api/v1/me', 'cors');
    const all = [...sw.stores.values()].flatMap((s) => [...s.keys()]);
    expect(all.some((k) => k.startsWith('/api/') || k === '/ar/intake')).toBe(false);
  });
});
