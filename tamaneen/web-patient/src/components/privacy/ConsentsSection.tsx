'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { api } from '@/lib/api/client';
import type { Consent, ConsentKey, UpdateConsentsRequest } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Tag } from '@/components/ui/Chip';
import { Notice } from '@/components/ui/Notice';
import { Switch } from '@/components/ui/Switch';
import { Dialog, DialogCancel } from '@/components/settings/Dialog';
import { problemMessage } from '@/components/settings/problem';

/** MyData listens for this to open its delete dialog: the AI dialog offers deletion instead (PB-56). */
export const OPEN_DELETE_EVENT = 'tamaneen:open-delete-account';

/**
 * "My consents" (P11). Consents that cannot be withdrawn (platform, sharing) show as on and locked; the rest are
 * switches saved at once. Withdrawing AI assistance (required but withdrawable) first explains which features stop
 * and offers the delete path; withdrawing the emergency location asks for confirmation.
 */
export function ConsentsSection({ initial }: { initial: Consent[] }) {
  const t = useTranslations('privacy');
  const te = useTranslations('errors');
  const [consents, setConsents] = useState(initial);
  const [pending, setPending] = useState<ConsentKey | null>(null);
  const [confirmAi, setConfirmAi] = useState(false);
  const [confirmLocation, setConfirmLocation] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  async function save(key: ConsentKey, granted: boolean) {
    setPending(key);
    setError(null);
    setSaved(false);
    try {
      const body: UpdateConsentsRequest = { items: [{ key, granted }] };
      const result = await api<Consent[] | undefined>('me/consents', { method: 'PUT', body });
      setConsents(result ?? consents.map((c) => (c.key === key ? { ...c, granted } : c)));
      setSaved(true);
    } catch (e) {
      setError(problemMessage(e, te('network')));
    } finally {
      setPending(null);
      setConfirmAi(false);
      setConfirmLocation(false);
    }
  }

  function change(consent: Consent, granted: boolean) {
    if (consent.key === 'ai' && !granted) {
      setConfirmAi(true);
      return;
    }
    if (consent.key === 'emergency_location' && !granted) {
      setConfirmLocation(true);
      return;
    }
    void save(consent.key, granted);
  }

  return (
    <section aria-labelledby="privacy-consents" className="flex flex-col gap-2">
      <h2 id="privacy-consents" className="text-lg font-bold">
        {t('consentsTitle')}
      </h2>
      <ul className="divide-y divide-line rounded-card border border-line bg-surface px-4 py-1">
        {consents.map((consent) => {
          const locked = consent.granted && !consent.withdrawable;
          return (
            <li key={consent.key} data-testid={`consent-${consent.key}`}>
              <Switch
                label={
                  <span className="flex flex-wrap items-center gap-2">
                    {t(`consents.${consent.key}`)}
                    {consent.required ? <Tag tone="muted">{t('required')}</Tag> : null}
                  </span>
                }
                description={consent.required ? t('requiredHint') : undefined}
                checked={consent.granted}
                disabled={locked || pending !== null}
                onChange={(next) => change(consent, next)}
              />
            </li>
          );
        })}
      </ul>
      <div aria-live="polite" className="empty:hidden">
        {saved ? <p className="text-sm font-semibold text-primary">{t('consentSaved')}</p> : null}
      </div>
      {error ? <Notice tone="danger">{error}</Notice> : null}

      <Dialog open={confirmAi} onClose={() => setConfirmAi(false)} title={t('aiDialogTitle')}>
        <p className="text-sm leading-relaxed text-muted">{t('aiDialogBody')}</p>
        <div className="flex flex-col gap-2">
          <Button variant="danger" full busy={pending === 'ai'} onClick={() => save('ai', false)}>
            {t('aiDialogConfirm')}
          </Button>
          <DialogCancel onClick={() => setConfirmAi(false)} />
          <Button
            variant="ghost"
            full
            onClick={() => {
              setConfirmAi(false);
              window.dispatchEvent(new Event(OPEN_DELETE_EVENT));
            }}
          >
            {t('aiDialogDelete')}
          </Button>
        </div>
      </Dialog>

      <Dialog open={confirmLocation} onClose={() => setConfirmLocation(false)} title={t('locationDialogTitle')}>
        <p className="text-sm leading-relaxed text-muted">{t('locationDialogBody')}</p>
        <div className="flex flex-col gap-2">
          <Button variant="danger" full busy={pending === 'emergency_location'} onClick={() => save('emergency_location', false)}>
            {t('locationDialogConfirm')}
          </Button>
          <DialogCancel onClick={() => setConfirmLocation(false)} />
        </div>
      </Dialog>
    </section>
  );
}
