'use client';

import { useTranslations } from 'next-intl';
import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { Link, usePathname, useRouter } from '@/i18n/navigation';
import { apiMessage } from '@/components/pwa/errors';
import { AbroadEmergencyLine } from './AbroadEmergencyLine';
import { CallLink } from './CallLink';
import { flushHelp, HELP_RETRY_MS, pendingHelp, requestHelp, type HelpResult } from './helpNow';
import { useHelpNowSession, type HelpNowSession } from './helpNowSession';

const HELP_CLASS = 'inline-flex min-h-touch items-center rounded-full border-[1.5px] border-help bg-surface px-4 text-sm font-semibold text-help';

/**
 * The header's Help now. Signed in (announced by the signed-in layout), it is the button below. Otherwise, and
 * before the page is interactive, it is a link to the public emergency resources (P14, cached for offline use).
 */
export function HelpNow() {
  const tCommon = useTranslations('common');
  const session = useHelpNowSession();
  if (session) return <HelpNowButton {...session} />;
  return (
    <Link href="/crisis-resources" aria-label={tCommon('helpNowLabel')} data-testid="help-now" className={HELP_CLASS}>
      {tCommon('helpNow')}
    </Link>
  );
}

type Phase = 'closed' | 'sending' | 'queued' | 'failed';

/**
 * "Help now" in the header for a signed-in patient (PB-26). Never disabled. A press shows the emergency
 * numbers at once, over whatever screen is open, then sends POST /v1/crisis/help (a Critical event) and opens
 * the crisis screen (P06). Offline, or while the API is slow or down, the numbers stay on screen and the request
 * waits in the queue (EC-04): it is sent when the connection comes back, from any signed-in page.
 */
export function HelpNowButton({ emergencyNumber, supportLine }: HelpNowSession) {
  const tCommon = useTranslations('common');
  const t = useTranslations('crisis');
  const router = useRouter();
  const pathname = usePathname();
  const titleId = useId();
  const firstAction = useRef<HTMLDivElement>(null);
  const [phase, setPhase] = useState<Phase>('closed');
  const [error, setError] = useState<string | null>(null);

  const openCrisis = useCallback(() => {
    if (pathname === '/crisis') {
      router.refresh();
      setPhase('closed');
    } else {
      router.push('/crisis');
    }
  }, [pathname, router]);

  const handle = useCallback(
    (result: HelpResult | null) => {
      if (!result) return;
      if (result.status === 'sent') {
        setError(null);
        openCrisis();
      } else if (result.status === 'queued') {
        setPhase((p) => (p === 'closed' ? p : 'queued'));
      } else {
        setError(apiMessage(result.error) ?? t('helpNow.failed'));
        setPhase((p) => (p === 'closed' ? p : 'failed'));
      }
    },
    [openCrisis, t],
  );

  // The crisis screen is open: the overlay has done its job.
  useEffect(() => {
    if (pathname === '/crisis') setPhase('closed');
  }, [pathname]);

  // Deliver a request that was queued earlier: now, when the browser is back online, and every few seconds.
  useEffect(() => {
    const retry = () => {
      if (pendingHelp()) void flushHelp().then(handle);
    };
    retry();
    window.addEventListener('online', retry);
    const id = window.setInterval(retry, HELP_RETRY_MS);
    return () => {
      window.removeEventListener('online', retry);
      window.clearInterval(id);
    };
  }, [handle]);

  // Focus the first call button when the panel opens; Escape closes it (a queued request keeps waiting).
  const open = phase !== 'closed';
  useEffect(() => {
    if (!open) return;
    firstAction.current?.querySelector<HTMLElement>('a[href], button')?.focus();
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') setPhase('closed');
    };
    document.addEventListener('keydown', onKeyDown);
    return () => document.removeEventListener('keydown', onKeyDown);
  }, [open]);

  function press() {
    setError(null);
    setPhase('sending');
    void requestHelp().then(handle);
  }

  return (
    <>
      <button
        type="button"
        onClick={press}
        aria-label={tCommon('helpNowLabel')}
        data-testid="help-now"
        className={HELP_CLASS}
      >
        {tCommon('helpNow')}
      </button>
      {!open ? null : (
        <div role="dialog" aria-modal="true" aria-labelledby={titleId} data-testid="help-now-panel" className="fixed inset-0 z-50 overflow-y-auto bg-bg text-ink">
          <div ref={firstAction} className="mx-auto flex w-full max-w-3xl flex-col gap-5 px-4 py-6">
            <div aria-hidden="true" className="h-2 rounded-full bg-help" />
            <h2 id={titleId} className="text-3xl font-bold leading-snug">
              {t('title')}
            </h2>
            <section aria-label={t('dangerTitle')} className="flex flex-col gap-3">
              <p className="text-lg font-bold">{t('dangerTitle')}</p>
              {emergencyNumber ? (
                <CallLink number={emergencyNumber} icon>
                  {t('callEmergency')}
                </CallLink>
              ) : (
                <p className="rounded-[18px] bg-help px-5 py-4 text-lg font-bold text-surface">{t('noNumber')}</p>
              )}
              {supportLine ? (
                <CallLink number={supportLine} variant="outline">
                  {t('supportLine')}
                </CallLink>
              ) : null}
              <AbroadEmergencyLine />
            </section>
            <div aria-live="polite" className="rounded-[16px] bg-tint-sand px-4 py-3 font-semibold" data-testid="help-now-status">
              {phase === 'sending' ? t('helpNow.sending') : phase === 'queued' ? t('helpNow.queued') : null}
              {phase === 'failed' ? <span role="alert">{error}</span> : null}
            </div>
            <p className="leading-relaxed">{t('untilBody')}</p>
            <button type="button" onClick={() => setPhase('closed')} className="flex min-h-touch items-center justify-center font-semibold text-primary underline underline-offset-4">
              {t('helpNow.close')}
            </button>
          </div>
        </div>
      )}
    </>
  );
}
