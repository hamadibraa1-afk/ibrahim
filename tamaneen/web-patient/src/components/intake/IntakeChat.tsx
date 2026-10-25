'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useCallback, useEffect, useRef, useState } from 'react';
import { useRouter } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { api } from '@/lib/api/client';
import { ApiError } from '@/lib/api/errors';
import type { ContactRequestCreated, IntakeState, IntakeSummary, IntakeTurnResponse, SendMessageRequest } from '@/lib/api/types';
import { formatDate, formatDateTime } from '@/lib/format';
import { Dialog } from '@/components/booking/Dialog';
import { clearDraft, loadDraft, saveDraft } from '@/components/pwa/drafts';
import { apiMessage, isNetworkError } from '@/components/pwa/errors';
import { useOnline } from '@/components/pwa/hooks';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { ProgressBar } from '@/components/ui/ProgressBar';
import { Spinner } from '@/components/ui/Spinner';
import { Composer } from './Composer';
import { MessageList, type DisplayMessage } from './MessageList';
import { QuestionCard } from './QuestionCard';

const DRAFT_KEY = 'intake';
const RETRY_MS = 15_000;

/** A fresh id for one patient message; every retry of that message reuses it (PB-21). */
function newClientMessageId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') return crypto.randomUUID();
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = Math.floor(Math.random() * 16);
    return (c === 'x' ? r : (r % 4) + 8).toString(16);
  });
}

/** The assistant is down when its latest turn was the fixed fallback (EC-10/11/16). */
function assistantUnavailable(state: IntakeState | null): boolean {
  const last = [...(state?.messages ?? [])].reverse().find((m) => m.role === 'assistant');
  return Boolean(last?.fallback);
}

type Phase = 'loading' | 'ready' | 'paused' | 'expired' | 'error';

/** Where a patient belongs when the intake is already past the chat. */
function laterStep(status: IntakeState['status']): string | null {
  if (status === 'summarizing' || status === 'review') return '/intake/summary';
  if (status === 'submitted') return '/waiting';
  return null;
}

/**
 * P04 Intake chat with Rafiq. Starts or resumes the intake, sends free text and questionnaire answers
 * through the crisis gate, and goes to the crisis screen the moment a response says `crisis: true`.
 * Each message carries a client id, so "Retry" can never create a second turn. When the assistant is
 * down the patient can switch to a form (PB-28); pausing returns home (PB-24); asking for a person is
 * confirmed first and says when the team will reach out (PB-25).
 */
