import { act, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { ApiError } from '@/lib/api/errors';
import { renderWithIntl } from '../intl';

const mocks = vi.hoisted(() => ({ api: vi.fn(), replace: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api, authApi: vi.fn() }));
vi.mock('@/i18n/navigation', () => ({
  Link: ({ href, children, ...rest }: AnchorHTMLAttributes<HTMLAnchorElement> & { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
  useRouter: () => ({ replace: mocks.replace, push: vi.fn(), refresh: vi.fn() }),
  usePathname: () => '/home',
}));

import { OutboxStatus } from '@/components/pwa/OutboxStatus';
import { OutboxView } from '@/components/pwa/OutboxView';
import { outboxItems, resetOutbox, setOutboxOwner, submitWrite } from '@/components/pwa/outbox';

let online = true;
function goOnline(value: boolean) {
  online = value;
  window.dispatchEvent(new Event(value ? 'online' : 'offline'));
}

describe('OutboxStatus and the P66 page', () => {
  beforeEach(() => {
    mocks.api.mockReset();
    mocks.replace.mockReset();
    resetOutbox();
    online = true;
    Object.defineProperty(window.navigator, 'onLine', { configurable: true, get: () => online });
  });
  afterEach(() => {
    resetOutbox();
    window.localStorage.clear();
    online = true;
  });

  it('says the connection is weak and what is waiting, then sends it when the connection returns (Arabic)', async () => {
    renderWithIntl(<OutboxStatus owner="P-TEST1" />, 'ar');
    act(() => goOnline(false));
    expect(await screen.findByTestId('outbox-banner')).toHaveTextContent('الاتصال ضعيف · نعيد المحاولة تلقائيًا');
    await act(() => submitWrite({ kind: 'mood', method: 'POST', path: 'mood', body: { score: 4 } }));
    expect(screen.getByTestId('outbox-banner')).toHaveTextContent('عنصر واحد بانتظار الإرسال · محفوظة على جوالك');
    expect(screen.getByRole('link', { name: 'عرض' })).toHaveAttribute('href', '/outbox');

    mocks.api.mockResolvedValue({ crisis: false, entry: { id: 'e' } });
    act(() => goOnline(true));
    await waitFor(() => expect(mocks.api).toHaveBeenCalledWith('mood', expect.objectContaining({ idempotencyKey: expect.any(String), navigate: false })));
    await waitFor(() => expect(screen.queryByTestId('outbox-banner')).toBeNull());
  });

  it('opens the crisis screen the moment a queued note is answered as a crisis', async () => {
    renderWithIntl(<OutboxStatus owner="P-TEST1" />, 'en');
    act(() => goOnline(false));
    await act(() => submitWrite({ kind: 'mood', method: 'POST', path: 'mood', body: { score: 1, note: 'x' } }));
    mocks.api.mockResolvedValue({ crisis: true, level: 4, entry: { id: 'e' } });
    act(() => goOnline(true));
    await waitFor(() => expect(mocks.replace).toHaveBeenCalledWith('/crisis'));
  });

  it('only shows and sends the signed-in patient’s items', async () => {
    setOutboxOwner('P-OTHER');
    online = false;
    await submitWrite({ kind: 'mood', method: 'POST', path: 'mood', body: { score: 2 } });
    renderWithIntl(<OutboxStatus owner="P-TEST1" />, 'en');
    act(() => goOnline(true));
    await act(async () => undefined);
    expect(mocks.api).not.toHaveBeenCalled();
    expect(screen.queryByTestId('outbox-banner')).toBeNull();
  });

  it('lists what will be sent, shows a refusal with its reason, and discards only after confirming (English)', async () => {
    setOutboxOwner('P-TEST1');
    online = false;
    await submitWrite({ kind: 'homework-checkin', method: 'POST', path: 'homework/h1/checkins', body: { status: 'done', feeling: 7, note: 'Slept sooner.' }, label: 'Breathing before bed' });
    renderWithIntl(<OutboxView />, 'en');
    const item = screen.getByTestId('outbox-item');
    expect(item).toHaveTextContent('Exercise check-in');
    expect(item).toHaveTextContent('Breathing before bed · Done');
    expect(item).toHaveTextContent('Slept sooner.');
    expect(item).toHaveTextContent('◷ Waiting to send · saved on your phone');

    online = true;
    mocks.api.mockRejectedValue(new ApiError(409, { title: 't', status: 409, code: 'error.homework.ended', detail: 'This exercise has ended.' }));
    await userEvent.click(screen.getByRole('button', { name: 'Try again now' }));
    const alert = await within(screen.getByTestId('outbox-item')).findByRole('alert');
    expect(alert).toHaveTextContent('Not sent · This exercise has ended.');

    await userEvent.click(within(alert).getByRole('button', { name: 'Discard' }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveTextContent('It will be deleted from your phone and not sent.');
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(outboxItems()).toHaveLength(1);
    await userEvent.click(within(screen.getByTestId('outbox-item')).getByRole('button', { name: 'Discard' }));
    await userEvent.click(screen.getByRole('button', { name: 'Yes, discard it' }));
    expect(await screen.findByTestId('outbox-empty')).toHaveTextContent('Nothing is waiting to send.');
    expect(outboxItems()).toEqual([]);
  });

  it('describes a waiting mood check-in in Arabic', async () => {
    setOutboxOwner('P-TEST1');
    online = false;
    await submitWrite({ kind: 'mood', method: 'POST', path: 'mood', body: { score: 6, tags: [] } });
    renderWithIntl(<OutboxView />, 'ar');
    const item = screen.getByTestId('outbox-item');
    expect(item).toHaveTextContent('تسجيل المزاج');
    expect(item).toHaveTextContent('المزاج 6 من 10');
    expect(item).toHaveTextContent('◷ بانتظار الإرسال · محفوظة على جوالك');
  });
});
