import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { apiError, resetAll, router } from './helpers';

vi.mock('@/i18n/navigation', async () => (await import('./helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('./helpers')).clientMock());

import { api } from '@/lib/api/client';
import { EligibilityForm } from '@/components/onboarding/EligibilityForm';
import { FLOW_KEYS } from '@/components/onboarding/flowStorage';

const apiMock = vi.mocked(api);
const props = {
  cities: [
    { code: 'ruh', name: 'الرياض' },
    { code: 'jed', name: 'جدة' },
  ],
  region: 'منطقة الرياض',
  minAge: 18,
  emergencyNumber: '997',
  supportLine: '920033360',
};

describe('P15 Eligibility', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
  });

  it('shows the questions in Arabic with the region and minimum age', () => {
    renderWithIntl(<EligibilityForm {...props} />, 'ar');
    expect(screen.getByRole('heading', { level: 1, name: 'سؤالان قبل البدء' })).toBeInTheDocument();
    expect(screen.getByText('التجربة الحالية للبالغين داخل منطقة الرياض فقط.')).toBeInTheDocument();
    expect(screen.getByText('هل عمرك 18 سنة أو أكثر؟')).toBeInTheDocument();
    expect(screen.getByRole('radio', { name: 'نعم' })).toHaveAttribute('aria-checked', 'false');
  });

  it('asks for both answers before calling the API', async () => {
    renderWithIntl(<EligibilityForm {...props} />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));
    expect(screen.getByText('Please answer the age question.')).toBeInTheDocument();
    expect(screen.getByText('Please choose the city you live in.')).toBeInTheDocument();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('keeps the eligibility token in sessionStorage and moves on to signup', async () => {
    apiMock.mockResolvedValueOnce({ eligible: true, eligibilityToken: 'tok-1' });
    renderWithIntl(<EligibilityForm {...props} />, 'ar');
    await userEvent.click(screen.getByRole('radio', { name: 'نعم' }));
    await userEvent.selectOptions(screen.getByLabelText('أين تقيم حاليًا؟'), 'jed');
    await userEvent.click(screen.getByRole('button', { name: 'متابعة' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/start/signup'));
    expect(apiMock).toHaveBeenCalledWith('auth/eligibility', { method: 'POST', body: { isAdult: true, cityCode: 'jed' } });
    expect(JSON.parse(window.sessionStorage.getItem(FLOW_KEYS.eligibility)!)).toEqual({ eligibilityToken: 'tok-1' });
  });

  it('shows other support services and creates no account when not eligible (English)', async () => {
    apiMock.mockResolvedValueOnce({ eligible: false, reason: 'age' });
    renderWithIntl(<EligibilityForm {...props} />, 'en');
    await userEvent.click(screen.getByRole('radio', { name: 'No' }));
    await userEvent.selectOptions(screen.getByLabelText('Where do you live right now?'), 'ruh');
    await userEvent.click(screen.getByRole('button', { name: 'Continue' }));

    const heading = await screen.findByRole('heading', { level: 1, name: 'Other support services for you' });
    await waitFor(() => expect(heading).toHaveFocus());
    expect(screen.getByText(/for people aged 18 or older, so we won't create an account/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Emergency: 997' })).toHaveAttribute('href', 'tel:997');
    expect(screen.getByRole('link', { name: 'Mental-health support line: 920033360' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'All emergency resources' })).toHaveAttribute('href', '/crisis-resources');
    expect(router.push).not.toHaveBeenCalled();
    expect(window.sessionStorage.getItem(FLOW_KEYS.eligibility)).toBeNull();
  });

  it('shows the region message in Arabic and the API problem on failure', async () => {
    apiMock.mockResolvedValueOnce({ eligible: false, reason: 'region' });
    renderWithIntl(<EligibilityForm {...props} />, 'ar');
    await userEvent.click(screen.getByRole('radio', { name: 'نعم' }));
    await userEvent.selectOptions(screen.getByLabelText('أين تقيم حاليًا؟'), 'ruh');
    await userEvent.click(screen.getByRole('button', { name: 'متابعة' }));
    expect(await screen.findByText(/التجربة الحالية متاحة داخل منطقة الرياض فقط/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'الطوارئ: 997' })).toBeInTheDocument();
  });

  it('shows the localized API problem when the request fails', async () => {
    apiMock.mockRejectedValueOnce(apiError(503, { title: 'الخدمة غير متاحة مؤقتًا' }));
    renderWithIntl(<EligibilityForm {...props} />, 'ar');
    await userEvent.click(screen.getByRole('radio', { name: 'نعم' }));
    await userEvent.selectOptions(screen.getByLabelText('أين تقيم حاليًا؟'), 'ruh');
    await userEvent.click(screen.getByRole('button', { name: 'متابعة' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('الخدمة غير متاحة مؤقتًا');
  });
});
