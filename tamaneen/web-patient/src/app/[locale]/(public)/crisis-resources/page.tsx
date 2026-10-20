import { getTranslations, setRequestLocale } from 'next-intl/server';
import { CallLink } from '@/components/crisis/CallLink';
import { RequestCallButton } from '@/components/crisis/RequestCallButton';
import { BackLink } from '@/components/ui/BackLink';
import { hasSession } from '@/lib/api/server';
import { loadSafetyNumbers } from '@/lib/platform-config';

// Rendered per request (numbers are runtime configuration) and cached by the service worker for offline use.
export const dynamic = 'force-dynamic';

const INTERNATIONAL_EMERGENCY = '112';

/** P14 Crisis resources: public, works offline, every number is a server-rendered tel: link. */
export default async function CrisisResourcesPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('crisisResources');
  const [{ emergencyNumber, supportLine }, signedIn] = await Promise.all([loadSafetyNumbers(), hasSession()]);

  return (
    <div className="flex flex-col gap-5 pt-2">
      <div className="flex items-center justify-between gap-3">
        <BackLink href={signedIn ? '/home' : '/'} />
        <span className="inline-flex items-center gap-2 rounded-full bg-tint-teal px-4 py-1.5 text-sm font-semibold text-primary-strong">
          <svg aria-hidden="true" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
            <path d="M5 12l4 4 10-10" />
          </svg>
          {t('offlineBadge')}
        </span>
      </div>

      <h1 className="text-3xl font-bold leading-snug">{t('title')}</h1>

      <div className="flex flex-col gap-3">
        {emergencyNumber ? (
          <CallLink number={emergencyNumber} icon>
            {t('emergency')}
          </CallLink>
        ) : (
          <p className="flex min-h-[60px] items-center justify-center rounded-[18px] bg-help px-5 text-center text-lg font-bold text-surface">{t('noEmergencyNumber')}</p>
        )}
        {supportLine ? (
          <CallLink number={supportLine} variant="outline">
            {t('supportLine')}
          </CallLink>
        ) : null}
        <div className="flex flex-col gap-1 rounded-[18px] bg-tint-sand px-5 py-4 text-center">
          <p className="font-bold">{t('hospital')}</p>
          <p className="text-sm leading-relaxed">{t('hospitalBody')}</p>
        </div>
      </div>

      {signedIn ? <RequestCallButton label={t('requestCall')} hint={t('requestCallHint')} /> : null}

      <section aria-labelledby="cr-steps" className="flex flex-col gap-3 rounded-card border-[1.5px] border-line bg-surface p-5">
        <h2 id="cr-steps" className="text-lg font-bold">
          {t('stepsTitle')}
        </h2>
        <ul className="flex flex-col gap-3">
          {[t('step1'), t('step2'), t('step3')].map((step) => (
            <li key={step} className="flex items-start gap-3 leading-relaxed">
              <span aria-hidden="true" className="mt-2.5 h-2 w-2 shrink-0 rounded-full bg-primary" />
              <span>{step}</span>
            </li>
          ))}
        </ul>
      </section>

      <section aria-labelledby="cr-helping" className="flex flex-col gap-2 rounded-card border-[1.5px] border-line bg-surface p-5">
        <h2 id="cr-helping" className="text-lg font-bold">
          {t('helpingTitle')}
        </h2>
        <p className="leading-relaxed text-muted">{t('helpingBody')}</p>
      </section>

      <p className="text-sm leading-relaxed text-muted">
        {t.rich('abroad', {
          number: INTERNATIONAL_EMERGENCY,
          call: (chunks) => (
            <a href={`tel:${INTERNATIONAL_EMERGENCY}`} className="font-bold text-help underline underline-offset-4">
              <bdi dir="ltr">{chunks}</bdi>
            </a>
          ),
        })}
      </p>
    </div>
  );
}
