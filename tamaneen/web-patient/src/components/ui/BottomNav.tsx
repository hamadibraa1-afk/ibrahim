import { getTranslations } from 'next-intl/server';
import { Link } from '@/i18n/navigation';

export type NavTab = 'home' | 'appointments' | 'homework' | 'settings';

const tabs: { key: NavTab; href: string; icon: string }[] = [
  { key: 'home', href: '/home', icon: 'M3 11l9-7 9 7v9a1 1 0 01-1 1h-5v-6H9v6H4a1 1 0 01-1-1z' },
  { key: 'appointments', href: '/appointments', icon: 'M4 6h16v14H4zM4 10h16M8 3v4M16 3v4' },
  { key: 'homework', href: '/homework', icon: 'M9 11l2 2 4-4M5 4h14v16H5z' },
  { key: 'settings', href: '/settings', icon: 'M12 15a3 3 0 100-6 3 3 0 000 6zM19 12l2-1-2-4-2 1-2-2V4h-4v2L9 7 7 6 5 10l2 1v2l-2 1 2 4 2-1 2 2v2h4v-2l2-2 2 1 2-4-2-1z' },
];

/** Main tabs of the signed-in app (P09, P27, P29, P31). */
export async function BottomNav({ active }: { active: NavTab }) {
  const t = await getTranslations('nav');
  return (
    <nav aria-label={t('label')} className="fixed inset-x-0 bottom-0 z-20 border-t border-line bg-surface pb-[env(safe-area-inset-bottom)]">
      <ul className="mx-auto flex max-w-3xl">
        {tabs.map((tab) => (
          <li key={tab.key} className="flex-1">
            <Link
              href={tab.href}
              aria-current={tab.key === active ? 'page' : undefined}
              className={`flex min-h-[60px] flex-col items-center justify-center gap-1 text-xs font-semibold ${tab.key === active ? 'text-primary' : 'text-muted'}`}
            >
              <svg aria-hidden="true" width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinejoin="round" strokeLinecap="round">
                <path d={tab.icon} />
              </svg>
              {t(tab.key)}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}

/** Space so page content is not hidden behind the fixed bottom navigation. */
export function BottomNavSpacer() {
  return <div aria-hidden="true" className="h-20" />;
}
