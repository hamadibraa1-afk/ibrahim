import { useTranslations } from 'next-intl';
import { Link } from '@/i18n/navigation';

const items = [
  { key: 'profile', href: '/settings' },
  { key: 'security', href: '/settings/security' },
  { key: 'privacy', href: '/privacy' },
  { key: 'notifications', href: '/settings/notifications' },
  { key: 'safety', href: '/settings/safety' },
] as const;

export type SettingsSection = (typeof items)[number]['key'];

/** The settings sections (P31). The current one is highlighted and marked aria-current. */
export function SettingsMenu({ current }: { current: SettingsSection }) {
  const t = useTranslations('settings');
  return (
    <nav aria-label={t('menuLabel')}>
      <ul className="overflow-hidden rounded-card border border-line bg-surface">
        {items.map((item) => (
          <li key={item.key} className="border-b border-line last:border-b-0">
            <Link
              href={item.href}
              aria-current={item.key === current ? 'page' : undefined}
              className={`flex min-h-[52px] items-center px-5 text-base font-semibold ${item.key === current ? 'bg-tint-teal text-primary-strong' : 'text-ink'}`}
            >
              {t(`menu.${item.key}`)}
            </Link>
          </li>
        ))}
      </ul>
    </nav>
  );
}
