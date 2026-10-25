'use client';

import { useEffect } from 'react';
import { rememberReturnPath } from '@/lib/returnPath';

/** Keeps the page the session expired on, so signing in again (P18, P19) returns there. Renders nothing. */
export function RememberReturnPath({ path }: { path: string | null }) {
  useEffect(() => {
    rememberReturnPath(path);
  }, [path]);
  return null;
}
