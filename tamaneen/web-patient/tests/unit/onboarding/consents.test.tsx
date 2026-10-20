import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { resetAll, router } from './helpers';

vi.mock('@/i18n/navigation', async () => (await import('./helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('./helpers')).clientMock());

import { api } from '@/lib/api/client';
import type { Consent } from '@/lib/api/types';
import { ConsentsForm } from '@/components/onboarding/ConsentsForm';

const apiMock = vi.mocked(api);
const c = (key: Consent['key'], required: boolean): Consent => ({ key, required, granted: false, version: '1.0', updatedAt: null, withdrawable: !required });
const consents = [c('platform', true), c('ai', true), c('share_with_therapist', true), c('research', false), c('emergency_location', false)];

describe('P03 Consents', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
  });

  it('marks required items and keeps continue disabled until all of them are on (Arabic)', async () => {
    renderWithIntl(<ConsentsForm consents={consents} />, 'ar');
    expect(screen.getAllByText('مطلوب')).toHaveLength(3);
    expect(screen.getAllByText('اختياري')).toHaveLength(2);
    const submit = screen.getByRole('button', { name: 'موافق، متابعة' });
    expect(submit).toBeDisabled();
    expect(screen.getByText('فعّل الموافقات المطلوبة لتتمكن من المتابعة.')).toBeInTheDocument();

    await userEvent.click(screen.getByRole('checkbox', { name: /استخدام المنصة/ }));
    await userEvent.click(screen.getByRole('checkbox', { name: /المساعدة بالذكاء الاصطناعي/ }));
    expect(submit).toBeDisabled();
    await userEvent.click(screen.getByRole('checkbox', { name: /المشاركة مع أخصائيك/ }));
    expect(submit).toBeEnabled();
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('expands the full text inline and saves the choices (English)', async () => {
    apiMock.mockResolvedValueOnce([]);
    renderWithIntl(<ConsentsForm consents={consents.map((x) => (x.required ? { ...x, granted: true } : x))} />, 'en');
    const toggles = screen.getAllByRole('button', { name: 'Read the full text' });
    await userEvent.click(toggles[1]!);
    expect(screen.getByText(/your words are never used to train it/)).toBeVisible();
    expect(screen.getByRole('button', { name: 'Hide the text' })).toHaveAttribute('aria-expanded', 'true');

    await userEvent.click(screen.getByRole('checkbox', { name: /Improving the service/ }));
    await userEvent.click(screen.getByRole('button', { name: 'Agree and continue' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/start/safety-info'));
    expect(apiMock).toHaveBeenCalledWith('me/consents', {
      method: 'PUT',
      body: {
        items: [
          { key: 'platform', granted: true },
          { key: 'ai', granted: true },
          { key: 'share_with_therapist', granted: true },
          { key: 'research', granted: true },
          { key: 'emergency_location', granted: false },
        ],
      },
    });
  });
});
