'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useId, useRef, useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { AppointmentDetail, CancelReasonCode, CancelRequest } from '@/lib/api/types';
import { formatDateTime } from '@/lib/format';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { OptionButton } from '@/components/ui/OptionButton';
import { Dialog } from '@/components/booking/Dialog';
import { ProblemNotice } from '@/components/booking/ProblemNotice';
import { toShownProblem, type ShownProblem } from '@/components/booking/problem';
import { asLocale } from '@/components/matching/labels';

/** The reasons the API accepts (SchedulingEndpoints.CancelAsync / CancelReasons.Patient), in display order. */
export const CANCEL_REASONS: readonly CancelReasonCode[] = ['schedule_conflict', 'feeling_better', 'unwell', 'therapist_fit', 'other'];

/**
 * P28 change / cancel. Both are disabled once canChange is false, with the 24-hour deadline shown.
 * "Change" opens booking in reschedule mode; "cancel" asks for a reason and confirmation in a modal first (PB-45).
 */
export function AppointmentActions({ appointment, timeZone }: { appointment: AppointmentDetail; timeZone: string }) {
  const t = useTranslations('appointmentDetail');
  const tErrors = useTranslations('errors');
  const locale = asLocale(useLocale());
  const router = useRouter();
  const noteId = useId();
  const keepRef = useRef<HTMLDivElement>(null);
  const doneRef = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<ShownProblem | null>(null);
  const [cancelled, setCancelled] = useState(false);
  const [reason, setReason] = useState<CancelReasonCode | null>(null);
  const [reasonMissing, setReasonMissing] = useState(false);
  const reasonLabelId = useId();
  const reasonErrorId = useId();

  useEffect(() => {
    if (cancelled) doneRef.current?.focus();
  }, [cancelled]);

  const deadline = formatDateTime(appointment.changeDeadline, locale, timeZone);
  const canChange = appointment.canChange && appointment.status === 'confirmed' && !cancelled;

  function openDialog() {
    setProblem(null);
    setReason(null);
    setReasonMissing(false);
    setOpen(true);
  }

  async function cancelAppointment() {
    if (!reason) {
      setReasonMissing(true);
      return;
    }
    setBusy(true);
    setProblem(null);
    try {
      const body: CancelRequest = { reasonCode: reason };
      await api<AppointmentDetail>(`appointments/${encodeURIComponent(appointment.id)}/cancel`, { method: 'POST', body });
      setOpen(false);
      setCancelled(true);
      router.refresh();
    } catch (error) {
      setProblem(toShownProblem(error, tErrors('generic')));
    } finally {
      setBusy(false);
    }
  }

  if (cancelled || appointment.status !== 'confirmed') {
    return (
      <div className="flex flex-col gap-3">
        {cancelled ? (
          <div ref={doneRef} tabIndex={-1} className="outline-none">
            <Notice tone="success">{t('cancelled')}</Notice>
          </div>
        ) : null}
        <ButtonLink href={`/therapists/${encodeURIComponent(appointment.therapist.id)}/book`} variant="secondary" full>
          {t('bookNew')}
        </ButtonLink>
      </div>
    );
  }

  const changeHref = `/therapists/${encodeURIComponent(appointment.therapist.id)}/book?reschedule=${encodeURIComponent(appointment.id)}`;

  return (
    <div className="flex flex-col gap-3">
      {canChange ? (
        <ButtonLink href={changeHref} variant="secondary" full aria-describedby={noteId}>
          {t('change')}
        </ButtonLink>
      ) : (
        <Button variant="secondary" full disabled aria-describedby={noteId}>
          {t('change')}
        </Button>
      )}
      <Button variant="danger-outline" full disabled={!canChange} aria-describedby={noteId} onClick={openDialog}>
        {t('cancel')}
      </Button>
      <p id={noteId} className="text-center text-sm text-muted">
        {canChange ? t('changeUntil', { deadline }) : t('changeClosed', { deadline })}
      </p>

      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        locked={busy}
        initialFocus={keepRef}
        title={t('cancelTitle')}
        description={t('cancelBody', { date: formatDateTime(appointment.startsAt, locale, timeZone), name: appointment.therapist.displayName })}
      >
        {problem ? <ProblemNotice problem={problem} /> : null}
        <div className="mb-4 flex flex-col gap-2">
          <p id={reasonLabelId} className="text-sm font-semibold">
            {t('reasonLabel')}
          </p>
          <div role="radiogroup" aria-labelledby={reasonLabelId} aria-describedby={reasonMissing ? reasonErrorId : undefined} className="flex flex-col gap-2">
            {CANCEL_REASONS.map((code) => (
              <OptionButton
                key={code}
                selected={reason === code}
                disabled={busy}
                onClick={() => {
                  setReason(code);
                  setReasonMissing(false);
                }}
              >
                {t(`reasons.${code}`)}
              </OptionButton>
            ))}
          </div>
          {reasonMissing ? (
            <p id={reasonErrorId} role="alert" className="text-sm font-semibold text-help">
              {t('reasonRequired')}
            </p>
          ) : null}
        </div>
        <div className="flex flex-col gap-3">
          <Button variant="danger" full busy={busy} onClick={cancelAppointment}>
            {t('cancelConfirm')}
          </Button>
          <div ref={keepRef} className="contents">
            <Button variant="secondary" full disabled={busy} onClick={() => setOpen(false)}>
              {t('cancelKeep')}
            </Button>
          </div>
        </div>
      </Dialog>
    </div>
  );
}
