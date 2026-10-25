'use client';

import { useTranslations } from 'next-intl';
import { useId, useState } from 'react';
import { Link, useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { Consent, ConsentKey, UpdateConsentsRequest } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Tag } from '@/components/ui/Chip';
import { Notice } from '@/components/ui/Notice';
import { problemText } from '@/components/auth/errorText';
import { CheckboxRow } from './CheckboxRow';

/** Message keys are camelCase; the API keys are snake_case. */
export const CONSENT_MESSAGE_KEY: Record<ConsentKey, string> = {
  platform: 'platform',
  ai: 'ai',
  share_with_therapist: 'shareWithTherapist',
  research: 'research',
  emergency_location: 'emergencyLocation',
};

/**
 * P03 Consents, step 3 of 4. Required items must be on; optional ones are the patient's choice. A required
 * item can only be ticked once its full text has been opened (PB-15), unless it was already granted.
 */
export function ConsentsForm({ consents }: { consents: Consent[] }) {
  const t = useTranslations('consents');
  const te = useTranslations('errors');
  const router = useRouter();
  const baseId = useId();
  const hintId = `${baseId}-hint`;
  const [granted, setGranted] = useState<Record<string, boolean>>(() => Object.fromEntries(consents.map((c) => [c.key, c.granted])));
  const [open, setOpen] = useState<Record<string, boolean>>({});
  const [read, setRead] = useState<Record<string, boolean>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const items = consents.filter((c) => c.key in CONSENT_MESSAGE_KEY);
  const missingRequired = items.some((c) => c.required && !granted[c.key]);

  async function save() {
    if (missingRequired) return;
    setBusy(true);
    setError(null);
    const body: UpdateConsentsRequest = { items: items.map((c) => ({ key: c.key, granted: Boolean(granted[c.key]) })) };
    try {
      await api<Consent[]>('me/consents', { method: 'PUT', body });
      router.push('/start/safety-info');
    } catch (e) {
      setError(problemText(e, te('network')));
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-5">
      <ul className="flex flex-col divide-y divide-line overflow-hidden rounded-card border border-line bg-surface">
        {items.map((c) => {
          const key = CONSENT_MESSAGE_KEY[c.key];
          const panelId = `${baseId}-${key}-full`;
          const mustReadId = `${baseId}-${key}-must-read`;
          const locked = c.required && !read[c.key] && !granted[c.key] && !c.granted;
          return (
            <li key={c.key} className="flex flex-col gap-2 p-4" data-testid={`consent-${c.key}`}>
              <CheckboxRow
                checked={Boolean(granted[c.key])}
                onChange={(next) => setGranted((g) => ({ ...g, [c.key]: next }))}
                label={t(`items.${key}.title`)}
                trailing={<Tag tone={c.required ? 'teal' : 'muted'}>{c.required ? t('required') : t('optional')}</Tag>}
                description={t(`items.${key}.body`)}
                disabled={locked}
                describedBy={locked ? mustReadId : undefined}
              />
              {locked ? (
                <p id={mustReadId} className="ps-9 text-xs text-muted">
                  {t('readFirst')}
                </p>
              ) : null}
              <div className="ps-9">
                <button
                  type="button"
                  aria-expanded={Boolean(open[c.key])}
                  aria-controls={panelId}
                  onClick={() => {
                    setOpen((o) => ({ ...o, [c.key]: !o[c.key] }));
                    setRead((r) => ({ ...r, [c.key]: true }));
                  }}
                  className="inline-flex min-h-touch items-center text-sm font-semibold text-primary underline underline-offset-4"
                >
                  {open[c.key] ? t('hideFull') : t('readFull')}
                </button>
                <div id={panelId} hidden={!open[c.key]} className="flex flex-col gap-2 rounded-[14px] bg-tint-sand p-3 text-sm leading-relaxed">
                  <p>{t(`items.${key}.full`)}</p>
                  <p className="text-xs text-muted">{t('version', { version: c.version })}</p>
                </div>
              </div>
            </li>
          );
        })}
      </ul>

      <p className="text-sm leading-relaxed text-muted">
        {t.rich('legal', {
          privacy: (chunks) => (
            <Link href={{ pathname: '/legal', query: { doc: 'privacy' } }} className="font-semibold text-primary underline underline-offset-4">
              {chunks}
            </Link>
          ),
          terms: (chunks) => (
            <Link href={{ pathname: '/legal', query: { doc: 'terms' } }} className="font-semibold text-primary underline underline-offset-4">
              {chunks}
            </Link>
          ),
        })}
      </p>

      {error ? <Notice tone="danger">{error}</Notice> : null}

      <div className="mt-4 flex flex-col gap-2">
        {missingRequired ? (
          <p id={hintId} className="text-center text-sm text-muted">
            {t('requiredHint')}
          </p>
        ) : null}
        <Button full onClick={save} busy={busy} disabled={missingRequired} aria-describedby={missingRequired ? hintId : undefined}>
          {t('submit')}
        </Button>
      </div>
    </div>
  );
}
