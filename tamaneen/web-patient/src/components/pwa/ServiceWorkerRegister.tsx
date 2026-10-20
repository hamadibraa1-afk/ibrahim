'use client';

import { useEffect } from 'react';

/**
 * Registers /sw.js in production builds, which keeps the crisis resources (P14) and the offline page
 * (P35) available without a connection. Development builds skip it so hot reload is never cached.
 */
export function ServiceWorkerRegister() {
  useEffect(() => {
    if (process.env.NODE_ENV !== 'production' || !('serviceWorker' in navigator)) return;
    const register = () => {
      navigator.serviceWorker.register('/sw.js', { scope: '/' }).catch(() => {
        // Registration is an enhancement; the app works without it.
      });
    };
    if (document.readyState === 'complete') {
      register();
      return;
    }
    window.addEventListener('load', register, { once: true });
    return () => window.removeEventListener('load', register);
  }, []);
  return null;
}
