'use client';

import { useLocale, useTranslations } from 'next-intl';
import { useEffect, useId, useState, type FormEvent, type ReactNode } from 'react';
import { isLocale, routing } from '@/i18n/routing';
import { api } from '@/lib/api/client';
import type { NotificationPrefs, ReminderFrequency } from '@/lib/api/types';
import { Button } from '@/components/ui/Button';
import { Notice } from '@/components/ui/Notice';
import { Switch } from '@/components/ui/Switch';
import { TextField } from '@/components/ui/TextField';
import { problemMessage } from './problem';
import { setQuickExitCookie } from './QuickExit';
import { SegmentedControl } from './SegmentedControl';
import { formatClock } from './time';

const FREQUENCIES: ReminderFrequency[] = ['daily', 'every2days', 'off'];

function Group({ title, children }: { title: ReactNode; children: ReactNode }) {
  const id = useId();
  return (
    <section aria-labelledby={id} className="flex flex-col gap-2">
      <h2 id={id} className="text-base font-bold">
        {title}
      </h2>
      <div className="flex flex-col divide-y divide-line rounded-card border border-line bg-surface px-4 py-1">{children}</div>
    </section>
  );
}

/**
 * P33 Notifications. Saving also stores the quick-exit choice in a small readable cookie so the quick-exit
 * button shows (or hides) on every signed-in page right away.
 */
export function NotificationsForm({ initial }: { initial: NotificationPrefs }) {
  const t = useTranslations('settings.notifications');
  const te = useTranslations('errors');
  const current = useLocale();
  const locale = isLocale(current) ? current : routing.defaultLocale;
  const quietId = useId();

  const [prefs, setPrefs] = useState(initial);
  const [editingQuiet, setEditingQuiet] = useState(false);
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // This device follows the saved preference, also when it was changed on another device.
  useEffect(() => {
    setQuickExitCookie(initial.quickExit);
  }, [initial.quickExit]);

  function set<K extends keyof NotificationPrefs>(key: K, value: NotificationPrefs[K]) {
    setSaved(false);
    setPrefs((p) => ({ ...p, [key]: value }));
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setError(null);
    setSaved(false);
    try {
      const result = await api<NotificationPrefs | undefined>('me/notifications', { method: 'PUT', body: prefs });
      const stored = result ?? prefs;
      setPrefs(stored);
      setQuickExitCookie(stored.quickExit);
      setEditingQuiet(false);
      setSaved(true);
    } catch (e) {
      setError(problemMessage(e, te('network')));
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-5">
      <Group title={t('channels')}>
        <Switch label={t('push')} checked={prefs.push} onChange={(v) => set('push', v)} />
        <Switch label={t('sms')} checked={prefs.sms} onChange={(v) => set('sms', v)} />
        <Switch label={t('email')} checked={prefs.email} onChange={(v) => set('email', v)} />
      </Group>

      <Group title={t('privacy')}>
        <Switch label={t('neutral')} description={t('neutralHint')} checked={prefs.neutralText} onChange={(v) => set('neutralText', v)} />
        <Switch label={t('quickExit')} description={t('quickExitHint')} checked={prefs.quickExit} onChange={(v) => set('quickExit', v)} />
      </Group>

      <Group title={t('homework')}>
        <div className="py-3">
          <SegmentedControl<ReminderFrequency>
            label={t('frequency')}
            labelClassName="text-base font-semibold"
            options={FREQUENCIES.map((f) => ({ value: f, label: t(`frequencies.${f}`) }))}
            value={prefs.homeworkReminder}
            onChange={(v) => set('homeworkReminder', v)}
          />
        </div>
        <div className="py-3">
          <TextField label={t('time')} type="time" dir="ltr" value={prefs.reminderTime} disabled={prefs.homeworkReminder === 'off'} onChange={(e) => set('reminderTime', e.target.value)} />
        </div>
      </Group>

      <Group title={t('quiet')}>
        <div className="flex items-center justify-between gap-3 py-3">
          <span className="text-base">{t('quietRange', { from: formatClock(prefs.quietFrom, locale), to: formatClock(prefs.quietTo, locale) })}</span>
          <button
            type="button"
            aria-label={t('editQuiet')}
            aria-expanded={editingQuiet}
            aria-controls={quietId}
            onClick={() => setEditingQuiet((v) => !v)}
            className="min-h-touch px-2 text-sm font-semibold text-primary underline underline-offset-4"
          >
            {t('edit')}
          </button>
        </div>
        {editingQuiet ? (
          <div id={quietId} className="grid grid-cols-2 gap-3 py-3">
            <TextField label={t('quietFrom')} type="time" dir="ltr" value={prefs.quietFrom} onChange={(e) => set('quietFrom', e.target.value)} />
            <TextField label={t('quietTo')} type="time" dir="ltr" value={prefs.quietTo} onChange={(e) => set('quietTo', e.target.value)} />
          </div>
        ) : null}
      </Group>

      <p className="rounded-[16px] bg-tint-teal px-4 py-3 text-sm leading-relaxed text-ink">{t('emergencyNote')}</p>

      {error ? <Notice tone="danger">{error}</Notice> : null}
      {saved ? <Notice tone="success">{t('saved')}</Notice> : null}

      <Button type="submit" full busy={busy}>
        {t('save')}
      </Button>
    </form>
  );
}
