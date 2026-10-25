import { act, fireEvent, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ApiError } from '@/lib/api/errors';
import type { IntakeState, IntakeTurnResponse } from '@/lib/api/types';
import { renderWithIntl } from '../intl';
import { api, Link, resetMocks, routeApi, router } from './mocks';

vi.mock('@/lib/api/client', () => ({ api: (...args: unknown[]) => api(...args) }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router, Link }));

import { IntakeChat } from '@/components/intake/IntakeChat';

const base: IntakeState = {
  intakeId: 'in-1',
  status: 'active',
  progress: 45,
  messages: [{ id: 'm1', role: 'assistant', text: 'أهلًا بك. كيف كان شعورك خلال الأسبوعين الماضيين؟', at: '2026-10-20T10:00:00Z' }],
  pendingQuestion: null,
  canFinish: false,
  expiresAt: '2026-11-03T10:00:00Z',
  formMode: false,
};

const phq: IntakeState['pendingQuestion'] = {
  instrument: 'phq9',
  item: 2,
  total: 9,
  text: 'خلال الأسبوعين الماضيين، كم مرة شعرت بالحزن أو الاكتئاب أو اليأس؟',
  options: [
    { value: 0, label: 'أبدًا' },
    { value: 1, label: 'عدة أيام' },
    { value: 2, label: 'أكثر من نصف الأيام' },
    { value: 3, label: 'تقريبًا كل يوم' },
  ],
};

const notFound = () => {
  throw new ApiError(404, { title: 'Not found', status: 404 });
};

function turn(state: Partial<IntakeState>, extra: Partial<IntakeTurnResponse> = {}): IntakeTurnResponse {
  return { ...base, ...state, crisis: false, ...extra };
}

beforeEach(resetMocks);

