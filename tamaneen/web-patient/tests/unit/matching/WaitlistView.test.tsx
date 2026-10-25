import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import type { Waitlist } from '@/lib/api/types';
import { renderWithIntl } from '../intl';
import { khaled, sara, suggestion } from './fixtures';

const mocks = vi.hoisted(() => ({ api: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api }));
vi.mock('@/i18n/navigation', () => ({
  Link: ({ href, children, ...rest }: { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { MatchSuggestions } from '@/components/matching/MatchSuggestions';
import { WaitlistView } from '@/components/matching/WaitlistView';

const none: Waitlist = { status: 'none', therapistId: null, therapistName: null, preferences: null, joinedAt: null, expectedDaysMin: 3, expectedDaysMax: 5, offer: null };
const waiting: Waitlist = {
  ...none,
  status: 'waiting',
  preferences: { gender: 'female', language: 'ar', timesOfDay: ['evening'], days: ['weekend'] },
  joinedAt: '2026-10-19T09:00:00Z',
};

describe('WaitlistView (P56)', () => {
  afterEach(() => mocks.api.mockReset());

  it('joins with the chosen preferences for the therapist from P25 (Arabic)', async () => {
    mocks.api.mockResolvedValue({ ...waiting, therapistId: 't-sara', therapistName: 'أ. سارة م.' });
    renderWithIntl(<WaitlistView initial={none} therapistId="t-sara" therapistName="أ. سارة م." timeZone="Asia/Riyadh" />, 'ar');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('انضم لقائمة الانتظار');
    expect(screen.getByText(/لا توجد مواعيد متاحة حاليًا مع أ. سارة م/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'أخصائية' }));
    expect(screen.getByRole('button', { name: 'أخصائية' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'مساءً' }));
    await userEvent.click(screen.getByRole('button', { name: 'نهاية الأسبوع' }));
    await userEvent.click(screen.getAllByRole('button', { name: 'انضم لقائمة الانتظار' })[0]!);
    expect(mocks.api).toHaveBeenCalledWith('me/waitlist', {
      method: 'PUT',
      body: { gender: 'female', language: 'any', timesOfDay: ['evening'], days: ['weekend'], therapistId: 't-sara' },
    });
    expect(await screen.findByText('نبحث لك عن الأخصائي المناسب')).toBeInTheDocument();
    expect(screen.getByText('3 – 5 أيام')).toBeInTheDocument();
    expect(screen.getByText(/لن تنتظر في الطوارئ/)).toBeInTheDocument();
  });

  it('widens the preferences and leaves the waitlist (English)', async () => {
    mocks.api.mockResolvedValueOnce({ ...waiting, preferences: { ...waiting.preferences!, days: [] } }).mockResolvedValueOnce(undefined);
    renderWithIntl(<WaitlistView initial={waiting} therapistId={null} therapistName={null} timeZone="Asia/Riyadh" />, 'en');
    expect(screen.getByText('3–5 days')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Edit my preferences' }));
    expect(screen.getByRole('button', { name: 'Weekends' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.click(screen.getByRole('button', { name: 'Weekends' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save my preferences' }));
    expect(mocks.api).toHaveBeenCalledWith('me/waitlist', { method: 'PUT', body: { gender: 'female', language: 'ar', timesOfDay: ['evening'], days: [], therapistId: null } });
    expect(await screen.findByText(/your place in the queue stays the same/)).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: 'Leave the waitlist' }));
    expect(mocks.api).toHaveBeenLastCalledWith('me/waitlist', { method: 'DELETE' });
    await waitFor(() => expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Join the waitlist'));
  });

  it('offers the time that opened with a link to book it', () => {
    const available: Waitlist = { ...waiting, status: 'available', offer: { therapistId: 't-sara', therapistName: 'أ. سارة م.', startsAt: '2026-10-23T15:00:00Z' } };
    renderWithIntl(<WaitlistView initial={available} therapistId={null} therapistName={null} timeZone="Asia/Riyadh" />, 'en');
    expect(screen.getByTestId('waitlist-offer')).toHaveTextContent('A time that suits you is free');
    expect(screen.getByRole('link', { name: 'Book this time' })).toHaveAttribute('href', '/therapists/t-sara/book');
  });
});

describe('P07 without free capacity', () => {
  it('points to the waitlist when no suggested therapist has a free time', () => {
    const full = [suggestion({ ...sara, nextAvailableAt: null }, 1), suggestion({ ...khaled, nextAvailableAt: null }, 2)];
    renderWithIntl(<MatchSuggestions initial={full} patientLanguage="ar" timeZone="Asia/Riyadh" />, 'ar');
    expect(screen.getByTestId('no-capacity')).toHaveTextContent('كل الأخصائيين المقترحين مشغولون حاليًا');
    expect(screen.getByRole('link', { name: 'انضم لقائمة الانتظار' })).toHaveAttribute('href', '/waitlist');
  });

  it('shows no waitlist card while someone has a time', () => {
    renderWithIntl(<MatchSuggestions initial={[suggestion(sara, 1)]} patientLanguage="en" timeZone="Asia/Riyadh" />, 'en');
    expect(screen.queryByTestId('no-capacity')).toBeNull();
  });
});
