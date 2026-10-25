import { screen } from '@testing-library/react';
import { renderWithIntl } from '../intl';
import { Link } from './mocks';

vi.mock('@/i18n/navigation', () => ({ Link }));

import { orderReferrals, TriageClosed, TriageEscalated } from '@/components/intake/TriageOutcome';
import { onboardingStep } from '@/lib/onboarding';
import type { Me, TriageStatus } from '@/lib/api/types';

const numbers = { emergencyNumber: '997', supportLine: '920033360' };

describe('P36 closed with referrals', () => {
  it('in Arabic explains kindly and lists every referral, with tel: links where the config has a number', () => {
    renderWithIntl(<TriageClosed referrals={['primary_care', 'crisis_line', 'psychiatry', 'community_support', 'emergency_services']} numbers={numbers} />, 'ar');
    expect(screen.getByRole('heading', { level: 1, name: 'شكرًا لأنك شاركتنا' })).toBeInTheDocument();
    expect(screen.getByText(/هذا ليس رفضًا لك/)).toBeInTheDocument();
    const items = screen.getAllByRole('listitem');
    expect(items.map((li) => li.querySelector('p')?.textContent)).toEqual(['خدمات الطوارئ', 'خط الدعم النفسي', 'طبيب نفسي', 'طبيب الرعاية الأولية', 'الدعم المجتمعي']);
    expect(items[0]!.querySelector('a')).toHaveAttribute('href', 'tel:997');
    expect(items[1]!.querySelector('a')).toHaveAttribute('href', 'tel:920033360');
    expect(items[2]!.querySelector('a')).toBeNull();
    expect(screen.getByText('997').closest('bdi')).toHaveAttribute('dir', 'ltr');
    expect(screen.getByRole('link', { name: 'أرقام المساعدة' })).toHaveAttribute('href', '/crisis-resources');
  });

  it('in English shows only the referrals given, and no call button without a configured number', () => {
    renderWithIntl(<TriageClosed referrals={['crisis_line', 'unknown']} numbers={{ emergencyNumber: null, supportLine: null }} />, 'en');
    expect(screen.getByRole('heading', { level: 1, name: 'Thank you for sharing with us' })).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(1);
    expect(screen.getByText('Mental health support line')).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Call/ })).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Home' })).toHaveAttribute('href', '/home');
  });

  it('orders the codes and drops repeats', () => {
    expect(orderReferrals(['psychiatry', 'emergency_services', 'psychiatry'])).toEqual(['emergency_services', 'psychiatry']);
    expect(orderReferrals(null)).toEqual([]);
  });
});

describe('P36 escalated', () => {
  it('guides the patient to the crisis screen (Arabic)', () => {
    renderWithIntl(<TriageEscalated numbers={numbers} />, 'ar');
    expect(screen.getByRole('link', { name: 'افتح شاشة المساعدة' })).toHaveAttribute('href', '/crisis');
    expect(screen.getByRole('link', { name: /اتصل بالطوارئ/ })).toHaveAttribute('href', 'tel:997');
  });

  it('keeps the crisis link without an emergency number (English)', () => {
    renderWithIntl(<TriageEscalated numbers={{ emergencyNumber: null, supportLine: null }} />, 'en');
    expect(screen.getByRole('link', { name: 'Open the help screen' })).toHaveAttribute('href', '/crisis');
    expect(screen.queryByRole('link', { name: /Call emergency/ })).not.toBeInTheDocument();
  });
});

describe('routing', () => {
  const me = (triageStatus: TriageStatus): Me => ({
    pseudonymId: 'TMN-0000001',
    nickname: 'n',
    language: 'ar',
    timeZone: 'Asia/Riyadh',
    chatLocked: false,
    onboarding: { consentsDone: true, safetyInfoDone: true, path: 'intake', screeningDone: false, intakeStatus: 'submitted', triageStatus },
  });

  it('sends closed and escalated cases to P36 like pending ones', () => {
    expect(onboardingStep(me('pending'))).toBe('/waiting');
    expect(onboardingStep(me('closed'))).toBe('/waiting');
    expect(onboardingStep(me('escalated'))).toBe('/waiting');
    expect(onboardingStep(me('reviewed'))).toBeNull();
  });
});
