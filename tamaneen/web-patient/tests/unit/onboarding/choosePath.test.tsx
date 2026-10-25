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

  it('asks the team to get in touch only after a confirmation sheet with the expected wait', async () => {
    apiMock.mockResolvedValueOnce({ requestedAt: '2026-10-20T09:00:00Z', expectedBy: '2026-10-21T09:00:00Z', withinHours: 24 });
    renderWithIntl(<ChoosePath current={null} triageWithinHours={24} />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Ask the team to contact you' }));
    const sheet = await screen.findByRole('dialog', { name: 'Ask the team to contact you?' });
    expect(sheet).toHaveTextContent('within 24 hours at most');
    expect(apiMock).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'Yes, ask them to contact me' }));
    expect(apiMock).toHaveBeenCalledWith('me/contact-request?source=choose_path', { method: 'POST' });
    expect(await screen.findByRole('status')).toHaveTextContent('We got your request. Someone from the team will contact you before');
  });

  it('cancelling the sheet sends nothing (Arabic)', async () => {
    renderWithIntl(<ChoosePath current={null} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'اطلب التواصل مع الفريق' }));
    await userEvent.click(await screen.findByRole('button', { name: 'ليس الآن' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('says "continue" on the intake card while an intake is open', () => {
    renderWithIntl(<ChoosePath current="intake" intakeStatus="paused" />, 'ar');
    expect(screen.getByTestId('path-intake')).toHaveTextContent('متابعة محادثة التعارف');
  });
});
