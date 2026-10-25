'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useState, type ReactNode } from 'react';
import { Link } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { api } from '@/lib/api/client';
import type { DayKind, JoinWaitlistRequest, TimeOfDay, Waitlist, WaitlistGender, WaitlistLanguage, WaitlistPreferences } from '@/lib/api/types';
import { formatDateTime, formatNumber } from '@/lib/format';
import { apiMessage, isNetworkError } from '@/components/pwa/errors';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Chip } from '@/components/ui/Chip';
import { Notice } from '@/components/ui/Notice';

const GENDERS: WaitlistGender[] = ['any', 'female', 'male'];
const LANGUAGES: WaitlistLanguage[] = ['ar', 'en', 'any'];
const TIMES: TimeOfDay[] = ['morning', 'afternoon', 'evening'];
const DAYS: DayKind[] = ['weekdays', 'weekend'];

export const DEFAULT_PREFERENCES: WaitlistPreferences = { gender: 'any', language: 'any', timesOfDay: [], days: [] };

function toggle<T>(list: T[], value: T): T[] {
  return list.includes(value) ? list.filter((v) => v !== value) : [...list, value];
}

function Group({ legend, children }: { legend: string; children: ReactNode }) {
  return (
    <fieldset className="flex flex-col gap-2">
      <legend className="mb-2 text-sm font-bold">{legend}</legend>
      <div className="flex flex-wrap gap-2">{children}</div>
    </fieldset>
  );
}

/** The P47 preference cards the waitlist uses: therapist gender, session language, times of day and days. */
export function WaitlistPreferencesForm({ value, onChange }: { value: WaitlistPreferences; onChange: (next: WaitlistPreferences) => void }) {
  const t = useTranslations('matching.waitlist.prefs');
  return (
    <div className="flex flex-col gap-5">
      <Group legend={t('gender')}>
        {GENDERS.map((g) => (
          <Chip key={g} selected={value.gender === g} onClick={() => onChange({ ...value, gender: g })}>
            {t(`genders.${g}`)}
          </Chip>
        ))}
      </Group>
      <Group legend={t('language')}>
        {LANGUAGES.map((l) => (
          <Chip key={l} selected={value.language === l} onClick={() => onChange({ ...value, language: l })} lang={l === 'en' ? 'en' : undefined}>
            {t(`languages.${l}`)}
          </Chip>
        ))}
      </Group>
      <Group legend={t('times')}>
        {TIMES.map((time) => (
          <Chip key={time} selected={value.timesOfDay.includes(time)} onClick={() => onChange({ ...value, timesOfDay: toggle(value.timesOfDay, time) })}>
            {t(`timesOfDay.${time}`)}
          </Chip>
        ))}
        {DAYS.map((day) => (
          <Chip key={day} selected={value.days.includes(day)} onClick={() => onChange({ ...value, days: toggle(value.days, day) })}>
            {t(`days.${day}`)}
          </Chip>
        ))}
      </Group>
      <p className="text-sm text-muted">{t('anyHint')}</p>
    </div>
  );
}

/**
 * P56 Waitlist. Not on the list yet: the preferences and «انضم». Waiting: the expected wait, «تبغى أسرع؟» to widen
 * the preferences, what is there meanwhile, and the emergency reminder. A time opened: the offer with a link to
 * book it. The patient can leave at any time.
 */
