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

/**
 * "My consents" (P11). Required consents show as on and locked. Each change is saved at once; withdrawing
 * AI assistance first explains which features stop.
 */
export function ConsentsSection({ initial }: { initial: Consent[] }) {
  const t = useTranslations('privacy');
  const te = useTranslations('errors');
  const [consents, setConsents] = useState(initial);
  const [pending, setPending] = useState<ConsentKey | null>(null);
  const [confirmAi, setConfirmAi] = useState(false);
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
    }
  }

  function change(consent: Consent, granted: boolean) {
    if (consent.key === 'ai' && !granted) {
      setConfirmAi(true);
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
          const locked = consent.required || (consent.granted && !consent.withdrawable);
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
                checked={consent.required ? true : consent.granted}
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
        </div>
      </Dialog>
    </section>
  );
}
