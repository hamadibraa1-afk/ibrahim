import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { parseFilters, therapistsPath } from '@/components/matching/directoryQuery';
import { timeZoneOf } from '@/components/matching/labels';
import { TherapistDirectory } from '@/components/matching/TherapistDirectory';
import { BackLink } from '@/components/ui/BackLink';
import { getMe, serverGet } from '@/lib/api/server';
import type { TherapistSummary } from '@/lib/api/types';

type SearchParams = Record<string, string | string[] | undefined>;

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'therapists' });
  return { title: t('metaTitle') };
}

/** P24 Directory: search and filter every therapist in the pilot. */
export default async function TherapistsPage({ params, searchParams }: { params: Promise<{ locale: string }>; searchParams: Promise<SearchParams> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const filters = parseFilters(await searchParams);
  const [me, results] = await Promise.all([getMe(), serverGet<TherapistSummary[]>(therapistsPath(filters))]);

  return (
    <div className="flex flex-col gap-2 pt-2">
      <BackLink href="/home" />
      <TherapistDirectory initialFilters={filters} initialResults={results} timeZone={timeZoneOf(me)} />
    </div>
  );
}
