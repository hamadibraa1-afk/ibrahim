import type { Metadata } from 'next';
import { notFound } from 'next/navigation';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { SessionRoom } from '@/components/session/SessionRoom';
import { serverGetOptional } from '@/lib/api/server';
import type { AppointmentDetail } from '@/lib/api/types';

type Params = Promise<{ locale: string; id: string }>;

export async function generateMetadata({ params }: { params: Params }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'session.live' });
  return { title: t('metaTitle') };
}

/** P42 in session: the provider's room, or the local test room in development. */
export default async function SessionLivePage({ params }: { params: Params }) {
  const { locale, id } = await params;
  setRequestLocale(locale);
  const [t, appointment] = await Promise.all([getTranslations('session.live'), serverGetOptional<AppointmentDetail>(`appointments/${encodeURIComponent(id)}`)]);
  if (!appointment) notFound();
  return (
    <div className="flex flex-col gap-4 pt-2">
      <h1 className="sr-only">{t('metaTitle')}</h1>
      <SessionRoom appointment={appointment} />
    </div>
  );
}
