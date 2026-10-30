import { cleanup, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createTranslator } from 'next-intl';
import type { ReactNode } from 'react';
import type { AppointmentDetail, HomeSummary, Me } from '@/lib/api/types';
import { messagesFor, renderWithIntl } from '../intl';
import { appointment, appointmentDetail, khaled } from '../matching/fixtures';

const mocks = vi.hoisted(() => ({
  getMe: vi.fn(),
  serverGetOptional: vi.fn(),
  push: vi.fn(),
  redirect: vi.fn(),
  requestHelp: vi.fn(),
  api: vi.fn(),
  locale: 'ar' as 'ar' | 'en',
}));
vi.mock('@/lib/api/server', () => ({ getMe: mocks.getMe, serverGetOptional: mocks.serverGetOptional }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api }));
vi.mock('@/lib/platform-config', () => ({ loadSafetyNumbers: async () => ({ emergencyNumber: '997', supportLine: '920033360' }) }));
vi.mock('next/navigation', () => ({ notFound: () => { throw new Error('not found'); } }));
vi.mock('next-intl/server', () => ({
  setRequestLocale: () => undefined,
  getTranslations: async (arg: string | { namespace: string }) =>
    createTranslator({ locale: mocks.locale, messages: messagesFor(mocks.locale), namespace: (typeof arg === 'string' ? arg : arg.namespace) as never }),
}));
vi.mock('@/components/ui/BackLink', () => ({ BackLink: ({ href }: { href: string }) => <a href={href}>back</a> }));
vi.mock('@/components/crisis/helpNow', () => ({ requestHelp: mocks.requestHelp }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => ({ push: mocks.push, replace: vi.fn(), refresh: vi.fn() }),
  usePathname: () => '/home',
  redirect: (arg: { href: string }) => {
    mocks.redirect(arg.href);
    return null;
  },
  Link: ({ href, children, ...rest }: { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import CancelledPage from '@/app/[locale]/(app)/appointments/[id]/cancelled/page';
import MissedPage from '@/app/[locale]/(app)/appointments/[id]/missed/page';
import { MissedCheckIn } from '@/components/appointments/MissedCheckIn';
import { TalkNowButton } from '@/components/appointments/TalkNowButton';
import { HomeView } from '@/components/home/HomeView';

const me: Me = {
  pseudonymId: 'P-1',
  nickname: 'نور',
  language: 'ar',
  timeZone: 'Asia/Riyadh',
  onboarding: { consentsDone: true, safetyInfoDone: true, path: 'direct', screeningDone: true, intakeStatus: 'none', triageStatus: 'matched' },
  chatLocked: false,
};

async function renderPage(page: typeof CancelledPage, detail: AppointmentDetail, locale: 'ar' | 'en' = 'ar') {
  mocks.locale = locale;
  mocks.getMe.mockResolvedValue(me);
  mocks.serverGetOptional.mockResolvedValue(detail);
  const ui = await page({ params: Promise.resolve({ locale, id: detail.id }) });
  if (ui) renderWithIntl(ui, locale);
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('P67 therapist cancelled', () => {
  const cancelled = appointmentDetail({ status: 'cancelled_by_therapist', cancelledBy: 'therapist', canChange: false });

  it('apologises without the real reason and offers the same therapist or the matches', async () => {
    await renderPage(CancelledPage, cancelled);
    expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('نعتذر، أ. سارة م. اعتذرت عن الجلسة');
    expect(screen.getByText('لظرف طارئ. ما يتطلب منك أي شي.')).toBeInTheDocument();
    expect(screen.getByTestId('rebook-same')).toHaveAttribute('href', '/therapists/t-sara/book?reschedule=a1');
    expect(screen.getByTestId('rebook-priority')).toHaveTextContent('موعدك الجديد له أولوية في جدولها');
    expect(screen.queryByText(/د\. منى|substitute/i)).toBeNull();
    expect(screen.getByTestId('rebook-other')).toHaveAttribute('href', '/matches');
    expect(screen.getByText('لن ينقص هذا من عدد جلساتك.')).toBeInTheDocument();
    expect(screen.getByTestId('talk-now')).toBeInTheDocument();
  });

  it('uses the male form and English', async () => {
    await renderPage(CancelledPage, appointmentDetail({ therapist: khaled, status: 'cancelled_by_therapist', cancelledBy: 'therapist' }), 'en');
    expect(screen.getByRole('heading', { level: 1 }).textContent).toContain('had to cancel your session');
    expect(screen.getByTestId('rebook-same')).toHaveAttribute('href', '/therapists/t-khaled/book?reschedule=a1');
    expect(screen.getByTestId('rebook-priority')).toHaveTextContent('priority in his schedule');
  });

  it('sends anything else back to the appointment', async () => {
    await renderPage(CancelledPage, appointmentDetail({ status: 'cancelled_by_patient', cancelledBy: 'patient', cancelReasonCode: 'unwell' }));
    expect(mocks.redirect).toHaveBeenCalledWith('/appointments/a1');
  });
});

describe('P63 missed session', () => {
  it('reassures, asks how the patient is, and offers rebooking and talking now', async () => {
    await renderPage(MissedPage, appointmentDetail({ status: 'no_show', missed: true, startsAt: '2026-10-01T13:00:00Z' }));
    expect(screen.getByText('يصير، ولا عليك شي. ما تنخصم ولا تنحسب ضدك.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { level: 1 }).textContent).toContain('فاتتك جلسة');
    expect(screen.getByTestId('rebook')).toHaveAttribute('href', '/therapists/t-sara/book');
    expect(screen.getByRole('button', { name: 'أحتاج أتكلم مع أحد الآن' })).toBeInTheDocument();
  });

  it('redirects when the session was not missed', async () => {
    await renderPage(MissedPage, appointmentDetail());
    expect(mocks.redirect).toHaveBeenCalledWith('/appointments/a1');
  });

  it('sends each answer and shows the crisis resources and the talk-now option for «مو بخير»', async () => {
    mocks.api.mockResolvedValue({ feeling: 'ok', recordedAt: '2026-10-21T14:00:00Z' });
    renderWithIntl(<MissedCheckIn appointmentId="a1" resources={<a href="tel:997">997</a>} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'بخير' }));
    expect(screen.queryByTestId('not-ok-support')).toBeNull();
    expect(mocks.api).toHaveBeenLastCalledWith('appointments/a1/missed-check-in', expect.objectContaining({ method: 'POST', body: { feeling: 'ok' }, idempotencyKey: expect.any(String) }));
    await userEvent.click(screen.getByRole('button', { name: 'مو بخير' }));
    expect(mocks.api).toHaveBeenLastCalledWith('appointments/a1/missed-check-in', expect.objectContaining({ body: { feeling: 'not_ok' } }));
    expect(screen.getByRole('button', { name: 'مو بخير' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByTestId('not-ok-support')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '997' })).toHaveAttribute('href', 'tel:997');
    expect(screen.getByRole('link', { name: 'أرقام الطوارئ والدعم' })).toHaveAttribute('href', '/crisis-resources');
  });

  it('keeps the support visible and offers a retry with the same key when the answer fails to send (English)', async () => {
    mocks.api.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce({ feeling: 'not_ok', recordedAt: '2026-10-21T14:00:00Z' });
    renderWithIntl(<MissedCheckIn appointmentId="a1" />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Not okay' }));
    expect(await screen.findByRole('alert')).toHaveTextContent("Your answer didn't reach us.");
    expect(screen.getByTestId('not-ok-support')).toBeInTheDocument();
    const firstKey = mocks.api.mock.calls[0]?.[1]?.idempotencyKey;
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    await waitFor(() => expect(screen.queryByRole('alert')).toBeNull());
    expect(mocks.api.mock.calls[1]?.[1]?.idempotencyKey).toBe(firstKey);
  });

  it('shows the answer already given', () => {
    renderWithIntl(<MissedCheckIn appointmentId="a1" initial="tired" />, 'en');
    expect(screen.getByRole('button', { name: 'A bit tired' })).toHaveAttribute('aria-pressed', 'true');
  });
});

describe('TalkNowButton', () => {
  it('opens the crisis screen once the request is sent', async () => {
    mocks.requestHelp.mockResolvedValue({ status: 'sent', response: {} });
    renderWithIntl(<TalkNowButton label="talk" />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'talk' }));
    await waitFor(() => expect(mocks.push).toHaveBeenCalledWith('/crisis'));
  });

  it('opens the emergency numbers when the request has to wait', async () => {
    mocks.requestHelp.mockResolvedValue({ status: 'queued' });
    renderWithIntl(<TalkNowButton label="talk" />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'talk' }));
    await waitFor(() => expect(mocks.push).toHaveBeenCalledWith('/crisis-resources'));
  });
});

describe('P09 home notice', () => {
  const home: HomeSummary = {
    nickname: 'نور',
    nextAppointment: null,
    todayHomework: null,
    latestFeedback: null,
    moodToday: null,
    hasSafetyPlan: false,
    triage: { status: 'matched', submittedAt: null, expectedBy: null, suggestionsReady: false },
    appointmentNotice: { appointmentId: 'a9', kind: 'cancelled' },
  };

  it('links a cancelled or missed session to its screen', () => {
    const { unmount } = renderWithIntl(<HomeView home={home} locale="ar" timeZone="Asia/Riyadh" now={new Date('2026-10-19T17:00:00Z')} />, 'ar');
    expect(screen.getByTestId('appointment-notice')).toHaveAttribute('href', '/appointments/a9/cancelled');
    expect(screen.getByText('الأخصائي ألغى جلسة')).toBeInTheDocument();
    unmount();
    renderWithIntl(<HomeView home={{ ...home, appointmentNotice: { appointmentId: 'a8', kind: 'missed' } }} locale="en" timeZone="Asia/Riyadh" now={new Date('2026-10-19T17:00:00Z')} />, 'en');
    expect(screen.getByTestId('appointment-notice')).toHaveAttribute('href', '/appointments/a8/missed');
    expect(screen.getByText('You missed a session')).toBeInTheDocument();
  });

  it('keeps the appointment fixture contract complete', () => {
    expect(appointment()).toEqual(expect.objectContaining({ therapistLateMinutes: null, cancelledBy: null, cancelReasonCode: null, missed: false }));
  });
});
