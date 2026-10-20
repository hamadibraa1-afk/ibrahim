import { api } from '@/lib/api/client';
import type { Me } from '@/lib/api/types';
import { landingPath } from '@/lib/onboarding';

/** After the BFF has set the session cookies: where this patient should land. */
export async function signedInPath(): Promise<string> {
  const me = await api<Me>('me');
  return landingPath(me);
}
