'use client';

import { useTranslations } from 'next-intl';
import type { PendingQuestion } from '@/lib/api/types';
import { OptionButton } from '@/components/ui/OptionButton';

const INSTRUMENT_LABEL: Record<PendingQuestion['instrument'], string> = { phq9: 'PHQ-9', gad7: 'GAD-7' };

/** A validated questionnaire item (PHQ-9 / GAD-7) asked inside the chat. Each option shows its score. */
export function QuestionCard({ question, selected, disabled, onAnswer }: { question: PendingQuestion; selected: number | null; disabled: boolean; onAnswer: (value: number) => void }) {
  const t = useTranslations('intake');
  const id = `q-${question.instrument}-${question.item}`;
  return (
    <section aria-labelledby={`${id}-text`} className="flex flex-col gap-3 rounded-card border-[1.5px] border-primary bg-surface p-4" data-testid="question-card">
      <p className="self-start rounded-full bg-tint-teal px-3 py-1 text-xs font-bold text-primary-strong">
        {t('question', { instrument: INSTRUMENT_LABEL[question.instrument], item: question.item, total: question.total })}
      </p>
      <h2 id={`${id}-text`} className="text-base font-bold leading-relaxed">
        {question.text}
      </h2>
      <div role="radiogroup" aria-labelledby={`${id}-text`} className="flex flex-col gap-2">
        {question.options.map((option) => (
          <OptionButton
            key={option.value}
            selected={selected === option.value}
            trailing={<bdi dir="ltr">{option.value}</bdi>}
            disabled={disabled}
            onClick={() => onAnswer(option.value)}
          >
            {option.label}
          </OptionButton>
        ))}
      </div>
    </section>
  );
}
