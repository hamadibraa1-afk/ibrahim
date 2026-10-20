import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { timeZoneOf } from '@/components/matching/labels';
import { MatchSuggestions } from '@/components/matching/MatchSuggestions';
import { ButtonLink } from '@/components/ui/Button';
import { PageHeading } from '@/components/ui/PageHeading';
import { getMe, serverGet } from '@/lib/api/server';
import type { MatchList } from '@/lib/api/types';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'matching' });
  return { title: t('metaTitle') };
}

/** P07 Matches: the therapists a professional approved for this patient, or a pointer to the review status. */
export default async function MatchesPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, me, matches] = await Promise.all([getTranslations('matching'), getMe(), serverGet<MatchList>('matches')]);

  if (matches.status === 'pending') {
    return (
      <div className="flex flex-col gap-5 pt-4">
        <PageHeading title={t('pendingTitle')} subtitle={t('pendingBody')} />
        <ButtonLink href="/waiting" full>
          {t('pendingAction')}
        </ButtonLink>
        <ButtonLink href="/therapists" variant="ghost" full>
          {t('browseAll')}
        </ButtonLink>
      </div>
    );
  }

  return <MatchSuggestions initial={matches.items} patientLanguage={me.language} timeZone={timeZoneOf(me)} />;
}
