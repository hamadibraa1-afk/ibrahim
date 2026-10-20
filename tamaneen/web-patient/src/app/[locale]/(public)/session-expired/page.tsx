import { getTranslations, setRequestLocale } from 'next-intl/server';
import { StatePanel } from '@/components/pwa/StatePanel';
import { ButtonLink } from '@/components/ui/Button';

/** P35 Session expired. Unsent text stays on the device for 10 minutes (see components/pwa/drafts). */
export default async function SessionExpiredPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('systemStates.sessionExpired');
  return (
    <StatePanel icon="clock" title={t('title')} body={t('body')}>
      <ButtonLink href="/login" full>
        {t('login')}
      </ButtonLink>
    </StatePanel>
  );
}
