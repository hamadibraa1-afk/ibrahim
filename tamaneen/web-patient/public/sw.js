/* Tamaneen service worker.
 *
 * - Precaches the crisis resources (P14) and offline (P35) pages in both languages, plus the static
 *   assets they reference, so emergency numbers are reachable without a connection.
 * - /_next/static/*: cache-first (file names are content-hashed).
 * - Navigations: network-first; offline, a cached copy of the page if there is one, else the offline page.
 * - Crisis resources: network-first with a short timeout, always answered from the cache when offline.
 * - /api/* and every non-GET request are never cached.
 *
 * The routing helpers below are plain functions so they can be unit-tested (tests/unit/pwa).
 */
'use strict';

const VERSION = 'v1';
const PRECACHE = `tamaneen-precache-${VERSION}`;
const RUNTIME = `tamaneen-runtime-${VERSION}`;
const LOCALES = ['ar', 'en'];
const DEFAULT_LOCALE = 'ar';
const CRISIS_PATH = '/crisis-resources';
const OFFLINE_PATH = '/offline';
/** Pages whose last successful response is kept for offline use (no personal data in their HTML; P66's list is read on the device). */
const OFFLINE_CAPABLE = [CRISIS_PATH, OFFLINE_PATH, '/safety-plan', '/outbox'];
const PRECACHE_PAGES = LOCALES.flatMap((l) => [`/${l}${CRISIS_PATH}`, `/${l}${OFFLINE_PATH}`]);
const STATIC_FILES = ['/manifest.webmanifest', '/icon.svg'];
const CRISIS_TIMEOUT_MS = 3000;

/** The locale prefix of a path, or the default locale. */
function localeOf(pathname) {
  const first = pathname.split('/')[1];
  return LOCALES.includes(first) ? first : DEFAULT_LOCALE;
}

/** The path without its locale prefix ("/ar/crisis-resources" → "/crisis-resources"). */
function unlocalized(pathname) {
  const parts = pathname.split('/');
  const rest = LOCALES.includes(parts[1]) ? `/${parts.slice(2).join('/')}` : pathname;
  return rest.length > 1 ? rest.replace(/\/+$/, '') : rest;
}

/**
 * Which strategy a request gets: 'bypass' (straight to the network, never cached), 'static'
 * (cache-first), 'crisis' (network with timeout, cache fallback) or 'navigate' (network-first,
 * offline fallback).
 */
function routeFor(request, origin) {
  if (request.method !== 'GET') return 'bypass';
  const url = new URL(request.url);
  if (url.origin !== origin) return 'bypass';
  if (url.pathname.startsWith('/api/') || url.pathname === '/api') return 'bypass';
  if (url.pathname.startsWith('/_next/static/') || STATIC_FILES.includes(url.pathname)) return 'static';
  if (request.mode === 'navigate') return unlocalized(url.pathname) === CRISIS_PATH ? 'crisis' : 'navigate';
  return 'bypass';
}

/** Whether the response to this navigation may be stored for offline use. */
function isOfflineCapable(pathname) {
  return OFFLINE_CAPABLE.includes(unlocalized(pathname));
}

/** The offline page for the locale of a path. */
function offlinePageFor(pathname) {
  return `/${localeOf(pathname)}${OFFLINE_PATH}`;
}

/** Same-origin /_next/static asset URLs referenced by an HTML page (scripts, styles, fonts). */
function extractStaticAssets(html) {
  const found = new Set();
  for (const match of html.matchAll(/\/_next\/static\/[^"'\s)\\]+/g)) found.add(match[0]);
  return [...found];
}

/** Only complete, same-request, successful responses are cached (no redirects, errors or opaque bodies). */
function cacheable(response) {
  return Boolean(response) && response.ok && response.status === 200 && response.type === 'basic' && !response.redirected;
}

function withTimeout(promise, ms) {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('timeout')), ms);
    promise.then(
      (value) => {
        clearTimeout(timer);
        resolve(value);
      },
      (error) => {
        clearTimeout(timer);
        reject(error);
      },
    );
  });
}

async function precache() {
  const cache = await caches.open(PRECACHE);
  const assets = new Set(STATIC_FILES);
  await Promise.all(
    PRECACHE_PAGES.map(async (page) => {
      // Without cookies, so the stored copy is the public version of the page.
      const response = await fetch(page, { credentials: 'omit', cache: 'reload' });
      if (!cacheable(response)) throw new Error(`precache failed: ${page}`);
      const html = await response.clone().text();
      extractStaticAssets(html).forEach((a) => assets.add(a));
      await cache.put(page, response);
    }),
  );
  await Promise.all(
    [...assets].map((asset) =>
      cache.add(asset).catch(() => {
        // A missing asset must not block the pages; cache-first fills it in later.
      }),
    ),
  );
}

async function fromCache(key) {
  return (await caches.match(key, { ignoreSearch: true })) || undefined;
}

async function staticFirst(request) {
  const hit = await caches.match(request);
  if (hit) return hit;
  const response = await fetch(request);
  if (cacheable(response)) {
    const cache = await caches.open(RUNTIME);
    await cache.put(request, response.clone());
  }
  return response;
}

async function remember(pathname, response) {
  if (!cacheable(response) || !isOfflineCapable(pathname)) return;
  const cache = await caches.open(PRECACHE_PAGES.includes(pathname) ? PRECACHE : RUNTIME);
  await cache.put(pathname, response.clone());
}

async function crisisPage(request) {
  const { pathname } = new URL(request.url);
  try {
    const response = await withTimeout(fetch(request), CRISIS_TIMEOUT_MS);
    await remember(pathname, response);
    return response;
  } catch {
    const cached = (await fromCache(pathname)) || (await fromCache(`/${localeOf(pathname)}${CRISIS_PATH}`)) || (await fromCache(`/${DEFAULT_LOCALE}${CRISIS_PATH}`));
    if (cached) return cached;
    return (await fromCache(offlinePageFor(pathname))) || Response.error();
  }
}

async function navigation(request) {
  const { pathname } = new URL(request.url);
  try {
    const response = await fetch(request);
    await remember(pathname, response);
    return response;
  } catch {
    if (isOfflineCapable(pathname)) {
      const cached = await fromCache(pathname);
      if (cached) return cached;
    }
    return (await fromCache(offlinePageFor(pathname))) || (await fromCache(`/${DEFAULT_LOCALE}${OFFLINE_PATH}`)) || Response.error();
  }
}

if (typeof self !== 'undefined' && typeof self.addEventListener === 'function') {
  self.addEventListener('install', (event) => {
    event.waitUntil(precache().then(() => self.skipWaiting()));
  });

  self.addEventListener('activate', (event) => {
    event.waitUntil(
      caches
        .keys()
        .then((keys) => Promise.all(keys.filter((k) => k.startsWith('tamaneen-') && k !== PRECACHE && k !== RUNTIME).map((k) => caches.delete(k))))
        .then(() => self.clients.claim()),
    );
  });

  self.addEventListener('fetch', (event) => {
    const route = routeFor(event.request, self.location.origin);
    if (route === 'static') event.respondWith(staticFirst(event.request));
    else if (route === 'crisis') event.respondWith(crisisPage(event.request));
    else if (route === 'navigate') event.respondWith(navigation(event.request));
  });
}
