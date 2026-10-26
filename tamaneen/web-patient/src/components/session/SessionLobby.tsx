'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useState } from 'react';
import { CheckboxRow } from '@/components/onboarding/CheckboxRow';
import { genderKey } from '@/components/matching/labels';
import { Avatar } from '@/components/ui/Avatar';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { useRouter } from '@/i18n/navigation';
import type { AppointmentDetail, SessionState } from '@/lib/api/types';
import { LateNotice } from './LateNotice';
import { LocalPreview } from './LocalPreview';
import { PreSessionNoteField } from './PreSessionNoteField';
import { canJoin, lobbyPhase, mmss } from './sessionClock';
import { useLocalMedia } from './useLocalMedia';
import { useSessionState } from './useSessionState';

const quietKey = (id: string) => `tamaneen:quiet:${id}`;

function readQuiet(id: string): boolean {
  try {
    return window.localStorage.getItem(quietKey(id)) === '1';
  } catch {
    return false;
  }
}

/**
 * P41 lobby: a countdown from server time, the readiness checks (connection, camera and microphone on the device
 * only, the quiet-place tick kept on this device only), the pre-session note, the privacy reminder and the join
 * button, enabled from ten minutes before. Turns into P54 when the therapist is late. Help now and quick exit come
 * from the signed-in layout and stay visible.
 */
export function SessionLobby({ appointment, initialState, initialNote, sessionLabel }: { appointment: AppointmentDetail; initialState: SessionState | null; initialNote: string; sessionLabel: string }) {
  const t = useTranslations('session.lobby');
  const router = useRouter();
  const { state, serverNow, online } = useSessionState(appointment.id, initialState);
  const media = useLocalMedia();
  const [quiet, setQuiet] = useState(false);
  const therapist = appointment.therapist;
  const g = therapist.gender;

  useEffect(() => setQuiet(readQuiet(appointment.id)), [appointment.id]);

  function changeQuiet(next: boolean) {
    setQuiet(next);
    try {
      if (next) window.localStorage.setItem(quietKey(appointment.id), '1');
      else window.localStorage.removeItem(quietKey(appointment.id));
    } catch {
      // Private windows may refuse storage; the tick is only a reminder.
    }
  }

  const view = state ?? {
    status: appointment.status,
    startsAt: appointment.startsAt,
    opensAt: appointment.joinAvailableAt,
    closesAt: new Date(Date.parse(appointment.startsAt) + appointment.durationMinutes * 60_000).toISOString(),
    peer: { present: false, joinedAt: null, lastSeenAt: null },
    therapistLate: false,
    lateOptionsAfterMinutes: 15,
  };
  const phase = lobbyPhase(view, serverNow, appointment.therapistLateMinutes);
  const starts = Date.parse(view.startsAt);
  const late = phase === 'late' || phase === 'lateOptions';
  const devicesText =
    media.status === 'ok' || media.status === 'audio_only'
      ? t('devicesOk')
      : media.status === 'denied'
        ? t('devicesDenied')
        : media.status === 'unavailable'
          ? t('devicesUnavailable')
          : media.status === 'checking'
            ? t('devicesChecking')
            : null;
  const devicesGood = media.status === 'ok' || media.status === 'audio_only';

  return (
    <div className="flex flex-col gap-5">
      {!online ? <Notice tone="warning">{t('offline')}</Notice> : null}

      {late ? (
        <LateNotice
          appointmentId={appointment.id}
          therapist={therapist}
          lateMs={serverNow - starts}
          lateMinutesNotice={appointment.therapistLateMinutes}
          optionsOpen={phase === 'lateOptions'}
          optionsAfterMinutes={view.lateOptionsAfterMinutes}
        />
      ) : (
        <Card tone="teal" className="flex flex-col items-center gap-2 text-center" data-testid="countdown">
          <Avatar initials={therapist.initials} />
          <p className="text-base">{serverNow < starts ? t('startsIn', { name: therapist.displayName }) : t('started', { name: therapist.displayName })}</p>
          {serverNow < starts ? (
            <p dir="ltr" className="text-4xl font-bold tabular-nums" aria-live="off">
              {mmss(starts - serverNow)}
            </p>
          ) : null}
          <p className="text-sm text-muted">{t('meta', { session: sessionLabel, minutes: appointment.durationMinutes })}</p>
        </Card>
      )}

      {phase !== 'closed' ? (
        <p role="status" className="text-sm font-semibold" data-testid="peer">
          {view.peer.present ? t(genderKey('peerIn', g), { name: therapist.displayName }) : t(genderKey('peerNotYet', g), { name: therapist.displayName })}
        </p>
      ) : null}

      <Card className="flex flex-col gap-3 border border-line">
        <h2 className="text-lg font-bold">{t('ready')}</h2>
        <ul className="flex flex-col gap-2 text-base">
          <li className="flex items-start gap-2">
            <span aria-hidden="true">{online ? '✓' : '⚠'}</span>
            <span>{online ? t('online') : t('offline')}</span>
          </li>
          <li className="flex flex-col gap-2">
            {devicesText ? (
              <span className="flex items-start gap-2" role="status">
                <span aria-hidden="true">{devicesGood ? '✓' : media.status === 'checking' ? '…' : '⚠'}</span>
                <span>{devicesText}</span>
              </span>
            ) : null}
            {media.status === 'idle' || media.status === 'denied' ? (
              <Button variant="secondary" compact onClick={() => void media.start()}>
                {t('devicesCheck')}
              </Button>
            ) : null}
            {media.stream && media.status === 'ok' ? <LocalPreview stream={media.stream} label={t('preview')} className="aspect-video w-full max-w-xs" /> : null}
            <span className="text-xs text-muted">{t('devicesLocal')}</span>
          </li>
        </ul>
        <CheckboxRow checked={quiet} onChange={changeQuiet} label={t('quietPlace')} />
      </Card>

      {phase !== 'closed' ? <PreSessionNoteField appointmentId={appointment.id} initial={initialNote} gender={g} /> : null}

      <Notice tone="info">{t('privacy')}</Notice>

      {phase === 'closed' ? (
        <div className="flex flex-col gap-2">
          <p className="text-base">{t('closed')}</p>
          <ButtonLink href={`/appointments/${encodeURIComponent(appointment.id)}/summary`} variant="secondary" full>
            {t('summary')}
          </ButtonLink>
        </div>
      ) : (
        <div className="flex flex-col gap-1">
          <Button full disabled={!canJoin(phase)} aria-describedby={canJoin(phase) ? undefined : 'join-hint'} onClick={() => router.push(`/appointments/${encodeURIComponent(appointment.id)}/live`)}>
            {t('join')}
          </Button>
          {!canJoin(phase) ? (
            <p id="join-hint" className="text-center text-sm text-muted">
              {t('joinOpensBefore')}
            </p>
          ) : null}
        </div>
      )}
    </div>
  );
}
