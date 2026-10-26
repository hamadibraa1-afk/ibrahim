import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import type { CrisisFollowUp, Inbox } from '@/lib/api/types';
import { renderWithIntl } from '../intl';

const mocks = vi.hoisted(() => ({ api: vi.fn(), push: vi.fn(), session: { value: null as null | { emergencyNumber: string | null; supportLine: string | null } } }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api, authApi: vi.fn() }));
vi.mock('@/i18n/navigation', () => ({
  Link: ({ href, children, ...rest }: AnchorHTMLAttributes<HTMLAnchorElement> & { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
  useRouter: () => ({ replace: vi.fn(), push: mocks.push, refresh: vi.fn() }),
  usePathname: () => '/home',
}));
vi.mock('@/components/crisis/helpNowSession', () => ({ useHelpNowSession: () => mocks.session.value }));

import { AfterCrisisView } from '@/components/afterCrisis/AfterCrisisView';
import { NotificationBell } from '@/components/notifications/NotificationBell';
import { NotificationList } from '@/components/notifications/NotificationList';

const now = new Date().toISOString();
const inbox: Inbox = {
  items: [
    { id: 'n1', kind: 'appointment.reminder_24h', category: 'appointments', text: 'جلستك غدًا 4:00 م', link: '/appointments/a1', createdAt: now, read: false, pinned: false, data: { appointmentId: 'a1' } },
    { id: 'n2', kind: 'security.new_sign_in', category: 'security', text: 'دخول جديد من Chrome · Windows', link: '/settings/security', createdAt: now, read: true, pinned: false, data: { sessionId: 's9' } },
  ],
  unreadCount: 1,
  nextCursor: null,
  counts: { all: 1 },
};

describe('NotificationList (P46)', () => {
  beforeEach(() => {
    mocks.api.mockReset();
    mocks.push.mockReset();
  });

  it('shows text, relative time and the unread dot, and opening marks read then follows the link', async () => {
    mocks.api.mockResolvedValue(undefined);
    renderWithIntl(<NotificationList initial={inbox} />, 'ar');
    expect(screen.getAllByTestId('notification')).toHaveLength(2);
    expect(screen.getAllByTestId('unread-dot')).toHaveLength(1);
    await userEvent.click(screen.getByRole('button', { name: /جلستك غدًا/ }));
    expect(mocks.api).toHaveBeenCalledWith('me/inbox/n1/read', { method: 'POST' });
    expect(mocks.push).toHaveBeenCalledWith('/appointments/a1');
    expect(screen.queryAllByTestId('unread-dot')).toHaveLength(0);
  });

  it('ends a strange session after a confirmation and suggests a new password', async () => {
    mocks.api.mockResolvedValue(undefined);
    renderWithIntl(<NotificationList initial={inbox} />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Not you? End the session' }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('End the session on the other device?');
    await userEvent.click(screen.getByRole('button', { name: 'End session' }));
    expect(mocks.api).toHaveBeenCalledWith('me/sessions/s9', { method: 'DELETE' });
    expect(await screen.findByText(/We ended that session/)).toBeInTheDocument();
  });

  it('has an empty state in both languages', () => {
    const { unmount } = renderWithIntl(<NotificationList initial={{ ...inbox, items: [] }} />, 'ar');
    expect(screen.getByText('لا توجد إشعارات')).toBeInTheDocument();
    unmount();
    renderWithIntl(<NotificationList initial={{ ...inbox, items: [] }} />, 'en');
    expect(screen.getByText('No notifications')).toBeInTheDocument();
  });
});

describe('NotificationBell', () => {
  beforeEach(() => mocks.api.mockReset());

  it('is hidden without a session', () => {
    mocks.session.value = null;
    renderWithIntl(<NotificationBell />, 'ar');
    expect(screen.queryByTestId('notification-bell')).toBeNull();
  });

  it('shows the unread count for a signed-in patient', async () => {
    mocks.session.value = { emergencyNumber: '911', supportLine: null };
    mocks.api.mockResolvedValue({ unreadCount: 3 });
    renderWithIntl(<NotificationBell />, 'en');
    await waitFor(() => expect(screen.getByTestId('notification-bell')).toHaveAccessibleName('Notifications, 3 unread'));
    expect(screen.getByTestId('notification-bell')).toHaveAttribute('href', '/notifications');
    expect(mocks.api).toHaveBeenCalledWith('me/inbox/unread-count', { navigate: false });
  });
});

describe('AfterCrisisView (P38)', () => {
  const followUp: CrisisFollowUp = {
    state: 'closed', eventId: 'e1', closedAt: now, responder: 'د. فهد ر.', talked: true, therapistInformed: true, therapistName: 'أ. سارة', nextAppointmentId: 'a1', nextAppointmentAt: '2026-10-29T13:00:00Z', unread: true,
  };

  beforeEach(() => {
    mocks.api.mockReset();
    mocks.push.mockReset();
  });

  it('says who the patient spoke with, what follows, and marks it seen on the way home', async () => {
    mocks.api.mockResolvedValue(undefined);
    renderWithIntl(<AfterCrisisView followUp={followUp} timeZone="Asia/Riyadh" resources={<p>numbers</p>} />, 'ar');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('تحدثت اليوم مع د. فهد ر.');
    expect(screen.getByText('أ. سارة تعرف ما حدث، وستتابع معك.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'فتح' })).toHaveAttribute('href', '/safety-plan');
    expect(screen.getByRole('link', { name: 'سجّل الآن' })).toHaveAttribute('href', '/check-in');
    expect(screen.getByRole('button', { name: 'أحتاج التحدث مع أحد الآن' })).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'العودة للرئيسية' }));
    expect(mocks.api).toHaveBeenCalledWith('crisis/follow-up/seen', { method: 'POST' });
    expect(mocks.push).toHaveBeenCalledWith('/home');
  });

  it('shows only reassurance and the plan when nothing was agreed (English)', () => {
    renderWithIntl(<AfterCrisisView followUp={{ ...followUp, talked: false, therapistInformed: false, nextAppointmentAt: null }} timeZone="Asia/Riyadh" resources={null} />, 'en');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('Our team is still with you');
    expect(screen.queryByText('What you agreed')).toBeNull();
    expect(screen.getByText('Your safety plan is ready')).toBeInTheDocument();
  });
});
