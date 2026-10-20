'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useId, useState, type FormEvent, type ReactNode } from 'react';
import { isLocale, routing } from '@/i18n/routing';
import { api, authApi } from '@/lib/api/client';
import type { ChangePasswordRequest, RecoveryCodes, SecurityOverview, SessionInfo, TotpSetup } from '@/lib/api/types';
import { asciiDigits } from '@/lib/format';
import { Button, buttonClass } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/TextField';
import { Dialog, DialogCancel } from './Dialog';
import { fieldProblems, problemMessage } from './problem';
import { formatAgo } from './time';

const RECOVERY_TOTAL = 10;
const MIN_PASSWORD = 12;

function Row({ title, status, action, children }: { title: ReactNode; status: ReactNode; action?: ReactNode; children?: ReactNode }) {
  return (
    <li className="flex flex-col gap-3 border-b border-line px-4 py-3.5 last:border-b-0">
      <div className="flex items-center justify-between gap-3">
        <div className="flex min-w-0 flex-col">
          <span className="text-base font-bold">{title}</span>
          <span className="text-sm text-muted">{status}</span>
        </div>
        {action}
      </div>
      {children}
    </li>
  );
}

const rowButton = `${buttonClass('secondary', false, true)} rounded-[12px]`;

/** Six digits, accepting Arabic-Indic input. */
function cleanCode(value: string): string {
  return asciiDigits(value).replace(/\D/g, '').slice(0, 6);
}

/**
 * P32 Security: password, authenticator app, recovery codes and signed-in devices. Passkeys are not part
 * of this release, so their row is left out.
 */
