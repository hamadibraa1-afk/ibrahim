import { setRequestLocale } from 'next-intl/server';
import { SummaryReview } from '@/components/intake/SummaryReview';
import { redirect } from '@/i18n/navigation';
import { isLocale, routing } from '@/i18n/routing';
import { serverGet, serverGetOptional } from '@/lib/api/server';
import type { IntakeState, IntakeSummary } from '@/lib/api/types';
import { loadSafetyNumbers } from '@/lib/platform-config';

/** P05 Summary review. Finds the current intake, then loads its summary for the patient to correct and send. */
export default async function SummaryPage({ params }: { params: Promise<{ locale: string }> }) {
  const { locale: raw } = await params;
  const locale = isLocale(raw) ? raw : routing.defaultLocale;
  setRequestLocale(locale);

  const intake = await serverGetOptional<IntakeState>('intake/current');
  if (!intake) return redirect({ href: '/intake', locale });
  if (intake.status === 'submitted') return redirect({ href: '/waiting', locale });
  if (intake.status !== 'summarizing' && intake.status !== 'review') return redirect({ href: '/intake', locale });

  const [summary, numbers] = await Promise.all([serverGet<IntakeSummary>(`intake/${intake.intakeId}/summary`), loadSafetyNumbers()]);
  return <SummaryReview initial={summary} reviewHours={numbers.triageWithinHours} />;
}
