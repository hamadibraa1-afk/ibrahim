'use client';

import { useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { Button } from '@/components/ui/Button';
import { requestHelp } from '@/components/crisis/helpNow';

/**
 * «أحتاج أتكلم مع أحد الآن» on P63 and P67: the same request as Help now (POST /v1/crisis/help, a Critical event),
 * then the crisis screen. Offline or when sending fails, the request stays queued (the header delivers it later)
 * and the public emergency numbers open at once.
 */
export function TalkNowButton({ label, variant = 'secondary' }: { label: string; variant?: 'primary' | 'secondary' | 'danger-outline' }) {
  const router = useRouter();
  const [busy, setBusy] = useState(false);

  async function press() {
    setBusy(true);
    const result = await requestHelp();
    router.push(result.status === 'sent' ? '/crisis' : '/crisis-resources');
  }

  return (
    <Button variant={variant} full busy={busy} onClick={() => void press()} data-testid="talk-now">
      {label}
    </Button>
  );
}