export function SecurityPanel({ overview, nowIso }: { overview: SecurityOverview; nowIso: string }) {
  const t = useTranslations('settings.security');
  const te = useTranslations('errors');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const now = new Date(nowIso);
  const ids = { password: useId(), totp: useId(), codes: useId() };

  const [notice, setNotice] = useState<string | null>(null);

  // Password
  const [passwordChangedAt, setPasswordChangedAt] = useState(overview.passwordChangedAt);
  const [passwordOpen, setPasswordOpen] = useState(false);
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [passwordErrors, setPasswordErrors] = useState<{ current?: string; next?: string; form?: string }>({});
  const [passwordBusy, setPasswordBusy] = useState(false);

  // Authenticator
  const [totpEnabled, setTotpEnabled] = useState(overview.totpEnabled);
  const [totpOpen, setTotpOpen] = useState(false);
  const [setup, setSetup] = useState<TotpSetup | null>(null);
  const [code, setCode] = useState('');
  const [totpError, setTotpError] = useState<string | null>(null);
  const [totpBusy, setTotpBusy] = useState(false);

  // Recovery codes
  const [remaining, setRemaining] = useState(overview.recoveryCodesRemaining);
  const [confirmCodes, setConfirmCodes] = useState(false);
  const [codes, setCodes] = useState<string[] | null>(null);
  const [codesBusy, setCodesBusy] = useState(false);
  const [codesError, setCodesError] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  // Devices
  const [sessions, setSessions] = useState<SessionInfo[]>(() => [...overview.sessions].sort((a, b) => Number(b.current) - Number(a.current)));
  const [ending, setEnding] = useState<string | null>(null);
  const [devicesError, setDevicesError] = useState<string | null>(null);
  const [revokingAll, setRevokingAll] = useState(false);

  async function changePassword(event: FormEvent) {
    event.preventDefault();
    const errors: typeof passwordErrors = {};
    if (!currentPassword) errors.current = t('passwordRequired');
    if (newPassword.length < MIN_PASSWORD) errors.next = t('passwordTooShort');
    setPasswordErrors(errors);
    if (errors.current || errors.next) return;
    setPasswordBusy(true);
    try {
      const body: ChangePasswordRequest = { currentPassword, newPassword };
      await api('me/password', { method: 'PUT', body });
      setPasswordChangedAt(new Date().toISOString());
      setPasswordOpen(false);
      setCurrentPassword('');
      setNewPassword('');
      setNotice(t('passwordChanged'));
    } catch (e) {
      const fields = fieldProblems(e);
      setPasswordErrors({ current: fields.currentPassword, next: fields.newPassword, form: fields.currentPassword || fields.newPassword ? undefined : problemMessage(e, te('network')) });
    } finally {
      setPasswordBusy(false);
    }
  }

  function toggleTotp() {
    setTotpOpen((open) => !open);
    setCode('');
    setTotpError(null);
  }

  async function startSetup() {
    setTotpBusy(true);
    setTotpError(null);
    try {
      setSetup(await api<TotpSetup>('me/totp/setup', { method: 'POST' }));
    } catch (e) {
      setTotpError(problemMessage(e, te('network')));
    } finally {
      setTotpBusy(false);
    }
  }

  async function submitCode(event: FormEvent) {
    event.preventDefault();
    if (code.length !== 6) {
      setTotpError(t('codeInvalid'));
      return;
    }
    setTotpBusy(true);
    setTotpError(null);
    try {
      if (totpEnabled) {
        await api('me/totp', { method: 'DELETE', body: { code } });
        setTotpEnabled(false);
        setNotice(t('totpOff'));
      } else {
        await api('me/totp/confirm', { method: 'POST', body: { code } });
        setTotpEnabled(true);
        setNotice(t('totpOn'));
      }
      setSetup(null);
      setCode('');
      setTotpOpen(false);
    } catch (e) {
      setTotpError(problemMessage(e, te('network')));
    } finally {
      setTotpBusy(false);
    }
  }

  async function generateCodes() {
    setCodesBusy(true);
    setCodesError(null);
    try {
      const result = await api<RecoveryCodes>('me/recovery-codes', { method: 'POST' });
      setCodes(result.recoveryCodes);
      setRemaining(result.recoveryCodes.length);
      setCopied(false);
      setConfirmCodes(false);
    } catch (e) {
      setCodesError(problemMessage(e, te('network')));
      setConfirmCodes(false);
    } finally {
      setCodesBusy(false);
    }
  }

  async function copyCodes() {
    if (!codes) return;
    try {
      await navigator.clipboard.writeText(codes.join('\n'));
      setCopied(true);
    } catch {
      setCopied(false);
    }
  }

  function downloadCodes() {
    if (!codes) return;
    const url = URL.createObjectURL(new Blob([`${codes.join('\n')}\n`], { type: 'text/plain;charset=utf-8' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = 'tamaneen-recovery-codes.txt';
    document.body.appendChild(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
  }

  async function endSession(session: SessionInfo) {
    setEnding(session.id);
    setDevicesError(null);
    try {
      await api(`me/sessions/${encodeURIComponent(session.id)}`, { method: 'DELETE' });
      setSessions((list) => list.filter((s) => s.id !== session.id));
      setNotice(t('sessionEnded'));
    } catch (e) {
      setDevicesError(problemMessage(e, te('network')));
    } finally {
      setEnding(null);
    }
  }

  async function signOutEverywhere() {
    setRevokingAll(true);
    setDevicesError(null);
    try {
      await api('me/sessions/revoke-all', { method: 'POST' });
    } catch (e) {
      setDevicesError(problemMessage(e, te('network')));
      setRevokingAll(false);
      return;
    }
    await authApi('logout').catch(() => undefined);
    window.location.assign(`/${locale}/login`);
  }

  const groupedSecret = setup?.secret.replace(/\s+/g, '').match(/.{1,4}/g)?.join(' ') ?? '';

  return (
    <div className="flex flex-col gap-5">
      <ul className="overflow-hidden rounded-card border border-line bg-surface">
        <Row
          title={t('password')}
          status={t('changedAgo', { ago: formatAgo(passwordChangedAt, locale, now) })}
          action={
            <button type="button" className={rowButton} aria-label={t('changeLabel')} aria-expanded={passwordOpen} aria-controls={ids.password} onClick={() => setPasswordOpen((o) => !o)}>
              {t('change')}
            </button>
          }
        >
          {passwordOpen ? (
            <form id={ids.password} onSubmit={changePassword} noValidate className="flex flex-col gap-3">
              <TextField label={t('currentPassword')} type="password" autoComplete="current-password" value={currentPassword} error={passwordErrors.current} onChange={(e) => setCurrentPassword(e.target.value)} />
              <TextField
                label={t('newPassword')}
                type="password"
                autoComplete="new-password"
                hint={t('newPasswordHint')}
                value={newPassword}
                error={passwordErrors.next}
                onChange={(e) => setNewPassword(e.target.value)}
              />
              {passwordErrors.form ? <Notice tone="danger">{passwordErrors.form}</Notice> : null}
              <Button type="submit" compact busy={passwordBusy} className="self-start">
                {t('savePassword')}
              </Button>
            </form>
          ) : null}
        </Row>

        <Row
          title={t('authenticator')}
          status={<span className={totpEnabled ? 'font-semibold text-primary' : undefined}>{totpEnabled ? t('enabled') : t('disabled')}</span>}
          action={
            <button type="button" className={rowButton} aria-label={t('manageLabel')} aria-expanded={totpOpen} aria-controls={ids.totp} onClick={toggleTotp}>
              {t('manage')}
            </button>
          }
        >
          {totpOpen ? (
            <div id={ids.totp} className="flex flex-col gap-3">
              {!totpEnabled && !setup ? (
                <>
                  <p className="text-sm leading-relaxed text-muted">{t('setupIntro')}</p>
                  <Button compact busy={totpBusy} onClick={startSetup} className="self-start">
                    {t('startSetup')}
                  </Button>
                  {totpError ? (
                    <p role="alert" className="text-sm font-semibold text-help">
                      {totpError}
                    </p>
                  ) : null}
                </>
              ) : (
                <form onSubmit={submitCode} noValidate className="flex flex-col gap-3">
                  {setup ? (
                    <>
                      <p className="text-sm leading-relaxed text-muted">{t('setupIntro')}</p>
                      <div className="flex flex-col gap-1">
                        <span className="text-sm font-semibold">{t('secretLabel')}</span>
                        <code dir="ltr" data-testid="totp-secret" className="select-all rounded-[12px] bg-tint-teal px-3 py-2 text-start font-mono text-base tracking-wider text-primary-strong">
                          {groupedSecret}
                        </code>
                      </div>
                      <a href={setup.otpauthUri} className={`${buttonClass('secondary', false, true)} self-start`}>
                        {t('openApp')}
                      </a>
                    </>
                  ) : (
                    <p className="text-sm leading-relaxed text-muted">{t('disableIntro')}</p>
                  )}
                  <TextField
                    label={t('codeLabel')}
                    dir="ltr"
                    inputMode="numeric"
                    autoComplete="one-time-code"
                    maxLength={6}
                    value={code}
                    error={totpError}
                    onChange={(e) => {
                      setCode(cleanCode(e.target.value));
                      setTotpError(null);
                    }}
                    className="tracking-[0.3em]"
                  />
                  <Button type="submit" compact busy={totpBusy} variant={totpEnabled ? 'danger-outline' : 'primary'} className="self-start">
                    {totpEnabled ? t('disableTotp') : t('confirmCode')}
                  </Button>
                </form>
              )}
            </div>
          ) : null}
        </Row>

        <Row
          title={t('recovery')}
          status={t('recoveryLeft', { remaining, total: RECOVERY_TOTAL })}
          action={
            <button type="button" className={rowButton} aria-label={t('generateLabel')} onClick={() => setConfirmCodes(true)}>
              {t('generate')}
            </button>
          }
        >
          {codesError ? (
            <p role="alert" className="text-sm font-semibold text-help">
              {codesError}
            </p>
          ) : null}
          {codes ? (
            <section aria-labelledby={ids.codes} className="flex flex-col gap-3 rounded-[16px] bg-tint-sand p-4">
              <h3 id={ids.codes} className="font-bold">
                {t('codesTitle')}
              </h3>
              <p className="text-sm leading-relaxed">{t('codesNote')}</p>
              <ol dir="ltr" data-testid="recovery-codes" className="grid grid-cols-2 gap-2 font-mono text-base">
                {codes.map((c) => (
                  <li key={c} className="rounded-[10px] bg-surface px-3 py-2 text-center">
                    {c}
                  </li>
                ))}
              </ol>
              <div className="flex flex-wrap gap-2">
                <Button variant="secondary" compact onClick={copyCodes}>
                  {t('copy')}
                </Button>
                <Button variant="secondary" compact onClick={downloadCodes}>
                  {t('download')}
                </Button>
                <Button compact onClick={() => setCodes(null)}>
                  {t('savedThem')}
                </Button>
              </div>
              <p aria-live="polite" className="text-sm font-semibold text-primary empty:hidden">
                {copied ? t('copied') : null}
              </p>
            </section>
          ) : null}
        </Row>
      </ul>

      <div aria-live="polite" className="empty:hidden">
        {notice ? <Notice tone="success">{notice}</Notice> : null}
      </div>

      <section aria-labelledby="security-devices" className="flex flex-col gap-3">
        <h2 id="security-devices" className="text-base font-bold">
          {t('devices')}
        </h2>
        <ul className="overflow-hidden rounded-card border border-line bg-surface">
          {sessions.map((session) =>
            session.current ? (
              <Row key={session.id} title={t('thisDevice')} status={t('activeNow', { device: session.deviceLabel })} />
            ) : (
              <Row
                key={session.id}
                title={session.deviceLabel}
                status={t('lastActive', { ago: formatAgo(session.lastActiveAt, locale, now) })}
                action={
                  <Button
                    variant="danger-outline"
                    compact
                    busy={ending === session.id}
                    aria-label={t('endSessionLabel', { device: session.deviceLabel })}
                    onClick={() => endSession(session)}
                    className="rounded-[12px] border-line"
                  >
                    {t('endSession')}
                  </Button>
                }
              />
            ),
          )}
        </ul>
        {devicesError ? <Notice tone="danger">{devicesError}</Notice> : null}
      </section>

      <Button variant="danger-outline" full busy={revokingAll} onClick={signOutEverywhere} className="mt-6 rounded-[16px]">
        {t('signOutAll')}
      </Button>

      <Dialog open={confirmCodes} onClose={() => setConfirmCodes(false)} title={t('generateTitle')}>
        <p className="text-sm leading-relaxed text-muted">{t('generateBody')}</p>
        <div className="flex flex-col gap-2">
          <Button full busy={codesBusy} onClick={generateCodes}>
            {t('generateConfirm')}
          </Button>
          <DialogCancel onClick={() => setConfirmCodes(false)} />
        </div>
      </Dialog>
    </div>
  );
}

