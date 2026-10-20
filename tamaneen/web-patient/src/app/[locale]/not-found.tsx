import { useTranslations } from 'next-intl';
import { ButtonLink } from '@/components/ui/Button';
import { Link } from '@/i18n/navigation';

/** P35 404: the localized not-found page, with a way home and to the emergency resources. */
export default function NotFound() {
  const t = useTranslations('notFound');
  return (
    <div className="mx-auto flex max-w-md flex-col items-center gap-4 pt-16 text-center">
      <p aria-hidden="true" className="text-6xl font-bold tabular-nums text-primary/30">
        {t('code')}
      </p>
      <h1 className="text-2xl font-bold">{t('title')}</h1>
      <p className="leading-relaxed text-muted">{t('body')}</p>
      <ButtonLink href="/" full>
        {t('home')}
      </ButtonLink>
      <Link href="/crisis-resources" className="inline-flex min-h-touch items-center font-semibold text-primary underline underline-offset-4">
        {t('crisisResources')}
      </Link>
    </div>
  );
}
