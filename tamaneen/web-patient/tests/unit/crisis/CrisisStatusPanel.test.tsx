import { act, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { CrisisStatus } from '@/lib/api/types';
import { renderWithIntl } from '../intl';
import { api, Link, resetMocks, routeApi, router } from '../intake/mocks';

vi.mock('@/lib/api/client', () => ({ api: (...args: unknown[]) => api(...args) }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router, Link }));

import { CRISIS_POLL_MS, CrisisStatusPanel } from '@/components/crisis/CrisisStatusPanel';

const open: CrisisStatus = { locked: true, eventOpen: true, acknowledged: false, contactPhoneLast4: '4521', script: null };

beforeEach(resetMocks);
afterEach(() => vi.useRealTimers());

describe('CrisisStatusPanel (P06)', () => {
  it('shows the steps and the last four digits in Arabic', () => {
    renderWithIntl(<CrisisStatusPanel initial={open} />, 'ar');
    const steps = screen.getByRole('list', { name: 'ما يحدث الآن' });
    expect(steps).toHaveTextContent('تم إبلاغ الفريق');
    expect(steps).toHaveTextContent('قيد الانتظار');
    expect(steps).toHaveTextContent('مختص يتصل بك');
    expect(steps).toHaveTextContent('على الرقم المنتهي بـ 4521');
    expect(screen.getByText('4521').tagName).toBe('BDI');
  });

  it('polls every 20 seconds and shows when a professional has acknowledged', async () => {
    vi.useFakeTimers();
    let calls = 0;
    routeApi({
      'GET crisis/status': () => {
        calls += 1;
        return calls === 1 ? open : { ...open, acknowledged: true, script: 'سنتصل بك الآن من رقم يبدأ بـ 011.' };
      },
    });
    renderWithIntl(<CrisisStatusPanel initial={open} />, 'en');
    expect(screen.queryByTestId('crisis-acknowledged')).not.toBeInTheDocument();

    await act(async () => vi.advanceTimersByTimeAsync(CRISIS_POLL_MS));
    expect(calls).toBe(1);
    expect(screen.queryByTestId('crisis-acknowledged')).not.toBeInTheDocument();

    await act(async () => vi.advanceTimersByTimeAsync(CRISIS_POLL_MS));
    expect(calls).toBe(2);
    expect(screen.getByTestId('crisis-acknowledged')).toHaveTextContent('A professional has your request and will call you now.');
    expect(screen.getByText('سنتصل بك الآن من رقم يبدأ بـ 011.')).toBeInTheDocument();

    // Polling stops once acknowledged.
    await act(async () => vi.advanceTimersByTimeAsync(CRISIS_POLL_MS * 3));
    expect(calls).toBe(2);
  });

  it('offers to request a call when no event is open', async () => {
    let opened = false;
    routeApi({
      'POST crisis/help': () => {
        opened = true;
        return { crisis: true, level: 4, eventOpen: true };
      },
      'GET crisis/status': () => (opened ? open : { ...open, locked: false, eventOpen: false }),
    });
    renderWithIntl(<CrisisStatusPanel initial={{ ...open, locked: false, eventOpen: false }} />, 'ar');
    expect(screen.getByText('هل تريد أن يتصل بك مختص؟')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'اطلب أن يتصل بك مختص الآن' }));
    expect(await screen.findByText('تم إبلاغ الفريق')).toBeInTheDocument();
    expect(router.replace).not.toHaveBeenCalled();
  });
});
