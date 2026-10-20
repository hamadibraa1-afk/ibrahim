'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState, type FormEvent } from 'react';
import { isLocale, routing } from '@/i18n/routing';
import { api } from '@/lib/api/client';
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

/** P34: the patient's number, a trusted person, and location sharing, used only in an emergency. */
export function SafetyForm({ initial, timeZone }: { initial: SafetyInfo; timeZone: string }) {
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
    setBusy(true);
    setError(null);
    setNotice(null);
    setErrors({});
    const body: UpdateSafetyInfoRequest = { contactPhone: phone(contactPhone), trustedName: text(trustedName), trustedPhone: phone(trustedPhone), shareLocation };
    try {
      const result = await api<SafetyInfo | undefined>('me/safety-info', { method: 'PUT', body });
      apply(result ?? { ...body, shareLocationSince: shareLocation ? (since ?? new Date().toISOString()) : null });
      setNotice(t('saved'));
    } catch (e) {
      const fields = fieldProblems(e);
      setErrors(fields);
      if (!fields.contactPhone && !fields.trustedName && !fields.trustedPhone) setError(problemMessage(e, te('network')));
    } finally {
      setBusy(false);
    }
  }

  async function deleteTrusted() {
    setDeleting(true);
    setError(null);
    try {
      await api('me/safety-info/trusted-contact', { method: 'DELETE' });
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
        <TextField label={t('contactPhone')} type="tel" dir="ltr" inputMode="tel" autoComplete="tel" value={contactPhone} error={errors.contactPhone} onChange={(e) => setContactPhone(e.target.value)} />
        <TextField label={t('trustedName')} autoComplete="off" maxLength={80} value={trustedName} error={errors.trustedName} onChange={(e) => setTrustedName(e.target.value)} />
        <TextField label={t('trustedPhone')} type="tel" dir="ltr" inputMode="tel" autoComplete="off" value={trustedPhone} error={errors.trustedPhone} onChange={(e) => setTrustedPhone(e.target.value)} />
        <div className="rounded-card border border-line bg-surface px-4 py-2">
          <Switch
            label={t('shareLocation')}
            description={shareLocation && since ? t('sharingSince', { date: formatDate(since, locale, timeZone, { day: 'numeric', month: 'long' }) }) : undefined}
            checked={shareLocation}
            onChange={setShareLocation}
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