export function IntakeChat({ triageWithinHours = 24 }: { triageWithinHours?: number }) {
  const t = useTranslations('intake');
  const tErrors = useTranslations('errors');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const router = useRouter();
  const online = useOnline();

  const [phase, setPhase] = useState<Phase>('loading');
  const [intake, setIntake] = useState<IntakeState | null>(null);
  const [pending, setPending] = useState<DisplayMessage | null>(null);
  const [text, setText] = useState('');
  const [waiting, setWaiting] = useState(false);
  const [answering, setAnswering] = useState<number | null>(null);
  const [busy, setBusy] = useState<'pause' | 'resume' | 'contact' | 'finish' | 'form' | null>(null);
  const [contactRequested, setContactRequested] = useState<ContactRequestCreated | null>(null);
  const [confirmContact, setConfirmContact] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const sending = useRef(false);

  const failure = useCallback((e: unknown) => apiMessage(e) ?? (isNetworkError(e) ? tErrors('network') : tErrors('generic')), [tErrors]);

  const apply = useCallback(
    (state: IntakeState) => {
      const next = laterStep(state.status);
      if (next) {
        router.replace(next);
        return;
      }
      setIntake(state);
      setPhase(state.status === 'paused' ? 'paused' : state.status === 'expired' ? 'expired' : 'ready');
    },
    [router],
  );

  /** POST /v1/intake starts a new intake or resumes the current one. Returns false (with the error shown) on failure. */
  const start = useCallback(async (): Promise<boolean> => {
    setError(null);
    try {
      apply(await api<IntakeState>('intake', { method: 'POST' }));
      return true;
    } catch (e) {
      setError(failure(e));
      return false;
    }
  }, [apply, failure]);

  // On load (once): an expired intake is shown as such (with a restart), anything else is started or resumed.
  const loaded = useRef(false);
  useEffect(() => {
    if (loaded.current) return;
    loaded.current = true;
    setText(loadDraft(DRAFT_KEY));
    void (async () => {
      try {
        const existing = await api<IntakeState>('intake/current');
        if (existing.status === 'expired' || laterStep(existing.status)) {
          apply(existing);
          return;
        }
      } catch (e) {
        if (!(e instanceof ApiError && e.status === 404)) {
          setError(failure(e));
          setPhase('error');
          return;
        }
      }
      if (!(await start())) setPhase('error');
    })();
  }, [apply, failure, start]);

  function changeText(value: string) {
    setText(value);
    saveDraft(DRAFT_KEY, value);
  }

  const turn = useCallback(
    (response: IntakeTurnResponse): boolean => {
      if (response.crisis) {
        router.replace('/crisis');
        return false;
      }
      apply(response);
      return true;
    },
    [apply, router],
  );

  const deliver = useCallback(
    async (message: DisplayMessage, intakeId: string) => {
      if (sending.current) return;
      sending.current = true;
      setPending({ ...message, queued: false, failed: false });
      setWaiting(true);
      setError(null);
      try {
        const body: SendMessageRequest = { text: message.text, clientMessageId: message.clientMessageId };
        const response = await api<IntakeTurnResponse>(`intake/${intakeId}/messages`, { method: 'POST', body });
        if (turn(response)) {
          setPending(null);
          clearDraft(DRAFT_KEY);
        }
      } catch (e) {
        if (isNetworkError(e)) {
          // Keep the bubble and the draft; the effect below sends it when the connection returns.
          setPending({ ...message, queued: true });
          saveDraft(DRAFT_KEY, message.text);
          setError(t('offline'));
        } else if (e instanceof ApiError && (e.status >= 500 || e.status === 429)) {
          // The server may or may not have stored it: keep the bubble so "Retry" resends the same id.
          setPending({ ...message, failed: true });
          saveDraft(DRAFT_KEY, message.text);
          setError(failure(e));
        } else {
          setPending(null);
          setText(message.text);
          setError(failure(e));
        }
      } finally {
        sending.current = false;
        setWaiting(false);
      }
    },
    [failure, t, turn],
  );

  // A queued message goes out when the browser reports the connection is back, or on a slow retry while
  // the browser believes it is online but the request still failed.
  const wasOnline = useRef(online);
  useEffect(() => {
    const cameBack = online && !wasOnline.current;
    wasOnline.current = online;
    if (!pending?.queued || !intake) return;
    if (cameBack) {
      void deliver(pending, intake.intakeId);
      return;
    }
    const id = window.setInterval(() => {
      if (navigator.onLine) void deliver(pending, intake.intakeId);
    }, RETRY_MS);
    return () => window.clearInterval(id);
  }, [online, pending, intake, deliver]);

  function send() {
    const trimmed = text.trim();
    if (!trimmed || !intake || pending) return;
    const message: DisplayMessage = { id: `local-${Date.now()}`, role: 'patient', text: trimmed, at: new Date().toISOString(), clientMessageId: newClientMessageId() };
    setText('');
    saveDraft(DRAFT_KEY, trimmed);
    void deliver(message, intake.intakeId);
  }

  function retry(message: DisplayMessage) {
    if (intake) void deliver(message, intake.intakeId);
  }

  async function answer(value: number | null) {
    const question = intake?.pendingQuestion;
    if (!intake || !question) return;
    setAnswering(value);
    setWaiting(true);
    setError(null);
    try {
      turn(await api<IntakeTurnResponse>(`intake/${intake.intakeId}/answers`, { method: 'POST', body: { instrument: question.instrument, item: question.item, value } }));
    } catch (e) {
      setError(failure(e));
    } finally {
      setAnswering(null);
      setWaiting(false);
    }
  }

  async function pause() {
    if (!intake) return;
    setBusy('pause');
    setError(null);
    try {
      await api<IntakeState>(`intake/${intake.intakeId}/pause`, { method: 'POST' });
      // PB-24: saved; home shows "Continue intake" for the next 14 days.
      router.push('/home');
    } catch (e) {
      setError(failure(e));
      setBusy(null);
    }
  }

  async function switchToForm() {
    if (!intake) return;
    setBusy('form');
    setError(null);
    try {
      apply(await api<IntakeState>(`intake/${intake.intakeId}/form-mode`, { method: 'POST' }));
    } catch (e) {
      setError(failure(e));
    } finally {
      setBusy(null);
    }
  }

  async function resume() {
    setBusy('resume');
    if (await start()) setError(null);
    setBusy(null);
  }

  async function requestContact() {
    setBusy('contact');
    setError(null);
    try {
      setContactRequested(await api<ContactRequestCreated>('me/contact-request?source=intake', { method: 'POST' }));
    } catch (e) {
      setError(failure(e));
    } finally {
      setConfirmContact(false);
      setBusy(null);
    }
  }

  async function finish() {
    if (!intake?.canFinish || busy) return;
    setBusy('finish');
    setError(null);
    try {
      await api<IntakeSummary>(`intake/${intake.intakeId}/complete`, { method: 'POST' });
      router.push('/intake/summary');
    } catch (e) {
      setError(failure(e));
      setBusy(null);
    }
  }

  if (phase === 'loading') return <Spinner label={t('loading')} />;

  if (phase === 'error' && !intake) {
    return (
      <div className="flex flex-col gap-4 pt-6">
        <Notice tone="danger">{error}</Notice>
        <Button onClick={() => void resume()} busy={busy === 'resume'}>
          {tErrors('retry')}
        </Button>
      </div>
    );
  }

  if (phase === 'expired') {
    return (
      <div className="flex flex-col gap-4 pt-6" data-testid="intake-expired">
        <h1 className="text-2xl font-bold">{t('expiredTitle')}</h1>
        <p className="leading-relaxed text-muted">{t('expiredBody')}</p>
        {error ? <Notice tone="danger">{error}</Notice> : null}
        <Button full busy={busy === 'resume'} onClick={() => void resume()}>
          {t('restart')}
        </Button>
      </div>
    );
  }

  const messages: DisplayMessage[] = [...(intake?.messages ?? []), ...(pending ? [pending] : [])];
  const question = phase === 'ready' ? (intake?.pendingQuestion ?? null) : null;
  const paused = phase === 'paused';
  const canFinish = Boolean(intake?.canFinish) && !paused;
  const formMode = Boolean(intake?.formMode);
  const offerForm = !paused && !formMode && assistantUnavailable(intake);

  return (
    <div className="flex flex-col gap-4 pt-2">
      <header className="flex flex-col gap-3 border-b border-line pb-4">
        <div className="flex items-center gap-3">
          <span aria-hidden="true" className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-tint-teal text-primary">
            <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
              <circle cx="12" cy="12" r="9" className="fill-primary/20" stroke="none" />
              <path d="M9 10h.01M15 10h.01M8.5 14.5c1.8 1.6 5.2 1.6 7 0" />
            </svg>
          </span>
          <div className="flex flex-col">
            <h1 className="text-xl font-bold">{t('title')}</h1>
            <p className="text-sm text-muted">{t('subtitle')}</p>
          </div>
        </div>
        <div className="flex flex-wrap gap-2">
          {!paused ? (
            <Button variant="secondary" compact busy={busy === 'pause'} onClick={() => void pause()}>
              {t('pause')}
            </Button>
          ) : null}
          <Button variant="secondary" compact busy={busy === 'contact'} disabled={contactRequested !== null} onClick={() => setConfirmContact(true)}>
            {t('talkToPerson')}
          </Button>
        </div>
        <div className="flex items-center gap-3">
          <ProgressBar value={intake?.progress ?? 0} label={t('progressLabel')} />
          <span aria-hidden="true" className="shrink-0 text-sm font-semibold tabular-nums text-muted">
            {t('progressValue', { value: Math.round(intake?.progress ?? 0) })}
          </span>
        </div>
        {contactRequested ? (
          <Notice tone="success">{t('contactRequestedBy', { time: formatDateTime(contactRequested.expectedBy, locale) })}</Notice>
        ) : null}
        {offerForm ? (
          <div data-testid="form-mode-offer" className="flex flex-col gap-2 rounded-[16px] bg-tint-sand px-4 py-3">
            <p className="text-sm leading-relaxed">{t('formOffer')}</p>
            <Button variant="secondary" compact busy={busy === 'form'} onClick={() => void switchToForm()}>
              {t('switchToForm')}
            </Button>
          </div>
        ) : null}
        {formMode ? <Notice tone="info">{t('formModeOn')}</Notice> : null}
      </header>

      <Dialog
        open={confirmContact}
        onClose={() => setConfirmContact(false)}
        locked={busy === 'contact'}
        title={t('contactConfirmTitle')}
        description={t('contactConfirmBody', { hours: triageWithinHours })}
      >
        <div className="flex flex-col gap-2">
          <Button full busy={busy === 'contact'} onClick={() => void requestContact()}>
            {t('contactConfirm')}
          </Button>
          <Button full variant="secondary" disabled={busy === 'contact'} onClick={() => setConfirmContact(false)}>
            {t('contactCancel')}
          </Button>
        </div>
      </Dialog>

      <MessageList messages={messages} typing={waiting} scrollKey={`${messages.length}-${question?.item ?? ''}`} onRetry={retry} retrying={waiting} />

      {question ? <QuestionCard question={question} selected={answering} disabled={waiting} onAnswer={(v) => void answer(v)} /> : null}

      <div className="sticky bottom-0 -mx-4 flex flex-col gap-2 border-t border-line bg-bg px-4 pb-[max(12px,env(safe-area-inset-bottom))] pt-3">
        {error ? (
          <p role="alert" className="text-sm font-semibold text-help">
            {error}
          </p>
        ) : null}
        {busy === 'finish' ? <p role="status" className="text-sm text-muted">{t('finishing')}</p> : null}

        {paused && intake ? (
          <div className="flex flex-col gap-3" data-testid="paused-notice">
            <Notice tone="success">
              <span className="font-bold">{t('pausedTitle')}</span>
              <span className="block">{t('pausedBody', { date: formatDate(intake.expiresAt, locale) })}</span>
            </Notice>
            <Button full busy={busy === 'resume'} onClick={() => void resume()}>
              {t('resume')}
            </Button>
          </div>
        ) : formMode ? null : (
          <Composer value={text} onChange={changeText} onSend={send} disabled={!intake || pending !== null || busy === 'finish'} />
        )}

        <div className="flex items-center justify-between gap-3">
          {question ? (
            <button type="button" className="min-h-touch text-sm font-semibold text-primary disabled:opacity-50" disabled={waiting} onClick={() => void answer(null)}>
              {t('skip')}
            </button>
          ) : (
            <span />
          )}
          <button
            type="button"
            aria-disabled={!canFinish || busy !== null}
            aria-describedby={canFinish ? undefined : 'intake-finish-hint'}
            onClick={() => void finish()}
            className={`min-h-touch text-sm font-semibold ${canFinish ? 'text-ink' : 'cursor-not-allowed text-muted opacity-60'}`}
            data-testid="finish"
          >
            {t('finish')}
          </button>
        </div>
        {!canFinish ? (
          <p id="intake-finish-hint" className="sr-only">
            {t('finishHint')}
          </p>
        ) : null}
      </div>
    </div>
  );
}
