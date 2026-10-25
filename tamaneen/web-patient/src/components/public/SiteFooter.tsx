import { getTranslations } from 'next-intl/server';
import { Link } from '@/i18n/navigation';

/** Footer of the public screens: how the service works (P12) and the legal documents (P13). */
export async function SiteFooter() {
  const t = await getTranslations('welcome');
  const links = [
    { key: 'how', href: '/how-it-works', label: t('footer.howItWorks') },
    { key: 'privacy', href: { pathname: '/legal', query: { doc: 'privacy' } }, label: t('footer.privacy') },
    { key: 'terms', href: { pathname: '/legal', query: { doc: 'terms' } }, label: t('footer.terms') },
  ] as const;
  return (
    <footer className="mt-8 border-t border-line pt-4">
      <nav aria-label={t('footer.label')}>
        <ul className="flex flex-wrap items-center justify-center gap-x-5 gap-y-1">
          {links.map((l) => (
            <li key={l.key}>
              <Link href={l.href} className="inline-flex min-h-touch items-center text-sm font-semibold text-primary underline-offset-4 hover:underline">
                {l.label}
              </Link>
            </li>
          ))}
        </ul>
      </nav>
    </footer>
  );
}
