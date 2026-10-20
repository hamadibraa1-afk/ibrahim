import { getTranslations } from 'next-intl/server';
import type { ComponentProps } from 'react';
import { Link } from '@/i18n/navigation';

/** "Back" with an arrow that points the right way in both directions (it flips with dir). */
export async function BackLink({ href, label }: { href: ComponentProps<typeof Link>['href']; label?: string }) {
  const t = await getTranslations('ui');
  return (
    <Link href={href} className="inline-flex min-h-touch items-center gap-2 text-sm font-semibold text-primary">
      <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="rtl:rotate-180">
        <path d="M15 6l-6 6 6 6" />
      </svg>
      {label ?? t('back')}
    </Link>
  );
}
