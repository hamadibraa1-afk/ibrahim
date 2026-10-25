import { useTranslations } from 'next-intl';
import { CallLink } from '@/components/crisis/CallLink';
import { ButtonLink } from '@/components/ui/Button';
import { Link } from '@/i18n/navigation';
import type { ReferralCode } from '@/lib/api/types';

/** The order the referrals are listed in, most urgent first. */
const ORDER: ReferralCode[] = ['emergency_services', 'crisis_line', 'psychiatry', 'primary_care', 'community_support'];

export interface OutcomeNumbers {
  emergencyNumber: string | null;
  supportLine: string | null;
}

/** Puts the referrals in a fixed order, drops unknown codes and repeats. */
export function orderReferrals(codes: readonly string[] | null | undefined): ReferralCode[] {
  const given = new Set(codes ?? []);
  return ORDER.filter((code) => given.has(code));
}

/**
 * P36, case closed with referrals (S03 «إغلاق مع إحالة»): a kind explanation that this service is not the right
 * fit right now, and where to go instead. Numbers come from the platform config; a referral without one keeps
 * its description and a link to the crisis resources page.
 */
export function TriageClosed({ referrals, numbers }: { referrals: readonly string[] | null | undefined; numbers: OutcomeNumbers }) {
  const t = useTranslations('waitingTriage.closed');
  const list = orderReferrals(referrals);
  const numberFor = (code: ReferralCode) => (code === 'emergency_services' ? numbers.emergencyNumber : code === 'crisis_line' ? numbers.supportLine : null);

  return (
    <div className="flex flex-col gap-5 pt-2" data-testid="triage-closed">
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-bold">{t('title')}</h1>
        <p className="leading-relaxed text-muted">{t('body')}</p>
      </div>

      {list.length > 0 ? (
        <section aria-labelledby="triage-referrals" className="flex flex-col gap-3">
          <h2 id="triage-referrals" className="text-base font-bold">
            {t('referralsTitle')}
          </h2>
          <ul className="flex flex-col gap-3">
            {list.map((code) => {
              const number = numberFor(code);
              return (
                <li key={code} className="flex flex-col gap-3 rounded-card bg-surface p-4 shadow-[0_1px_0_rgb(var(--line))]">
                  <div className="flex flex-col gap-1">
                    <p className="font-bold">{t(`referrals.${code}.title`)}</p>
                    <p className="text-sm leading-relaxed text-muted">{t(`referrals.${code}.body`)}</p>
                  </div>
                  {number ? (
                    <CallLink number={number} variant={code === 'emergency_services' ? 'solid' : 'outline'}>
                      {t('call')}
                    </CallLink>
                  ) : null}
                </li>
              );
            })}
          </ul>
        </section>
      ) : null}

      <p className="rounded-card bg-tint-sand p-5 leading-relaxed">{t('stillHere')}</p>

      <div className="flex flex-col gap-2">
        <ButtonLink href="/crisis-resources" variant="secondary" full>
          {t('resources')}
        </ButtonLink>
        <Link href="/home" className="flex min-h-touch items-center justify-center font-semibold text-primary underline-offset-4 hover:underline">
          {t('home')}
        </Link>
      </div>
    </div>
  );
}

/** P36, case escalated (S03 «تصعيد»): the team is reaching out now, so the screen points straight to P06. */
export function TriageEscalated({ numbers }: { numbers: OutcomeNumbers }) {
  const t = useTranslations('waitingTriage.escalated');
  return (
    <div className="flex flex-col gap-5 pt-2" data-testid="triage-escalated">
      <div aria-hidden="true" className="h-2 rounded-full bg-help" />
      <div className="flex flex-col gap-2">
        <h1 className="text-2xl font-bold">{t('title')}</h1>
        <p className="leading-relaxed">{t('body')}</p>
      </div>
      <ButtonLink href="/crisis" variant="danger" full>
        {t('openCrisis')}
      </ButtonLink>
      {numbers.emergencyNumber ? (
        <CallLink number={numbers.emergencyNumber} variant="outline" icon>
          {t('callEmergency')}
        </CallLink>
      ) : null}
      <p className="text-sm leading-relaxed text-muted">{t('hint')}</p>
    </div>
  );
}
