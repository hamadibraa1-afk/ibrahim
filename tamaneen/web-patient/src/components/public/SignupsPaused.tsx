import { getTranslations } from 'next-intl/server';
import { CallLink } from '@/components/crisis/CallLink';
import { Link } from '@/i18n/navigation';

/**
 * EC-26: shown in place of the start button and the sign-up form while the platform has paused new
 * sign-ups. The emergency numbers stay one tap away; existing patients can still sign in.
 */
export async function SignupsPaused({ emergencyNumber, supportLine }: { emergencyNumber: string | null; supportLine: string | null }) {
  const t = await getTranslations('welcome');
  return (
    <section data-testid="signups-paused" aria-labelledby="signups-paused-title" className="flex flex-col gap-3 rounded-card border-[1.5px] border-line bg-surface p-4">
      <h2 id="signups-paused-title" className="text-lg font-bold">
        {t('paused.title')}
      </h2>
      <p className="text-sm leading-relaxed text-muted">{t('paused.body')}</p>
      {emergencyNumber ? (
        <CallLink number={emergencyNumber} icon>
          {t('paused.emergency')}
        </CallLink>
      ) : null}
      {supportLine ? (
        <CallLink number={supportLine} variant="outline">
          {t('paused.support')}
        </CallLink>
      ) : null}
      {!emergencyNumber && !supportLine ? (
        <Link href="/crisis-resources" className="text-sm font-semibold text-help underline underline-offset-4">
          {t('emergencyResources')}
        </Link>
      ) : null}
    </section>
  );
}