export function WaitlistView({ initial, therapistId, therapistName, timeZone }: { initial: Waitlist; therapistId: string | null; therapistName: string | null; timeZone: string }) {
  const t = useTranslations('matching.waitlist');
  const te = useTranslations('errors');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const [waitlist, setWaitlist] = useState(initial);
  const [prefs, setPrefs] = useState<WaitlistPreferences>(initial.preferences ?? DEFAULT_PREFERENCES);
  const [editing, setEditing] = useState(initial.status === 'none');
  const [busy, setBusy] = useState<'save' | 'leave' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const forTherapist = waitlist.status === 'none' ? therapistId : waitlist.therapistId;
  const forName = waitlist.status === 'none' ? therapistName : waitlist.therapistName;

  async function save() {
    setBusy('save');
    setError(null);
    setNotice(null);
    try {
      const body: JoinWaitlistRequest = { ...prefs, therapistId: forTherapist };
      const next = await api<Waitlist>('me/waitlist', { method: 'PUT', body });
      setWaitlist(next);
      setPrefs(next.preferences ?? prefs);
      setEditing(false);
      setNotice(waitlist.status === 'none' ? t('joined') : t('updated'));
    } catch (e) {
      setError(apiMessage(e) ?? (isNetworkError(e) ? te('network') : te('generic')));
    } finally {
      setBusy(null);
    }
  }

  async function leave() {
    setBusy('leave');
    setError(null);
    setNotice(null);
    try {
      await api('me/waitlist', { method: 'DELETE' });
      setWaitlist({ ...waitlist, status: 'none', preferences: null, offer: null, joinedAt: null, therapistId: null, therapistName: null });
      setPrefs(DEFAULT_PREFERENCES);
      setEditing(true);
      setNotice(t('left'));
    } catch (e) {
      setError(apiMessage(e) ?? (isNetworkError(e) ? te('network') : te('generic')));
    } finally {
      setBusy(null);
    }
  }

  const range = t('expectedRange', { min: formatNumber(waitlist.expectedDaysMin, locale), max: formatNumber(waitlist.expectedDaysMax, locale) });
  const offer = waitlist.status === 'available' ? waitlist.offer : null;

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-bold">{waitlist.status === 'none' ? t('joinTitle') : t('title')}</h1>
        <p className="text-sm leading-relaxed text-muted">
          {forName ? t('busyOne', { name: forName }) : t('busy')}
        </p>
      </div>

      {notice ? <Notice tone="success">{notice}</Notice> : null}
      {error ? <Notice tone="danger">{error}</Notice> : null}

      {offer ? (
        <Card tone="teal" className="flex flex-col gap-3" data-testid="waitlist-offer">
          <p className="text-lg font-bold text-primary-strong">{t('availableTitle')}</p>
          <p className="text-sm leading-relaxed">
            {t('availableBody', { name: offer.therapistName })}{' '}
            <bdi dir="auto" className="font-semibold">
              {formatDateTime(offer.startsAt, locale, timeZone)}
            </bdi>
          </p>
          <ButtonLink href={`/therapists/${encodeURIComponent(offer.therapistId)}/book`} full>
            {t('book')}
          </ButtonLink>
        </Card>
      ) : null}

      {waitlist.status !== 'none' ? (
        <div className="flex flex-col gap-1.5 rounded-card bg-tint-teal p-4">
          <p className="text-sm text-muted">{t('expected')}</p>
          <p className="text-3xl font-bold text-primary">{range}</p>
          <p className="text-sm">{t('notify')}</p>
        </div>
      ) : (
        <p className="rounded-card bg-tint-teal p-4 text-sm leading-relaxed">{t('joinBody', { range })}</p>
      )}

      {editing ? (
        <Card className="flex flex-col gap-4">
          <WaitlistPreferencesForm value={prefs} onChange={setPrefs} />
          <Button full busy={busy === 'save'} disabled={busy !== null} onClick={() => void save()}>
            {waitlist.status === 'none' ? t('join') : t('save')}
          </Button>
          {waitlist.status !== 'none' ? (
            <Button variant="ghost" onClick={() => setEditing(false)}>
              {t('cancelEdit')}
            </Button>
          ) : null}
        </Card>
      ) : (
        <Card className="flex flex-col gap-2">
          <p className="text-sm font-bold">{t('fasterTitle')}</p>
          <p className="text-sm text-muted">{t('fasterBody')}</p>
          <Button variant="secondary" full onClick={() => setEditing(true)}>
            {t('editPrefs')}
          </Button>
        </Card>
      )}

      <Card className="flex flex-col gap-2">
        <p className="text-sm font-bold">{t('meanwhile')}</p>
        <ul className="flex flex-col text-sm leading-loose">
          <li>
            <Link href="/check-in" className="inline-flex min-h-touch items-center font-semibold text-primary underline-offset-4 hover:underline">
              {t('meanwhileCheckIn')}
            </Link>
          </li>
          <li>
            <Link href="/safety-plan" className="inline-flex min-h-touch items-center font-semibold text-primary underline-offset-4 hover:underline">
              {t('meanwhileSafetyPlan')}
            </Link>
          </li>
          <li>
            <Link href="/therapists" className="inline-flex min-h-touch items-center font-semibold text-primary underline-offset-4 hover:underline">
              {t('meanwhileDirectory')}
            </Link>
          </li>
        </ul>
      </Card>

      <p className="rounded-[18px] bg-help/10 px-4 py-3 text-sm leading-relaxed text-help">{t('emergency')}</p>

      {waitlist.status !== 'none' ? (
        <Button variant="ghost" busy={busy === 'leave'} disabled={busy !== null} onClick={() => void leave()} className="self-center text-muted">
          {t('leave')}
        </Button>
      ) : null}
    </div>
  );
}
