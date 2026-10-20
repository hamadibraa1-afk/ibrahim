import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { ProfileForm } from '@/components/settings/ProfileForm';
import { SettingsMenu } from '@/components/settings/SettingsMenu';
import { BottomNav, BottomNavSpacer } from '@/components/ui/BottomNav';
import { getMe } from '@/lib/api/server';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'settings' });
  return { title: t('title') };
}

/** P31 Settings: the section menu and the profile form. */
export default async function SettingsPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [me, t] = await Promise.all([getMe(), getTranslations('settings')]);
  return (
    <>
      <div className="flex flex-col gap-6 pt-4">
        <h1 className="text-2xl font-bold text-primary">{t('title')}</h1>
        <SettingsMenu current="profile" />
        <ProfileForm me={me} />
      </div>
      <BottomNavSpacer />
      <BottomNav active="settings" />
    </>
  );
}
