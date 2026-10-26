import { useTranslations } from 'next-intl';
import { Link } from '@/i18n/navigation';

/** Home (P09) after a supervisor signed off the end of care: the way to P53. */
export function CareEndedCard() {
  const t = useTranslations('care.home');
  return (
    <Link href="/care/complete" className="flex flex-col gap-1 rounded-card bg-tint-sand px-5 py-4 text-ink" data-testid="care-ended">
      <span className="font-bold">{t('title')}</span>
      <span className="text-sm">{t('body')}</span>
      <span className="text-sm font-semibold text-primary-strong">{t('link')}</span>
    </Link>
  );
}
