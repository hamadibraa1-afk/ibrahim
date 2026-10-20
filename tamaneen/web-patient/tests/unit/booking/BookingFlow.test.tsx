import { act, cleanup, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { ApiError } from '@/lib/api/errors';
import type { Hold, Slot } from '@/lib/api/types';
import { formatTime } from '@/lib/format';
import { renderWithIntl } from '../intl';
import { appointmentDetail, NOW, sara } from '../matching/fixtures';

const mocks = vi.hoisted(() => ({ api: vi.fn(), push: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => ({ push: mocks.push, replace: vi.fn(), refresh: vi.fn() }),
  Link: ({ href, children, ...rest }: { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { BookingFlow } from '@/components/booking/BookingFlow';

const slots: Slot[] = [
  { slotId: 's-10', startsAt: '2026-10-21T07:00:00Z' }, // Wed 21, 10:00 Riyadh
  { slotId: 's-16', startsAt: '2026-10-21T13:00:00Z' }, // Wed 21, 16:00
  { slotId: 's-22', startsAt: '2026-10-22T07:00:00Z' }, // Thu 22, 10:00
];
const time = (iso: string, locale: 'ar' | 'en' = 'ar') => formatTime(iso, locale);
const SLOTS_PATH = 'therapists/t-sara/slots?days=14';

function holdFor(slot: Slot, holdId: string, seconds = 300): Hold {
  return { holdId, slotId: slot.slotId, startsAt: slot.startsAt, expiresAt: new Date(Date.now() + seconds * 1000).toISOString(), therapist: sara };
}

type Handler = (body: unknown) => unknown;
/** Answers api(path, {method}) by "METHOD path"; unknown calls resolve to undefined (e.g. DELETE → 204). */
function route(handlers: Record<string, Handler>) {
  mocks.api.mockImplementation(async (path: string, options: { method?: string; body?: unknown } = {}) => {
    const handler = handlers[`${options.method ?? 'GET'} ${path}`];
    return handler ? handler(options.body) : undefined;
  });
}
const calls = () => mocks.api.mock.calls.map(([path, options]) => `${(options as { method?: string } | undefined)?.method ?? 'GET'} ${path}`);

describe('BookingFlow (P08)', () => {
  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(NOW);
  });
  afterEach(() => {
    // Unmount first: a test that ends with an open hold releases it, and that call belongs to this test.
    cleanup();
    vi.useRealTimers();
    mocks.api.mockReset();
    mocks.push.mockReset();
  });

  const setup = () => userEvent.setup({ advanceTimers: vi.advanceTimersByTime });

  it('lists days and times in the patient’s zone with the month label', async () => {
    route({});
    const user = setup();
    renderWithIntl(<BookingFlow therapistId="t-sara" initialSlots={slots} timeZone="Asia/Riyadh" />);
    const wednesday = screen.getByRole('button', { name: /الأربعاء/ });
    expect(wednesday).toHaveAttribute('aria-pressed', 'true');
    expect(wednesday).toHaveTextContent('21');
    expect(screen.getByRole('heading', { name: 'الأوقات المتاحة · أكتوبر 2026' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: time(slots[1]!.startsAt) })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'تأكيد الحجز' })).toBeDisabled();

    await user.click(screen.getByRole('button', { name: /الخميس/ }));
    expect(screen.queryByRole('button', { name: time(slots[1]!.startsAt) })).toBeNull();
    expect(screen.getByRole('button', { name: time(slots[2]!.startsAt) })).toBeInTheDocument();
  });

  it('holds a time with a live countdown, announces the last minute, and releases the UI on expiry', async () => {
    route({ 'POST bookings/holds': () => holdFor(slots[1]!, 'h1') });
    const user = setup();
    renderWithIntl(<BookingFlow therapistId="t-sara" initialSlots={slots} timeZone="Asia/Riyadh" />);

    await user.click(screen.getByRole('button', { name: time(slots[1]!.startsAt) }));
    expect(mocks.api).toHaveBeenCalledWith('bookings/holds', { method: 'POST', body: { slotId: 's-16' } });
    const countdown = await screen.findByTestId('hold-countdown');
    expect(countdown).toHaveAttribute('role', 'timer');
    expect(countdown.closest('p')).toHaveTextContent(`حجزنا لك الأربعاء، 21 أكتوبر، ${time(slots[1]!.startsAt)} لمدة 5:00 دقيقة. أكّد قبل انتهاء الوقت.`);
    const live = screen.getAllByRole('status').find((el) => el.getAttribute('aria-live') === 'polite')!;
    expect(live).toHaveTextContent('حجزنا لك');
    expect(screen.getByRole('button', { name: 'تأكيد الحجز' })).toBeEnabled();

    await act(async () => {
      vi.advanceTimersByTime(28_000);
    });
    expect(screen.getByTestId('hold-countdown')).toHaveTextContent('4:32');

    await act(async () => {
      vi.advanceTimersByTime(212_000); // 1:00 left
    });
    expect(live).toHaveTextContent('بقيت دقيقة واحدة لتأكيد الحجز.');

    await act(async () => {
      vi.advanceTimersByTime(60_000);
    });
    expect(screen.queryByTestId('hold-countdown')).toBeNull();
    expect(live).toHaveTextContent('انتهت مدة الحجز المؤقت');
    expect(screen.getByText(/انتهت مدة الحجز المؤقت وأُتيح الوقت لغيرك/, { selector: 'p:not([aria-live])' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'تأكيد الحجز' })).toBeDisabled();
    expect(screen.getByRole('button', { name: time(slots[1]!.startsAt) })).toHaveAttribute('aria-pressed', 'false');
  });

  it('releases the previous hold before holding a different time', async () => {
    let n = 0;
    route({ 'POST bookings/holds': (body) => holdFor(slots.find((s) => s.slotId === (body as { slotId: string }).slotId)!, `h${++n}`) });
    const user = setup();
    renderWithIntl(<BookingFlow therapistId="t-sara" initialSlots={slots} timeZone="Asia/Riyadh" />);

    await user.click(screen.getByRole('button', { name: time(slots[0]!.startsAt) }));
    await screen.findByTestId('hold-countdown');
    await user.click(screen.getByRole('button', { name: time(slots[1]!.startsAt) }));
    await waitFor(() => expect(screen.getByRole('button', { name: time(slots[1]!.startsAt) })).toHaveAttribute('aria-pressed', 'true'));

    expect(calls()).toEqual(['POST bookings/holds', 'DELETE bookings/holds/h1', 'POST bookings/holds']);
    expect(mocks.api.mock.calls[2]![1]).toEqual({ method: 'POST', body: { slotId: 's-16' } });
    expect(screen.getByRole('button', { name: time(slots[0]!.startsAt) })).toHaveAttribute('aria-pressed', 'false');
  });

  it('shows the localized conflict and reloads the times when the slot was just taken', async () => {
    route({
      'POST bookings/holds': () => {
        throw new ApiError(409, { title: 'هذا الوقت لم يعد متاحًا', detail: 'اختر وقتًا آخر.', status: 409, code: 'error.booking.slot_taken' });
      },
      [`GET ${SLOTS_PATH}`]: () => [slots[0], slots[2]],
    });
    const user = setup();
    renderWithIntl(<BookingFlow therapistId="t-sara" initialSlots={slots} timeZone="Asia/Riyadh" />);

    await user.click(screen.getByRole('button', { name: time(slots[1]!.startsAt) }));
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('هذا الوقت لم يعد متاحًا');
    expect(alert).toHaveTextContent('اختر وقتًا آخر.');
    expect(calls()).toEqual(['POST bookings/holds', `GET ${SLOTS_PATH}`]);
    await waitFor(() => expect(screen.queryByRole('button', { name: time(slots[1]!.startsAt) })).toBeNull());
    expect(screen.getByRole('button', { name: 'تأكيد الحجز' })).toBeDisabled();
  });

  it('confirms the hold and opens the confirmation page', async () => {
    route({
      'POST bookings/holds': () => holdFor(slots[1]!, 'h1'),
      'POST bookings/holds/h1/confirm': () => appointmentDetail({ id: 'a77' }),
    });
    const user = setup();
    renderWithIntl(<BookingFlow therapistId="t-sara" initialSlots={slots} timeZone="Asia/Riyadh" />, 'en');

    await user.click(screen.getByRole('button', { name: time(slots[1]!.startsAt, 'en') }));
    await screen.findByTestId('hold-countdown');
    await user.click(screen.getByRole('button', { name: 'Confirm booking' }));
    await waitFor(() => expect(mocks.push).toHaveBeenCalledWith('/appointments/a77/confirmed'));
    expect(calls()).toEqual(['POST bookings/holds', 'POST bookings/holds/h1/confirm']);
  });

  it('in reschedule mode moves the appointment to the held slot and releases the hold', async () => {
    route({
      'POST bookings/holds': () => holdFor(slots[2]!, 'h9'),
      'POST appointments/a1/reschedule': () => appointmentDetail({ id: 'a1', startsAt: slots[2]!.startsAt }),
    });
    const user = setup();
    renderWithIntl(<BookingFlow therapistId="t-sara" initialSlots={slots} timeZone="Asia/Riyadh" rescheduleId="a1" />);

    await user.click(screen.getByRole('button', { name: /الخميس/ }));
    await user.click(screen.getByRole('button', { name: time(slots[2]!.startsAt) }));
    await screen.findByTestId('hold-countdown');
    await user.click(screen.getByRole('button', { name: 'تأكيد الموعد الجديد' }));

    await waitFor(() => expect(mocks.push).toHaveBeenCalledWith('/appointments/a1/confirmed'));
    expect(calls()).toEqual(['POST bookings/holds', 'POST appointments/a1/reschedule', 'DELETE bookings/holds/h9']);
    expect(mocks.api).toHaveBeenCalledWith('appointments/a1/reschedule', { method: 'POST', body: { slotId: 's-22' } });
  });

  it('explains when booking is not allowed yet and links to the path choice', async () => {
    route({
      'POST bookings/holds': () => {
        throw new ApiError(403, { title: 'أكمل فحص السلامة أولًا', detail: 'يلزم إكمال خطوات البدء قبل الحجز.', status: 403, code: 'error.booking.not_eligible' });
      },
    });
    const user = setup();
    renderWithIntl(<BookingFlow therapistId="t-sara" initialSlots={slots} timeZone="Asia/Riyadh" />);

    await user.click(screen.getByRole('button', { name: time(slots[0]!.startsAt) }));
    expect(await screen.findByText('أكمل فحص السلامة أولًا')).toBeInTheDocument();
    expect(screen.getByText('يلزم إكمال خطوات البدء قبل الحجز.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'إكمال الخطوات قبل الحجز' })).toHaveAttribute('href', '/start/path');
    expect(screen.queryByRole('button', { name: 'تأكيد الحجز' })).toBeNull();
  });

  it('releases an open hold when the patient leaves the page', async () => {
    route({ 'POST bookings/holds': () => holdFor(slots[0]!, 'h5') });
    const user = setup();
    const { unmount } = renderWithIntl(<BookingFlow therapistId="t-sara" initialSlots={slots} timeZone="Asia/Riyadh" />);
    await user.click(screen.getByRole('button', { name: time(slots[0]!.startsAt) }));
    await screen.findByTestId('hold-countdown');
    unmount();
    expect(calls()).toContain('DELETE bookings/holds/h5');
  });
});
