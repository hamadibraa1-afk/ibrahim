import { useTranslations } from 'next-intl';
import type { Locale } from '@/i18n/routing';
import { Link } from '@/i18n/navigation';
import type { HomeworkItem, HomeworkList } from '@/lib/api/types';
import { formatDate } from '@/lib/format';
import { EmptyState } from '@/components/ui/EmptyState';
import { pluralKey } from './plural';

/** "Daily until 28 October" / "Twice a week". */
export function useSchedule(locale: Locale, timeZone: string) {
  const t = useTranslations('homework');
  return (item: Pick<HomeworkItem, 'frequency' | 'endsOn'>) => {
    const frequency = t(`frequency.${item.frequency}`);
    return item.frequency === 'daily' ? t('until', { frequency, date: formatDate(item.endsOn, locale, timeZone, { day: 'numeric', month: 'long' }) }) : frequency;
  };
}

function ActiveCard({ item, locale, timeZone }: { item: HomeworkItem; locale: Locale; timeZone: string }) {
  const t = useTranslations('homework');
  const schedule = useSchedule(locale, timeZone);
  const done = Math.min(7, item.doneThisPeriod);
  const showCount = item.frequency !== 'daily' && item.frequency !== 'once' && item.doneThisPeriod > 0;

  return (
    <li>
      <Link href={`/homework/${item.id}`} className="flex flex-col gap-3 rounded-card border border-line bg-surface p-4 text-ink">
        <span className="flex items-start justify-between gap-3">
          <span className="text-base font-bold">{item.title}</span>
          {item.hasNewFeedback ? (
            <span className="shrink-0 rounded-full bg-primary px-3 py-1 text-xs font-bold text-surface">{t('newReply')}</span>
          ) : item.dueToday ? (
            <span className="shrink-0 text-xs font-bold text-primary">{t('dueToday')}</span>
          ) : null}
        </span>
        <span className="text-sm text-muted">{showCount ? `${schedule(item)} · ${t(`doneThisWeek.${pluralKey(locale, item.doneThisPeriod)}`, { count: item.doneThisPeriod })}` : schedule(item)}</span>
        {item.frequency === 'daily' ? (
          <span role="img" aria-label={t('weekProgress', { done })} className="flex gap-1.5">
            {Array.from({ length: 7 }, (_, i) => (
              <span key={i} className={`h-2 flex-1 rounded-full ${i < done ? 'bg-primary' : 'bg-line'}`} />
            ))}
          </span>
        ) : null}
      </Link>
    </li>
  );
}

/** P29: active homework first, then the completed ones. */
export function HomeworkListView({ list, locale, timeZone }: { list: HomeworkList; locale: Locale; timeZone: string }) {
  const t = useTranslations('homework');
  return (
    <div className="flex flex-col gap-4 pt-4">
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-bold">{t('listTitle')}</h1>
        <p className="text-base text-muted">{t('intro')}</p>
      </div>

      {list.active.length === 0 && list.completed.length === 0 ? <EmptyState title={t('empty')} body={t('emptyBody')} /> : null}

      {list.active.length > 0 ? (
        <ul className="flex flex-col gap-3">
          {list.active.map((item) => (
            <ActiveCard key={item.id} item={item} locale={locale} timeZone={timeZone} />
          ))}
        </ul>
      ) : null}

      {list.completed.length > 0 ? (
        <section aria-labelledby="homework-completed" className="flex flex-col gap-3">
          <h2 id="homework-completed" className="text-base font-bold text-muted">
            {t('completed')}
          </h2>
          <ul className="flex flex-col gap-3">
            {list.completed.map((item) => (
              <li key={item.id}>
                <Link href={`/homework/${item.id}`} className="flex flex-col gap-2 rounded-card border border-line bg-surface/70 p-4">
                  <span className="text-base font-bold text-muted">{item.title}</span>
                  <span className="text-sm text-muted">{t('ended', { date: formatDate(item.endsOn, locale, timeZone, { day: 'numeric', month: 'long' }) })}</span>
                </Link>
              </li>
            ))}
          </ul>
        </section>
      ) : null}
    </div>
  );
}
