import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { resetAll, router } from './helpers';

vi.mock('@/i18n/navigation', async () => (await import('./helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('./helpers')).clientMock());

import { api } from '@/lib/api/client';
import { ChoosePath } from '@/components/onboarding/ChoosePath';

const apiMock = vi.mocked(api);

describe('P22 Choose path', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
  });

  it('starts the intake path (Arabic)', async () => {
    apiMock.mockResolvedValueOnce({});
    renderWithIntl(<ChoosePath current={null} />, 'ar');
    expect(screen.getByText('ننصح به')).toBeInTheDocument();
    expect(screen.getByText('يمكنك الإيقاف والعودة خلال 14 يومًا')).toBeInTheDocument();
    await userEvent.click(screen.getByTestId('path-intake'));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/intake'));
    expect(apiMock).toHaveBeenCalledWith('me/path', { method: 'PUT', body: { path: 'intake' } });
  });

  it('books directly through the safety check (English)', async () => {
    apiMock.mockResolvedValueOnce({});
    renderWithIntl(<ChoosePath current={null} />, 'en');
    await userEvent.click(screen.getByRole('button', { name: /I know what I want, I'll book directly/ }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/start/safety-check'));
  });

  it('asks the team to get in touch and confirms', async () => {
    apiMock.mockResolvedValueOnce({ requestedAt: '2026-10-20T09:00:00Z' });
    renderWithIntl(<ChoosePath current={null} />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Ask the team to contact you' }));
    expect(await screen.findByRole('status')).toHaveTextContent("We've received your request.");
    expect(apiMock).toHaveBeenCalledWith('me/contact-request', { method: 'POST' });
  });
});
