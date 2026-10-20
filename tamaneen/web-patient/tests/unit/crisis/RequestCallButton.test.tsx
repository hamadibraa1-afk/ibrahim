import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { api, Link, resetMocks, routeApi, router } from '../intake/mocks';

vi.mock('@/lib/api/client', () => ({ api: (...args: unknown[]) => api(...args) }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router, Link }));

import { RequestCallButton } from '@/components/crisis/RequestCallButton';

beforeEach(resetMocks);

describe('RequestCallButton (P14)', () => {
  it('asks for a call and replaces the page with the crisis screen', async () => {
    routeApi({ 'POST crisis/help': () => ({ crisis: true, level: 4, eventOpen: true }) });
    renderWithIntl(<RequestCallButton label="Ask a professional to call me now" />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Ask a professional to call me now' }));
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/crisis'));
  });

  it('shows the network error when offline', async () => {
    routeApi({
      'POST crisis/help': () => {
        throw new TypeError('Failed to fetch');
      },
    });
    renderWithIntl(<RequestCallButton label="اطلب" />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'اطلب' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('تعذّر الاتصال');
  });
});
