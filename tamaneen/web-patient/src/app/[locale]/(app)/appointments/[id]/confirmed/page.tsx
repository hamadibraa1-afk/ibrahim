import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { asLocale, timeZoneOf } from '@/components/matching/labels';
import { ButtonLink, buttonClass } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { getMe, serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail } from '@/lib/api/types';
import { formatDateTime, formatNumber } from '@/lib/format';

type Params = Promise<{ locale: string; id: string }>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'bookConfirmed' });
  return { title: t('metaTitle') };
}

/** P26 Booking confirmed. */
export default async function BookConfirmedPage({ params }: { params: Params }) {
  const { locale: rawLocale, id } = await params;
  setRequestLocale(rawLocale);
  const locale = asLocale(rawLocale);
  const [t, me, appointment] = await Promise.all([
    getTranslations('bookConfirmed'),
    getMe(),
    serverGetOptional<AppointmentDetail>(`appointments/${encodeURIComponent(id)}`),
  ]);
  if (!appointment) notFound();
  const { therapist } = appointment;
  const steps = [t('step1', { gender: therapist.gender }), t('step2'), t('step3')];

  return (
    <div className="flex flex-col gap-5 pt-6">
      <span aria-hidden="true" className="flex h-16 w-16 items-center justify-center rounded-full bg-tint-teal text-primary">
        <svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.4" strokeLinecap="round" strokeLinejoin="round">
          <path d="M5 12l4 4 10-10" />
        </svg>
      </span>
      <h1 className="text-3xl font-bold">{t('title')}</h1>

      <Card className="flex flex-col gap-1 border border-line">
        <p className="text-lg font-bold">{formatDateTime(appointment.startsAt, locale, timeZoneOf(me))}</p>
        <p className="text-sm text-muted">{t('with', { name: therapist.displayName, minutes: formatNumber(appointment.durationMinutes, locale) })}</p>
      </Card>

      <section aria-labelledby="next-heading" className="flex flex-col gap-3">
        <h2 id="next-heading" className="text-base font-bold">
          {t('nextTitle')}
        </h2>
        <ol className="flex flex-col gap-3">
          {steps.map((step, i) => (
            <li key={i} className="flex items-start gap-3">
              <span aria-hidden="true" className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-tint-teal text-sm font-bold text-primary">
                {formatNumber(i + 1, locale)}
              </span>
              <span className="text-base leading-relaxed">{step}</span>
            </li>
          ))}
        </ol>
      </section>

      <div className="mt-6 flex flex-col gap-3">
        <a href={`/api/v1/appointments/${encodeURIComponent(appointment.id)}/calendar`} download className={buttonClass('secondary', true)}>
          {t('addToCalendar')}
        </a>
        <ButtonLink href="/home" full>
          {t('goHome')}
        </ButtonLink>
      </div>
    </div>
  );
}
