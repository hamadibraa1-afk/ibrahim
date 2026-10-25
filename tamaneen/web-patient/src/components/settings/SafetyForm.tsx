'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState, type FormEvent } from 'react';
import { isLocale, routing } from '@/i18n/routing';
import { apiVersioned, isVersionConflict } from '@/lib/api/client';
import type { SafetyInfo, UpdateSafetyInfoRequest } from '@/lib/api/types';
import { asciiDigits, formatDate } from '@/lib/format';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { Switch } from '@/components/ui/Switch';
import { TextField } from '@/components/ui/TextField';
import { Dialog, DialogCancel } from './Dialog';
import { fieldProblems, problemMessage } from './problem';

const phone = (value: string) => asciiDigits(value).replace(/[^\d+]/g, '') || null;
const text = (value: string) => value.trim() || null;

/**
 * P34: the patient's number, a trusted person, and location sharing, used only in an emergency. The trusted
 * name and number go together; turning location sharing off asks first. The save sends the version that was
 * read (If-Match); on a 412 the form shows the values saved from the other device and says so.
 */
export function SafetyForm({ initial, version: initialVersion, timeZone }: { initial: SafetyInfo; version: string | null; timeZone: string }) {
  const t = useTranslations('settings.safety');
  const te = useTranslations('errors');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;

  const [contactPhone, setContactPhone] = useState(initial.contactPhone ?? '');
  const [trustedName, setTrustedName] = useState(initial.trustedName ?? '');
  const [trustedPhone, setTrustedPhone] = useState(initial.trustedPhone ?? '');
  const [shareLocation, setShareLocation] = useState(initial.shareLocation);
  const [since, setSince] = useState(initial.shareLocationSince);
  const [hasTrusted, setHasTrusted] = useState(Boolean(initial.trustedName || initial.trustedPhone));
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [confirmLocation, setConfirmLocation] = useState(false);
  const [version, setVersion] = useState(initialVersion);

  function apply(info: SafetyInfo) {
    setContactPhone(info.contactPhone ?? '');
    setTrustedName(info.trustedName ?? '');
    setTrustedPhone(info.trustedPhone ?? '');
    setShareLocation(info.shareLocation);
    setSince(info.shareLocationSince);
    setHasTrusted(Boolean(info.trustedName || info.trustedPhone));
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setNotice(null);
    const missing: Record<string, string> = {};
    if (trustedName.trim() && !trustedPhone.trim()) missing.trustedPhone = t('trustedPhoneRequired');
    if (trustedPhone.trim() && !trustedName.trim()) missing.trustedName = t('trustedNameRequired');
    setErrors(missing);
    if (Object.keys(missing).length > 0) return;
    setBusy(true);
    const body: UpdateSafetyInfoRequest = { contactPhone: phone(contactPhone), trustedName: text(trustedName), trustedPhone: phone(trustedPhone), shareLocation };
    try {
      const { data: result, etag } = await apiVersioned<SafetyInfo | undefined>('me/safety-info', { method: 'PUT', body, ifMatch: version });
      apply(result ?? { ...body, shareLocationSince: shareLocation ? (since ?? new Date().toISOString()) : null });
      if (etag) setVersion(etag);
      setNotice(t('saved'));
    } catch (e) {
      if (isVersionConflict(e)) {
        await reload();
        setError(problemMessage(e, te('network')));
        return;
      }
      const fields = fieldProblems(e);
      setErrors(fields);
      if (!fields.contactPhone && !fields.trustedName && !fields.trustedPhone) setError(problemMessage(e, te('network')));
    } finally {
      setBusy(false);
    }
  }

  /** Another device saved first (412): show what is stored now, with its version. */
  async function reload() {
    try {
      const latest = await apiVersioned<SafetyInfo>('me/safety-info');
      apply(latest.data);
      setVersion(latest.etag);
      setErrors({});
    } catch {
      // Keep what is on screen; the next save answers 412 again and retries the reload.
    }
  }

  async function deleteTrusted() {
    setDeleting(true);
    setError(null);
    try {
      const { etag } = await apiVersioned('me/safety-info/trusted-contact', { method: 'DELETE' });
      if (etag) setVersion(etag);
      setTrustedName('');
      setTrustedPhone('');
      setHasTrusted(false);
      setConfirmDelete(false);
      setNotice(t('deleted'));
    } catch (e) {
      setConfirmDelete(false);
      setError(problemMessage(e, te('network')));
    } finally {
      setDeleting(false);
    }
  }

  return (
    <>
      <form onSubmit={submit} noValidate className="flex flex-col gap-4">
        <TextField label={t('contactPhone')} type="tel" dir="ltr" inputMode="tel" autoComplete="tel" maxLength={32} value={contactPhone} error={errors.contactPhone} onChange={(e) => setContactPhone(e.target.value)} />
        <TextField label={t('trustedName')} autoComplete="off" maxLength={100} value={trustedName} error={errors.trustedName} onChange={(e) => setTrustedName(e.target.value)} />
        <TextField label={t('trustedPhone')} type="tel" dir="ltr" inputMode="tel" autoComplete="off" maxLength={32} value={trustedPhone} error={errors.trustedPhone} onChange={(e) => setTrustedPhone(e.target.value)} />
        <div className="rounded-card border border-line bg-surface px-4 py-2">
          <Switch
            label={t('shareLocation')}
            description={shareLocation && since ? t('sharingSince', { date: formatDate(since, locale, timeZone, { day: 'numeric', month: 'long' }) }) : undefined}
            checked={shareLocation}
            onChange={(next) => (next ? setShareLocation(true) : setConfirmLocation(true))}
          />
        </div>

        {error ? <Notice tone="danger">{error}</Notice> : null}
        {notice ? <Notice tone="success">{notice}</Notice> : null}

        <Button type="submit" full busy={busy} className="mt-6">
          {t('save')}
        </Button>
      </form>

      {hasTrusted ? (
        <Button variant="ghost" onClick={() => setConfirmDelete(true)} className="mt-2 w-full text-help">
          {t('delete')}
        </Button>
      ) : null}

      <Dialog open={confirmLocation} onClose={() => setConfirmLocation(false)} title={t('locationTitle')}>
        <p className="text-sm leading-relaxed text-muted">{t('locationBody')}</p>
        <div className="flex flex-col gap-2">
          <Button
            variant="danger"
            full
            onClick={() => {
              setShareLocation(false);
              setConfirmLocation(false);
            }}
          >
            {t('locationConfirm')}
          </Button>
          <DialogCancel onClick={() => setConfirmLocation(false)} />
        </div>
      </Dialog>

      <Dialog open={confirmDelete} onClose={() => setConfirmDelete(false)} title={t('deleteTitle')} tone="danger">
        <p className="text-sm leading-relaxed text-muted">{t('deleteBody')}</p>
        <div className="flex flex-col gap-2">
          <Button variant="danger" full busy={deleting} onClick={deleteTrusted}>
            {t('deleteConfirm')}
          </Button>
          <DialogCancel onClick={() => setConfirmDelete(false)} />
        </div>
      </Dialog>
    </>
  );
}
