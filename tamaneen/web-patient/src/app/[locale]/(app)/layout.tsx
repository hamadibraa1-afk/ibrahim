import type { ReactNode } from 'react';
import { QuickExit } from '@/components/settings/QuickExit';
import { getMe } from '@/lib/api/server';

// Everything in (app) needs a session. getMe() redirects to the session-expired page when there is none,
// and to the crisis screen while the chat is locked.
export const dynamic = 'force-dynamic';

export default async function SignedInLayout({ children }: { children: ReactNode }) {
  await getMe();
  return (
    <>
      {children}
      <QuickExit />
    </>
  );
}
