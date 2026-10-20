'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useId, useState, useTransition, type FormEvent } from 'react';
import { usePathname, useRouter } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { api, authApi } from '@/lib/api/client';
import type { Lang, Me, UpdateProfileRequest } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { TextField } from '@/components/ui/TextField';
import { fieldProblems, problemMessage } from './problem';
import { SegmentedControl } from './SegmentedControl';

/** The Gulf zones offered in the pilot; Riyadh is the pilot region's own. */
export const TIME_ZONES = [
  { id: 'Asia/Riyadh', key: 'riyadh' },
  { id: 'Asia/Dubai', key: 'dubai' },
  { id: 'Asia/Kuwait', key: 'kuwait' },
  { id: 'Asia/Qatar', key: 'qatar' },
  { id: 'Asia/Bahrain', key: 'bahrain' },
  { id: 'Asia/Muscat', key: 'muscat' },
] as const;

/**
 * P31 profile: nickname and time zone (saved together), and the language, which applies at once: the API
 * stores it, then the same page reloads under the other locale so the whole interface flips direction.
 */
export function ProfileForm({ me }: { me: Me }) {
  const t = useTranslations('settings.profile');
  const te = useTranslations('errors');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const router = useRouter();
  const pathname = usePathname();
  const zoneId = useId();
  const [switching, startTransition] = useTransition();

  const [nickname, setNickname] = useState(me.nickname);
  const [timeZone, setTimeZone] = useState(me.timeZone);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [languageBusy, setLanguageBusy] = useState(false);
  const [signingOut, setSigningOut] = useState(false);

  const knownZone = TIME_ZONES.some((z) => z.id === timeZone);

  async function changeLanguage(next: Lang) {
    if (next === locale || languageBusy) return;
    setLanguageBusy(true);
    setError(null);
    try {
      await api('me/language', { method: 'PUT', body: { language: next } });
      startTransition(() => {
        router.replace(pathname, { locale: next });
      });
    } catch (e) {
      setError(problemMessage(e, te('network')));
    } finally {
      setLanguageBusy(false);
    }
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setSaved(false);
    setError(null);
    const name = nickname.trim();
    if (!name) {
      setErrors({ nickname: t('nicknameRequired') });
      return;
    }
    setErrors({});
    setBusy(true);
    try {
      const body: UpdateProfileRequest = { nickname: name, timeZone };
      const updated = await api<Me>('me/profile', { method: 'PUT', body });
      if (updated) {
        setNickname(updated.nickname);
        setTimeZone(updated.timeZone);
      }
      setSaved(true);
      router.refresh();
    } catch (e) {
      const fields = fieldProblems(e);
      setErrors(fields);
      if (!fields.nickname && !fields.timeZone) setError(problemMessage(e, te('network')));
    } finally {
      setBusy(false);
    }
  }

  async function signOut() {
    setSigningOut(true);
    await authApi('logout').catch(() => undefined);
    window.location.assign(`/${locale}`);
  }

  return (
    <div className="flex flex-col gap-6">
      <form onSubmit={submit} noValidate className="flex flex-col gap-5">
        <h2 className="text-xl font-bold">{t('title')}</h2>
        <TextField
          label={t('nickname')}
          hint={t('nicknameHint')}
          value={nickname}
          maxLength={40}
          autoComplete="nickname"
          error={errors.nickname}
          onChange={(e) => {
            setNickname(e.target.value);
            setSaved(false);
          }}
        />

        <SegmentedControl<Lang>
          label={t('language')}
          labelClassName="text-sm font-semibold"
          options={routing.locales.map((l) => ({ value: l, label: t(`languageNames.${l}`), lang: l }))}
          value={locale}
          disabled={languageBusy || switching}
          onChange={changeLanguage}
        />

        <div className="flex flex-col gap-1.5">
          <label htmlFor={zoneId} className="text-sm font-semibold">
            {t('timeZone')}
          </label>
          <select
            id={zoneId}
            value={timeZone}
            aria-invalid={errors.timeZone ? true : undefined}
            aria-describedby={errors.timeZone ? `${zoneId}-error` : undefined}
            onChange={(e) => {
              setTimeZone(e.target.value);
              setSaved(false);
            }}
            className="min-h-[48px] w-full rounded-[14px] border border-line bg-surface px-4 text-base text-ink focus:border-primary"
          >
            {knownZone ? null : <option value={timeZone}>{timeZone}</option>}
            {TIME_ZONES.map((zone) => (
              <option key={zone.id} value={zone.id}>
                {t(`timeZones.${zone.key}`)}
              </option>
            ))}
          </select>
          {errors.timeZone ? (
            <p id={`${zoneId}-error`} role="alert" className="text-sm font-semibold text-help">
              {errors.timeZone}
            </p>
          ) : null}
        </div>

        {error ? <Notice tone="danger">{error}</Notice> : null}
        {saved ? <Notice tone="success">{t('saved')}</Notice> : null}

        <Button type="submit" full busy={busy}>
          {t('save')}
        </Button>
      </form>

      <Button variant="ghost" onClick={signOut} busy={signingOut} className="self-center text-muted">
        {t('signOut')}
      </Button>
    </div>
  );
}
