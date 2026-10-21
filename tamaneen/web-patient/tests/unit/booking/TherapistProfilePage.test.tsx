import { render, screen } from '@testing-library/react';
import { createTranslator } from 'next-intl';
import type { ReactNode } from 'react';
import type { Me, TherapistProfile } from '@/lib/api/types';
import { canBook } from '@/lib/booking';
import { messagesFor } from '../intl';
import { therapist } from '../matching/fixtures';

const mocks = vi.hoisted(() => ({ getMe: vi.fn(), serverGetOptional: vi.fn() }));
vi.mock('@/lib/api/server', () => ({ getMe: mocks.getMe, serverGetOptional: mocks.serverGetOptional }));
vi.mock('next/navigation', () => ({ notFound: () => { throw new Error('not found'); } }));
vi.mock('next-intl/server', () => ({
  setRequestLocale: () => undefined,
  getTranslations: async (arg: string | { namespace: string }) =>
    createTranslator({ locale: 'ar', messages: messagesFor('ar'), namespace: (typeof arg === 'string' ? arg : arg.namespace) as never }),
}));
vi.mock('@/components/ui/BackLink', () => ({ BackLink: ({ href }: { href: string }) => <a href={href}>back</a> }));
vi.mock('@/i18n/navigation', () => ({
  Link: ({ href, children, ...rest }: { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import TherapistProfilePage from '@/app/[locale]/(app)/therapists/[id]/page';

function me(onboarding: Partial<Me['onboarding']> = {}, chatLocked = false): Me {
  return {
    pseudonymId: 'P-1',
    nickname: 'نور',
    language: 'ar',
    timeZone: 'Asia/Riyadh',
    onboarding: { consentsDone: true, safetyInfoDone: true, path: 'intake', screeningDone: false, intakeStatus: 'submitted', triageStatus: 'reviewed', ...onboarding },
    chatLocked,
  };
}

const profile = (overrides: Partial<TherapistProfile> = {}): TherapistProfile => ({ ...therapist(overrides), bio: 'نهج هادئ', sessionType: 'remote', durationMinutes: 50, ...overrides });

async function renderPage(patient: Me, t: TherapistProfile) {
  mocks.getMe.mockResolvedValue(patient);
  mocks.serverGetOptional.mockResolvedValue(t);
  render(await TherapistProfilePage({ params: Promise.resolve({ locale: 'ar', id: t.id }) }));
}

describe('canBook', () => {
  it('follows the server rule: reviewed triage, or a finished safety check on the direct path', () => {
    expect(canBook(me({ triageStatus: 'reviewed' }))).toBe(true);
    expect(canBook(me({ triageStatus: 'matched' }))).toBe(true);
    expect(canBook(me({ triageStatus: 'pending' }))).toBe(false);
    expect(canBook(me({ path: 'direct', screeningDone: true, triageStatus: 'none' }))).toBe(true);
    expect(canBook(me({ path: 'direct', screeningDone: false, triageStatus: 'none' }))).toBe(false);
    expect(canBook(me({ path: 'intake', screeningDone: true, triageStatus: 'none' }))).toBe(false);
    expect(canBook(me({}, true))).toBe(false);
  });
});

describe('TherapistProfilePage (P25)', () => {
  afterEach(() => vi.clearAllMocks());

  it('links to booking when the patient may book and there are times', async () => {
    await renderPage(me(), profile());
    expect(screen.getByRole('link', { name: 'اختيار موعد' })).toHaveAttribute('href', '/therapists/t-sara/book');
  });

  it('does not render the booking button for a patient whose file is still under review', async () => {
    await renderPage(me({ triageStatus: 'pending' }), profile());
    expect(screen.queryByRole('link', { name: 'اختيار موعد' })).toBeNull();
    expect(screen.queryByRole('button', { name: 'اختيار موعد' })).toBeNull();
  });

  it('disables the button and points to other therapists when there are no times', async () => {
    await renderPage(me(), profile({ nextAvailableAt: null }));
    expect(screen.getByRole('button', { name: 'اختيار موعد' })).toBeDisabled();
    expect(screen.queryByRole('link', { name: 'اختيار موعد' })).toBeNull();
    expect(screen.getByRole('status')).toHaveTextContent('لا مواعيد حاليًا');
    expect(screen.getByRole('link', { name: 'عرض الأخصائيين' })).toHaveAttribute('href', '/therapists');
  });
});
