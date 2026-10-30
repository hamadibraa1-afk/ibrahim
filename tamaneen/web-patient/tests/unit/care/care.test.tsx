import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { ApiError } from '@/lib/api/errors';
import type { CareClosure, ChangeReason, ChangeTherapistRequest } from '@/lib/api/types';
import { renderWithIntl } from '../intl';

const mocks = vi.hoisted(() => ({ api: vi.fn(), push: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api }));
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => ({ push: mocks.push, replace: vi.fn() }),
  Link: ({ href, children, ...rest }: { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { CareCompleteView } from '@/components/care/CareCompleteView';
import { ChangeTherapistView } from '@/components/care/ChangeTherapistView';

const reasons: ChangeReason[] = [
  { code: 'not_comfortable', label: 'لم أشعر بالارتياح' },
  { code: 'schedule', label: 'المواعيد لا تناسبني' },
  { code: 'gender', label: 'أفضّل أخصائيًا من جنس آخر' },
  { code: 'specialty', label: 'أحتاج تخصصًا مختلفًا' },
  { code: 'other', label: 'سبب آخر' },
];

function request(overrides: Partial<ChangeTherapistRequest> = {}): ChangeTherapistRequest {
  return { id: 'r1', status: 'requested', reasonCode: null, createdAt: '2026-10-20T08:00:00Z', expectedBy: '2026-10-21T08:00:00Z', withinHours: 24, ...overrides };
}

describe('ChangeTherapistView (P44)', () => {
  afterEach(() => mocks.api.mockReset());

  it('reassures, offers five optional reasons and sends without one, with an Idempotency-Key', async () => {
    mocks.api.mockResolvedValue(request());
    renderWithIntl(<ChangeTherapistView reasons={reasons} initial={null} therapistName="أ. سارة م." inCare withinHours={24} timeZone="Asia/Riyadh" />);
    expect(screen.getByText(/لن نخبر أ\. سارة م\. بالسبب/)).toBeInTheDocument();
    expect(screen.getAllByRole('radio')).toHaveLength(5);
    expect(screen.getByText(/خلال 24 ساعة/)).toBeInTheDocument();
    await userEvent.click(screen.getByTestId('change-submit'));
    await waitFor(() => expect(mocks.api).toHaveBeenCalled());
    const [path, options] = mocks.api.mock.calls[0] as [string, { method: string; body: unknown; idempotencyKey: string }];
    expect(path).toBe('me/change-therapist-requests');
    expect(options.body).toEqual({});
    expect(options.idempotencyKey).toMatch(/^[0-9a-f-]{36}$/);
    expect(await screen.findByText('وصل طلبك، سنرسل لك الاقتراحات خلال 24 ساعة.')).toBeInTheDocument();
    expect(screen.getByTestId('change-status')).toBeInTheDocument();
  });

  it('sends the chosen reason code, never free text', async () => {
    mocks.api.mockResolvedValue(request({ reasonCode: 'schedule' }));
    renderWithIntl(<ChangeTherapistView reasons={reasons} initial={null} therapistName={null} inCare withinHours={24} timeZone="Asia/Riyadh" />, 'en');
    await userEvent.click(screen.getByTestId('reason-schedule'));
    expect(screen.getByTestId('reason-schedule')).toHaveAttribute('aria-checked', 'true');
    await userEvent.click(screen.getByTestId('change-submit'));
    await waitFor(() => expect((mocks.api.mock.calls[0]![1] as { body: unknown }).body).toEqual({ reasonCode: 'schedule' }));
  });

  it('shows an open request instead of the form, with the matches once offered', () => {
    renderWithIntl(<ChangeTherapistView reasons={reasons} initial={request({ status: 'offered' })} therapistName={null} inCare withinHours={24} timeZone="Asia/Riyadh" />, 'en');
    expect(screen.queryByTestId('change-submit')).toBeNull();
    expect(screen.getByText('Your new suggestions are ready.')).toBeInTheDocument();
    expect(screen.getByTestId('see-matches')).toHaveAttribute('href', '/matches');
  });

  it('shows the request the API says is already open (409)', async () => {
    mocks.api.mockRejectedValue(new ApiError(409, { title: 'Open', status: 409, current: request({ status: 'matching' }) } as never));
    renderWithIntl(<ChangeTherapistView reasons={reasons} initial={null} therapistName={null} inCare withinHours={24} timeZone="Asia/Riyadh" />, 'en');
    await userEvent.click(screen.getByTestId('change-submit'));
    expect(await screen.findByText('A supervisor is choosing therapists for you now.')).toBeInTheDocument();
  });

  it('keeps the form and shows the error with a retry', async () => {
    mocks.api.mockRejectedValue(new ApiError(503, { title: 'Unavailable', detail: 'Try later', status: 503 }));
    renderWithIntl(<ChangeTherapistView reasons={reasons} initial={null} therapistName={null} inCare withinHours={24} timeZone="Asia/Riyadh" />, 'en');
    await userEvent.click(screen.getByTestId('change-submit'));
    expect(await screen.findByRole('alert')).toHaveTextContent('Try later');
    expect(screen.getByTestId('change-submit')).toHaveTextContent('Try again');
  });
});

const closure: CareClosure = {
  therapistName: 'أ. سارة م.',
  sessionCount: 10,
  summary: 'تعلمتِ ملاحظة الأفكار القاسية والرد عليها.',
  warningSigns: 'قلة النوم، الانعزال',
  whatToDo: 'سجل الأفكار، المشي',
  followUp: 'phq9_1m_3m',
  keepLibrary: true,
  closedAt: '2026-10-20T08:00:00Z',
  returnRequested: false,
};

describe('CareCompleteView (P53)', () => {
  afterEach(() => {
    mocks.api.mockReset();
    mocks.push.mockReset();
  });

  it('thanks the patient, shows the relapse plan, what stays and the download', () => {
    renderWithIntl(<CareCompleteView closure={closure} />);
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('وصلت لمحطة مهمة');
    expect(screen.getByText(/أنهيت مع أ\. سارة م\. 10 جلسات/)).toBeInTheDocument();
    expect(screen.getByTestId('relapse-plan')).toHaveTextContent('علاماتي: قلة النوم، الانعزال');
    expect(screen.getByTestId('relapse-plan')).toHaveTextContent('تذكير باختبار قصير بعد شهر');
    expect(screen.getByTestId('keeps')).toHaveTextContent('خطة السلامة');
    expect(screen.getByTestId('download-summary')).toHaveAttribute('href', '/api/v1/me/care-closure/summary');
    expect(screen.getByText('تقدر ترجع في أي وقت دون تسجيل جديد')).toBeInTheDocument();
  });

  it('hides the plan card when there is no relapse plan', () => {
    renderWithIntl(<CareCompleteView closure={{ ...closure, warningSigns: null, whatToDo: null, followUp: 'none' }} />, 'en');
    expect(screen.queryByTestId('relapse-plan')).toBeNull();
    expect(screen.getByText(/You finished 10 sessions with/)).toBeInTheDocument();
  });

  it('sends a return through the short safety check first', () => {
    renderWithIntl(<CareCompleteView closure={closure} />, 'en');
    expect(screen.getByTestId('come-back')).toHaveAttribute('href', '/care/return');
    expect(mocks.api).not.toHaveBeenCalled();
  });
});
