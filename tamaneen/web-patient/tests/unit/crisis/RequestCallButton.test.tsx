import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { api, Link, resetMocks, routeApi, router } from '../intake/mocks';

vi.mock('@/lib/api/client', () => ({ api: (...args: unknown[]) => api(...args) }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router, Link }));

import { flushHelp, HELP_QUEUE_KEY, resetHelpQueue } from '@/components/crisis/helpNow';
import { RequestCallButton } from '@/components/crisis/RequestCallButton';
import { ApiError } from '@/lib/api/errors';

beforeEach(() => {
  resetMocks();
  resetHelpQueue();
});

describe('RequestCallButton (P14, P06)', () => {
  it('asks for a call with an idempotency key and replaces the page with the crisis screen', async () => {
    const bodies: unknown[] = [];
    routeApi({
      'POST crisis/help': (body) => {
        bodies.push(body);
        return { crisis: true, level: 4, eventOpen: true };
      },
    });
    renderWithIntl(<RequestCallButton label="Ask a professional to call me now" />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Ask a professional to call me now' }));
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/crisis'));
    expect(bodies).toEqual([{ requestId: expect.stringMatching(/^[0-9a-f-]{36}$/) }]);
    expect(window.localStorage.getItem(HELP_QUEUE_KEY)).toBeNull();
  });

  it('queues the request offline (EC-04) and sends it with the same key when the connection returns', async () => {
    const keys: string[] = [];
    let online = false;
    routeApi({
      'POST crisis/help': (body) => {
        keys.push((body as { requestId: string }).requestId);
        if (!online) throw new TypeError('Failed to fetch');
        return { crisis: true, level: 4, eventOpen: true };
      },
    });
    const onRequested = vi.fn();
    renderWithIntl(<RequestCallButton label="اطلب" onRequested={onRequested} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'اطلب' }));
    expect(await screen.findByTestId('request-queued')).toHaveTextContent('سنرسل طلبك عند عودة الاتصال');
    expect(window.localStorage.getItem(HELP_QUEUE_KEY)).toContain(keys[0]);

    // A second press while queued reuses the key: one event, however many taps.
    await userEvent.click(screen.getByRole('button', { name: 'اطلب' }));
    online = true;
    await act(async () => {
      await flushHelp();
    });
    await waitFor(() => expect(onRequested).toHaveBeenCalled());
    expect(new Set(keys).size).toBe(1);
    expect(screen.queryByTestId('request-queued')).toBeNull();
  });

  it('shows the API message when the request is refused, and does not keep it', async () => {
    routeApi({
      'POST crisis/help': () => {
        throw new ApiError(403, { title: 'Forbidden', status: 403, detail: 'غير مسموح' });
      },
    });
    renderWithIntl(<RequestCallButton label="اطلب" />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'اطلب' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('غير مسموح');
    expect(window.localStorage.getItem(HELP_QUEUE_KEY)).toBeNull();
  });
});
