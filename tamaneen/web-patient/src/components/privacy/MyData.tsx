'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useState, type FormEvent } from 'react';
import { Link } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { api, authApi } from '@/lib/api/client';
import type { DataExportRequest, DataRequestStatus, DeleteAccountRequest } from '@/lib/api/types';
import { ApiError } from '@/lib/api/errors';
import { formatDate } from '@/lib/format';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/TextField';
import { Dialog, DialogCancel } from '@/components/settings/Dialog';
import { problemMessage } from '@/components/settings/problem';
import { clearLocalSafetyData } from '@/components/safety/clearOnSignOut';
import { OPEN_DELETE_EVENT } from './ConsentsSection';

export const DELETION_CRISIS_CODE = 'error.privacy.deletion_crisis_open';

function Chevron() {
  return (
    <svg aria-hidden="true" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="shrink-0 rtl:rotate-180">
      <path d="M9 6l6 6-6 6" />
    </svg>
  );
}

/**
 * "My data" (P11). Both actions ask for the password again (PB-58, PB-59). Deleting also needs the confirmation
 * word typed in, and is refused while a crisis event is open (EC-55), with a way to the crisis screen.
 */
export function MyData({ initialExport, timeZone }: { initialExport: DataRequestStatus; timeZone: string }) {
  const t = useTranslations('privacy');
  const te = useTranslations('errors');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;

  const [exportStatus, setExportStatus] = useState(initialExport);
  const [exportBusy, setExportBusy] = useState(false);
  const [exportNotice, setExportNotice] = useState<string | null>(null);
  const [exportError, setExportError] = useState<string | null>(null);
  const [exportOpen, setExportOpen] = useState(false);
  const [exportPassword, setExportPassword] = useState('');
  const [exportPasswordError, setExportPasswordError] = useState<string | null>(null);

  const [deleteOpen, setDeleteOpen] = useState(false);
  const [password, setPassword] = useState('');
  const [passwordError, setPasswordError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [confirmWord, setConfirmWord] = useState('');
  const [confirmError, setConfirmError] = useState<string | null>(null);
  const [blockedByCrisis, setBlockedByCrisis] = useState(false);

  // "Delete my account instead" in the AI consent dialog.
  useEffect(() => {
    const open = () => setDeleteOpen(true);
    window.addEventListener(OPEN_DELETE_EVENT, open);
    return () => window.removeEventListener(OPEN_DELETE_EVENT, open);
  }, []);

  const requested = exportStatus.exportRequestedAt !== null && !exportStatus.exportReady;
  const short = (iso: string) => formatDate(iso, locale, timeZone, { day: 'numeric', month: 'long' });
  const exportLine = exportStatus.exportReady
    ? t('exportReady')
    : requested && exportStatus.exportRequestedAt
      ? exportStatus.exportReadyBy
        ? t('exportRequested', { requested: short(exportStatus.exportRequestedAt), ready: short(exportStatus.exportReadyBy) })
        : t('exportStarted')
      : t('exportHint');

  function closeExport() {
    setExportOpen(false);
    setExportPassword('');
    setExportPasswordError(null);
  }

  async function requestExport(event: FormEvent) {
    event.preventDefault();
    if (!exportPassword) {
      setExportPasswordError(t('deletePasswordRequired'));
      return;
    }
    setExportBusy(true);
    setExportError(null);
    setExportPasswordError(null);
    try {
      const body: DataExportRequest = { password: exportPassword };
      setExportStatus(await api<DataRequestStatus>('me/data-export', { method: 'POST', body }));
      setExportNotice(t('exportStarted'));
      closeExport();
    } catch (e) {
      setExportPasswordError(problemMessage(e, te('network')));
    } finally {
      setExportBusy(false);
    }
  }

  function closeDelete() {
    setDeleteOpen(false);
    setPassword('');
    setPasswordError(null);
    setConfirmWord('');
    setConfirmError(null);
    setBlockedByCrisis(false);
  }

  async function deleteAccount(event: FormEvent) {
    event.preventDefault();
    const wordOk = confirmWord.trim().toLocaleLowerCase(locale) === t('deleteWord').toLocaleLowerCase(locale);
    setPasswordError(password ? null : t('deletePasswordRequired'));
    setConfirmError(wordOk ? null : t('deleteWordRequired', { word: t('deleteWord') }));
    if (!password || !wordOk) return;
    setDeleting(true);
    setBlockedByCrisis(false);
    try {
      const body: DeleteAccountRequest = { password };
      await api('me/delete-account', { method: 'POST', body });
    } catch (e) {
      if (e instanceof ApiError && e.code === DELETION_CRISIS_CODE) setBlockedByCrisis(true);
      else setPasswordError(problemMessage(e, te('network')));
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
            onClick={() => setExportOpen(true)}
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

      <Dialog open={exportOpen} onClose={closeExport} title={t('exportDialogTitle')}>
        <form onSubmit={requestExport} noValidate className="flex flex-col gap-4">
          <p className="text-sm leading-relaxed text-muted">{t('exportDialogBody')}</p>
          <TextField label={t('deletePassword')} type="password" autoComplete="current-password" value={exportPassword} error={exportPasswordError} onChange={(e) => setExportPassword(e.target.value)} />
          <div className="flex flex-col gap-2">
            <Button type="submit" full busy={exportBusy}>
              {t('exportConfirm')}
            </Button>
            <DialogCancel onClick={closeExport} />
          </div>
        </form>
      </Dialog>

      <Dialog open={deleteOpen} onClose={closeDelete} title={t('deleteDialogTitle')} tone="danger">
        <form onSubmit={deleteAccount} noValidate className="flex flex-col gap-4">
          <p className="text-sm leading-relaxed text-muted">{t('deleteDialogBody')}</p>
          {blockedByCrisis ? (
            <div role="alert" data-testid="delete-blocked" className="flex flex-col gap-2 rounded-[16px] bg-help/10 px-4 py-3 text-sm leading-relaxed text-help">
              <p className="font-bold">{t('deleteBlockedTitle')}</p>
              <p>{t('deleteBlockedBody')}</p>
              <Link href="/crisis" className="font-bold underline underline-offset-4">
                {t('deleteBlockedLink')}
              </Link>
            </div>
          ) : null}
          <TextField label={t('deletePassword')} type="password" autoComplete="current-password" value={password} error={passwordError} onChange={(e) => setPassword(e.target.value)} />
          <TextField label={t('deleteWordLabel', { word: t('deleteWord') })} autoComplete="off" value={confirmWord} error={confirmError} dir="auto" onChange={(e) => setConfirmWord(e.target.value)} />
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
