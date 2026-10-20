import { getTranslations, setRequestLocale } from 'next-intl/server';
import { Link } from '@/i18n/navigation';
import { publicPlatformConfig } from '@/lib/platform-config';

// Rendered per request: the emergency number and matching window are runtime configuration.
export const dynamic = 'force-dynamic';

/** P01 Welcome: the first screen a visitor sees, before any account exists. */
export default async function WelcomePage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('welcome');
  const { emergencyNumber, matchWithinHours } = publicPlatformConfig();

  return (
    <div className="flex flex-col gap-5 pt-4">
      <section aria-hidden="true" className="relative flex h-56 items-end justify-center overflow-hidden rounded-card bg-tint-teal p-5">
        <svg className="absolute inset-x-0 top-6 mx-auto" width="200" height="120" viewBox="0 0 200 120" fill="none">
          <ellipse cx="100" cy="110" rx="80" ry="8" className="fill-tint-sand" />
          <circle cx="150" cy="30" r="18" className="fill-accent/40" />
          <path d="M60 100c0-30 20-55 40-55s40 25 40 55" className="stroke-primary" strokeWidth="5" strokeLinecap="round" />
          <path d="M100 45V20" className="stroke-primary" strokeWidth="5" strokeLinecap="round" />
        </svg>
        <p className="relative rounded-full bg-surface/90 px-4 py-2 text-sm font-semibold text-primary-strong">{t('calm')}</p>
      </section>

      <div className="flex flex-col gap-2">
        <h1 className="text-3xl font-bold leading-snug">{t('title')}</h1>
        <p className="text-base leading-relaxed text-muted">{t('subtitle', { hours: matchWithinHours })}</p>
      </div>

      <ul className="flex flex-col gap-3">
        {[t('trustPrivate'), t('trustHuman')].map((line) => (
          <li key={line} className="flex items-center gap-3 rounded-[20px] bg-surface px-4 py-3 shadow-[0_1px_0_rgb(var(--line))]">
            <span aria-hidden="true" className="flex h-10 w-10 shrink-0 items-center justify-center rounded-[14px] bg-tint-teal text-primary">
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                <path d="M5 12l4 4 10-10" />
              </svg>
            </span>
            <span className="text-sm font-semibold">{line}</span>
          </li>
        ))}
      </ul>

      <div className="flex flex-col gap-3">
        <Link href="/start/eligibility" data-testid="cta-start" className="flex min-h-cta items-center justify-center rounded-full bg-primary px-6 text-base font-semibold text-surface">
          {t('start')}
        </Link>
        <Link href="/login" className="flex min-h-cta items-center justify-center rounded-full border-[1.5px] border-tint-teal bg-surface px-6 text-base font-semibold text-primary">
          {t('haveAccount')}
        </Link>
      </div>

      {emergencyNumber ? (
        <a href={`tel:${emergencyNumber}`} className="text-center text-sm font-semibold text-help">
          {t('emergencyCall', { number: emergencyNumber })}
        </a>
      ) : (
        <Link href="/crisis-resources" className="text-center text-sm font-semibold text-help">
          {t('emergencyResources')}
        </Link>
      )}
    </div>
  );
}
