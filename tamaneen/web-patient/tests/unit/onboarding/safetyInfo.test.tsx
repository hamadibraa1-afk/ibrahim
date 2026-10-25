import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { apiError, ifMatchSent, resetAll, router } from './helpers';

vi.mock('@/i18n/navigation', async () => (await import('./helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('./helpers')).clientMock());

import { api } from '@/lib/api/client';
import { SafetyInfoForm } from '@/components/onboarding/SafetyInfoForm';

const apiMock = vi.mocked(api);

describe('P21 Safety info', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
  });

  it('skips by saving empty values with location off (Arabic)', async () => {
    apiMock.mockResolvedValueOnce({});
    renderWithIntl(<SafetyInfoForm initial={null} version='"v1"' />, 'ar');
    expect(screen.getByLabelText('رقم جوالك للاتصال')).toHaveAttribute('dir', 'ltr');
    await userEvent.click(screen.getByRole('button', { name: 'تخطَّ الآن' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/start/path'));
    expect(apiMock).toHaveBeenCalledWith('me/safety-info', { method: 'PUT', body: { contactPhone: null, trustedName: null, trustedPhone: null, shareLocation: false } });
  });

  it('validates and saves the details (English)', async () => {
    apiMock.mockResolvedValueOnce({});
    renderWithIntl(<SafetyInfoForm initial={null} version='"v1"' />, 'en');
    await userEvent.type(screen.getByLabelText('Their mobile number'), '0551112222');
    await userEvent.click(screen.getByRole('button', { name: 'Save and continue' }));
    expect(screen.getByText('Add the name of the person you trust, or remove their number.')).toBeInTheDocument();
    expect(apiMock).not.toHaveBeenCalled();

    await userEvent.type(screen.getByLabelText('Name of someone you trust'), 'My sister');
    await userEvent.type(screen.getByLabelText('Your mobile number for calls'), '٠٥٥ ١٢٣ ٤٥٢١');
    await userEvent.click(screen.getByRole('switch', { name: 'Allow my location to be known in emergencies only' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save and continue' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/start/path'));
    expect(apiMock).toHaveBeenCalledWith('me/safety-info', {
      method: 'PUT',
      body: { contactPhone: '0551234521', trustedName: 'My sister', trustedPhone: '0551112222', shareLocation: true },
    });
  });

  it('skips without erasing details saved earlier', async () => {
    renderWithIntl(<SafetyInfoForm initial={{ contactPhone: '0551234521', trustedName: null, trustedPhone: null, shareLocation: false, shareLocationSince: null }} version='"v1"' />, 'en');
    expect(screen.getByText(/Why this helps/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Skip for now' }));
    expect(router.push).toHaveBeenCalledWith('/start/path');
    expect(apiMock).not.toHaveBeenCalled();
  });

  it('saves with the version it read, and on 412 keeps what was typed and saves with the fresh version', async () => {
    apiMock
      .mockRejectedValueOnce(apiError(412, { code: 'error.settings.version_changed' }))
      .mockResolvedValueOnce({ contactPhone: '0559999999', trustedName: null, trustedPhone: null, shareLocation: false, shareLocationSince: null })
      .mockResolvedValueOnce({});
    renderWithIntl(<SafetyInfoForm initial={null} version='"v1"' />, 'ar');
    const phone = screen.getByLabelText('رقم جوالك للاتصال');
    await userEvent.type(phone, '0551234521');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ والمتابعة' }));
    expect(ifMatchSent).toHaveBeenNthCalledWith(1, 'me/safety-info', '"v1"');
    expect(await screen.findByText(/أبقينا ما كتبته هنا/)).toBeInTheDocument();
    expect(phone).toHaveValue('0551234521');
    expect(apiMock).toHaveBeenNthCalledWith(2, 'me/safety-info');
    expect(router.push).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'حفظ والمتابعة' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/start/path'));
    expect(ifMatchSent).toHaveBeenLastCalledWith('me/safety-info', '"v2"');
  });

  it('asks for the number when only a trusted name is given', async () => {
    renderWithIntl(<SafetyInfoForm initial={null} version='"v1"' />, 'ar');
    await userEvent.type(screen.getByLabelText('اسم شخص تثق به'), 'أختي');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ والمتابعة' }));
    expect(screen.getByText('أضف رقم جوال الشخص الذي تثق به، أو احذف اسمه.')).toBeInTheDocument();
    expect(apiMock).not.toHaveBeenCalled();
  });
});
