import { cleanup, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import type { SessionState } from '@/lib/api/types';
import { ApiError } from '@/lib/api/errors';
import { renderWithIntl } from '../intl';
import { appointment, appointmentDetail, khaled } from '../matching/fixtures';

const mocks = vi.hoisted(() => ({ api: vi.fn(), push: vi.fn(), replace: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => ({ push: mocks.push, replace: mocks.replace, refresh: vi.fn() }),
  Link: ({ href, children, ...rest }: { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { SessionLobby } from '@/components/session/SessionLobby';
import { SessionRoom, safeEmbedUrl } from '@/components/session/SessionRoom';
import { SessionSummaryView, doneHeading } from '@/components/session/SessionSummaryView';
import { PreSessionNoteField } from '@/components/session/PreSessionNoteField';
import { canJoin, lobbyPhase, mmss } from '@/components/session/sessionClock';

const START = '2026-10-21T13:00:00Z';
const startMs = Date.parse(START);
const min = 60_000;

function state(overrides: Partial<SessionState> = {}): SessionState {
  return {
    appointmentId: 'a1',
    serverNow: new Date().toISOString(),
    startsAt: START,
    durationMinutes: 50,
    opensAt: '2026-10-21T12:50:00Z',
    closesAt: '2026-10-21T13:50:00Z',
    open: false,
    status: 'confirmed',
    peer: { present: false, joinedAt: null, lastSeenAt: null },
    therapistLate: false,
    lateOptionsAfterMinutes: 15,
    heartbeatSeconds: 15,
    provider: 'dev',
    patientCrisisOpen: false,
    ...overrides,
  };
}

afterEach(() => {
  cleanup();
  vi.useRealTimers();
  mocks.api.mockReset();
  mocks.push.mockReset();
  mocks.replace.mockReset();
});

describe('lobby states (P41, P54)', () => {
  const s = state();
  it('counts down, opens ten minutes before, turns late after the start and offers options after 15 minutes', () => {
    expect(lobbyPhase(s, startMs - 11 * min)).toBe('before');
    expect(lobbyPhase(s, startMs - 10 * min)).toBe('open');
    expect(lobbyPhase(s, startMs + 1000)).toBe('late');
    expect(lobbyPhase(s, startMs + 15 * min)).toBe('lateOptions');
    expect(lobbyPhase(s, startMs + 51 * min)).toBe('closed');
    expect(lobbyPhase({ ...s, status: 'cancelled_by_therapist' }, startMs)).toBe('closed');
  });

  it('is not late once the therapist has joined, and a "running late" notice counts from the opening', () => {
    expect(lobbyPhase({ ...s, peer: { present: true, joinedAt: START, lastSeenAt: START } }, startMs + 5 * min)).toBe('open');
    expect(lobbyPhase(s, startMs - 5 * min, 10)).toBe('late');
    expect(lobbyPhase(s, startMs - 20 * min, 10)).toBe('before');
  });

  it('lets the patient join in every open state only', () => {
    expect(['before', 'open', 'late', 'lateOptions', 'closed'].map((p) => canJoin(p as never))).toEqual([false, true, true, true, false]);
    expect(mmss(6 * min + 42_000)).toBe('06:42');
    expect(mmss(-5)).toBe('00:00');
    expect(mmss(61 * min)).toBe('1:01:00');
  });
});

describe('SessionLobby (P41)', () => {
  it('keeps the join button disabled before the window and explains when it opens (Arabic)', () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(startMs - 20 * min));
    mocks.api.mockResolvedValue(state());
    renderWithIntl(<SessionLobby appointment={appointmentDetail()} initialState={state({ serverNow: new Date().toISOString() })} initialNote="" sessionLabel="الجلسة الأولى" />, 'ar');
    expect(screen.getByText('جلستك مع أ. سارة م. تبدأ بعد')).toBeInTheDocument();
    expect(screen.getByText('20:00')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'دخول الجلسة' })).toBeDisabled();
    expect(screen.getByRole('button', { name: 'دخول الجلسة' })).toHaveAccessibleDescription('يفتح قبل البدء بـ 10 دقائق');
    expect(screen.getByText(/الجلسة لا تُسجَّل/)).toBeInTheDocument();
    expect(screen.getByRole('checkbox', { name: 'أنا في مكان هادئ وخاص' })).not.toBeChecked();
  });

  it('opens the room inside the window and shows that the therapist has joined (English)', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(startMs - 5 * min));
    const s = state({ serverNow: new Date().toISOString(), open: true, peer: { present: true, joinedAt: new Date().toISOString(), lastSeenAt: null } });
    mocks.api.mockResolvedValue(s);
    renderWithIntl(<SessionLobby appointment={appointmentDetail()} initialState={s} initialNote="" sessionLabel="First session" />, 'en');
    expect(screen.getByTestId('peer')).toHaveTextContent('أ. سارة م. is in the session room');
    const join = screen.getByRole('button', { name: 'Join the session' });
    expect(join).toBeEnabled();
    await userEvent.click(join);
    expect(mocks.push).toHaveBeenCalledWith('/appointments/a1/live');
  });

  it('turns into P54 when the therapist is late: counter, free reschedule, and another therapist after 15 minutes', () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(startMs + 7 * min + 12_000));
    const s = state({ serverNow: new Date().toISOString(), open: true, therapistLate: true });
    mocks.api.mockResolvedValue(s);
    const { unmount } = renderWithIntl(<SessionLobby appointment={appointmentDetail({ therapistLateMinutes: 5 })} initialState={s} initialNote="" sessionLabel="الجلسة الأولى" />, 'ar');
    expect(screen.getByTestId('late-state')).toHaveTextContent('أ. سارة م. تأخرت قليلًا');
    expect(screen.getByTestId('late-state')).toHaveTextContent('أرسلت أنها ستكون معك خلال 5 دقيقة تقريبًا.');
    expect(screen.getByTestId('late-state')).toHaveTextContent('الوقت المتأخر لا يُخصم من جلستك.');
    expect(screen.getByTestId('late-counter')).toHaveTextContent('07:12');
    expect(screen.getByRole('link', { name: 'إعادة الجدولة الآن' })).toHaveAttribute('href', '/therapists/t-sara/book?reschedule=a1');
    expect(screen.queryByRole('link', { name: 'أخصائي آخر متاح' })).toBeNull();
    expect(screen.getByRole('button', { name: 'دخول الجلسة' })).toBeEnabled();
    unmount();

    vi.setSystemTime(new Date(startMs + 16 * min));
    renderWithIntl(<SessionLobby appointment={appointmentDetail({ therapist: khaled })} initialState={state({ serverNow: new Date().toISOString(), therapistLate: true })} initialNote="" sessionLabel="First session" />, 'en');
    expect(screen.getByTestId('late-state')).toHaveTextContent('أ. خالد ع. is running a little late');
    expect(screen.getByRole('link', { name: 'Another available therapist' })).toHaveAttribute('href', '/therapists');
  });

  it('opens the breathing exercise as an overlay without leaving the lobby', async () => {
    vi.useFakeTimers({ toFake: ['Date'] });
    vi.setSystemTime(new Date(startMs + 2 * min));
    const s = state({ serverNow: new Date().toISOString(), therapistLate: true });
    mocks.api.mockResolvedValue(s);
    renderWithIntl(<SessionLobby appointment={appointmentDetail()} initialState={s} initialNote="" sessionLabel="First session" />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Breathing exercise' }));
    expect(screen.getByRole('dialog', { name: 'Breathing exercise' })).toBeInTheDocument();
    expect(screen.getByTestId('breathing')).toBeInTheDocument();
  });
});

