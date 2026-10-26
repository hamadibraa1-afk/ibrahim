'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useRef, useState } from 'react';
import { isLocale, routing } from '@/i18n/routing';
import { api } from '@/lib/api/client';
import { ApiError } from '@/lib/api/errors';
import type { ChangeReason, ChangeReasonCode, ChangeTherapistRequest } from '@/lib/api/types';
import { formatDateTime } from '@/lib/format';
import { apiMessage, isNetworkError } from '@/components/pwa/errors';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { OptionButton } from '@/components/ui/OptionButton';
import { PageHeading } from '@/components/ui/PageHeading';

function RequestStatus({ request, timeZone }: { request: ChangeTherapistRequest; timeZone: string }) {
  const t = useTranslations('care.change');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  return (
    <Card tone="teal" data-testid="change-status">
      <h2 className="mb-2 text-lg font-bold">{t('status.title')}</h2>
      <p>{t(`status.${request.status}`, { time: formatDateTime(request.expectedBy, locale, timeZone) })}</p>
      {request.status === 'offered' ? (
        <ButtonLink href="/matches" className="mt-4" data-testid="see-matches">
          {t('seeMatches')}
        </ButtonLink>
      ) : null}
    </Card>
  );
}

/**
 * P44 Change therapist. No reason is required and there is no free text: one optional reason from the catalog. The
 * current therapist never sees the reason and the file and current appointment stay. A request already open shows
 * its state (requested, matching, offered) instead of the form. Sent once per press (Idempotency-Key).
 */
export function ChangeTherapistView({ reasons, initial, therapistName, inCare, withinHours, timeZone }: { reasons: ChangeReason[]; initial: ChangeTherapistRequest | null; therapistName: string | null; inCare: boolean; withinHours: number; timeZone: string }) {
  const t = useTranslations('care.change');
  const te = useTranslations('errors');
  const [request, setRequest] = useState(initial);
  const [reason, setReason] = useState<ChangeReasonCode | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [sent, setSent] = useState(false);
  const key = useRef<string | null>(null);

  async function submit() {
    setBusy(true);
    setError(null);
    key.current ??= crypto.randomUUID();
    try {
      const created = await api<ChangeTherapistRequest>('me/change-therapist-requests', { method: 'POST', body: reason ? { reasonCode: reason } : {}, idempotencyKey: key.current });
      setRequest(created);
      setSent(true);
    } catch (e) {
      const current = e instanceof ApiError ? (e.problem as unknown as { current?: ChangeTherapistRequest }).current : undefined;
      if (current) {
        setRequest(current);
      } else {
        setError(apiMessage(e) ?? (isNetworkError(e) ? te('network') : te('generic')));
        key.current = null;
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <PageHeading title={t('title')} subtitle={therapistName ? t('reassure', { name: therapistName }) : t('reassureNoName')} />
      {sent ? <Notice tone="success">{t('sent', { hours: withinHours })}</Notice> : null}
      {request ? (
        <RequestStatus request={request} timeZone={timeZone} />
      ) : !inCare ? (
        <Card data-testid="not-in-care">
          <p className="mb-4">{t('notInCare')}</p>
          <ButtonLink href="/therapists">{t('findTherapist')}</ButtonLink>
        </Card>
      ) : (
        <>
          <fieldset>
            <legend className="mb-3 text-base font-bold">{t('reasonLegend')}</legend>
            <div role="radiogroup" className="flex flex-col gap-2">
              {reasons.map((r) => (
                <OptionButton key={r.code} selected={reason === r.code} onClick={() => setReason(reason === r.code ? null : r.code)} data-testid={`reason-${r.code}`}>
                  {r.label}
                </OptionButton>
              ))}
            </div>
          </fieldset>
          <Card tone="sand">
            <p>{t('next', { hours: withinHours })}</p>
          </Card>
          {error ? (
            <Notice tone="danger">
              {error}
            </Notice>
          ) : null}
          <Button full busy={busy} onClick={submit} data-testid="change-submit">
            {error ? t('retry') : t('submit')}
          </Button>
        </>
      )}
    </div>
  );
}
