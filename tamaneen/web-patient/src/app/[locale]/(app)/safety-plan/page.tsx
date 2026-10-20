import { getTranslations, setRequestLocale } from 'next-intl/server';
import { CallLink } from '@/components/crisis/CallLink';
import { SafetyPlanView } from '@/components/safety/SafetyPlanView';
import { BackLink } from '@/components/ui/BackLink';
import { Link } from '@/i18n/navigation';
import { loadSafetyNumbers } from '@/lib/platform-config';

/** P37 Safety plan. The emergency numbers are server-rendered tel: links handed to the client view. */
export default async function SafetyPlanPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, { emergencyNumber, supportLine }] = await Promise.all([getTranslations('safetyPlan'), loadSafetyNumbers()]);

  const numbers = (
    <div className="flex flex-wrap gap-2">
      {emergencyNumber ? (
        <CallLink number={emergencyNumber} variant="chip">
          {t('emergency')}
        </CallLink>
      ) : (
        <Link href="/crisis-resources" className="inline-flex min-h-touch items-center rounded-full bg-help/10 px-4 text-sm font-bold text-help">
          {t('resourcesLink')}
        </Link>
      )}
      {supportLine ? (
        <CallLink number={supportLine} variant="chip">
          {t('supportLine')}
        </CallLink>
      ) : null}
    </div>
  );

  return (
    <div className="flex flex-col gap-2">
      <BackLink href="/home" />
      <SafetyPlanView numbers={numbers} />
    </div>
  );
}
