'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { AppointmentDetail, Hold, Slot } from '@/lib/api/types';
import { formatDate, formatTime } from '@/lib/format';
import { asLocale } from '@/components/matching/labels';
import { Button, ButtonLink } from '@/components/ui/Button';
import { EmptyState } from '@/components/ui/EmptyState';
import { ProblemNotice } from './ProblemNotice';
import { isConflict, isNotEligible, toShownProblem, type ShownProblem } from './problem';
import { formatCountdown, groupSlotsByDay, secondsLeft } from './slots';

const ONE_MINUTE = 60;

export interface BookingFlowProps {
  therapistId: string;
  initialSlots: Slot[];
  timeZone: string;
  /** Set when the patient came from P28 "change": the hold is turned into a reschedule instead of a new booking. */
  rescheduleId?: string | null;
  /** The API refused booking for this patient (403 error.booking.not_eligible) while rendering the page. */
  notEligible?: ShownProblem | null;
}

/**
 * P08 booking: pick a day and a time, which holds that slot for a few minutes, then confirm.
 * Only one hold exists at a time: choosing another time releases the previous one first.
 */
export function BookingFlow({ therapistId, initialSlots, timeZone, rescheduleId = null, notEligible: initialNotEligible = null }: BookingFlowProps) {
  const t = useTranslations('booking');
  const tErrors = useTranslations('errors');
  const locale = asLocale(useLocale());
  const router = useRouter();

  const [slots, setSlots] = useState(initialSlots);
  const days = useMemo(() => groupSlotsByDay(slots, timeZone), [slots, timeZone]);
  const [chosenDay, setChosenDay] = useState<string | null>(null);
  const activeDay = days.find((d) => d.key === chosenDay) ?? days[0] ?? null;

  const [hold, setHold] = useState<Hold | null>(null);
  const holdRef = useRef<Hold | null>(null);
  const [pendingSlotId, setPendingSlotId] = useState<string | null>(null);
  const [remaining, setRemaining] = useState(0);
  const oneMinuteAnnounced = useRef(false);
  const [announcement, setAnnouncement] = useState('');
  const [expired, setExpired] = useState(false);
  const [problem, setProblem] = useState<ShownProblem | null>(null);
  const [notEligible, setNotEligible] = useState<ShownProblem | null>(initialNotEligible);
  const [confirming, setConfirming] = useState(false);

  const slotsPath = `therapists/${encodeURIComponent(therapistId)}/slots?days=14`;

  const setCurrentHold = useCallback((next: Hold | null) => {
    holdRef.current = next;
    setHold(next);
  }, []);

  const release = useCallback(async (previous: Hold) => {
    try {
      await api<void>(`bookings/holds/${encodeURIComponent(previous.holdId)}`, { method: 'DELETE' });
    } catch {
      // A hold that is already gone (expired or consumed) needs no release.
    }
  }, []);

  const reloadSlots = useCallback(async () => {
    try {
      setSlots(await api<Slot[]>(slotsPath));
    } catch {
      // Keep the current list; the problem shown already tells the patient what happened.
    }
  }, [slotsPath]);

  /** Shows a failed call: not-eligible replaces the picker, a conflict reloads the times. */
  const handleFailure = useCallback(
    async (error: unknown) => {
      if (isNotEligible(error)) {
        setNotEligible(toShownProblem(error, t('notEligibleFallback')));
        return;
      }
      if (isConflict(error)) {
        setProblem(toShownProblem(error, t('slotsUpdated')));
        await reloadSlots();
        return;
      }
      setProblem(toShownProblem(error, tErrors('generic')));
    },
    [reloadSlots, t, tErrors],
  );

  const describeHold = useCallback(
    (h: Hold, seconds: number) => ({
      date: formatDate(h.startsAt, locale, timeZone),
      time: formatTime(h.startsAt, locale, timeZone),
      remaining: formatCountdown(seconds),
    }),
    [locale, timeZone],
  );

  // The countdown is computed from expiresAt on every tick, so a throttled background tab stays correct.
  useEffect(() => {
    if (!hold) return;
    const tick = () => {
      const left = secondsLeft(hold.expiresAt);
      setRemaining(left);
      if (left <= 0) {
        setCurrentHold(null);
        setExpired(true);
        setAnnouncement(t('expired'));
      } else if (left <= ONE_MINUTE && !oneMinuteAnnounced.current) {
        oneMinuteAnnounced.current = true;
        setAnnouncement(t('oneMinute'));
      }
    };
    tick();
    const id = window.setInterval(tick, 1000);
    return () => window.clearInterval(id);
  }, [hold, setCurrentHold, t]);

  // Leaving the page with a hold gives the time back to other patients right away.
  useEffect(
    () => () => {
      const open = holdRef.current;
      if (open) void release(open);
    },
    [release],
  );

  async function choose(slot: Slot) {
    if (pendingSlotId || confirming || holdRef.current?.slotId === slot.slotId) return;
    setProblem(null);
    setExpired(false);
    setPendingSlotId(slot.slotId);
    const previous = holdRef.current;
    if (previous) {
      setCurrentHold(null);
      await release(previous);
    }
    try {
      const next = await api<Hold>('bookings/holds', { method: 'POST', body: { slotId: slot.slotId } });
      oneMinuteAnnounced.current = false;
      const left = secondsLeft(next.expiresAt);
      setRemaining(left);
      setCurrentHold(next);
      setAnnouncement(t('heldPlain', describeHold(next, left)));
    } catch (error) {
      await handleFailure(error);
    } finally {
      setPendingSlotId(null);
    }
  }

  async function confirm() {
    const current = holdRef.current;
    if (!current || confirming) return;
    setConfirming(true);
    setProblem(null);
    try {
      let appointment: AppointmentDetail;
      if (rescheduleId) {
        appointment = await api<AppointmentDetail>(`appointments/${encodeURIComponent(rescheduleId)}/reschedule`, {
          method: 'POST',
          body: { slotId: current.slotId },
        });
        setCurrentHold(null);
        await release(current);
      } else {
        appointment = await api<AppointmentDetail>(`bookings/holds/${encodeURIComponent(current.holdId)}/confirm`, { method: 'POST' });
        setCurrentHold(null);
      }
      router.push(`/appointments/${encodeURIComponent(appointment.id)}/confirmed`);
    } catch (error) {
      if (isConflict(error)) {
        // The slot or the hold is gone; let go of what is left and offer fresh times.
        setCurrentHold(null);
        if (rescheduleId) await release(current);
      }
      await handleFailure(error);
      setConfirming(false);
    }
  }

  if (notEligible) {
    return (
      <ProblemNotice
        tone="warning"
        problem={notEligible}
        action={
          <ButtonLink href="/start/path" compact>
            {t('notEligibleAction')}
          </ButtonLink>
        }
      />
    );
  }

  const heldParts = hold ? describeHold(hold, remaining) : null;

  return (
    <div className="flex flex-col gap-5">
      <p aria-live="polite" role="status" className="sr-only">
        {announcement}
      </p>

      {problem ? <ProblemNotice problem={problem} /> : null}

      {days.length === 0 || !activeDay ? (
        <EmptyState
          title={t('noSlotsTitle')}
          body={t('noSlotsBody')}
          action={
            <ButtonLink href="/therapists" variant="secondary" compact>
              {t('browse')}
            </ButtonLink>
          }
        />
      ) : (
        <>
          <section aria-labelledby="choose-day" className="flex flex-col gap-3">
            <h2 id="choose-day" className="text-base font-bold">
              {t('chooseDay')}
            </h2>
            <ul aria-label={t('daysLabel')} className="-mx-4 flex gap-2 overflow-x-auto px-4 pb-1">
              {days.map((day) => {
                const first = day.slots[0]!.startsAt;
                const selected = day.key === activeDay.key;
                return (
                  <li key={day.key} className="min-w-[76px] flex-1">
                    <button
                      type="button"
                      aria-pressed={selected}
                      aria-label={formatDate(first, locale, timeZone)}
                      onClick={() => setChosenDay(day.key)}
                      className={`flex w-full flex-col items-center gap-1 rounded-[16px] px-2 py-3 ${selected ? 'border-2 border-primary bg-tint-teal text-primary-strong' : 'border border-line bg-surface text-ink'}`}
                    >
                      <span className="text-sm">{formatDate(first, locale, timeZone, { weekday: 'long' })}</span>
                      <span className="text-xl font-bold">{formatDate(first, locale, timeZone, { day: 'numeric' })}</span>
                    </button>
                  </li>
                );
              })}
            </ul>
          </section>

          <section aria-labelledby="choose-time" className="flex flex-col gap-3">
            <h2 id="choose-time" className="text-base font-bold">
              {t('times', { month: formatDate(activeDay.slots[0]!.startsAt, locale, timeZone, { month: 'long', year: 'numeric' }) })}
            </h2>
            <ul aria-label={t('timesLabel')} className="grid grid-cols-3 gap-2">
              {activeDay.slots.map((slot) => {
                const selected = hold?.slotId === slot.slotId || pendingSlotId === slot.slotId;
                return (
                  <li key={slot.slotId}>
                    <button
                      type="button"
                      aria-pressed={selected}
                      aria-busy={pendingSlotId === slot.slotId || undefined}
                      disabled={confirming || (pendingSlotId !== null && pendingSlotId !== slot.slotId)}
                      onClick={() => choose(slot)}
                      className={`min-h-[52px] w-full rounded-[14px] text-base font-bold disabled:opacity-60 ${selected ? 'bg-primary text-surface' : 'border border-line bg-surface text-ink'}`}
                    >
                      {formatTime(slot.startsAt, locale, timeZone)}
                    </button>
                  </li>
                );
              })}
            </ul>
          </section>
        </>
      )}

      {hold && heldParts ? (
        <div className="flex items-start gap-3 rounded-[16px] bg-tint-sand px-4 py-3 text-sm leading-relaxed text-ink">
          <svg aria-hidden="true" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="mt-0.5 shrink-0 text-accent">
            <circle cx="12" cy="12" r="9" />
            <path d="M12 7v5l3 2" />
          </svg>
          <p>
            {t.rich('held', {
              date: heldParts.date,
              time: heldParts.time,
              remaining: heldParts.remaining,
              b: (chunks) => (
                <b role="timer" data-testid="hold-countdown">
                  {chunks}
                </b>
              ),
            })}
          </p>
        </div>
      ) : pendingSlotId ? (
        <p className="text-sm text-muted">{t('holding')}</p>
      ) : expired ? (
        // Not a live region itself: the polite announcer above already reads this out once.
        <p className="rounded-[16px] bg-tint-sand px-4 py-3 text-sm leading-relaxed text-ink">{t('expired')}</p>
      ) : days.length > 0 ? (
        <p className="text-sm text-muted">{t('pickHint')}</p>
      ) : null}

      {days.length > 0 ? <p className="text-sm leading-relaxed text-muted">{t('reminders')}</p> : null}

      {days.length > 0 ? (
        <div className="sticky bottom-0 -mx-4 mt-4 bg-bg/95 px-4 pb-4 pt-2">
          <Button full onClick={confirm} disabled={!hold} busy={confirming}>
            {rescheduleId ? t('confirmReschedule') : t('confirm')}
          </Button>
        </div>
      ) : null}
    </div>
  );
}
