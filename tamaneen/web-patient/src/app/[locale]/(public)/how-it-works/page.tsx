import type { Metadata } from 'next';
import { getTranslations, setRequestLocale } from 'next-intl/server';
import { BackLink } from '@/components/ui/BackLink';
import { ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { PageHeading } from '@/components/ui/PageHeading';

const STEPS = ['talk', 'review', 'specialist', 'choose'] as const;
const DOES = ['listens', 'organizes', 'reminds'] as const;
const DOESNT = ['diagnose', 'prescribe', 'decide'] as const;

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'howItWorks' });
  return { title: t('metaTitle') };
}

/** P12 How it works (public, static): the four steps and what the AI does and doesn't do. */
export default async function HowItWorksPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const t = await getTranslations('howItWorks');

  return (
    <div className="flex flex-col gap-5 pt-2">
      <BackLink href="/" />
      <PageHeading title={t('title')} />

      <Card className="border border-line">
        <ol className="flex flex-col gap-5">
          {STEPS.map((step, i) => (
            <li key={step} className="flex items-start gap-4">
              <span aria-hidden="true" className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary text-base font-bold text-surface">
                {i + 1}
              </span>
              <div className="flex flex-col gap-1">
                <h2 className="text-lg font-bold">{t(`steps.${step}.title`)}</h2>
                <p className="text-sm leading-relaxed text-muted">{t(`steps.${step}.body`)}</p>
              </div>
            </li>
          ))}
        </ol>
      </Card>

      <section aria-labelledby="ai-heading" className="flex flex-col gap-3">
        <h2 id="ai-heading" className="text-lg font-bold">
          {t('aiTitle')}
        </h2>
        <div className="grid grid-cols-2 gap-3">
          <Card className="flex flex-col gap-2 border border-line">
            <h3 className="text-base font-bold text-primary">{t('does')}</h3>
            <ul className="flex flex-col gap-2 text-sm">
              {DOES.map((k) => (
                <li key={k}>{t(`doesItems.${k}`)}</li>
              ))}
            </ul>
          </Card>
          <Card className="flex flex-col gap-2 border border-line">
            <h3 className="text-base font-bold text-help">{t('doesnt')}</h3>
            <ul className="flex flex-col gap-2 text-sm">
              {DOESNT.map((k) => (
                <li key={k}>{t(`doesntItems.${k}`)}</li>
              ))}
            </ul>
          </Card>
        </div>
      </section>

      <ButtonLink href="/start/eligibility" full className="mt-6">
        {t('cta')}
      </ButtonLink>
    </div>
  );
}
