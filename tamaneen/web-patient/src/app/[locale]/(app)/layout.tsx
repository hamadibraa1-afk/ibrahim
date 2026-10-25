import type { ReactNode } from 'react';
import { CrisisLockGuard } from '@/components/crisis/CrisisLockGuard';
import { HelpNowSessionMarker } from '@/components/crisis/helpNowSession';
import { QuickExit } from '@/components/settings/QuickExit';
import { getMe } from '@/lib/api/server';
import { loadSafetyNumbers } from '@/lib/platform-config';

// Everything in (app) needs a session: getMe() redirects to the session-expired page when there is none.
// While a crisis event holds the chat, CrisisLockGuard keeps the patient on the crisis screen.
// HelpNowSessionMarker turns the header's Help now into the signed-in button (PB-26) with these numbers.
export const dynamic = 'force-dynamic';

export default async function SignedInLayout({ children }: { children: ReactNode }) {
  const [me, numbers] = await Promise.all([getMe(), loadSafetyNumbers()]);
  return (
    <>
      <CrisisLockGuard locked={me.chatLocked} />
      <HelpNowSessionMarker emergencyNumber={numbers.emergencyNumber} supportLine={numbers.supportLine} />
      {children}
      <QuickExit />
    </>
  );
}