describe('IntakeChat (P04)', () => {
  it('starts the intake and renders the header, progress and messages in Arabic', async () => {
    routeApi({ 'GET intake/current': notFound, 'POST intake': () => base });
    renderWithIntl(<IntakeChat />, 'ar');
    expect(await screen.findByText('رفيق · مساعد التعارف')).toBeInTheDocument();
    expect(screen.getByText('مساعد ذكي، ويراجع كلامك مختص بشري')).toBeInTheDocument();
    expect(screen.getByRole('progressbar', { name: 'تقدّم المحادثة' })).toHaveAttribute('aria-valuenow', '45');
    expect(screen.getByText('45%')).toBeInTheDocument();
    const log = screen.getByRole('log');
    expect(log).toHaveAttribute('aria-live', 'polite');
    expect(log).toHaveTextContent('أهلًا بك');
    expect(api).toHaveBeenCalledWith('intake', { method: 'POST' });
  });

  it('shows the English copy', async () => {
    routeApi({ 'GET intake/current': notFound, 'POST intake': () => base });
    renderWithIntl(<IntakeChat />, 'en');
    expect(await screen.findByText('Rafiq · intake assistant')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'I want to talk to a person' })).toBeInTheDocument();
    expect(screen.getByRole('textbox', { name: 'Your message' })).toHaveAttribute('placeholder', 'Type your message…');
  });

  it('sends a message with an optimistic bubble and a typing indicator, then shows the reply', async () => {
    let resolve!: (r: IntakeTurnResponse) => void;
    routeApi({
      'GET intake/current': () => base,
      'POST intake': () => base,
      'POST intake/in-1/messages': () => new Promise((r) => (resolve = r)),
    });
    renderWithIntl(<IntakeChat />, 'ar');
    const box = await screen.findByRole('textbox', { name: 'رسالتك' });
    await userEvent.type(box, 'أشعر بضغط كبير{Enter}');

    expect(api).toHaveBeenCalledWith('intake/in-1/messages', { method: 'POST', body: { text: 'أشعر بضغط كبير', clientMessageId: expect.stringMatching(/^[0-9a-f-]{36}$/) } });
    const bubble = screen.getByText('أشعر بضغط كبير');
    expect(bubble.closest('[data-role]')).toHaveAttribute('data-role', 'patient');
    expect(screen.getByTestId('typing')).toHaveTextContent('رفيق يكتب…');
    expect(box).toHaveValue('');

    await act(async () =>
      resolve(
        turn({
          messages: [...base.messages, { id: 'm2', role: 'patient', text: 'أشعر بضغط كبير', at: '' }, { id: 'm3', role: 'assistant', text: 'شكرًا لأنك شاركتني هذا.', at: '', fallback: true }],
        }),
      ),
    );
    expect(screen.queryByTestId('typing')).not.toBeInTheDocument();
    expect(screen.getByText('شكرًا لأنك شاركتني هذا.')).toBeInTheDocument();
    expect(screen.getByText('رد تلقائي')).toBeInTheDocument();
    expect(screen.getAllByText('أشعر بضغط كبير')).toHaveLength(1);
  });

  it('Shift+Enter adds a new line instead of sending', async () => {
    routeApi({ 'GET intake/current': notFound, 'POST intake': () => base });
    renderWithIntl(<IntakeChat />, 'ar');
    const box = await screen.findByRole('textbox', { name: 'رسالتك' });
    await userEvent.type(box, 'سطر{Shift>}{Enter}{/Shift}ثانٍ');
    expect(box).toHaveValue('سطر\nثانٍ');
    expect(api).not.toHaveBeenCalledWith('intake/in-1/messages', expect.anything());
  });

  it('goes to the crisis screen when the gate answers crisis: true', async () => {
    routeApi({
      'GET intake/current': notFound,
      'POST intake': () => base,
      'POST intake/in-1/messages': () => turn({}, { crisis: true }),
    });
    renderWithIntl(<IntakeChat />, 'ar');
    await userEvent.type(await screen.findByRole('textbox', { name: 'رسالتك' }), 'لا أريد أن أعيش{Enter}');
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/crisis'));
  });

  it('puts the text back when the API refuses the message', async () => {
    routeApi({
      'GET intake/current': notFound,
      'POST intake': () => base,
      'POST intake/in-1/messages': () => {
        throw new ApiError(400, { title: 'تعذّر الإرسال', status: 400 });
      },
    });
    renderWithIntl(<IntakeChat />, 'ar');
    const box = await screen.findByRole('textbox', { name: 'رسالتك' });
    await userEvent.type(box, 'مرحبًا{Enter}');
    expect(await screen.findByRole('alert')).toHaveTextContent('تعذّر الإرسال');
    expect(box).toHaveValue('مرحبًا');
  });

  it('keeps a failed message with a retry button that resends the same client id (PB-27)', async () => {
    const bodies: { text: string; clientMessageId: string }[] = [];
    routeApi({
      'GET intake/current': notFound,
      'POST intake': () => base,
      'POST intake/in-1/messages': (body) => {
        bodies.push(body as never);
        if (bodies.length === 1) throw new ApiError(503, { title: 'الخدمة غير متاحة', status: 503 });
        return turn({ messages: [...base.messages, { id: 'm2', role: 'patient', text: 'مرحبًا', at: '' }] });
      },
    });
    renderWithIntl(<IntakeChat />, 'ar');
    await userEvent.type(await screen.findByRole('textbox', { name: 'رسالتك' }), 'مرحبًا{Enter}');
    expect(await screen.findByText('لم تُرسل بعد')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'إعادة المحاولة' }));
    await waitFor(() => expect(bodies).toHaveLength(2));
    expect(bodies[1]!.clientMessageId).toBe(bodies[0]!.clientMessageId);
    await waitFor(() => expect(screen.queryByText('لم تُرسل بعد')).not.toBeInTheDocument());
    expect(screen.getAllByText('مرحبًا')).toHaveLength(1);
  });

  it('offers the form when the assistant is down and hides the composer in form mode (PB-28)', async () => {
    const down: IntakeState = { ...base, messages: [...base.messages, { id: 'm2', role: 'assistant', text: 'رد ثابت', at: '', fallback: true }] };
    routeApi({
      'GET intake/current': () => down,
      'POST intake': () => down,
      'POST intake/in-1/form-mode': () => ({ ...down, formMode: true, pendingQuestion: phq }),
    });
    renderWithIntl(<IntakeChat />, 'en');
    expect(await screen.findByTestId('form-mode-offer')).toHaveTextContent('The assistant is not available right now.');
    await userEvent.click(screen.getByRole('button', { name: 'Switch to form' }));
    expect(api).toHaveBeenCalledWith('intake/in-1/form-mode', { method: 'POST' });
    expect(await screen.findByText(/^Form mode:/)).toBeInTheDocument();
    expect(screen.queryByTestId('form-mode-offer')).not.toBeInTheDocument();
    expect(screen.queryByRole('textbox', { name: 'Your message' })).not.toBeInTheDocument();
    expect(screen.getByRole('radiogroup')).toBeInTheDocument();
  });

  it('answers the questionnaire with the chosen score and can skip a question', async () => {
    const answers: unknown[] = [];
    routeApi({
      'GET intake/current': notFound,
      'POST intake': () => ({ ...base, pendingQuestion: phq }),
      'POST intake/in-1/answers': (body) => {
        answers.push(body);
        return answers.length === 1 ? turn({ pendingQuestion: { ...phq!, item: 3, text: 'سؤال ثالث' } }) : turn({ pendingQuestion: null });
      },
    });
    renderWithIntl(<IntakeChat />, 'ar');
    expect(await screen.findByText('PHQ-9 · السؤال 2 من 9')).toBeInTheDocument();
    const group = screen.getByRole('radiogroup');
    expect(group).toHaveAccessibleName(phq!.text);
    await userEvent.click(screen.getByRole('radio', { name: /أكثر من نصف الأيام/ }));
    expect(answers[0]).toEqual({ instrument: 'phq9', item: 2, value: 2 });

    expect(await screen.findByText('PHQ-9 · السؤال 3 من 9')).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'تخطي السؤال' }));
    expect(answers[1]).toEqual({ instrument: 'phq9', item: 3, value: null });
    await waitFor(() => expect(screen.queryByTestId('question-card')).not.toBeInTheDocument());
  });

  it('enables "End conversation" only when canFinish, then completes and opens the summary', async () => {
    routeApi({
      'GET intake/current': notFound,
      'POST intake': () => base,
      'POST intake/in-1/messages': () => turn({ canFinish: true }),
      'POST intake/in-1/complete': () => ({ intakeId: 'in-1', sections: {}, generatedByAi: true }),
    });
    renderWithIntl(<IntakeChat />, 'ar');
    const finish = await screen.findByTestId('finish');
    expect(finish).toHaveAttribute('aria-disabled', 'true');
    expect(finish).toHaveAccessibleDescription('يمكنك إنهاء المحادثة بعد أن نتعرّف عليك أكثر قليلًا.');
    await userEvent.click(finish);
    expect(api).not.toHaveBeenCalledWith('intake/in-1/complete', expect.anything());

    await userEvent.type(screen.getByRole('textbox', { name: 'رسالتك' }), 'هذا كل شيء{Enter}');
    await waitFor(() => expect(finish).toHaveAttribute('aria-disabled', 'false'));
    await userEvent.click(finish);
    expect(api).toHaveBeenCalledWith('intake/in-1/complete', { method: 'POST' });
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/intake/summary'));
  });

  it('pauses and goes back home (PB-24)', async () => {
    routeApi({
      'GET intake/current': notFound,
      'POST intake': () => base,
      'POST intake/in-1/pause': () => ({ ...base, status: 'paused' }),
    });
    renderWithIntl(<IntakeChat />, 'ar');
    await userEvent.click(await screen.findByRole('button', { name: 'إيقاف مؤقت' }));
    expect(api).toHaveBeenCalledWith('intake/in-1/pause', { method: 'POST' });
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/home'));
  });

  it('shows a paused intake as saved for 14 days with the expiry date', async () => {
    routeApi({ 'GET intake/current': () => ({ ...base, status: 'paused', expiresAt: '2026-11-03T10:00:00Z' }), 'POST intake': () => ({ ...base, status: 'paused', expiresAt: '2026-11-03T10:00:00Z' }) });
    renderWithIntl(<IntakeChat />, 'ar');
    const notice = await screen.findByTestId('paused-notice');
    expect(notice).toHaveTextContent('حفظنا المحادثة');
    expect(notice).toHaveTextContent('يمكنك العودة خلال 14 يومًا، حتى');
    expect(notice).toHaveTextContent('3 نوفمبر');
    expect(screen.queryByRole('textbox', { name: 'رسالتك' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'متابعة المحادثة' })).toBeInTheDocument();
  });

  it('asks for a person only after confirming, with the expected wait (PB-25)', async () => {
    routeApi({
      'GET intake/current': notFound,
      'POST intake': () => base,
      'POST me/contact-request?source=intake': () => ({ requestedAt: '2026-10-20T10:00:00Z', expectedBy: '2026-10-21T10:00:00Z', withinHours: 24 }),
    });
    renderWithIntl(<IntakeChat triageWithinHours={24} />, 'ar');
    await userEvent.click(await screen.findByRole('button', { name: 'أريد التحدث مع شخص' }));
    const dialog = await screen.findByRole('dialog', { name: 'نوصلك بشخص من فريقنا؟' });
    expect(dialog).toHaveTextContent('خلال 24 ساعة');
    expect(api).not.toHaveBeenCalledWith('me/contact-request?source=intake', expect.anything());
    await userEvent.click(screen.getByRole('button', { name: 'نعم، اطلب التواصل' }));
    expect(await screen.findByText(/استلمنا طلبك. سيتواصل معك أحد أفراد فريقنا قبل/)).toHaveTextContent('21');
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('offers a restart for an expired intake', async () => {
    routeApi({ 'GET intake/current': () => ({ ...base, status: 'expired' }), 'POST intake': () => base });
    renderWithIntl(<IntakeChat />, 'ar');
    expect(await screen.findByText('انتهت صلاحية هذه المحادثة')).toBeInTheDocument();
    expect(api).not.toHaveBeenCalledWith('intake', expect.anything());
    await userEvent.click(screen.getByRole('button', { name: 'ابدأ محادثة جديدة' }));
    expect(await screen.findByRole('log')).toBeInTheDocument();
    expect(api).toHaveBeenCalledWith('intake', { method: 'POST' });
  });

  it('counts toward 2,000 characters and asks to send longer text in parts (EC-19)', async () => {
    routeApi({ 'GET intake/current': notFound, 'POST intake': () => base });
    renderWithIntl(<IntakeChat />, 'en');
    const box = await screen.findByRole('textbox', { name: 'Your message' });
    expect(screen.queryByText(/of 2000 characters/)).not.toBeInTheDocument();
    fireEvent.change(box, { target: { value: 'a'.repeat(1900) } });
    expect(screen.getByText('1900 of 2000 characters')).toBeInTheDocument();
    fireEvent.change(box, { target: { value: 'a'.repeat(2500) } });
    expect(box).toHaveValue('a'.repeat(2500));
    expect(screen.getByRole('alert')).toHaveTextContent('Please send it in parts');
    expect(screen.getByRole('button', { name: 'Send' })).toBeDisabled();
  });

  it('sends the user on to the summary when the intake is already being summarized', async () => {
    routeApi({ 'GET intake/current': () => ({ ...base, status: 'review' }) });
    renderWithIntl(<IntakeChat />, 'ar');
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/intake/summary'));
  });
});
