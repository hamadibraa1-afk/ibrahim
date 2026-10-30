import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { resetAll, router } from './helpers';

vi.mock('@/i18n/navigation', async () => (await import('./helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('./helpers')).clientMock());

import { api } from '@/lib/api/client';
import type { ScreeningForm } from '@/lib/api/types';
import { SafetyCheck } from '@/components/onboarding/SafetyCheck';

const apiMock = vi.mocked(api);
const form: ScreeningForm = {
  instrument: 'cssrs',
  version: '1',
  questions: Array.from({ length: 6 }, (_, i) => ({ index: i + 1, text: `سؤال ${i + 1}` })),
  note: 'الترجمة العربية المعتمدة للمقياس.',
};

describe('P23 Safety check', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
  });

  it('asks one question at a time with a counter, the note and the help line, and no way back (Arabic)', async () => {
    apiMock.mockResolvedValue({ crisis: false, level: 0 });
    renderWithIntl(<SafetyCheck form={form} emergencyNumber="997" />, 'ar');
    expect(screen.getByTestId('question-count')).toHaveTextContent('1 من 6');
    expect(screen.getByRole('heading', { level: 2, name: 'سؤال 1' })).toBeInTheDocument();
    expect(screen.getByText('الترجمة العربية المعتمدة للمقياس.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '997' })).toHaveAttribute('href', 'tel:997');

    await userEvent.click(screen.getByRole('radio', { name: 'لا' }));
    expect(await screen.findByRole('heading', { level: 2, name: 'سؤال 2' })).toHaveFocus();
    expect(screen.getByTestId('question-count')).toHaveTextContent('2 من 6');
    expect(screen.queryByRole('button', { name: 'السؤال السابق' })).toBeNull();
    expect(apiMock).toHaveBeenCalledWith('screening/cssrs', { method: 'POST', body: { answers: [false] } });
  });

  it('sends each answer to the gate as it is given and opens the crisis screen at the first «yes» that triggers it', async () => {
    apiMock.mockResolvedValueOnce({ crisis: false, level: 0 }).mockResolvedValueOnce({ crisis: true, level: 3 });
    renderWithIntl(<SafetyCheck form={form} emergencyNumber="997" />, 'en');
    await userEvent.click(screen.getByRole('radio', { name: 'No' }));
    await screen.findByRole('heading', { level: 2, name: 'سؤال 2' });
    await userEvent.click(screen.getByRole('radio', { name: 'Yes' }));
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/crisis'));
    expect(apiMock).toHaveBeenLastCalledWith('screening/cssrs', { method: 'POST', body: { answers: [false, true] } });
    expect(apiMock).toHaveBeenCalledTimes(2);
    expect(router.push).not.toHaveBeenCalled();
  });

  it('keeps the answer and offers a retry when sending fails', async () => {
    apiMock.mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce({ crisis: true, level: 4 });
    renderWithIntl(<SafetyCheck form={form} emergencyNumber="997" />, 'en');
    await userEvent.click(screen.getByRole('radio', { name: 'Yes' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Send again' }));
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/crisis'));
    expect(apiMock).toHaveBeenLastCalledWith('screening/cssrs', { method: 'POST', body: { answers: [true] } });
  });

  it('continues to the therapist directory after the last answer', async () => {
    apiMock.mockResolvedValue({ crisis: false, level: 0 });
    renderWithIntl(<SafetyCheck form={form} emergencyNumber={null} />, 'en');
    expect(screen.getByRole('link', { name: 'open emergency resources' })).toHaveAttribute('href', '/crisis-resources');
    for (let i = 0; i < 6; i++) {
      await screen.findByRole('heading', { level: 2, name: `سؤال ${i + 1}` });
      await userEvent.click(screen.getByRole('radio', { name: 'No' }));
    }
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/therapists'));
    expect(apiMock).toHaveBeenLastCalledWith('screening/cssrs', { method: 'POST', body: { answers: [false, false, false, false, false, false] } });
    expect(router.replace).not.toHaveBeenCalled();
  });

  it('P53 return: after the check passes it reopens matching, and a failed return retries only that step', async () => {
    apiMock.mockImplementation(async (path: string) => {
      if (path === 'me/care/return') throw new TypeError('Failed to fetch');
      return { crisis: false, level: 0 } as never;
    });
    renderWithIntl(<SafetyCheck form={form} emergencyNumber="997" mode="careReturn" />, 'en');
    for (let i = 0; i < 6; i++) {
      await screen.findByRole('heading', { level: 2, name: `سؤال ${i + 1}` });
      await userEvent.click(screen.getByRole('radio', { name: 'No' }));
    }
    await waitFor(() => expect(apiMock).toHaveBeenLastCalledWith('me/care/return', { method: 'POST' }));
    expect(router.push).not.toHaveBeenCalled();
    apiMock.mockImplementation(async () => ({ next: '/matches' }) as never);
    await userEvent.click(await screen.findByRole('button', { name: 'Send again' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/matches'));
    expect(apiMock.mock.calls.filter(([p]) => p === 'screening/cssrs')).toHaveLength(6);
  });

  it('P53 return: a «yes» that triggers the gate opens the crisis screen and never reopens matching', async () => {
    apiMock.mockResolvedValue({ crisis: true, level: 3 });
    renderWithIntl(<SafetyCheck form={form} emergencyNumber="997" mode="careReturn" />, 'en');
    await userEvent.click(screen.getByRole('radio', { name: 'Yes' }));
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/crisis'));
    expect(apiMock).not.toHaveBeenCalledWith('me/care/return', expect.anything());
  });
});
