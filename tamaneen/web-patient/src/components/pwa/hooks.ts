'use client';

import { useSyncExternalStore } from 'react';

function subscribeOnline(callback: () => void): () => void {
  window.addEventListener('online', callback);
  window.addEventListener('offline', callback);
  return () => {
    window.removeEventListener('online', callback);
    window.removeEventListener('offline', callback);
  };
}

/** navigator.onLine as React state. The server render assumes online. */
export function useOnline(): boolean {
  return useSyncExternalStore(
    subscribeOnline,
    () => navigator.onLine,
    () => true,
  );
}

const REDUCED = '(prefers-reduced-motion: reduce)';

function subscribeMotion(callback: () => void): () => void {
  if (typeof window.matchMedia !== 'function') return () => {};
  const query = window.matchMedia(REDUCED);
  query.addEventListener('change', callback);
  return () => query.removeEventListener('change', callback);
}

/** True when the person asked the system for less motion. The server render assumes reduced motion (the safe default). */
export function useReducedMotion(): boolean {
  return useSyncExternalStore(
    subscribeMotion,
    () => typeof window.matchMedia === 'function' && window.matchMedia(REDUCED).matches,
    () => true,
  );
}
