'use client';

import { useTranslations } from 'next-intl';
import { useCallback, useEffect, useRef, useState } from 'react';
import { Dialog } from '@/components/booking/Dialog';
import { genderKey } from '@/components/matching/labels';
import { Avatar } from '@/components/ui/Avatar';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { Spinner } from '@/components/ui/Spinner';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import { ApiError } from '@/lib/api/errors';
import type { AppointmentDetail, JoinDescriptor, SessionState } from '@/lib/api/types';
import { LocalPreview } from './LocalPreview';
import { mmss, serverOffsetMs } from './sessionClock';
import { useLocalMedia } from './useLocalMedia';

const TOKEN_RENEW_MS = 60_000;
const TOKEN_INVALID = 'error.session.token_invalid';

/** Only an https room address may be framed. */
export function safeEmbedUrl(url: string | null): string | null {
  return url && /^https:\/\//i.test(url) ? url : null;
}

/**
 * P42 in session. Joins through the API (only inside the window), then sends a heartbeat with the session pass so
 * the therapist sees the patient, and learns from the answer whether the therapist is in. With the dev provider
 * there is no call: a clearly labelled test room shows the patient's own camera and the therapist's presence.
 * With a real provider the room is framed. Not recorded either way; Help now stays in the header.
 */
export function SessionRoom({ appointment }: { appointment: AppointmentDetail }) {
  const t = useTranslations('session.live');
  const tLobby = useTranslations('session.lobby');
  const router = useRouter();
  const media = useLocalMedia();
  const [join, setJoin] = useState<JoinDescriptor | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [state, setState] = useState<SessionState | null>(null);
  const [offset, setOffset] = useState(0);
  const [reconnecting, setReconnecting] = useState(false);
  const [now, setNow] = useState(() => Date.now());
  const [leaving, setLeaving] = useState(false);
  const [confirmLeave, setConfirmLeave] = useState(false);
  const [report, setReport] = useState(false);
  const joinRef = useRef<JoinDescriptor | null>(null);
  const id = encodeURIComponent(appointment.id);
  const therapist = appointment.therapist;

  const enter = useCallback(async () => {
    try {
      const descriptor = await api<JoinDescriptor>(`appointments/${id}/join`, { method: 'POST' });
      joinRef.current = descriptor;
      setJoin(descriptor);
      setError(null);
      return descriptor;
    } catch (e) {
      setError(e instanceof ApiError ? (e.problem.detail ?? e.problem.title) : null);
      if (!(e instanceof ApiError)) setReconnecting(true);
      return null;
    }
  }, [id]);

  const beat = useCallback(
    async (leavingNow = false) => {
      const current = joinRef.current;
      if (!current) return;
      try {
        const next = await api<SessionState>(`appointments/${id}/presence`, { method: 'POST', body: { token: current.token, leaving: leavingNow || undefined } });
        setState(next);
        setOffset(serverOffsetMs(next.serverNow, Date.now()));
        setReconnecting(false);
      } catch (e) {
        if (e instanceof ApiError && e.code === TOKEN_INVALID) {
          await enter();
        } else {
          setReconnecting(true);
        }
      }
    },
    [id, enter],
  );

  useEffect(() => {
    void enter().then((d) => {
      if (d) void beat();
      if (d?.mode === 'test_room') void media.start();
    });
    // Join once when the page opens; media.start is stable.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  useEffect(() => {
    if (!join) return;
    const every = Math.max(5, join.heartbeatSeconds) * 1000;
    const timer = window.setInterval(() => {
      const current = joinRef.current;
      if (current && Date.parse(current.expiresAt) - Date.now() < TOKEN_RENEW_MS && Date.now() < Date.parse(current.closesAt)) {
        void enter().then(() => beat());
      } else {
        void beat();
      }
    }, every);
    return () => window.clearInterval(timer);
  }, [join, beat, enter]);

  useEffect(() => {
    const tick = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(tick);
  }, []);

  async function leave() {
    setLeaving(true);
    await beat(true);
    media.stop();
    router.push(`/appointments/${id}/summary`);
  }

  if (!join) {
    return error ? (
      <div className="flex flex-col gap-4">
        <Notice tone="warning">{error}</Notice>
        <ButtonLink href={`/appointments/${id}/lobby`} variant="secondary" full>
          {t('backToLobby')}
        </ButtonLink>
      </div>
    ) : (
      <Spinner label={t('joining')} />
    );
  }

  const serverNow = now + offset;
  const peer = state?.peer;
  const since = peer?.joinedAt ? Math.max(Date.parse(peer.joinedAt), Date.parse(appointment.startsAt)) : null;
  const embed = join.mode === 'embed' ? safeEmbedUrl(join.url) : null;

  return (
    <div className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-3">
        <div className="flex items-center gap-3">
          <Avatar initials={therapist.initials} size={40} />
          <div>
            <p className="font-bold">{therapist.displayName}</p>
            <p className="text-sm text-muted" data-testid="elapsed">
              {since !== null && serverNow >= since ? t('elapsed', { time: mmss(serverNow - since) }) : t('notStarted')}
            </p>
          </div>
        </div>
        <span className="rounded-full bg-tint-teal px-3 py-1 text-xs font-semibold text-primary-strong">{t('noRecording')}</span>
      </div>

      {reconnecting ? <Notice tone="warning">{t('reconnecting')}</Notice> : null}

      {join.mode === 'test_room' ? (
        <Card tone="sand" className="flex flex-col gap-1" data-testid="test-room">
          <p className="text-base font-bold">{t('testRoom')}</p>
          <p className="text-sm leading-relaxed">{t('testRoomBody')}</p>
        </Card>
      ) : null}

      {embed ? (
        <iframe title={t('embedTitle')} src={embed} allow="camera; microphone; fullscreen; display-capture" className="aspect-[3/4] w-full rounded-card border border-line sm:aspect-video" />
      ) : (
        <div className="grid grid-cols-2 gap-3">
          <div className="flex aspect-[3/4] flex-col items-center justify-center gap-2 rounded-card bg-tint-teal p-3 text-center" data-testid="peer-tile">
            <Avatar initials={therapist.initials} />
            <p role="status" className="text-sm font-semibold">
              {peer?.present ? tLobby(genderKey('peerIn', therapist.gender), { name: therapist.displayName }) : tLobby(genderKey('peerNotYet', therapist.gender), { name: therapist.displayName })}
            </p>
          </div>
          <div className="relative flex aspect-[3/4] items-center justify-center rounded-card bg-ink/80 text-surface">
            {media.stream && media.cameraOn && media.status === 'ok' ? <LocalPreview stream={media.stream} label={t('you')} className="absolute inset-0 h-full w-full" /> : <span className="text-sm">{t('cameraOffTile')}</span>}
            <span className="absolute bottom-2 start-2 rounded-full bg-ink/70 px-2 py-0.5 text-xs">{t('you')}</span>
          </div>
        </div>
      )}

      {media.status === 'denied' ? (
        <div className="flex flex-col gap-2">
          <Notice tone="warning">{t('cameraDenied')}</Notice>
          <Button variant="secondary" onClick={() => void media.start(true)}>
            {t('audioOnly')}
          </Button>
        </div>
      ) : null}

      <div className="flex flex-wrap items-center justify-center gap-2" role="toolbar" aria-label={t('metaTitle')}>
        {!embed ? (
          <>
            <Button variant="secondary" compact aria-pressed={!media.micOn} onClick={media.toggleMic} disabled={!media.stream}>
              {media.micOn ? t('mute') : t('unmute')}
            </Button>
            <Button variant="secondary" compact aria-pressed={!media.cameraOn} onClick={media.toggleCamera} disabled={!media.stream || media.status === 'audio_only'}>
              {media.cameraOn ? t('cameraOff') : t('cameraOn')}
            </Button>
          </>
        ) : null}
        <Button variant="ghost" compact onClick={() => setReport(true)}>
          {t('report')}
        </Button>
        <Button variant="danger" compact onClick={() => setConfirmLeave(true)}>
          {t('leave')}
        </Button>
      </div>

      <Dialog open={confirmLeave} onClose={() => setConfirmLeave(false)} title={t('leaveTitle')} description={t('leaveBody')} locked={leaving}>
        <div className="flex flex-col gap-2">
          <Button variant="danger" full busy={leaving} onClick={() => void leave()}>
            {t('leaveConfirm')}
          </Button>
          <Button variant="ghost" full onClick={() => setConfirmLeave(false)} disabled={leaving}>
            {t('leaveCancel')}
          </Button>
        </div>
      </Dialog>

      <Dialog open={report} onClose={() => setReport(false)} title={t('reportTitle')}>
        <ul className="flex list-disc flex-col gap-2 ps-5 text-base leading-relaxed">
          <li>{t('tipConnection')}</li>
          <li>{t('tipPermissions')}</li>
          <li>{t('tipAudio')}</li>
          <li>{t('tipHelp')}</li>
        </ul>
        <Button
          full
          onClick={() => {
            setReport(false);
            void enter().then((d) => {
              if (d) void beat();
              if (d?.mode === 'test_room') void media.start();
            });
          }}
        >
          {t('rejoin')}
        </Button>
      </Dialog>
    </div>
  );
}
