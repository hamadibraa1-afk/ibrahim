import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { HomeView } from '@/components/home/HomeView';
import { BottomNav, BottomNavSpacer } from '@/components/ui/BottomNav';
import { isLocale, routing } from '@/i18n/routing';
import { getMe, serverGet } from '@/lib/api/server';
import type { HomeSummary } from '@/lib/api/types';
import { DEFAULT_TIME_ZONE } from '@/lib/format';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'home' });
  return { title: t('metaTitle') };
}

/** P09 Home: mood in one tap, the next session, the latest note, today's homework and the safety plan. */
export default async function HomePage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [home, me] = await Promise.all([serverGet<HomeSummary>('home'), getMe()]);
  return (
    <>
      <HomeView home={home} locale={isLocale(locale) ? locale : routing.defaultLocale} timeZone={me.timeZone || DEFAULT_TIME_ZONE} now={new Date()} />
      <BottomNavSpacer />
      <BottomNav active="home" />
    </>
  );
}