describe('PreSessionNoteField', () => {
  it('saves after a pause and opens the crisis screen when the gate says so', async () => {
    mocks.api.mockResolvedValue({ crisis: true, level: 4, script: '…', text: 'x', updatedAt: null });
    renderWithIntl(<PreSessionNoteField appointmentId="a1" initial="" gender="female" />, 'ar');
    expect(screen.getByText('تصل لأخصائيتك قبل الجلسة')).toBeInTheDocument();
    await userEvent.type(screen.getByRole('textbox', { name: /عن ماذا تحب أن نتحدث اليوم؟/ }), 'تعبت');
    await waitFor(() => expect(mocks.api).toHaveBeenCalledWith('appointments/a1/pre-session-note', { method: 'PUT', body: { text: 'تعبت' } }), { timeout: 3000 });
    await waitFor(() => expect(mocks.replace).toHaveBeenCalledWith('/crisis'));
  });
});

describe('SessionRoom (P42)', () => {
  it('labels the dev room honestly and shows the therapist presence from the heartbeat', async () => {
    mocks.api.mockImplementation(async (path: string) =>
      path.endsWith('/join')
        ? { provider: 'dev', mode: 'test_room', url: null, token: 'pass', expiresAt: new Date(Date.now() + 15 * min).toISOString(), opensAt: '', closesAt: new Date(Date.now() + 50 * min).toISOString(), participant: 'patient', heartbeatSeconds: 15 }
        : state({ serverNow: new Date().toISOString(), peer: { present: true, joinedAt: new Date(Date.now() - 2 * min).toISOString(), lastSeenAt: null } }),
    );
    renderWithIntl(<SessionRoom appointment={appointmentDetail({ startsAt: new Date(Date.now() - 5 * min).toISOString() })} />, 'ar');
    expect(await screen.findByTestId('test-room')).toHaveTextContent('غرفة تجريبية');
    expect(screen.getByText('لا يوجد تسجيل')).toBeInTheDocument();
    await waitFor(() => expect(screen.getByTestId('peer-tile')).toHaveTextContent('أ. سارة م. في غرفة الجلسة الآن'));
    expect(mocks.api).toHaveBeenCalledWith('appointments/a1/presence', { method: 'POST', body: { token: 'pass', leaving: undefined } });
    expect(screen.getByTestId('elapsed')).toHaveTextContent(/مدة الجلسة 0[12]:/);

    await userEvent.click(screen.getByRole('button', { name: 'إنهاء الجلسة' }));
    await userEvent.click(screen.getByRole('button', { name: 'نعم، غادر الجلسة' }));
    await waitFor(() => expect(mocks.push).toHaveBeenCalledWith('/appointments/a1/summary'));
    expect(mocks.api).toHaveBeenCalledWith('appointments/a1/presence', { method: 'POST', body: { token: 'pass', leaving: true } });
  });

  it('explains a closed window and links back to the lobby', async () => {
    mocks.api.mockRejectedValue(new ApiError(409, { title: 'x', status: 409, code: 'error.session.not_open', detail: 'The session room opens 10 minutes before the start and closes when the session ends.' }));
    renderWithIntl(<SessionRoom appointment={appointmentDetail()} />, 'en');
    expect(await screen.findByText(/opens 10 minutes before/)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to the waiting room' })).toHaveAttribute('href', '/appointments/a1/lobby');
  });

  it('frames only https rooms', () => {
    expect(safeEmbedUrl('https://video.example/r?jwt=x')).toBe('https://video.example/r?jwt=x');
    expect(safeEmbedUrl('javascript:alert(1)')).toBeNull();
    expect(safeEmbedUrl(null)).toBeNull();
  });
});

describe('SessionSummaryView (P43)', () => {
  const props = { appointment: appointmentDetail({ status: 'completed' as const }), next: appointment({ id: 'a2', startsAt: '2026-10-28T13:00:00Z' }), homework: [], feedback: null, timeZone: 'Asia/Riyadh' };

  it('names the session in the heading', () => {
    const t = (key: string, values?: Record<string, string>) => `${key}${values ? JSON.stringify(values) : ''}`;
    expect(doneHeading(t, 1, 'ar')).toBe('done1');
    expect(doneHeading(t, 12, 'en')).toBe('doneN{"num":"12"}');
  });

  it('saves the private rating once with an idempotency key and goes home (Arabic)', async () => {
    mocks.api.mockResolvedValue({ crisis: false, level: 1, feedback: { rating: 'helpful', comfortable: true, comment: null, createdAt: '' } });
    renderWithIntl(<SessionSummaryView {...props} />, 'ar');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('أحسنت، أنهيت جلستك الأولى');
    expect(screen.getByText('رأيك خاص ويصل للمشرف فقط')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'التفاصيل' })).toHaveAttribute('href', '/appointments/a2');
    await userEvent.click(screen.getByRole('radio', { name: 'مفيدة' }));
    await userEvent.click(screen.getByRole('checkbox', { name: 'شعرت بالارتياح مع الأخصائية' }));
    await userEvent.click(screen.getByRole('button', { name: 'حفظ والعودة للرئيسية' }));
    await waitFor(() => expect(mocks.replace).toHaveBeenCalledWith('/home'));
    const [path, options] = mocks.api.mock.calls[0]!;
    expect(path).toBe('appointments/a1/feedback');
    expect(options).toMatchObject({ method: 'POST', body: { rating: 'helpful', comfortable: true } });
    expect(options.idempotencyKey).toEqual(expect.any(String));
  });

  it('keeps the choice when saving fails, and only goes home when nothing was chosen (English)', async () => {
    mocks.api.mockRejectedValueOnce(new Error('offline'));
    renderWithIntl(<SessionSummaryView {...props} next={null} homework={[{ id: 'h1', title: 'Breathing before bed', therapistName: 'x', frequency: 'daily', endsOn: '', dueToday: true, doneThisPeriod: 0, hasNewFeedback: false, completed: false }]} />, 'en');
    expect(screen.getByRole('link', { name: 'Book your next session' })).toHaveAttribute('href', '/therapists/t-sara/book');
    expect(screen.getByText('Breathing before bed · Daily')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('radio', { name: 'Okay' }));
    await userEvent.click(screen.getByRole('button', { name: 'Save and go home' }));
    expect(await screen.findByRole('alert')).toHaveTextContent("Couldn't save your rating");
    expect(screen.getByRole('radio', { name: 'Okay' })).toBeChecked();
    expect(mocks.replace).not.toHaveBeenCalled();
  });

  it('shows an existing rating read-only', () => {
    renderWithIntl(<SessionSummaryView {...props} feedback={{ rating: 'okay', comfortable: true, comment: 'Thanks', createdAt: '' }} />, 'en');
    expect(screen.getByTestId('feedback-readonly')).toHaveTextContent('Okay · Felt comfortable');
    expect(screen.queryByRole('radio')).toBeNull();
  });
});

