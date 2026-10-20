import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { Link } from '@/i18n/navigation';
import { isLocale, otherLocale } from '@/i18n/routing';
import { BackLink } from '@/components/ui/BackLink';
import { Card } from '@/components/ui/Card';

type Doc = 'privacy' | 'terms';

const SECTIONS: Record<Doc, readonly string[]> = {
  privacy: ['collect', 'access', 'ai', 'retention', 'rights'],
  terms: ['scope', 'notEmergency', 'security', 'use', 'ending'],
};

/** Published version of both documents; the full legal text follows after legal review. */
const LEGAL_VERSION = '1.0';

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'legal' });
  return { title: t('metaTitle') };
}

/** P13 Legal (public, static): privacy policy and terms of use, switched with ?doc=privacy|terms. */
export default async function LegalPage({ params, searchParams }: { params: Promise<{ locale: string }>; searchParams: Promise<Record<string, string | string[] | undefined>> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const [t, query] = await Promise.all([getTranslations('legal'), searchParams]);
  const doc: Doc = query.doc === 'terms' ? 'terms' : 'privacy';
  const other = otherLocale(isLocale(locale) ? locale : 'ar');

  return (
    <div className="flex flex-col gap-5 pt-2">
      <BackLink href="/" />

      <nav aria-label={t('tabsLabel')} className="flex overflow-hidden rounded-[14px] border-[1.5px] border-line bg-surface">
        {(['privacy', 'terms'] as const).map((d) => (
          <Link
            key={d}
            href={{ pathname: '/legal', query: { doc: d } }}
            aria-current={d === doc ? 'page' : undefined}
            className={`flex min-h-[52px] flex-1 items-center justify-center px-3 text-base font-semibold ${d === doc ? 'bg-primary text-surface' : 'text-primary hover:bg-tint-teal'}`}
          >
            {t(`${d}.tab`)}
          </Link>
        ))}
      </nav>

      <div className="flex items-center justify-between gap-3">
        <h1 className="text-2xl font-bold leading-snug">{t(`${doc}.title`)}</h1>
        <Link href={{ pathname: '/legal', query: { doc } }} locale={other} lang={other} className="inline-flex min-h-touch items-center text-sm font-semibold text-primary underline underline-offset-4">
          {t('otherLanguage')}
        </Link>
      </div>
      <p className="-mt-3 text-sm text-muted">{t('version', { version: LEGAL_VERSION })}</p>

      <Card className="border border-line p-0">
        <div className="divide-y divide-line">
          {SECTIONS[doc].map((s) => (
            <section key={s} className="flex flex-col gap-1.5 p-4">
              <h2 className="text-lg font-bold">{t(`${doc}.sections.${s}.title`)}</h2>
              <p className="text-base leading-relaxed text-muted">{t(`${doc}.sections.${s}.body`)}</p>
            </section>
          ))}
        </div>
      </Card>

      <p role="note" className="rounded-[16px] bg-tint-sand px-4 py-3 text-sm leading-relaxed">
        {t('fullTextNote')}
      </p>
    </div>
  );
}
