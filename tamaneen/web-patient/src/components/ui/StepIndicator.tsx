import { getTranslations } from 'next-intl/server';

/** "Step 1 of 4" with a segmented bar (signup → verify → consents → safety info). */
export async function StepIndicator({ step, total, optional }: { step: number; total: number; optional?: boolean }) {
  const t = await getTranslations('ui');
  return (
    <div className="flex flex-col gap-2">
      <p className="text-sm font-semibold text-primary">
        {t('step', { step, total })}
        {optional ? ` · ${t('optional')}` : ''}
      </p>
      <div aria-hidden="true" className="flex gap-1.5">
        {Array.from({ length: total }, (_, i) => (
          <span key={i} className={`h-1.5 flex-1 rounded-full ${i < step ? 'bg-primary' : 'bg-line'}`} />
        ))}
      </div>
    </div>
  );
}
