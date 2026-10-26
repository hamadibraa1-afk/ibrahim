import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import type { HomeSummary } from '@/lib/api/types';
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

import { HomeView } from '@/components/home/HomeView';
import { MoodFaces } from '@/components/home/MoodFaces';

const home: HomeSummary = {
  nickname: 'نخلة هادئة',
  nextAppointment: {
    id: 'a1',
    therapist: { id: 't1', displayName: 'أ. سارة', initials: 'س م', title: '', specialties: [], languages: ['ar'], gender: 'female', licenseVerified: true, nextAvailableAt: null, hasEvening: true },
    startsAt: '2026-10-21T13:00:00Z',
    durationMinutes: 50,
    sequenceNumber: 2,
    status: 'confirmed',
    rescheduled: false,
    therapistLateMinutes: null,
    cancelledBy: null,
    cancelReasonCode: null,
    missed: false,
  },
  todayHomework: { id: 'h1', title: 'تمرين التنفس', minutes: 5, doneDays: 3, totalDays: 7 },
  latestFeedback: { therapistName: 'أ. سارة', initials: 'س م', text: 'أحسنت في الاستمرار هذا الأسبوع', at: '2026-10-18T10:00:00Z' },
  moodToday: null,
  hasSafetyPlan: true,
  triage: { status: 'matched', submittedAt: null, expectedBy: null, suggestionsReady: false },
  appointmentNotice: null,
};

// 20:00 in Riyadh, two days before the session.
const evening = new Date('2026-10-19T17:00:00Z');

describe('MoodFaces', () => {
  beforeEach(() => {
    mocks.api.mockReset();
    mocks.replace.mockReset();
  });

  it('labels each face and posts the mapped score', async () => {
    mocks.api.mockResolvedValue({ crisis: false, level: 0, entry: { id: 'm1', score: 7, tags: [], at: '2026-10-19T17:00:00Z' } });
    renderWithIntl(<MoodFaces initialScore={null} />, 'ar');
    const labels = ['سيئ جدًا', 'سيئ', 'عادي', 'جيد', 'ممتاز'];
    for (const name of labels) expect(screen.getByRole('button', { name })).toHaveAttribute('aria-pressed', 'false');

    await userEvent.click(screen.getByRole('button', { name: 'جيد' }));
    expect(mocks.api).toHaveBeenCalledWith('mood', { method: 'POST', body: { score: 7, tags: [] } });
    await waitFor(() => expect(screen.getByRole('button', { name: 'جيد' })).toHaveAttribute('aria-pressed', 'true'));
    expect(screen.getByText('حفظنا شعورك: جيد.')).toBeInTheDocument();
  });

  it.each([
    ['Very bad', 0],
    ['Bad', 3],
    ['Okay', 5],
    ['Excellent', 10],
  ])('maps "%s" to %i', async (name, score) => {
    mocks.api.mockResolvedValue({ crisis: false, level: 0, entry: { id: 'm', score, tags: [], at: '' } });
    renderWithIntl(<MoodFaces initialScore={null} />, 'en');
    await userEvent.click(screen.getByRole('button', { name }));
    expect(mocks.api).toHaveBeenCalledWith('mood', { method: 'POST', body: { score, tags: [] } });
  });

  it('opens the crisis screen when the gate blocks', async () => {
    mocks.api.mockResolvedValue({ crisis: true, level: 4, entry: { id: 'm', score: 0, tags: [], at: '' } });
    renderWithIntl(<MoodFaces initialScore={null} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'سيئ جدًا' }));
    await waitFor(() => expect(mocks.replace).toHaveBeenCalledWith('/crisis'));
  });

  it("shows today's saved score as selected", () => {
    renderWithIntl(<MoodFaces initialScore={3} />, 'ar');
    expect(screen.getByRole('button', { name: 'سيئ' })).toHaveAttribute('aria-pressed', 'true');
  });

  it('links to the full check-in to add a note', () => {
    renderWithIntl(<MoodFaces initialScore={null} />, 'ar');
    expect(screen.getByRole('link', { name: 'أضف ملاحظة إن أحببت' })).toHaveAttribute('href', '/check-in');
  });
});

