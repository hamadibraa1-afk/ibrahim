'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { Dialog } from '@/components/booking/Dialog';
import { BreathingPacer } from '@/components/safety/BreathingPacer';
import { Button } from '@/components/ui/Button';

/**
 * P46 / P29 empty homework: «تمرين تنفس الآن», the useful alternative the design asks for. It opens the breathing
 * pacer in a dialog (as P54 does), since the library page for exercises is not built yet.
 */
export function BreathingNowButton() {
  const t = useTranslations('homework');
  const [open, setOpen] = useState(false);
  return (
    <>
      <Button variant="secondary" onClick={() => setOpen(true)} data-testid="breathing-now">
        {t('breathingNow')}
      </Button>
      <Dialog open={open} onClose={() => setOpen(false)} title={t('breathingTitle')}>
        <BreathingPacer />
        <Button variant="ghost" full onClick={() => setOpen(false)}>
          {t('breathingClose')}
        </Button>
      </Dialog>
    </>
  );
}
