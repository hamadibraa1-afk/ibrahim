import { getTranslations, setRequestLocale } from 'next-intl/server';
import { EmergencyLines } from '@/components/pwa/EmergencyLines';
import { RememberReturnPath } from '@/components/pwa/RememberReturnPath';
import { StatePanel } from '@/components/pwa/StatePanel';
import { ButtonLink } from '@/components/ui/Button';
import { safeReturnPath } from '@/lib/returnPath';

/**
 * P35 Session expired. Unsent text stays on the device for 10 minutes (see components/pwa/drafts), and
 * ?next= (validated) is remembered so signing in again returns to that page.
 */
export default async function SessionExpiredPage({ params, searchParams }: { params: Promise<{ locale: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const [{ locale }, query] = await Promise.all([params, searchParams]);
  setRequestLocale(locale);
  const t = await getTranslations('systemStates.sessionExpired');
  const next = safeReturnPath(typeof query.next === 'string' ? query.next : null);
  return (
    <StatePanel icon="clock" title={t('title')} body={t('body')}>
      <RememberReturnPath path={next} />
      <ButtonLink href="/login" full>
        {t('login')}
      </ButtonLink>
      <EmergencyLines />
    </StatePanel>
  );
}
