'use client';

import { useTranslations } from 'next-intl';
import { useState, type FormEvent } from 'react';
import { useRouter } from '@/i18n/navigation';
import { apiVersioned, isVersionConflict } from '@/lib/api/client';
import type { SafetyInfo, UpdateSafetyInfoRequest } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Notice } from '@/components/ui/Notice';
import { Switch } from '@/components/ui/Switch';
import { TextField } from '@/components/ui/TextField';
import { isValidPhone, normalizePhone } from '@/components/auth/contact';
import { fieldErrorsOf, problemText } from '@/components/auth/errorText';

type Errors = Partial<Record<'contactPhone' | 'trustedName' | 'trustedPhone' | 'form', string>>;

const EMPTY: UpdateSafetyInfoRequest = { contactPhone: null, trustedName: null, trustedPhone: null, shareLocation: false };

/**
 * P21 Safety info, step 4 of 4 (optional). Saving or skipping both mark the step done. Skipping never erases
 * details saved earlier: when there are some, it just moves on (the step is already done). The save sends the
 * version that was read (If-Match); on a 412 «تبقى البيانات مع الخطأ»: what the patient typed stays, the
 * version is refreshed, and pressing save again stores it.
 */
export function SafetyInfoForm({ initial, version: initialVersion }: { initial: SafetyInfo | null; version: string | null }) {
  const t = useTranslations('safetyInfo');
  const te = useTranslations('errors');
  const router = useRouter();
  const [contactPhone, setContactPhone] = useState(initial?.contactPhone ?? '');
  const [trustedName, setTrustedName] = useState(initial?.trustedName ?? '');
  const [trustedPhone, setTrustedPhone] = useState(initial?.trustedPhone ?? '');
  const [shareLocation, setShareLocation] = useState(initial?.shareLocation ?? false);
  const [errors, setErrors] = useState<Errors>({});
  const [busy, setBusy] = useState<'save' | 'skip' | null>(null);
  const [version, setVersion] = useState(initialVersion);
  const hasSaved = Boolean(initial && (initial.contactPhone || initial.trustedName || initial.trustedPhone || initial.shareLocation));

  function skip() {
    if (hasSaved) router.push('/start/path');
    else void put(EMPTY, 'skip');
  }

  async function put(body: UpdateSafetyInfoRequest, mode: 'save' | 'skip') {
    setBusy(mode);
    try {
      await apiVersioned<SafetyInfo>('me/safety-info', { method: 'PUT', body, ifMatch: version });
      router.push('/start/path');
    } catch (e) {
      if (isVersionConflict(e)) {
        try {
          setVersion((await apiVersioned<SafetyInfo>('me/safety-info')).etag);
        } catch {
          // The next save answers 412 again and retries.
        }
        setErrors({ form: t('conflict') });
        setBusy(null);
        return;
      }
      const fields = fieldErrorsOf(e);
      const mapped: Errors = { contactPhone: fields.contactPhone, trustedName: fields.trustedName, trustedPhone: fields.trustedPhone };
      setErrors({ ...mapped, form: mapped.contactPhone || mapped.trustedName || mapped.trustedPhone ? undefined : problemText(e, te('network')) });
      setBusy(null);
    }
  }

  function save(e: FormEvent) {
    e.preventDefault();
    const next: Errors = {};
    if (contactPhone.trim() && !isValidPhone(contactPhone)) next.contactPhone = t('phoneInvalid');
    if (trustedPhone.trim() && !isValidPhone(trustedPhone)) next.trustedPhone = t('phoneInvalid');
    if (trustedPhone.trim() && !trustedName.trim()) next.trustedName = t('trustedNameRequired');
    if (trustedName.trim() && !trustedPhone.trim()) next.trustedPhone = t('trustedPhoneRequired');
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    void put(
      {
        contactPhone: contactPhone.trim() ? normalizePhone(contactPhone) : null,
        trustedName: trustedName.trim() || null,
        trustedPhone: trustedPhone.trim() ? normalizePhone(trustedPhone) : null,
        shareLocation,
      },
      'save',
    );
  }

  const phoneProps = { dir: 'ltr' as const, type: 'tel', inputMode: 'tel' as const, placeholder: '05X XXX XXXX', className: 'text-start', maxLength: 32 };

  return (
    <form onSubmit={save} noValidate className="flex flex-col gap-5">
      <TextField label={t('contactPhone')} value={contactPhone} onChange={(e) => setContactPhone(e.target.value)} autoComplete="tel" error={errors.contactPhone} {...phoneProps} />
      <TextField label={t('trustedName')} value={trustedName} onChange={(e) => setTrustedName(e.target.value)} autoComplete="off" placeholder={t('trustedNamePlaceholder')} maxLength={100} error={errors.trustedName} />
      <TextField label={t('trustedPhone')} value={trustedPhone} onChange={(e) => setTrustedPhone(e.target.value)} autoComplete="off" error={errors.trustedPhone} {...phoneProps} />
      <Card className="border border-line">
        <Switch checked={shareLocation} onChange={setShareLocation} label={t('shareLocation')} description={t('shareLocationHint')} />
      </Card>

      {errors.form ? <Notice tone="danger">{errors.form}</Notice> : null}

      <div className="mt-6 flex flex-col gap-3">
        <Button type="submit" full busy={busy === 'save'} disabled={busy !== null}>
          {t('save')}
        </Button>
        <Button variant="secondary" full busy={busy === 'skip'} disabled={busy !== null} onClick={skip}>
          {t('skip')}
        </Button>
        <p className="text-center text-sm leading-relaxed text-muted">{t('skipWhy')}</p>
      </div>
    </form>
  );
}
