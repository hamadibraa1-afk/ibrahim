import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { DetailRows } from '@/components/appointments/DetailRows';
import { asLocale, specialtyLabels, timeZoneOf } from '@/components/matching/labels';
import { Avatar } from '@/components/ui/Avatar';
import { BackLink } from '@/components/ui/BackLink';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Tag } from '@/components/ui/Chip';
import { Notice } from '@/components/ui/Notice';
import { Link } from '@/i18n/navigation';
import { getMe, serverGetOptional } from '@/lib/api/server';
import type { TherapistProfile } from '@/lib/api/types';
import { canBook } from '@/lib/booking';
import { formatDate, formatNumber } from '@/lib/format';

type Params = Promise<{ locale: string; id: string }>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'therapistProfile' });
  return { title: t('metaTitle') };
}

/** P25 Therapist profile: who they are, how they work, and the way into booking. */
export default async function TherapistProfilePage({ params }: { params: Params }) {
  const { locale: rawLocale, id } = await params;
  setRequestLocale(rawLocale);
  const locale = asLocale(rawLocale);
  const [t, tSpecialties, me, therapist] = await Promise.all([
    getTranslations('therapistProfile'),
    getTranslations('specialties'),
    getMe(),
    serverGetOptional<TherapistProfile>(`therapists/${encodeURIComponent(id)}`),
  ]);
  if (!therapist) notFound();

  const timeZone = timeZoneOf(me);
  const eligible = canBook(me);
  const labels = specialtyLabels(tSpecialties, therapist.specialties);
  const languages = therapist.languages.map((lang) => t(`languageName.${lang}`)).join(t('listSeparator'));

  return (
    <div className="flex flex-col gap-5 pt-2">
      <BackLink href="/therapists" />

      <div className="flex items-center gap-4">
        <Avatar initials={therapist.initials} size={72} />
        <div className="flex min-w-0 flex-col gap-1">
          <h1 className="text-2xl font-bold leading-snug">{therapist.displayName}</h1>
          <p className="text-base text-muted">{therapist.title}</p>
          {therapist.licenseVerified ? <p className="text-sm font-semibold text-primary">{t('licenseVerified')}</p> : null}
        </div>
      </div>

      {labels.length > 0 ? (
        <ul aria-label={t('specialtiesLabel')} className="flex flex-wrap gap-2">
          {labels.map((label) => (
            <li key={label}>
              <Tag tone="muted">{label}</Tag>
            </li>
          ))}
        </ul>
      ) : null}

      {therapist.bio ? (
        <Card aria-labelledby="about-heading" className="border border-line">
          <h2 id="about-heading" className="mb-2 text-lg font-bold">
            {t('about')}
          </h2>
          <p className="whitespace-pre-line text-base leading-relaxed">{therapist.bio}</p>
        </Card>
      ) : null}

      <DetailRows
        label={t('detailsLabel')}
        rows={[
          { label: t('languages'), value: languages },
          { label: t('sessionType'), value: t('sessionValue', { minutes: formatNumber(therapist.durationMinutes, locale) }) },
          {
            label: t('nextAvailable'),
            value: therapist.nextAvailableAt ? formatDate(therapist.nextAvailableAt, locale, timeZone) : t('noSlots'),
          },
        ]}
      />

      {!therapist.nextAvailableAt ? (
        <Notice tone="warning">
          {t('noSlotsNote')}{' '}
          <Link href="/therapists" className="font-semibold underline underline-offset-4">
            {t('otherTherapists')}
          </Link>
        </Notice>
      ) : null}

      {/* PB-39: the booking button is not rendered at all for a patient who may not book yet. */}
      {eligible ? (
        <div className="sticky bottom-0 -mx-4 mt-4 bg-bg/95 px-4 pb-4 pt-2">
          {therapist.nextAvailableAt ? (
            <ButtonLink href={`/therapists/${encodeURIComponent(therapist.id)}/book`} full>
              {t('choose')}
            </ButtonLink>
          ) : (
            <Button full disabled>
              {t('choose')}
            </Button>
          )}
        </div>
      ) : null}
    </div>
  );
}
