'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState, type FormEvent } from 'react';
import { isLocale, routing } from '@/i18n/routing';
import { api, authApi } from '@/lib/api/client';
import type { DataRequestStatus, DeleteAccountRequest } from '@/lib/api/types';
import { formatDate } from '@/lib/format';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/TextField';
import { Dialog, DialogCancel } from '@/components/settings/Dialog';
import { problemMessage } from '@/components/settings/problem';
import { clearLocalSafetyData } from '@/components/safety/clearOnSignOut';

function Chevron() {
  return (
    <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="shrink-0 rtl:rotate-180">
      <path d="M9 6l6 6-6 6" />
    </svg>
  );
}

/** "My data" (P11): request a copy (ready within 7 days) or delete the account after re-entering the password. */
export function MyData({ initialExport, timeZone }: { initialExport: DataRequestStatus; timeZone: string }) {
  const t = useTranslations('privacy');
  const te = useTranslations('errors');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;

  const [exportStatus, setExportStatus] = useState(initialExport);
  const [exportBusy, setExportBusy] = useState(false);
  const [exportNotice, setExportNotice] = useState<string | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [password, setPassword] = useState('');
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);

  const requested = exportStatus.exportRequestedAt !== null && !exportStatus.exportReady;
  const short = (iso: string) => formatDate(iso, locale, timeZone, { day: 'numeric', month: 'long' });
  const exportLine = exportStatus.exportReady
    ? t('exportReady')
    : requested && exportStatus.exportRequestedAt
      ? exportStatus.exportReadyBy
        ? t('exportRequested', { requested: short(exportStatus.exportRequestedAt), ready: short(exportStatus.exportReadyBy) })
        : t('exportStarted')
      : t('exportHint');

  async function requestExport() {
    setExportBusy(true);
    setExportError(null);
    try {
      setExportStatus(await api<DataRequestStatus>('me/data-export', { method: 'POST' }));
      setExportNotice(t('exportStarted'));
    } catch (e) {
      setExportError(problemMessage(e, te('network')));
    } finally {
      setExportBusy(false);
    }
  }

  function closeDelete() {
    setDeleteOpen(false);
    setPassword('');
    setPasswordError(null);
  }

  async function deleteAccount(event: FormEvent) {
    event.preventDefault();
    if (!password) {
      setPasswordError(t('deletePasswordRequired'));
      return;
    }
    setDeleting(true);
    setPasswordError(null);
    try {
      const body: DeleteAccountRequest = { password };
      await api('me/delete-account', { method: 'POST', body });
    } catch (e) {
      setPasswordError(problemMessage(e, te('network')));
      setDeleting(false);
      return;
    }
    await clearLocalSafetyData();
    await authApi('logout').catch(() => undefined);
    window.location.assign(`/${locale}?deleted=1`);
  }

  return (
    <section aria-labelledby="privacy-data" className="flex flex-col gap-2">
      <h2 id="privacy-data" className="text-lg font-bold">
        {t('dataTitle')}
      </h2>
      <ul className="divide-y divide-line rounded-card border border-line bg-surface">
        <li>
          <button
            type="button"
            onClick={requestExport}
            disabled={exportBusy || requested || exportStatus.exportReady}
            aria-busy={exportBusy || undefined}
            className="flex min-h-[64px] w-full items-center justify-between gap-3 px-4 py-3 text-start disabled:cursor-default"
          >
            <span className="flex flex-col">
              <span className="font-bold">{t('export')}</span>
              <span className="text-sm text-muted">{exportLine}</span>
            </span>
            {requested || exportStatus.exportReady ? null : <Chevron />}
          </button>
        </li>
        <li>
          <button type="button" onClick={() => setDeleteOpen(true)} className="flex min-h-[64px] w-full items-center justify-between gap-3 px-4 py-3 text-start text-help">
            <span className="flex flex-col">
              <span className="font-bold">{t('delete')}</span>
              <span className="text-sm text-muted">{t('deleteHint')}</span>
            </span>
            <Chevron />
          </button>
        </li>
      </ul>
      <div aria-live="polite" className="empty:hidden">
        {exportNotice ? <Notice tone="success">{exportNotice}</Notice> : null}
      </div>
      {exportError ? <Notice tone="danger">{exportError}</Notice> : null}

      <Dialog open={deleteOpen} onClose={closeDelete} title={t('deleteDialogTitle')} tone="danger">
        <form onSubmit={deleteAccount} noValidate className="flex flex-col gap-4">
          <p className="text-sm leading-relaxed text-muted">{t('deleteDialogBody')}</p>
          <TextField label={t('deletePassword')} type="password" autoComplete="current-password" value={password} error={passwordError} onChange={(e) => setPassword(e.target.value)} />
          <div className="flex flex-col gap-2">
            <Button type="submit" variant="danger" full busy={deleting}>
              {t('deleteConfirm')}
            </Button>
            <DialogCancel onClick={closeDelete} />
          </div>
        </form>
      </Dialog>
    </section>
  );
}
