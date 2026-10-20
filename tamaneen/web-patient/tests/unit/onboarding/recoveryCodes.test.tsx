import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithIntl } from '../intl';
import { resetAll, router } from './helpers';

vi.mock('@/i18n/navigation', async () => (await import('./helpers')).navigationMock());
vi.mock('@/lib/api/client', async () => (await import('./helpers')).clientMock());

import { api } from '@/lib/api/client';
import { FLOW_KEYS } from '@/components/onboarding/flowStorage';
import { RECOVERY_FILE_NAME, RecoveryCodesPanel } from '@/components/onboarding/RecoveryCodesPanel';
import { groupSecret } from '@/components/onboarding/TotpSetup';

const apiMock = vi.mocked(api);
const codes = ['7KQ2-M9XA', 'P4TZ-81LD', 'C6WN-3RHE', 'Q8VB-52JS', 'H1MD-7TPK', 'Z3YA-64NC', 'L9EF-20UW', 'R5GX-38QB', 'B2KJ-95VM', 'T7NP-16DX'];

describe('P17 Recovery codes', () => {
  beforeEach(() => {
    resetAll();
    apiMock.mockReset();
    window.sessionStorage.setItem(FLOW_KEYS.recoveryCodes, JSON.stringify(codes));
  });

  it('shows the ten codes left-to-right and gates continue on the checkbox (Arabic)', async () => {
    renderWithIntl(<RecoveryCodesPanel />, 'ar');
    const list = await screen.findByRole('list', { name: 'رموز الاسترجاع العشرة' });
    expect(list).toHaveAttribute('dir', 'ltr');
    expect(list.querySelectorAll('li')).toHaveLength(10);
    expect(screen.getByRole('heading', { level: 1, name: 'احفظ رموز الاسترجاع' })).toBeInTheDocument();

    const next = screen.getByRole('button', { name: 'متابعة' });
    expect(next).toBeDisabled();
    await userEvent.click(screen.getByLabelText('حفظت الرموز في مكان آمن'));
    expect(next).toBeEnabled();
    await userEvent.click(next);
    expect(router.replace).toHaveBeenCalledWith('/start/consents');
    expect(window.sessionStorage.getItem(FLOW_KEYS.recoveryCodes)).toBeNull();
  });

  it('downloads a localized .txt file (English)', async () => {
    const blobs: Blob[] = [];
    Object.assign(URL, {
      createObjectURL: vi.fn((b: Blob) => {
        blobs.push(b);
        return 'blob:codes';
      }),
      revokeObjectURL: vi.fn(),
    });
    let downloadName = '';
    const click = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      downloadName = this.download;
    });
    renderWithIntl(<RecoveryCodesPanel />, 'en');
    await userEvent.click(await screen.findByRole('button', { name: 'Download as a file' }));
    expect(click).toHaveBeenCalled();
    expect(downloadName).toBe(RECOVERY_FILE_NAME);
    const text = await new Promise<string>((resolve) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result));
      reader.readAsText(blobs[0]!);
    });
    expect(text).toContain('Tamaneen · Recovery codes');
    expect(text).toContain('Each code works once.');
    for (const c of codes) expect(text).toContain(c);
    expect(screen.getByText('File downloaded.')).toBeInTheDocument();
    click.mockRestore();
  });

  it('copies the codes and confirms, or explains when copying fails', async () => {
    const writeText = vi.fn().mockResolvedValueOnce(undefined).mockRejectedValueOnce(new Error('denied'));
    renderWithIntl(<RecoveryCodesPanel />, 'en');
    // user-event installs its own clipboard stub on setup; replace it afterwards.
    const user = userEvent.setup();
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    await user.click(await screen.findByRole('button', { name: 'Copy' }));
    expect(writeText).toHaveBeenCalledWith(codes.join('\n'));
    expect(await screen.findByText('Codes copied.')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Copy' }));
    expect(await screen.findByText("We couldn't copy them. Select the codes and copy them yourself.")).toBeInTheDocument();
  });

  it('sets up an authenticator app with the key grouped in fours', async () => {
    apiMock.mockResolvedValueOnce({ secret: 'JBSWY3DPEHPK3PXP', otpauthUri: 'otpauth://totp/Tamaneen?secret=JBSWY3DPEHPK3PXP' }).mockResolvedValueOnce(undefined);
    renderWithIntl(<RecoveryCodesPanel />, 'en');
    await userEvent.click(await screen.findByRole('button', { name: 'Set up' }));
    expect(await screen.findByTestId('totp-secret')).toHaveTextContent('JBSW Y3DP EHPK 3PXP');
    expect(screen.getByRole('link', { name: 'Open in authenticator app' })).toHaveAttribute('href', 'otpauth://totp/Tamaneen?secret=JBSWY3DPEHPK3PXP');
    await userEvent.type(screen.getByLabelText('Code from the app'), '123456');
    await userEvent.click(screen.getByRole('button', { name: 'Confirm' }));
    expect(await screen.findByText(/Authenticator app turned on/)).toBeInTheDocument();
    expect(apiMock).toHaveBeenLastCalledWith('me/totp/confirm', { method: 'POST', body: { code: '123456' } });
    expect(groupSecret('ABCDEFGHIJ')).toBe('ABCD EFGH IJ');
  });

  it('offers new codes when the stored ones are gone', async () => {
    window.sessionStorage.clear();
    apiMock.mockResolvedValueOnce({ recoveryCodes: codes });
    renderWithIntl(<RecoveryCodesPanel />, 'ar');
    await userEvent.click(await screen.findByRole('button', { name: 'إنشاء رموز جديدة' }));
    await waitFor(() => expect(screen.getByTestId('recovery-codes').querySelectorAll('li')).toHaveLength(10));
    expect(apiMock).toHaveBeenCalledWith('me/recovery-codes', { method: 'POST' });
  });
});
