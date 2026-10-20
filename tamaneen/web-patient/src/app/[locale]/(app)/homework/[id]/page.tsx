import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { HomeworkDetailView } from '@/components/homework/HomeworkDetailView';
import { BackLink } from '@/components/ui/BackLink';
import { isLocale, routing } from '@/i18n/routing';
import { getMe, serverGetOptional } from '@/lib/api/server';
import type { HomeworkDetail } from '@/lib/api/types';
import { DEFAULT_TIME_ZONE } from '@/lib/format';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'homework' });
  return { title: t('detailTitle') };
}

/** P10 Homework: instructions and today's check-in. */
export default async function HomeworkDetailPage({ params }: { params: Promise<{ locale: string; id: string }> }) {
  const { locale, id } = await params;
  setRequestLocale(locale);
  const [homework, me, t] = await Promise.all([serverGetOptional<HomeworkDetail>(`homework/${encodeURIComponent(id)}`), getMe(), getTranslations('homework')]);
  if (!homework) notFound();
  return (
    <div className="flex flex-col gap-3 pt-2">
      <BackLink href="/homework" label={t('back')} />
      <HomeworkDetailView homework={homework} locale={isLocale(locale) ? locale : routing.defaultLocale} timeZone={me.timeZone || DEFAULT_TIME_ZONE} />
    </div>
  );
}