describe('HomeView', () => {
  it('in Arabic greets by local time and shows the cards in the design order', () => {
    renderWithIntl(<HomeView home={home} locale="ar" timeZone="Asia/Riyadh" now={evening} />, 'ar');
    expect(screen.getByText('مساء الخير')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1, name: 'نخلة هادئة' })).toBeInTheDocument();
    expect(screen.getByText('جلستك مع أ. سارة')).toBeInTheDocument();
    expect(screen.getByText('الأربعاء 4:00 م · بعد الغد')).toBeInTheDocument();
    expect(screen.getByText('أكتوبر')).toBeInTheDocument();
    expect(screen.getByText('21')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'تفاصيل جلستك مع أ. سارة' })).toHaveAttribute('href', '/appointments/a1');
    expect(screen.getByText('رسالة من أ. سارة')).toBeInTheDocument();
    expect(screen.getByText('تمرين التنفس · 5 دقائق')).toBeInTheDocument();
    expect(screen.getByText('3 من 7 أيام')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /واجب اليوم/ })).toHaveAttribute('href', '/homework/h1');
    expect(screen.getByRole('link', { name: /خطة أماني/ })).toHaveAttribute('href', '/safety-plan');
    expect(screen.getByText('تعمل بلا إنترنت')).toBeInTheDocument();
  });

  it('in English says good morning and uses Latin digits', () => {
    renderWithIntl(<HomeView home={home} locale="en" timeZone="Asia/Riyadh" now={new Date('2026-10-19T05:30:00Z')} />, 'en');
    expect(screen.getByText('Good morning')).toBeInTheDocument();
    expect(screen.getByText('Your session with أ. سارة')).toBeInTheDocument();
    expect(screen.getByText('تمرين التنفس · 5 minutes')).toBeInTheDocument();
    expect(screen.getByText('3 of 7 days')).toBeInTheDocument();
    expect(screen.getByText('Works offline')).toBeInTheDocument();
  });

  it('points to the waiting screen while triage is pending', () => {
    renderWithIntl(<HomeView home={{ ...home, triage: { status: 'pending', submittedAt: null, expectedBy: null, suggestionsReady: false } }} locale="ar" timeZone="Asia/Riyadh" now={evening} />, 'ar');
    expect(screen.getByRole('link', { name: /طلبك قيد المراجعة/ })).toHaveAttribute('href', '/waiting');
  });

  it('points to P36 when the case was closed with referrals or escalated', () => {
    const { unmount } = renderWithIntl(<HomeView home={{ ...home, triage: { status: 'closed', submittedAt: null, expectedBy: null, suggestionsReady: false, referrals: ['crisis_line'] } }} locale="ar" timeZone="Asia/Riyadh" now={evening} />, 'ar');
    expect(screen.getByRole('link', { name: /راجعنا ملفك/ })).toHaveAttribute('href', '/waiting');
    unmount();
    renderWithIntl(<HomeView home={{ ...home, triage: { status: 'escalated', submittedAt: null, expectedBy: null, suggestionsReady: false } }} locale="en" timeZone="Asia/Riyadh" now={evening} />, 'en');
    expect(screen.getByRole('link', { name: /We want to check on you/ })).toHaveAttribute('href', '/waiting');
  });

  it('points to the matches when suggestions are ready', () => {
    renderWithIntl(<HomeView home={{ ...home, triage: { status: 'reviewed', submittedAt: null, expectedBy: null, suggestionsReady: true } }} locale="en" timeZone="Asia/Riyadh" now={evening} />, 'en');
    expect(screen.getByRole('link', { name: /Your suggestions are ready/ })).toHaveAttribute('href', '/matches');
  });

  it('offers the directory when no session is booked', () => {
    renderWithIntl(<HomeView home={{ ...home, nextAppointment: null, todayHomework: null, latestFeedback: null }} locale="ar" timeZone="Asia/Riyadh" now={evening} />, 'ar');
    expect(screen.getByText('لا توجد جلسة قادمة')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'تصفح الأخصائيين' })).toHaveAttribute('href', '/therapists');
  });

  it('shows "Continue intake" in place of the session card while an intake is open (PB-46)', () => {
    renderWithIntl(<HomeView home={{ ...home, nextAppointment: null }} locale="ar" timeZone="Asia/Riyadh" now={evening} intakeStatus="paused" />, 'ar');
    expect(screen.getByRole('link', { name: /متابعة الاستقبال/ })).toHaveAttribute('href', '/intake');
    expect(screen.queryByText('لا توجد جلسة قادمة')).not.toBeInTheDocument();
  });

  it('has no intake card once the intake was submitted (English)', () => {
    renderWithIntl(<HomeView home={home} locale="en" timeZone="Asia/Riyadh" now={evening} intakeStatus="submitted" />, 'en');
    expect(screen.queryByTestId('continue-intake')).not.toBeInTheDocument();
  });

  describe('join button (PB-43)', () => {
    afterEach(() => vi.useRealTimers());

    it('stays hidden until ten minutes before, then opens the session lobby', () => {
      vi.useFakeTimers({ toFake: ['Date'] });
      vi.setSystemTime(new Date('2026-10-21T12:45:00Z'));
      const join = { joinAvailableAt: '2026-10-21T12:50:00Z' };
      const { unmount } = renderWithIntl(<HomeView home={home} locale="ar" timeZone="Asia/Riyadh" now={new Date()} join={join} />, 'ar');
      expect(screen.queryByTestId('home-join')).not.toBeInTheDocument();
      unmount();

      vi.setSystemTime(new Date('2026-10-21T12:51:00Z'));
      renderWithIntl(<HomeView home={home} locale="en" timeZone="Asia/Riyadh" now={new Date()} join={join} />, 'en');
      const link = screen.getByRole('link', { name: 'Join the session with أ. سارة' });
      expect(link).toHaveAttribute('href', `/appointments/${home.nextAppointment!.id}/lobby`);
      expect(link).not.toHaveAttribute('target');
    });
  });
});
