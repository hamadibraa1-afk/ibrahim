import type { ReactNode } from 'react';
import { CrisisLockGuard } from '@/components/crisis/CrisisLockGuard';
import { QuickExit } from '@/components/settings/QuickExit';
import { getMe } from '@/lib/api/server';

// Everything in (app) needs a session: getMe() redirects to the session-expired page when there is none.
// While a crisis event holds the chat, CrisisLockGuard keeps the patient on the crisis screen.
export const dynamic = 'force-dynamic';

export default async function SignedInLayout({ children }: { children: ReactNode }) {
  const me = await getMe();
  return (
    <>
      <CrisisLockGuard locked={me.chatLocked} />
      {children}
      <QuickExit />
    </>
  );
}
