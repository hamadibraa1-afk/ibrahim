'use client';

import { useTranslations } from 'next-intl';
import { useEffect, useRef, useState } from 'react';
import { TextArea } from '@/components/ui/TextField';
import { useRouter } from '@/i18n/navigation';
import { api } from '@/lib/api/client';
import type { Gender, PreSessionNoteSaved } from '@/lib/api/types';
import { genderKey } from '@/components/matching/labels';

export const NOTE_MAX = 1000;
export const NOTE_DEBOUNCE_MS = 1500;

/**
 * P41 «عن ماذا تحب أن نتحدث اليوم؟ (اختياري)»: up to 1,000 characters, saved automatically after a pause in typing.
 * The text goes through the crisis gate on the server; a crisis answer opens the crisis screen at once.
 */
export function PreSessionNoteField({ appointmentId, initial, gender, disabled = false }: { appointmentId: string; initial: string; gender: Gender; disabled?: boolean }) {
  const t = useTranslations('session.lobby');
  const tUi = useTranslations('ui');
  const router = useRouter();
  const [text, setText] = useState(initial);
  const [status, setStatus] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
  const saved = useRef(initial);

  useEffect(() => {
    if (disabled || text === saved.current) return;
    const id = window.setTimeout(async () => {
      setStatus('saving');
      try {
        const result = await api<PreSessionNoteSaved>(`appointments/${encodeURIComponent(appointmentId)}/pre-session-note`, { method: 'PUT', body: { text } });
        saved.current = text;
        if (result.crisis) {
          router.replace('/crisis');
          return;
        }
        setStatus('saved');
      } catch {
        setStatus('error');
      }
    }, NOTE_DEBOUNCE_MS);
    return () => window.clearTimeout(id);
  }, [text, appointmentId, disabled, router]);

  const hint = status === 'saving' ? t('noteSaving') : status === 'saved' ? t('noteSaved') : t(genderKey('noteHint', gender));
  return (
    <div className="flex flex-col gap-1">
      <TextArea
        label={t('noteLabel')}
        optional={`(${tUi('optional')})`}
        hint={hint}
        error={status === 'error' ? t('noteError') : undefined}
        value={text}
        maxLength={NOTE_MAX}
        rows={3}
        disabled={disabled}
        onChange={(e) => setText(e.target.value)}
      />
    </div>
  );
}
