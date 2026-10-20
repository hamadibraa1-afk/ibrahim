'use client';

import { useTranslations } from 'next-intl';
import { useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { CarePath, ContactRequestCreated, Me } from '@/lib/api/types';
import { Tag } from '@/components/ui/Chip';
import { Notice } from '@/components/ui/Notice';
import { problemText } from '@/components/auth/errorText';

const NEXT: Record<CarePath, string> = { intake: '/intake', direct: '/start/safety-check' };

/** P22 Choose path: the intake chat (recommended) or booking directly after the safety check. */
export function ChoosePath({ current }: { current: CarePath | null }) {
  const t = useTranslations('choosePath');
  const te = useTranslations('errors');
  const router = useRouter();
  const [busy, setBusy] = useState<CarePath | 'contact' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [requested, setRequested] = useState(false);

  async function choose(path: CarePath) {
    setBusy(path);
    setError(null);
    try {
      await api<Me>('me/path', { method: 'PUT', body: { path } });
      router.push(NEXT[path]);
    } catch (e) {
      setError(problemText(e, te('network')));
      setBusy(null);
    }
  }

  async function requestContact() {
    setBusy('contact');
    setError(null);
    try {
      await api<ContactRequestCreated>('me/contact-request', { method: 'POST' });
      setRequested(true);
    } catch (e) {
      setError(problemText(e, te('network')));
    } finally {
      setBusy(null);
    }
  }

  const card = (path: CarePath, recommended: boolean) => (
    <button
      type="button"
      onClick={() => void choose(path)}
      disabled={busy !== null}
      aria-busy={busy === path || undefined}
      aria-current={current === path ? 'true' : undefined}
      data-testid={`path-${path}`}
      className={`flex w-full flex-col gap-2 rounded-card bg-surface p-5 text-start transition-colors hover:bg-tint-teal/40 disabled:cursor-wait ${recommended ? 'border-2 border-primary' : 'border border-line'}`}
    >
      <span className="flex w-full items-center justify-between gap-3">
        <span className="text-lg font-bold">{t(`${path}.title`)}</span>
        {recommended ? <Tag>{t('recommended')}</Tag> : null}
      </span>
      <span className="text-base leading-relaxed text-muted">{t(`${path}.body`)}</span>
      {path === 'intake' ? <span className="text-sm font-semibold text-primary">{t('intake.note')}</span> : null}
      {busy === path ? <span aria-hidden="true" className="h-4 w-4 animate-spin rounded-full border-2 border-primary border-t-transparent" /> : null}
    </button>
  );

  return (
    <div className="flex flex-col gap-4">
      {card('intake', true)}
      {card('direct', false)}

      {error ? <Notice tone="danger">{error}</Notice> : null}

      <div className="mt-8 rounded-card border border-line bg-surface p-4 text-base leading-relaxed">
        {requested ? (
          <Notice tone="success">{t('contactSent')}</Notice>
        ) : (
          <p>
            {t('talkPrompt')}{' '}
            <button
              type="button"
              onClick={() => void requestContact()}
              disabled={busy !== null}
              aria-busy={busy === 'contact' || undefined}
              className="inline font-semibold text-primary underline underline-offset-4 disabled:opacity-60"
            >
              {t('talkLink')}
            </button>
          </p>
        )}
      </div>
    </div>
  );
}
