'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { CarePath, ContactRequestCreated, IntakeStatus, Me } from '@/lib/api/types';
import { isLocale, routing } from '@/i18n/routing';
import { formatDateTime } from '@/lib/format';
import { Dialog } from '@/components/booking/Dialog';
import { Button } from '@/components/ui/Button';
import { Tag } from '@/components/ui/Chip';
import { Notice } from '@/components/ui/Notice';
import { problemText } from '@/components/auth/errorText';

const NEXT: Record<CarePath, string> = { intake: '/intake', direct: '/start/safety-check' };

/**
 * P22 Choose path: the intake chat (recommended) or booking directly after the safety check. Asking the
 * team to get in touch is confirmed first in a sheet that says how long it may take (the triage SLA).
 */
export function ChoosePath({ current, intakeStatus = 'none', triageWithinHours = 24 }: { current: CarePath | null; intakeStatus?: IntakeStatus; triageWithinHours?: number }) {
  const t = useTranslations('choosePath');
  const raw = useLocale();
  const locale = isLocale(raw) ? raw : routing.defaultLocale;
  const te = useTranslations('errors');
  const router = useRouter();
  const [busy, setBusy] = useState<CarePath | 'contact' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [requested, setRequested] = useState<ContactRequestCreated | null>(null);
  const [confirming, setConfirming] = useState(false);
  const intakeOpen = intakeStatus === 'active' || intakeStatus === 'paused';

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
      setRequested(await api<ContactRequestCreated>('me/contact-request?source=choose_path', { method: 'POST' }));
    } catch (e) {
      setError(problemText(e, te('network')));
    } finally {
      setConfirming(false);
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
        <span className="text-lg font-bold">{path === 'intake' && intakeOpen ? t('intake.continueTitle') : t(`${path}.title`)}</span>
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
          <Notice tone="success">{t('contactSentBy', { time: formatDateTime(requested.expectedBy, locale) })}</Notice>
        ) : (
          <p>
            {t('talkPrompt')}{' '}
            <button
              type="button"
              onClick={() => setConfirming(true)}
              disabled={busy !== null}
              aria-busy={busy === 'contact' || undefined}
              className="inline font-semibold text-primary underline underline-offset-4 disabled:opacity-60"
            >
              {t('talkLink')}
            </button>
          </p>
        )}
      </div>

      <Dialog
        open={confirming}
        onClose={() => setConfirming(false)}
        locked={busy === 'contact'}
        title={t('contactConfirmTitle')}
        description={t('contactConfirmBody', { hours: triageWithinHours })}
      >
        <div className="flex flex-col gap-2">
          <Button full busy={busy === 'contact'} onClick={() => void requestContact()}>
            {t('contactConfirm')}
          </Button>
          <Button full variant="secondary" disabled={busy === 'contact'} onClick={() => setConfirming(false)}>
            {t('contactCancel')}
          </Button>
        </div>
      </Dialog>
    </div>
  );
}
