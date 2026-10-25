import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { ProfileForm } from '@/components/settings/ProfileForm';
import { SettingsMenu } from '@/components/settings/SettingsMenu';
import { BottomNav, BottomNavSpacer } from '@/components/ui/BottomNav';
import { serverGetVersioned } from '@/lib/api/server';
import type { Me } from '@/lib/api/types';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'settings' });
  return { title: t('title') };
}

/** P31 Settings: the section menu and the profile form. */
export default async function SettingsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  // GET /v1/me carries the profile version the save sends back in If-Match.
  const [me, t] = await Promise.all([serverGetVersioned<Me>('me'), getTranslations('settings')]);
  return (
    <>
      <div className="flex flex-col gap-6 pt-4">
        <h1 className="text-2xl font-bold text-primary">{t('title')}</h1>
        <SettingsMenu current="profile" />
        <ProfileForm me={me.data} version={me.etag} />
      </div>
      <BottomNavSpacer />
      <BottomNav active="settings" />
    </>
  );
}
