import { act, cleanup, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createTranslator } from 'next-intl';
import type { ReactNode } from 'react';
import { newBookingHref, sessionLabel } from '@/components/appointments/labels';
import type { Translator } from '@/components/matching/labels';
import { ApiError } from '@/lib/api/errors';
import { formatRelativeDays } from '@/lib/format';
import { messagesFor, renderWithIntl } from '../intl';
import { appointment, appointmentDetail, khaled, NOW } from '../matching/fixtures';

const mocks = vi.hoisted(() => ({ api: vi.fn(), push: vi.fn(), refresh: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => ({ push: mocks.push, replace: vi.fn(), refresh: mocks.refresh }),
  Link: ({ href, children, ...rest }: { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { AppointmentActions } from '@/components/appointments/AppointmentActions';
import { AppointmentListView } from '@/components/appointments/AppointmentListView';
import { JoinSessionCard } from '@/components/appointments/JoinSessionCard';

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  mocks.api.mockReset();
  mocks.refresh.mockReset();
});

describe('session ordinals', () => {
  it('writes sessions one to ten as words, then as numbers, in both languages', () => {
    const ar = createTranslator({ locale: 'ar', messages: messagesFor('ar'), namespace: 'appointments' }) as unknown as Translator;
    const en = createTranslator({ locale: 'en', messages: messagesFor('en'), namespace: 'appointments' }) as unknown as Translator;
    expect([1, 2, 3, 10, 11].map((n) => sessionLabel(ar, n, 'ar'))).toEqual(['الجلسة الأولى', 'الجلسة الثانية', 'الجلسة الثالثة', 'الجلسة العاشرة', 'الجلسة 11']);
    expect([1, 2, 3, 10, 11].map((n) => sessionLabel(en, n, 'en'))).toEqual(['First session', 'Second session', 'Third session', 'Tenth session', 'Session 11']);
  });
});

describe('newBookingHref (P27 «حجز موعد جديد»)', () => {
  it('books with the current therapist, or opens the matches when there is none', () => {
    expect(newBookingHref({ upcoming: [appointment()], past: [] })).toBe('/therapists/t-sara/book');
    expect(newBookingHref({ upcoming: [], past: [appointment({ therapist: khaled, status: 'completed' })] })).toBe('/therapists/t-khaled/book');
    expect(newBookingHref({ upcoming: [], past: [] })).toBe('/matches');
  });
});

describe('AppointmentListView (P27)', () => {
  const list = {
    upcoming: [appointment({ id: 'a2', startsAt: '2026-10-28T13:00:00Z', sequenceNumber: 2 }), appointment({ id: 'a1' })],
    past: [appointment({ id: 'a0', startsAt: '2026-10-12T13:00:00Z', status: 'cancelled_by_therapist', rescheduled: true })],
  };

  it('shows upcoming and past lists with date badges, ordinals and statuses in Arabic', () => {
    renderWithIntl(<AppointmentListView list={list} timeZone="Asia/Riyadh" />);
    const upcoming = within(screen.getByRole('region', { name: 'القادمة' })).getAllByRole('link');
    expect(upcoming.map((a) => a.getAttribute('href'))).toEqual(['/appointments/a1', '/appointments/a2']);
    expect(upcoming[0]).toHaveTextContent('أكتوبر21');
    expect(upcoming[0]).toHaveTextContent('أ. سارة م. · الجلسة الأولى');
    expect(upcoming[0]).toHaveTextContent('مؤكد');
    expect(upcoming[0]).toHaveClass('border-primary');
    expect(upcoming[1]).toHaveTextContent('أ. سارة م. · الجلسة الثانية');
    const past = within(screen.getByRole('region', { name: 'السابقة' })).getByRole('link');
    expect(past).toHaveTextContent('تم إلغاؤه');
    expect(past).toHaveTextContent('ألغته الأخصائية · أُعيدت الجدولة');
  });

  it('uses English ordinals and labels', () => {
    renderWithIntl(<AppointmentListView list={list} timeZone="Asia/Riyadh" />, 'en');
    expect(screen.getByRole('link', { name: /Wednesday 21 October/ })).toHaveTextContent('أ. سارة م. · First session');
    expect(screen.getByRole('link', { name: /Wednesday 28 October/ })).toHaveTextContent('Second session');
    expect(screen.getByRole('region', { name: 'Past' })).toHaveTextContent('Cancelled by your therapist · Rescheduled');
  });
});

describe('JoinSessionCard (P28)', () => {
  it('stays disabled with a relative label until ten minutes before, with the lobby open for getting ready', async () => {
    vi.useFakeTimers();
    vi.setSystemTime(NOW);
    const opensAt = '2026-10-21T12:50:00Z';
    renderWithIntl(<JoinSessionCard appointmentId="a1" joinAvailableAt={opensAt} timeZone="Asia/Riyadh" />);
    const button = screen.getByRole('button', { name: `دخول الجلسة · ${formatRelativeDays(opensAt, 'ar', NOW)}` });
    expect(button).toBeDisabled();
    expect(screen.getByRole('link', { name: 'تجهيز الجلسة الآن' })).toHaveAttribute('href', '/appointments/a1/lobby');

    await act(async () => {
      vi.advanceTimersByTime(Date.parse(opensAt) - NOW.getTime() + 1000);
    });
    expect(screen.getByRole('link', { name: 'دخول غرفة الانتظار' })).toHaveAttribute('href', '/appointments/a1/lobby');
  });

  it('opens the lobby in the same tab once the window is open', () => {
    renderWithIntl(<JoinSessionCard appointmentId="a1" joinAvailableAt={new Date(Date.now() - 60_000).toISOString()} timeZone="Asia/Riyadh" />, 'en');
    const link = screen.getByRole('link', { name: 'Go to the waiting room' });
    expect(link).toHaveAttribute('href', '/appointments/a1/lobby');
    expect(link).not.toHaveAttribute('target');
  });
});

describe('AppointmentActions (P28)', () => {
  it('links "change" to booking in reschedule mode and shows the deadline', () => {
    renderWithIntl(<AppointmentActions appointment={appointmentDetail()} timeZone="Asia/Riyadh" />);
    const change = screen.getByRole('link', { name: 'تغيير الموعد' });
    expect(change).toHaveAttribute('href', '/therapists/t-sara/book?reschedule=a1');
    expect(change).toHaveAccessibleDescription(/التغيير متاح حتى 24 ساعة قبل الموعد · حتى الثلاثاء، 20 أكتوبر/);
  });

  it('asks for confirmation in a modal that starts on "keep", closes on Escape and returns focus', async () => {
    const user = userEvent.setup();
    renderWithIntl(<AppointmentActions appointment={appointmentDetail()} timeZone="Asia/Riyadh" />);
    const cancel = screen.getByRole('button', { name: 'إلغاء الموعد' });
    await user.click(cancel);

    const dialog = screen.getByRole('dialog', { name: 'إلغاء الموعد؟' });
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toHaveAccessibleDescription(/سنلغي موعدك الأربعاء، 21 أكتوبر · .* مع أ. سارة م./);
    expect(within(dialog).getByRole('button', { name: 'تراجع' })).toHaveFocus();
    expect(within(dialog).getByRole('radiogroup', { name: 'سبب الإلغاء' })).toBeInTheDocument();
    expect(within(dialog).getAllByRole('radio')).toHaveLength(5);
    await user.tab();
    expect(within(dialog).getByRole('radio', { name: 'تعارض مع موعد آخر' })).toHaveFocus();

    await user.keyboard('{Escape}');
    expect(screen.queryByRole('dialog')).toBeNull();
    expect(cancel).toHaveFocus();
    expect(mocks.api).not.toHaveBeenCalled();
  });

  it('cancels after confirmation and refreshes the page', async () => {
    mocks.api.mockResolvedValue(appointmentDetail({ status: 'cancelled_by_patient', canChange: false }));
    const user = userEvent.setup();
    renderWithIntl(<AppointmentActions appointment={appointmentDetail()} timeZone="Asia/Riyadh" />, 'en');
    await user.click(screen.getByRole('button', { name: 'Cancel appointment' }));
    await user.click(screen.getByRole('button', { name: 'Yes, cancel it' }));
    expect(screen.getByRole('alert')).toHaveTextContent('Choose a reason for cancelling.');
    expect(mocks.api).not.toHaveBeenCalled();

    const feelingBetter = screen.getByRole('radio', { name: "I'm feeling better" });
    await user.click(feelingBetter);
    expect(feelingBetter).toHaveAttribute('aria-checked', 'true');
    expect(screen.queryByRole('alert')).toBeNull();
    await user.click(screen.getByRole('button', { name: 'Yes, cancel it' }));

    expect(mocks.api).toHaveBeenCalledWith('appointments/a1/cancel', { method: 'POST', body: { reasonCode: 'feeling_better' } });
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull());
    expect(screen.getByRole('status')).toHaveTextContent('Your appointment is cancelled.');
    expect(screen.getByRole('link', { name: 'Book a new appointment' })).toHaveAttribute('href', '/therapists/t-sara/book');
    expect(mocks.refresh).toHaveBeenCalled();
  });

  it('keeps the dialog open with the problem when cancelling is refused', async () => {
    mocks.api.mockImplementation(async () => {
      throw new ApiError(409, { title: 'لا يمكن الإلغاء قبل أقل من 24 ساعة', status: 409 });
    });
    const user = userEvent.setup();
    renderWithIntl(<AppointmentActions appointment={appointmentDetail()} timeZone="Asia/Riyadh" />);
    await user.click(screen.getByRole('button', { name: 'إلغاء الموعد' }));
    await user.click(screen.getByRole('radio', { name: 'تعارض مع موعد آخر' }));
    await user.click(screen.getByRole('button', { name: 'نعم، ألغِ الموعد' }));
    expect(await within(screen.getByRole('dialog')).findByRole('alert')).toHaveTextContent('لا يمكن الإلغاء قبل أقل من 24 ساعة');
  });

  it('disables change and cancel once canChange is false, and says why', () => {
    renderWithIntl(<AppointmentActions appointment={appointmentDetail({ canChange: false })} timeZone="Asia/Riyadh" />);
    const change = screen.getByRole('button', { name: 'تغيير الموعد' });
    const cancel = screen.getByRole('button', { name: 'إلغاء الموعد' });
    expect(change).toBeDisabled();
    expect(cancel).toBeDisabled();
    expect(screen.queryByRole('link', { name: 'تغيير الموعد' })).toBeNull();
    expect(cancel).toHaveAccessibleDescription(/انتهت مهلة التغيير/);
  });
});
