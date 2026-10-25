import { useTranslations } from 'next-intl';
import type { Locale } from '@/i18n/routing';
import type { HomeworkDetail } from '@/lib/api/types';
import { formatDate } from '@/lib/format';
import { Avatar } from '@/components/ui/Avatar';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { HomeworkCheckinForm } from './HomeworkCheckinForm';
import { useSchedule } from './HomeworkListView';

/** P10: the exercise, its instructions, and today's check-in. An ended exercise is read-only. */
export function HomeworkDetailView({ homework, locale, timeZone }: { homework: HomeworkDetail; locale: Locale; timeZone: string }) {
  const t = useTranslations('homework');
  const schedule = useSchedule(locale, timeZone);
  return (
    <div className="flex flex-col gap-5">
      <div className="flex flex-col gap-1.5">
        <h1 className="text-2xl font-bold leading-snug">{homework.title}</h1>
        <p className="text-sm text-muted">{t('from', { therapist: homework.therapistName, schedule: schedule(homework) })}</p>
      </div>

      {homework.instructions.length > 0 ? (
        <Card aria-labelledby="homework-instructions" className="flex flex-col gap-3 border border-line">
          <h2 id="homework-instructions" className="text-base font-bold">
            {t('instructions')}
          </h2>
          <ol className="flex list-decimal flex-col gap-2 ps-5 text-sm leading-relaxed">
            {homework.instructions.map((step, i) => (
              <li key={i}>{step}</li>
            ))}
          </ol>
        </Card>
      ) : null}

      {homework.completed ? (
        <div className="flex flex-col gap-4" data-testid="homework-read-only">
          <Notice tone="info">{t('readOnly', { date: formatDate(homework.endsOn, locale, timeZone, { day: 'numeric', month: 'long' }) })}</Notice>
          {homework.latestFeedback ? (
            <figure className="flex items-start gap-2.5">
              <figcaption className="sr-only">{t('feedbackLabel')}</figcaption>
              <Avatar initials={homework.latestFeedback.initials} size={40} />
              <blockquote className="flex-1 rounded-[18px] border border-line bg-surface px-4 py-3 text-sm leading-relaxed">{homework.latestFeedback.text}</blockquote>
            </figure>
          ) : null}
        </div>
      ) : (
        <HomeworkCheckinForm homework={homework} />
      )}
    </div>
  );
}
