'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useId, useState, type FormEvent } from 'react';
import { useRouter } from '@/i18n/navigation';
import type { MoodEntry, MoodEntryRequest, MoodSaveResponse, MoodTag } from '@/lib/api/types';
import { Button, ButtonLink } from '@/components/ui/Button';
import { Card } from '@/components/ui/Card';
import { Chip } from '@/components/ui/Chip';
import { Notice } from '@/components/ui/Notice';
import { TextArea } from '@/components/ui/TextField';
import { problemMessage } from '@/components/settings/problem';
import { dayKey } from '@/components/settings/time';
import { useOnline } from '@/components/pwa/hooks';
import { OUTBOX_SENT_EVENT, submitWrite, type OutboxSentDetail } from '@/components/pwa/outbox';
import { MoodChart } from './MoodChart';
import { MOOD_TAGS, moodWord } from './scale';
import { ScoreSlider } from './ScoreSlider';

/**
 * P30: mood 0–10, optional tags and note, and the last seven days. The note goes through the crisis gate.
 * «حفظ» goes through the outbox (P66): offline, the check-in is kept on the phone and sent when the connection
 * returns (the banner says so); a queued note that turns out to be a crisis opens the crisis screen on arrival.
 */
export function CheckInForm({ initialEntries, nowIso, timeZone }: { initialEntries: MoodEntry[]; nowIso: string; timeZone: string }) {
  const t = useTranslations('checkin');
  const te = useTranslations('errors');
  const router = useRouter();
  const online = useOnline();
  const tagsId = useId();
  const now = new Date(nowIso);

  const todayKey = dayKey(now, timeZone);
  const today = initialEntries.filter((e) => dayKey(new Date(e.at), timeZone) === todayKey).sort((a, b) => Date.parse(b.at) - Date.parse(a.at))[0];

  const [entries, setEntries] = useState(initialEntries);
  const [score, setScore] = useState(today?.score ?? 5);
  const [tags, setTags] = useState<MoodTag[]>([]);
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState(false);
  const [saved, setSaved] = useState(false);
  const [queued, setQueued] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // A check-in queued earlier arrives: it joins the chart.
  useEffect(() => {
    const sent = (event: Event) => {
      const { item, response } = (event as CustomEvent<OutboxSentDetail>).detail;
      const entry = (response as Partial<MoodSaveResponse> | null)?.entry;
      if (item.kind === 'mood' && entry) setEntries((current) => (current.some((e) => e.id === entry.id) ? current : [...current, entry]));
    };
    window.addEventListener(OUTBOX_SENT_EVENT, sent);
    return () => window.removeEventListener(OUTBOX_SENT_EVENT, sent);
  }, []);

  function toggle(tag: MoodTag) {
    setSaved(false);
    setTags((current) => (current.includes(tag) ? current.filter((x) => x !== tag) : [...current, tag]));
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setSaved(false);
    setQueued(false);
    setBusy(true);
    const body: MoodEntryRequest = { score, tags };
    if (note.trim()) body.note = note.trim();
    try {
      const result = await submitWrite<MoodSaveResponse>({ kind: 'mood', method: 'POST', path: 'mood', body });
      if (result.status === 'rejected') {
        setError(problemMessage(result.error, te('network')));
        return;
      }
      if (result.status === 'queued') {
        setNote('');
        setQueued(true);
        return;
      }
      if (result.response.crisis) {
        router.replace('/crisis');
        return;
      }
      setEntries((current) => [...current, result.response.entry]);
      setNote('');
      setSaved(true);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={submit} noValidate className="flex flex-col gap-5">
      <Card>
        <ScoreSlider
          variant="big"
          label={t('sliderLabel')}
          value={score}
          onChange={(v) => {
            setSaved(false);
            setScore(v);
          }}
          valueText={t('valueText', { value: score, word: t(`words.${moodWord(score)}`) })}
          lowLabel={t('low')}
          highLabel={t('high')}
        />
      </Card>

      <div className="flex flex-col gap-2">
        <p id={tagsId} className="text-base font-bold">
          {t('tagsLabel')} <span className="font-normal text-muted">{t('optional')}</span>
        </p>
        <div role="group" aria-labelledby={tagsId} className="flex flex-wrap gap-2">
          {MOOD_TAGS.map((tag) => (
            <Chip key={tag} selected={tags.includes(tag)} onClick={() => toggle(tag)}>
              {t(`tags.${tag}`)}
            </Chip>
          ))}
        </div>
      </div>

      <TextArea label={t('note')} optional={t('optional')} value={note} maxLength={1000} rows={3} onChange={(e) => setNote(e.target.value)} />

      <MoodChart entries={entries} now={now} timeZone={timeZone} />

      {!online && !error && !queued ? <Notice tone="warning">{t('offline')}</Notice> : null}
      {queued ? (
        <Notice tone="warning">
          <span data-testid="checkin-queued">{t('queued')}</span>
        </Notice>
      ) : null}
      {error ? <Notice tone="danger">{error}</Notice> : null}
      {saved ? (
        <Notice tone="success">
          <span className="flex flex-wrap items-center justify-between gap-2">
            {t('saved')}
            <ButtonLink href="/home" variant="ghost" compact>
              {t('backHome')}
            </ButtonLink>
          </span>
        </Notice>
      ) : null}

      <Button type="submit" full busy={busy}>
        {t('save')}
      </Button>
    </form>
  );
}
