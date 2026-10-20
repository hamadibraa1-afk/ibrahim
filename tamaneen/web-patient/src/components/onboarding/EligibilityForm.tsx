'use client';

import { useTranslations } from 'next-intl';
import { useId, useState, type FormEvent } from 'react';
import { Link, useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { EligibilityResponse, PilotCity } from '@/lib/api/types';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { OptionButton } from '@/components/ui/OptionButton';
import { problemText } from '@/components/auth/errorText';
import { FLOW_KEYS, clearFlow, writeFlow } from './flowStorage';
import { StepHeading } from './StepHeading';

interface EligibilityFormProps {
  cities: PilotCity[];
  region: string;
  minAge: number;
  emergencyNumber: string | null;
  supportLine: string | null;
  /** The visitor was sent back here because the sign-up state was lost. */
  restarted?: boolean;
}

/** P15: two questions before an account exists. Not eligible → other support services, no account. */
export function EligibilityForm({ cities, region, minAge, emergencyNumber, supportLine, restarted }: EligibilityFormProps) {
  const t = useTranslations('eligibility');
  const tu = useTranslations('ui');
  const te = useTranslations('errors');
  const router = useRouter();
  const ageId = useId();
  const cityId = useId();
  const [isAdult, setIsAdult] = useState<boolean | null>(null);
  const [cityCode, setCityCode] = useState('');
  const [errors, setErrors] = useState<{ age?: string; city?: string; form?: string }>({});
  const [busy, setBusy] = useState(false);
  const [result, setResult] = useState<EligibilityResponse | null>(null);
  const [returned, setReturned] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    const next: typeof errors = {};
    if (isAdult === null) next.age = t('ageRequired');
    if (!cityCode) next.city = t('cityRequired');
    setErrors(next);
    if (next.age || next.city) return;

    setBusy(true);
    try {
      const response = await api<EligibilityResponse>('auth/eligibility', { method: 'POST', body: { isAdult, cityCode } });
      if (response.eligible && response.eligibilityToken) {
        clearFlow(FLOW_KEYS.signup, FLOW_KEYS.recoveryCodes);
        if (!writeFlow(FLOW_KEYS.eligibility, { eligibilityToken: response.eligibilityToken })) {
          setErrors({ form: t('storageBlocked') });
          return;
        }
        router.push('/start/signup');
      } else {
        clearFlow(FLOW_KEYS.eligibility);
        setResult(response);
      }
    } catch (err) {
      setErrors({ form: problemText(err, te('network')) });
    } finally {
      setBusy(false);
    }
  }

  if (result && !result.eligible) {
    const body = result.reason === 'age' ? t('notEligible.age', { age: minAge }) : result.reason === 'region' ? t('notEligible.region', { region }) : t('notEligible.generic');
    return (
      <div className="flex flex-col gap-5" data-testid="not-eligible">
        <StepHeading title={t('notEligible.title')} subtitle={body} focusOnMount />
        <Card tone="help" className="flex flex-col gap-3">
          <h2 className="text-lg font-bold text-help">{t('notEligible.resourcesTitle')}</h2>
          <ul className="flex flex-col gap-2 text-base">
            {emergencyNumber ? (
              <li>
                <a href={`tel:${emergencyNumber}`} className="inline-flex min-h-touch items-center font-semibold text-help underline-offset-4 hover:underline">
                  {t.rich('notEligible.emergency', { number: emergencyNumber, b: (chunks) => <bdi dir="ltr">{chunks}</bdi> })}
                </a>
              </li>
            ) : null}
            {supportLine ? (
              <li>
                <a href={`tel:${supportLine}`} className="inline-flex min-h-touch items-center font-semibold text-ink underline-offset-4 hover:underline">
                  {t.rich('notEligible.supportLine', { number: supportLine, b: (chunks) => <bdi dir="ltr">{chunks}</bdi> })}
                </a>
              </li>
            ) : null}
          </ul>
          <p className="text-sm text-muted">{t('notEligible.international')}</p>
          <Link href="/crisis-resources" className="inline-flex min-h-touch items-center text-sm font-semibold text-primary underline underline-offset-4">
            {t('notEligible.allResources')}
          </Link>
        </Card>
        <div className="flex flex-col gap-3">
          <ButtonLink href="/" variant="secondary" full>
            {t('notEligible.backHome')}
          </ButtonLink>
          <Button
            variant="ghost"
            full
            onClick={() => {
              setResult(null);
              setReturned(true);
              setIsAdult(null);
              setCityCode('');
            }}
          >
            {t('notEligible.changeAnswers')}
          </Button>
        </div>
      </div>
    );
  }

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-5">
      <StepHeading title={t('title')} subtitle={t('subtitle', { region })} focusOnMount={returned} />
      {restarted ? <Notice tone="warning">{t('restartNotice')}</Notice> : null}

      <fieldset className="flex flex-col gap-3" aria-describedby={errors.age ? `${ageId}-error` : undefined}>
        <legend id={ageId} className="mb-3 text-base font-semibold">
          {t('ageQuestion', { age: minAge })}
        </legend>
        <div role="radiogroup" aria-labelledby={ageId} className="flex flex-col gap-3">
          <OptionButton selected={isAdult === true} onClick={() => setIsAdult(true)} className="min-h-[52px]">
            {tu('yes')}
          </OptionButton>
          <OptionButton selected={isAdult === false} onClick={() => setIsAdult(false)} className="min-h-[52px]">
            {tu('no')}
          </OptionButton>
        </div>
        {errors.age ? (
          <p id={`${ageId}-error`} role="alert" className="text-sm font-semibold text-help">
            {errors.age}
          </p>
        ) : null}
      </fieldset>

      <div className="flex flex-col gap-1.5">
        <label htmlFor={cityId} className="text-base font-semibold">
          {t('cityLabel')}
        </label>
        <select
          id={cityId}
          value={cityCode}
          onChange={(e) => setCityCode(e.target.value)}
          aria-invalid={errors.city ? true : undefined}
          aria-describedby={errors.city ? `${cityId}-error` : undefined}
          className="min-h-[52px] w-full rounded-[12px] border border-line bg-surface px-4 text-base text-ink focus:border-primary aria-[invalid=true]:border-help"
        >
          <option value="">{t('cityPlaceholder')}</option>
          {cities.map((c) => (
            <option key={c.code} value={c.code}>
              {c.name}
            </option>
          ))}
        </select>
        {errors.city ? (
          <p id={`${cityId}-error`} role="alert" className="text-sm font-semibold text-help">
            {errors.city}
          </p>
        ) : null}
      </div>

      <Card className="flex flex-col gap-1 border border-line">
        <h2 className="text-base font-semibold">{t('ifNotTitle')}</h2>
        <p className="text-sm leading-relaxed text-muted">{t('ifNotBody')}</p>
      </Card>

      {errors.form ? <Notice tone="danger">{errors.form}</Notice> : null}

      <Button type="submit" full busy={busy} className="mt-4">
        {tu('continue')}
      </Button>
    </form>
  );
}
