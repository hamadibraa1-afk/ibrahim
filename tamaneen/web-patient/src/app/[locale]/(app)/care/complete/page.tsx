import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { CareCompleteView } from '@/components/care/CareCompleteView';
import { BackLink } from '@/components/ui/BackLink';
import { EmptyState } from '@/components/ui/EmptyState';
import { serverGetOptional } from '@/lib/api/server';
import type { CareClosure } from '@/lib/api/types';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'care.complete' });
  return { title: t('metaTitle') };
}

/** P53 End of care: shown only after a supervisor signed off the closing summary (otherwise an empty state). */
export default async function CareCompletePage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, closure] = await Promise.all([getTranslations('care.complete'), serverGetOptional<CareClosure>('me/care-closure')]);
  return (
    <div className="flex flex-col gap-3 pt-2">
      <BackLink href="/home" label={t('home')} />
      {closure ? <CareCompleteView closure={closure} /> : <EmptyState title={t('none')} />}
    </div>
  );
}
