import { useTranslations } from 'next-intl';
import { Link } from '@/i18n/navigation';

export default function NotFound() {
  const t = useTranslations('notFound');
  return (
    <div className="flex flex-col items-center gap-4 pt-16 text-center">
      <h1 className="text-2xl font-bold">{t('title')}</h1>
      <p className="max-w-md text-muted">{t('body')}</p>
      <Link href="/" className="flex min-h-cta items-center rounded-full bg-primary px-6 font-semibold text-surface">
        {t('home')}
      </Link>
    </div>
  );
}
