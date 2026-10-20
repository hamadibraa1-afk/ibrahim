import { useTranslations } from 'next-intl';
import type { Locale } from '@/i18n/routing';
import type { HomeworkDetail } from '@/lib/api/types';
import { Card } from '@/components/ui/Card';
import { HomeworkCheckinForm } from './HomeworkCheckinForm';
import { useSchedule } from './HomeworkListView';

/** P10: the exercise, its instructions, and today's check-in. */
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

      <HomeworkCheckinForm homework={homework} />
    </div>
  );
}
