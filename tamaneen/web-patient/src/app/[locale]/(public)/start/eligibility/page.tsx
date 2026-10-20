import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { EligibilityForm } from '@/components/onboarding/EligibilityForm';
import { BackLink } from '@/components/ui/BackLink';
import { ButtonLink } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { publicGet } from '@/lib/api/server';
import type { PublicConfig } from '@/lib/api/types';

export const dynamic = 'force-dynamic';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'eligibility' });
  return { title: t('metaTitle') };
}

/** P15 Eligibility (public). The pilot cities and age come from the public configuration. */
export default async function EligibilityPage({ params, searchParams }: { params: Promise<{ locale: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, config, query] = await Promise.all([getTranslations('eligibility'), publicGet<PublicConfig>('meta/config'), searchParams]);

  return (
    <div className="flex flex-col gap-4 pt-2">
      <BackLink href="/" />
      {config && config.pilotCities.length > 0 ? (
        <EligibilityForm
          cities={config.pilotCities}
          region={config.pilotRegion}
          minAge={config.minAge}
          emergencyNumber={config.emergencyNumber || null}
          supportLine={config.supportLine || null}
          restarted={query.restart === '1'}
        />
      ) : (
        <div className="flex flex-col gap-4">
          <h1 className="text-2xl font-bold leading-snug">{t('title')}</h1>
          <Notice tone="warning">{t('configError')}</Notice>
          <ButtonLink href="/start/eligibility" variant="secondary" full>
            {t('retry')}
          </ButtonLink>
        </div>
      )}
    </div>
  );
}
