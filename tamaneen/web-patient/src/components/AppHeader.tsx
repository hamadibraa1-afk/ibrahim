import { getTranslations } from 'next-intl/server';
import { Suspense } from 'react';
import { HelpNowLink } from './HelpNowLink';
import { LanguageToggle } from './LanguageToggle';
import { NotificationBell } from './notifications/NotificationBell';

/** The top bar on every patient screen: brand, language switch, the notification bell (signed in) and Help now. */
export async function AppHeader() {
  const t = await getTranslations('common');
  return (
    <header className="mx-auto flex w-full max-w-3xl items-center justify-between gap-3 px-4 pt-4">
      <span data-testid="brand" className="text-xl font-bold text-primary">{t('appName')}</span>
      <div className="flex items-center gap-2">
        <Suspense fallback={null}>
          <LanguageToggle />
        </Suspense>
        <NotificationBell />
        <HelpNowLink />
      </div>
    </header>
  );
}
