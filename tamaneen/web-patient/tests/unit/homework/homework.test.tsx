import { fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AnchorHTMLAttributes, ReactNode } from 'react';
import { ApiError } from '@/lib/api/errors';
import type { HomeworkDetail, HomeworkItem } from '@/lib/api/types';
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
  usePathname: () => '/homework',
}));

import { HomeworkDetailView } from '@/components/homework/HomeworkDetailView';
import { HomeworkListView } from '@/components/homework/HomeworkListView';
import { outboxItems, resetOutbox, setOutboxOwner } from '@/components/pwa/outbox';

const item = (over: Partial<HomeworkItem>): HomeworkItem => ({
  id: 'h1',
  title: 'تمرين التنفس قبل النوم',
  therapistName: 'أ. سارة م.',
  frequency: 'daily',
  endsOn: '2026-10-28T12:00:00Z',
  dueToday: true,
  doneThisPeriod: 3,
  hasNewFeedback: false,
  completed: false,
  ...over,
});

const detail = (over: Partial<HomeworkDetail> = {}): HomeworkDetail => ({
  ...item({}),
  instructions: ['اجلس أو استلقِ في مكان هادئ قبل النوم بنصف ساعة.', 'تنفس من أنفك 4 ثوانٍ، واحبس النفس 4 ثوانٍ.', 'أخرج النفس ببطء 6 ثوانٍ. كرر ذلك 5 دقائق.'],
  minutes: 5,
  doneDays: 3,
  totalDays: 7,
  latestFeedback: { text: 'أحسنت في الاستمرار ثلاثة أيام.', at: '2026-10-19T10:00:00Z', initials: 'س م' },
  todayCheckin: null,
  ...over,
});

const list = {
  active: [
    item({}),
    item({ id: 'h2', title: 'سجل الأفكار', frequency: 'twice_weekly', dueToday: false, doneThisPeriod: 1, hasNewFeedback: true }),
    item({ id: 'h3', title: 'نشاط ممتع صغير', frequency: 'weekly', dueToday: false, doneThisPeriod: 0 }),
  ],
  completed: [item({ id: 'h4', title: 'قائمة مصادر الضغط', frequency: 'once', endsOn: '2026-10-14T12:00:00Z', completed: true, dueToday: false })],
};

describe('HomeworkListView', () => {
  it('lists active homework with tags and frequency, then completed (Arabic)', () => {
    renderWithIntl(<HomeworkListView list={list} locale="ar" timeZone="Asia/Riyadh" />, 'ar');
    expect(screen.getByRole('heading', { level: 1, name: 'واجباتي' })).toBeInTheDocument();
    expect(screen.getByText('تمارين اختارتها لك أخصائيتك. الأيام التي تفوتك لا بأس بها.')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /تمرين التنفس قبل النوم/ })).toHaveAttribute('href', '/homework/h1');
    expect(screen.getByText('اليوم')).toBeInTheDocument();
    expect(screen.getByText('يوميًا حتى 28 أكتوبر')).toBeInTheDocument();
    expect(screen.getByRole('img', { name: 'أنجزت 3 من 7 أيام هذا الأسبوع' })).toBeInTheDocument();
    expect(screen.getByText('رد جديد')).toBeInTheDocument();
    expect(screen.getByText('مرتين أسبوعيًا · أنجزت مرة هذا الأسبوع')).toBeInTheDocument();
    expect(screen.getByText('مرة أسبوعيًا')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'مكتملة' })).toBeInTheDocument();
    expect(screen.getByText('انتهت 14 أكتوبر')).toBeInTheDocument();
  });

  it('reads naturally in English', () => {
    renderWithIntl(<HomeworkListView list={list} locale="en" timeZone="Asia/Riyadh" />, 'en');
    expect(screen.getByText('Daily until 28 October')).toBeInTheDocument();
    expect(screen.getByText('Twice a week · Done once this week')).toBeInTheDocument();
    expect(screen.getByText('Ended 14 October')).toBeInTheDocument();
  });

  it('shows an empty state with no homework, with a breathing exercise to do now (P46)', async () => {
    renderWithIntl(<HomeworkListView list={{ active: [], completed: [] }} locale="ar" timeZone="Asia/Riyadh" />, 'ar');
    expect(screen.getByText('لا توجد واجبات بعد')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'تمرين تنفس الآن' }));
    expect(screen.getByRole('dialog', { name: 'تمرين تنفس' })).toBeInTheDocument();
    expect(screen.getByTestId('breathing')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'إغلاق' }));
    expect(screen.queryByRole('dialog')).toBeNull();
  });
});

