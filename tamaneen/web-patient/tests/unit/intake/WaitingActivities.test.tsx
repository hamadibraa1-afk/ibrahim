import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { hoursUntil } from '@/components/intake/triage';
import { renderWithIntl } from '../intl';
import { api, Link, resetMocks, routeApi, router } from './mocks';

vi.mock('@/lib/api/client', () => ({ api: (...args: unknown[]) => api(...args) }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router, Link }));

import { WaitingActivities } from '@/components/intake/WaitingActivities';

beforeEach(resetMocks);

describe('WaitingActivities (P36)', () => {
  it('opens the breathing exercise from its tile', async () => {
    renderWithIntl(<WaitingActivities />, 'ar');
    const tile = screen.getByRole('button', { name: /تمرين تنفس/ });
    expect(tile).toHaveAttribute('aria-expanded', 'false');
    await userEvent.click(tile);
    expect(tile).toHaveAttribute('aria-expanded', 'true');
    expect(screen.getByTestId('breathing')).toBeInTheDocument();
  });

  it('adds something to the summary through the gate', async () => {
    routeApi({ 'POST triage/addendum': () => ({ status: 'pending', submittedAt: null, expectedBy: null, suggestionsReady: false }) });
    renderWithIntl(<WaitingActivities />, 'en');
    await userEvent.click(screen.getByRole('button', { name: /Add something/ }));
    await userEvent.type(screen.getByRole('textbox', { name: 'What would you like to add?' }), 'I also stopped eating well.');
    await userEvent.click(screen.getByRole('button', { name: 'Add to the summary' }));
    expect(api).toHaveBeenCalledWith('triage/addendum', { method: 'POST', body: { text: 'I also stopped eating well.' } });
    expect(await screen.findByText('We added it to your summary. Thank you.')).toBeInTheDocument();
  });

  it('goes to the crisis screen when the addendum is gated', async () => {
    routeApi({ 'POST triage/addendum': () => ({ crisis: true }) });
    renderWithIntl(<WaitingActivities />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: /أضف شيئًا/ }));
    await userEvent.type(screen.getByRole('textbox', { name: 'ما الذي تريد إضافته؟' }), 'نص');
    await userEvent.click(screen.getByRole('button', { name: 'أضف إلى الملخص' }));
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/crisis'));
  });

  it('computes the expected hours from expectedBy', () => {
    const now = Date.parse('2026-10-20T10:00:00Z');
    expect(hoursUntil('2026-10-21T04:00:00Z', now)).toBe(18);
    expect(hoursUntil('2026-10-20T10:20:00Z', now)).toBe(1);
    expect(hoursUntil('2026-10-20T09:00:00Z', now)).toBe(0);
    expect(hoursUntil('not a date', now)).toBe(0);
  });
});
