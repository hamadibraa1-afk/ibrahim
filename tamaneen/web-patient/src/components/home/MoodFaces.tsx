'use client';

import { useTranslations } from 'next-intl';
import { useId, useState } from 'react';
import { Link, useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { MoodSaveResponse } from '@/lib/api/types';
import { Card } from '@/components/ui/Card';
import { FaceIcon } from '@/components/mood/FaceIcon';
import { MOOD_FACES, nearestFace, type MoodWord } from '@/components/mood/scale';
import { problemMessage } from '@/components/settings/problem';

/** "How do you feel now?": one tap saves a 0–10 score (0, 3, 5, 7, 10). A blocked gate opens the crisis screen. */
export function MoodFaces({ initialScore }: { initialScore: number | null }) {
  const t = useTranslations('home');
  const te = useTranslations('errors');
  const router = useRouter();
  const headingId = useId();
  const [selected, setSelected] = useState<MoodWord | null>(initialScore === null ? null : nearestFace(initialScore));
  const [busy, setBusy] = useState<MoodWord | null>(null);
  const [saved, setSaved] = useState<MoodWord | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function choose(key: MoodWord, score: number) {
    if (busy) return;
    setBusy(key);
    setError(null);
    setSaved(null);
    try {
      const result = await api<MoodSaveResponse>('mood', { method: 'POST', body: { score, tags: [] } });
      if (result.crisis) {
        router.replace('/crisis');
        return;
      }
      setSelected(key);
      setSaved(key);
    } catch (e) {
      setError(problemMessage(e, te('network')));
    } finally {
      setBusy(null);
    }
  }

  return (
    <Card aria-labelledby={headingId} className="flex flex-col gap-3">
      <h2 id={headingId} className="text-base font-bold">
        {t('moodTitle')}
      </h2>
      <div role="group" aria-label={t('moodGroup')} className="flex justify-between gap-1">
        {MOOD_FACES.map((face) => {
          const isSelected = selected === face.key;
          return (
            <button
              key={face.key}
              type="button"
              aria-label={t(`faces.${face.key}`)}
              aria-pressed={isSelected}
              aria-busy={busy === face.key || undefined}
              disabled={busy !== null}
              onClick={() => choose(face.key, face.score)}
              className={`flex h-14 w-14 items-center justify-center rounded-full border-2 transition-colors disabled:cursor-wait ${
                isSelected ? 'border-primary bg-tint-teal' : 'border-transparent bg-bg'
              }`}
            >
              <FaceIcon mood={face.key} />
            </button>
          );
        })}
      </div>
      <p className="text-center text-xs text-muted">
        {t('moodHintText')}
        {' · '}
        <Link href="/check-in" className="font-semibold text-primary underline-offset-4 hover:underline">
          {t('moodHintLink')}
        </Link>
      </p>
      <div aria-live="polite" className="empty:hidden">
        {saved ? <p className="text-center text-sm font-semibold text-primary">{t('moodSaved', { face: t(`faces.${saved}`) })}</p> : null}
      </div>
      {error ? (
        <p role="alert" className="text-center text-sm font-semibold text-help">
          {error}
        </p>
      ) : null}
    </Card>
  );
}
