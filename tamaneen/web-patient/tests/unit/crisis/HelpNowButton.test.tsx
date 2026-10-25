import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { api, Link, resetMocks, routeApi, router } from '../intake/mocks';

let pathname = '/home';
vi.mock('@/lib/api/client', () => ({ api: (...args: unknown[]) => api(...args) }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router, usePathname: () => pathname, Link }));

import { HelpNow } from '@/components/crisis/HelpNowButton';
import { HELP_QUEUE_KEY, resetHelpQueue } from '@/components/crisis/helpNow';
import { HelpNowSessionMarker } from '@/components/crisis/helpNowSession';

beforeEach(() => {
  resetMocks();
  resetHelpQueue();
  pathname = '/home';
});

function signedIn(locale: 'ar' | 'en' = 'ar') {
  return renderWithIntl(
    <>
      <HelpNowSessionMarker emergencyNumber="997" supportLine="920033360" />
      <HelpNow />
    </>,
    locale,
  );
}

describe('Help now in the header (PB-26)', () => {
  it('links to the public emergency resources without a session', () => {
    renderWithIntl(<HelpNow />, 'ar');
    expect(screen.getByTestId('help-now')).toHaveAttribute('href', '/crisis-resources');
  });

  it('signed in: shows the numbers at once, sends one Critical request and opens the crisis screen', async () => {
    let release!: () => void;
    const bodies: unknown[] = [];
    routeApi({
      'POST crisis/help': (body) => {
        bodies.push(body);
        return new Promise((resolve) => {
          release = () => resolve({ crisis: true, level: 4, eventOpen: true });
        });
      },
    });
    signedIn('ar');
    const button = await screen.findByRole('button', { name: /مساعدة فورية/ });
    await userEvent.click(button);

    // The API has not answered yet: the numbers are already on screen.
    const panel = screen.getByTestId('help-now-panel');
    expect(panel.querySelector('a[href="tel:997"]')).not.toBeNull();
    expect(panel.querySelector('a[href="tel:920033360"]')).not.toBeNull();
    expect(panel.querySelector('a[href="tel:112"]')).not.toBeNull();
    expect(panel.querySelector('a[href="tel:911"]')).not.toBeNull();
    expect(screen.getByTestId('help-now-status')).toHaveTextContent('نرسل طلبك');

    // A double tap while the first is in flight does not send again. The button is never disabled.
    expect(button).not.toBeDisabled();
    await userEvent.click(button);
    expect(bodies).toHaveLength(1);

    await act(async () => release());
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/crisis'));
    expect(bodies[0]).toEqual({ requestId: expect.any(String) });
  });

  it('offline: keeps the numbers, queues the request and sends it when the browser is back online', async () => {
    let online = false;
    const keys: string[] = [];
    routeApi({
      'POST crisis/help': (body) => {
        keys.push((body as { requestId: string }).requestId);
        if (!online) throw new TypeError('Failed to fetch');
        return { crisis: true, level: 4, eventOpen: true };
      },
    });
    signedIn('en');
    await userEvent.click(await screen.findByRole('button', { name: /Help now/ }));
    expect(await screen.findByTestId('help-now-status')).toHaveTextContent('We will send your request when the connection returns');
    expect(screen.getByTestId('help-now-panel').querySelector('a[href="tel:997"]')).not.toBeNull();
    expect(window.localStorage.getItem(HELP_QUEUE_KEY)).not.toBeNull();

    online = true;
    await act(async () => {
      window.dispatchEvent(new Event('online'));
    });
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/crisis'));
    expect(new Set(keys).size).toBe(1);
    expect(window.localStorage.getItem(HELP_QUEUE_KEY)).toBeNull();
  });

  it('on the crisis screen itself, refreshes it instead of navigating', async () => {
    pathname = '/crisis';
    routeApi({ 'POST crisis/help': () => ({ crisis: true, level: 4, eventOpen: true }) });
    signedIn('ar');
    await userEvent.click(await screen.findByRole('button', { name: /مساعدة فورية/ }));
    await waitFor(() => expect(router.refresh).toHaveBeenCalled());
    expect(router.push).not.toHaveBeenCalled();
  });
});
