import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { HomeworkListView } from '@/components/homework/HomeworkListView';
import { BottomNav, BottomNavSpacer } from '@/components/ui/BottomNav';
import { isLocale, routing } from '@/i18n/routing';
import { getMe, serverGet } from '@/lib/api/server';
import type { HomeworkList } from '@/lib/api/types';
import { DEFAULT_TIME_ZONE } from '@/lib/format';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'homework' });
  return { title: t('listTitle') };
}

/** P29 My homework. */
export default async function HomeworkPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [list, me] = await Promise.all([serverGet<HomeworkList>('homework'), getMe()]);
  return (
    <>
      <HomeworkListView list={list} locale={isLocale(locale) ? locale : routing.defaultLocale} timeZone={me.timeZone || DEFAULT_TIME_ZONE} />
      <BottomNavSpacer />
      <BottomNav active="homework" />
    </>
  );
}
