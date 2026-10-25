import { setRequestLocale } from 'next-intl/server';
import { IntakeChat } from '@/components/intake/IntakeChat';
import { loadSafetyNumbers } from '@/lib/platform-config';

/** P04 Intake chat. The conversation is client-side: it starts or resumes the intake with POST /v1/intake. */
export default async function IntakePage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  const { triageWithinHours } = await loadSafetyNumbers();
  return <IntakeChat triageWithinHours={triageWithinHours} />;
}
