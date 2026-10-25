import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { WaitlistView } from '@/components/matching/WaitlistView';
import { BackLink } from '@/components/ui/BackLink';
import { getMe, serverGet, serverGetOptional } from '@/lib/api/server';
import type { TherapistProfile, Waitlist } from '@/lib/api/types';
import { DEFAULT_TIME_ZONE } from '@/lib/format';

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'matching.waitlist' });
  return { title: t('metaTitle') };
}

/** P56 Waitlist. `?therapist=<id>` comes from P25 when that therapist has no bookable times. */
export default async function WaitlistPage({ params, searchParams }: { params: Promise<{ locale: string }>; searchParams: Promise<{ therapist?: string }> }) {
  const { locale } = await params;
  const { therapist } = await searchParams;
  setRequestLocale(locale);
  const therapistId = therapist && UUID.test(therapist) ? therapist : null;
  const [t, me, waitlist, profile] = await Promise.all([
    getTranslations('matching.waitlist'),
    getMe(),
    serverGet<Waitlist>('me/waitlist'),
    therapistId ? serverGetOptional<TherapistProfile>(`therapists/${therapistId}`).catch(() => null) : Promise.resolve(null),
  ]);
  return (
    <div className="flex flex-col gap-3 pt-2">
      <BackLink href="/home" label={t('home')} />
      <WaitlistView initial={waitlist} therapistId={profile?.id ?? null} therapistName={profile?.displayName ?? null} timeZone={me.timeZone || DEFAULT_TIME_ZONE} />
    </div>
  );
}