describe('HomeworkDetailView', () => {
  beforeEach(() => {
    mocks.api.mockReset();
    mocks.replace.mockReset();
  });

  it('shows an ended exercise read-only, without the check-in form', () => {
    renderWithIntl(<HomeworkDetailView homework={detail({ completed: true, endsOn: '2026-10-14T12:00:00Z' })} locale="ar" timeZone="Asia/Riyadh" />, 'ar');
    expect(screen.getByTestId('homework-read-only')).toHaveTextContent('انتهى هذا التمرين في 14 أكتوبر');
    expect(screen.getByText('أحسنت في الاستمرار ثلاثة أيام.')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'حفظ المتابعة' })).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
  });

  it('says so in English too', () => {
    renderWithIntl(<HomeworkDetailView homework={detail({ completed: true, endsOn: '2026-10-14T12:00:00Z' })} locale="en" timeZone="Asia/Riyadh" />, 'en');
    expect(screen.getByTestId('homework-read-only')).toHaveTextContent('This exercise ended on 14 October.');
  });

  it('shows the title, source, numbered instructions and the latest feedback', () => {
    renderWithIntl(<HomeworkDetailView homework={detail()} locale="ar" timeZone="Asia/Riyadh" />, 'ar');
    expect(screen.getByRole('heading', { level: 1, name: 'تمرين التنفس قبل النوم' })).toBeInTheDocument();
    expect(screen.getByText('من أ. سارة م. · يوميًا حتى 28 أكتوبر')).toBeInTheDocument();
    expect(screen.getAllByRole('listitem')).toHaveLength(3);
    expect(screen.getByRole('figure', { name: 'رد أخصائيتك' })).toHaveTextContent('أحسنت في الاستمرار ثلاثة أيام.');
  });

  it('requires a choice for how today went', async () => {
    renderWithIntl(<HomeworkDetailView homework={detail()} locale="ar" timeZone="Asia/Riyadh" />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ المتابعة' }));
    expect(screen.getByRole('alert')).toHaveTextContent('اختر كيف كان اليوم.');
    expect(screen.getByRole('radiogroup', { name: 'كيف كان اليوم؟' })).toHaveAttribute('aria-invalid', 'true');
    expect(mocks.api).not.toHaveBeenCalled();
  });

  it('labels the feeling slider with 0, the value and 10, and announces the value', () => {
    renderWithIntl(<HomeworkDetailView homework={detail()} locale="ar" timeZone="Asia/Riyadh" />, 'ar');
    const slider = screen.getByRole('slider', { name: 'شعورك بعد التمرين' });
    fireEvent.change(slider, { target: { value: '7' } });
    expect(slider).toHaveAttribute('aria-valuetext', '7 من 10');
    expect(slider).toHaveAttribute('min', '0');
    expect(slider).toHaveAttribute('max', '10');
    expect(slider.parentElement).toHaveTextContent(/0.*7.*10/);
  });

  it('saves status, feeling and note', async () => {
    const saved = detail({ latestFeedback: { text: 'رائع!', at: '2026-10-20T10:00:00Z', initials: 'س م' } });
    mocks.api.mockResolvedValue({ crisis: false, level: 0, homework: saved });
    renderWithIntl(<HomeworkDetailView homework={detail()} locale="ar" timeZone="Asia/Riyadh" />, 'ar');
    await userEvent.click(screen.getByRole('radio', { name: 'أنجزته' }));
    fireEvent.change(screen.getByRole('slider'), { target: { value: '7' } });
    await userEvent.type(screen.getByRole('textbox', { name: /ملاحظة لأخصائيتك/ }), 'نمت أسرع من الأمس.');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ المتابعة' }));
    expect(mocks.api).toHaveBeenCalledWith('homework/h1/checkins', expect.objectContaining({ method: 'POST', body: { status: 'done', feeling: 7, note: 'نمت أسرع من الأمس.' } }));
    expect(await screen.findByText('حفظنا متابعة اليوم. ستراها أخصائيتك.')).toBeInTheDocument();
    expect(screen.getByRole('figure')).toHaveTextContent('رائع!');
  });

  it('keeps the check-in on the phone when the connection drops and says it will be sent (English, P66)', async () => {
    resetOutbox();
    setOutboxOwner('P-TEST1');
    try {
      mocks.api.mockRejectedValue(new TypeError('Failed to fetch'));
      renderWithIntl(<HomeworkDetailView homework={detail()} locale="en" timeZone="Asia/Riyadh" />, 'en');
      await userEvent.click(screen.getByRole('radio', { name: 'Done it' }));
      await userEvent.click(screen.getByRole('button', { name: /Save/ }));
      expect(await screen.findByTestId('homework-queued')).toHaveTextContent('We will send it when you are back online.');
      expect(outboxItems()).toEqual([expect.objectContaining({ kind: 'homework-checkin', path: 'homework/h1/checkins', label: 'تمرين التنفس قبل النوم', state: 'waiting' })]);
    } finally {
      resetOutbox();
      window.localStorage.clear();
    }
  });

  it("prefills from today's check-in", () => {
    renderWithIntl(<HomeworkDetailView homework={detail({ todayCheckin: { status: 'partial', feeling: 4, note: 'تعبت قليلًا' } })} locale="en" timeZone="Asia/Riyadh" />, 'en');
    expect(screen.getByRole('radio', { name: 'Partly' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: 'Done it' })).toHaveAttribute('aria-checked', 'false');
    expect(screen.getByRole('slider', { name: 'How you felt after the exercise' })).toHaveValue('4');
    expect(screen.getByRole('textbox')).toHaveValue('تعبت قليلًا');
  });

  it('moves the choice with arrow keys following the reading direction', async () => {
    renderWithIntl(
      <div dir="rtl">
        <HomeworkDetailView homework={detail({ todayCheckin: { status: 'done', feeling: 5, note: null } })} locale="ar" timeZone="Asia/Riyadh" />
      </div>,
      'ar',
    );
    screen.getByRole('radio', { name: 'أنجزته' }).focus();
    await userEvent.keyboard('{ArrowLeft}');
    expect(screen.getByRole('radio', { name: 'جزئيًا' })).toHaveAttribute('aria-checked', 'true');
    expect(screen.getByRole('radio', { name: 'جزئيًا' })).toHaveFocus();
  });

  it('does not ask for a feeling on a skipped day', async () => {
    mocks.api.mockResolvedValue({ crisis: false, level: 0, homework: detail() });
    renderWithIntl(<HomeworkDetailView homework={detail()} locale="ar" timeZone="Asia/Riyadh" />, 'ar');
    await userEvent.click(screen.getByRole('radio', { name: 'ليس اليوم' }));
    expect(screen.queryByRole('slider')).toBeNull();
    await userEvent.click(screen.getByRole('button', { name: 'حفظ المتابعة' }));
    expect(mocks.api).toHaveBeenCalledWith('homework/h1/checkins', expect.objectContaining({ method: 'POST', body: { status: 'skipped', feeling: null } }));
  });

  it('opens the crisis screen when the note is blocked', async () => {
    mocks.api.mockResolvedValue({ crisis: true, level: 4, homework: detail() });
    renderWithIntl(<HomeworkDetailView homework={detail({ todayCheckin: { status: 'done', feeling: 2, note: null } })} locale="ar" timeZone="Asia/Riyadh" />, 'ar');
    await userEvent.type(screen.getByRole('textbox'), 'note');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ المتابعة' }));
    await waitFor(() => expect(mocks.replace).toHaveBeenCalledWith('/crisis'));
  });

  it("shows the API's own message when saving fails", async () => {
    mocks.api.mockRejectedValue(new ApiError(503, { title: 'الخدمة غير متاحة الآن', status: 503 }));
    renderWithIntl(<HomeworkDetailView homework={detail({ todayCheckin: { status: 'done', feeling: 2, note: null } })} locale="ar" timeZone="Asia/Riyadh" />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ المتابعة' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('الخدمة غير متاحة الآن');
  });
});
