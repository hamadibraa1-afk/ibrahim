import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import type { MoodEntry } from '@/lib/api/types';
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
  usePathname: () => '/check-in',
}));

import { CheckInForm } from '@/components/mood/CheckInForm';
import { buildWeek } from '@/components/mood/MoodChart';

// Tuesday 20 October 2026, 15:00 in Riyadh.
const nowIso = '2026-10-20T12:00:00Z';
const entries: MoodEntry[] = [
  { id: 'a', score: 4, tags: [], at: '2026-10-15T08:00:00Z' },
  { id: 'b', score: 2, tags: ['tired'], at: '2026-10-18T08:00:00Z' },
  { id: 'c', score: 5, tags: [], at: '2026-10-18T18:00:00Z' },
  { id: 'd', score: 6, tags: [], at: '2026-10-20T06:00:00Z' },
];

describe('buildWeek', () => {
  it('returns seven days ending today with the latest score of each day', () => {
    const week = buildWeek(entries, new Date(nowIso), 'Asia/Riyadh');
    expect(week.map((d) => d.key)).toEqual(['2026-10-14', '2026-10-15', '2026-10-16', '2026-10-17', '2026-10-18', '2026-10-19', '2026-10-20']);
    expect(week.map((d) => d.score)).toEqual([null, 4, null, null, 5, null, 6]);
    expect(week[6]?.today).toBe(true);
  });
});

describe('CheckInForm', () => {
  beforeEach(() => {
    mocks.api.mockReset();
    mocks.replace.mockReset();
  });

  it("starts from today's score and describes the slider value in words (Arabic)", () => {
    renderWithIntl(<CheckInForm initialEntries={entries} nowIso={nowIso} timeZone="Asia/Riyadh" />, 'ar');
    const slider = screen.getByRole('slider', { name: 'مزاجك من 0 إلى 10' });
    expect(slider).toHaveValue('6');
    expect(slider).toHaveAttribute('aria-valuetext', '6 من 10، عادي');
    fireEvent.change(slider, { target: { value: '9' } });
    expect(slider).toHaveAttribute('aria-valuetext', '9 من 10، ممتاز');
    expect(screen.getByText('سيئ جدًا')).toBeInTheDocument();
    expect(screen.getByText('ممتاز')).toBeInTheDocument();
  });

  it('describes the slider in English', () => {
    renderWithIntl(<CheckInForm initialEntries={[]} nowIso={nowIso} timeZone="Asia/Riyadh" />, 'en');
    const slider = screen.getByRole('slider', { name: 'Your mood from 0 to 10' });
    fireEvent.change(slider, { target: { value: '8' } });
    expect(slider).toHaveAttribute('aria-valuetext', '8 of 10, good');
    expect(screen.getByText("You haven't logged your mood this week yet.")).toBeInTheDocument();
  });

  it('gives the 7-day chart a text alternative: a table of day and score', () => {
    renderWithIntl(<CheckInForm initialEntries={entries} nowIso={nowIso} timeZone="Asia/Riyadh" />, 'ar');
    const table = screen.getByRole('table', { name: 'آخر 7 أيام' });
    const rows = within(table).getAllByRole('row');
    expect(rows).toHaveLength(8);
    expect(within(table).getByRole('rowheader', { name: 'الثلاثاء 20 أكتوبر' }).closest('tr')).toHaveTextContent('6 من 10');
    expect(within(table).getByRole('rowheader', { name: 'الأحد 18 أكتوبر' }).closest('tr')).toHaveTextContent('5 من 10');
    expect(within(table).getByRole('rowheader', { name: 'الجمعة 16 أكتوبر' }).closest('tr')).toHaveTextContent('لا يوجد تسجيل');
    expect(screen.getByTestId('mood-bars')).toHaveAttribute('aria-hidden', 'true');
  });

  it('saves the score, tags and note, then adds the entry to the chart', async () => {
    mocks.api.mockResolvedValue({ crisis: false, level: 0, entry: { id: 'e', score: 3, tags: ['tired', 'work_stress'], at: '2026-10-20T12:00:00Z' } });
    renderWithIntl(<CheckInForm initialEntries={[]} nowIso={nowIso} timeZone="Asia/Riyadh" />, 'ar');
    fireEvent.change(screen.getByRole('slider'), { target: { value: '3' } });
    await userEvent.click(screen.getByRole('button', { name: 'متعب' }));
    await userEvent.click(screen.getByRole('button', { name: 'ضغط عمل' }));
    expect(screen.getByRole('button', { name: 'متعب' })).toHaveAttribute('aria-pressed', 'true');
    await userEvent.type(screen.getByRole('textbox', { name: /ملاحظة/ }), '  يوم طويل ');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ' }));
    expect(mocks.api).toHaveBeenCalledWith('mood', { method: 'POST', body: { score: 3, tags: ['tired', 'work_stress'], note: 'يوم طويل' } });
    expect(await screen.findByText('حفظنا مزاجك اليوم.')).toBeInTheDocument();
    expect(screen.getByRole('rowheader', { name: 'الثلاثاء 20 أكتوبر' }).closest('tr')).toHaveTextContent('3 من 10');
  });

  it('opens the crisis screen when the note is blocked by the gate', async () => {
    mocks.api.mockResolvedValue({ crisis: true, level: 4, entry: { id: 'e', score: 1, tags: [], at: nowIso } });
    renderWithIntl(<CheckInForm initialEntries={[]} nowIso={nowIso} timeZone="Asia/Riyadh" />, 'en');
    await userEvent.type(screen.getByRole('textbox', { name: /Note/ }), 'note');
    await userEvent.click(screen.getByRole('button', { name: 'Save' }));
    await waitFor(() => expect(mocks.replace).toHaveBeenCalledWith('/crisis'));
  });
});
