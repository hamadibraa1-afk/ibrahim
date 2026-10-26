import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { ChangeTherapistView } from '@/components/care/ChangeTherapistView';
import { BackLink } from '@/components/ui/BackLink';
import { getMe, publicGet, serverGet, serverGetOptional } from '@/lib/api/server';
import type { AppointmentList, ChangeReason, ChangeTherapistRequest, PublicConfig } from '@/lib/api/types';
import { DEFAULT_TIME_ZONE } from '@/lib/format';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'care.change' });
  return { title: t('metaTitle') };
}

/** P44 Change therapist, from settings or the therapist page. */
export default async function ChangeTherapistPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, me, reasons, current, appointments, config] = await Promise.all([
    getTranslations('care.change'),
    getMe(),
    serverGet<ChangeReason[]>('catalogs/change-reasons'),
    serverGetOptional<ChangeTherapistRequest>('me/change-therapist-requests/current'),
    serverGet<AppointmentList>('appointments'),
    publicGet<PublicConfig>('meta/config'),
  ]);
  const latest = appointments.upcoming[0] ?? appointments.past[0] ?? null;
  return (
    <div className="flex flex-col gap-3 pt-2">
      <BackLink href="/settings" label={t('back')} />
      <ChangeTherapistView
        reasons={reasons}
        initial={current}
        therapistName={latest?.therapist.displayName ?? null}
        inCare={latest !== null && !me.careClosedAt}
        withinHours={current?.withinHours ?? config?.changeTherapistWithinHours ?? 24}
        timeZone={me.timeZone || DEFAULT_TIME_ZONE}
      />
    </div>
  );
}
