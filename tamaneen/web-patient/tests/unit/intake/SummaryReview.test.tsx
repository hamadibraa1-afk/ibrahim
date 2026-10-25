import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { IntakeSummary } from '@/lib/api/types';
import { renderWithIntl } from '../intl';
import { api, Link, resetMocks, routeApi, router } from './mocks';

vi.mock('@/lib/api/client', () => ({ api: (...args: unknown[]) => api(...args) }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router, Link }));

import { SummaryReview } from '@/components/intake/SummaryReview';

const summary: IntakeSummary = {
  intakeId: 'in-1',
  generatedByAi: true,
  sections: {
    concern: { text: 'تشعر بضغط كبير في العمل منذ نحو ثلاثة أشهر.', editedByPatient: false },
    goals: { text: 'أن تتعلم طرقًا لتهدئة نفسك قبل النوم.', editedByPatient: false },
    supports: { text: 'الحديث مع أختك، والمشي في المساء.', editedByPatient: false },
    priorCare: { text: 'لم تراجع مختصًا نفسيًا من قبل.', editedByPatient: false },
    extra: { text: '', editedByPatient: false },
  },
};

beforeEach(resetMocks);

describe('SummaryReview (P05)', () => {
  it('shows the sections, the 24-hour note and the add button in Arabic', () => {
    renderWithIntl(<SummaryReview initial={summary} reviewHours={24} />, 'ar');
    expect(screen.getByRole('heading', { name: 'هل فهمناك بشكل صحيح؟' })).toBeInTheDocument();
    for (const title of ['ما الذي يضايقك', 'ما تأمل تحقيقه', 'ما يساعدك حاليًا', 'رعاية سابقة']) {
      expect(screen.getByRole('heading', { name: title })).toBeInTheDocument();
    }
    expect(screen.getByText('سيراجع مختص ملفك خلال 24 ساعة، ثم نقترح عليك أخصائيين مناسبين.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'أضف شيئًا لم نذكره' })).toBeInTheDocument();
    expect(screen.queryByText('صحّحته أنت')).not.toBeInTheDocument();
  });

  it('edits one section, saves only that section and tags it as corrected', async () => {
    routeApi({
      'PUT intake/in-1/summary': (body) => {
        expect(body).toEqual({ sections: { goals: 'أن أنام جيدًا وأعود للرياضة.' } });
        return { ...summary, sections: { ...summary.sections, goals: { text: 'أن أنام جيدًا وأعود للرياضة.', editedByPatient: true } } };
      },
    });
    renderWithIntl(<SummaryReview initial={summary} reviewHours={24} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'تعديل: ما تأمل تحقيقه' }));
    const box = screen.getByTestId('edit-goals');
    expect(screen.getByRole('button', { name: 'إرسال إلى فريق الرعاية' })).toBeDisabled();
    expect(screen.getByText('احفظ تعديلاتك أو ألغِها قبل الإرسال.')).toBeInTheDocument();
    await userEvent.clear(box);
    await userEvent.type(box, 'أن أنام جيدًا وأعود للرياضة.');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ' }));
    expect(await screen.findByText('صحّحته أنت')).toBeInTheDocument();
    expect(screen.getByText('أن أنام جيدًا وأعود للرياضة.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'تعديل: ما تأمل تحقيقه' })).toHaveFocus();
  });

  it('goes to the crisis screen when the gate blocks an edit', async () => {
    routeApi({ 'PUT intake/in-1/summary': () => ({ ...summary, crisis: true }) });
    renderWithIntl(<SummaryReview initial={summary} reviewHours={24} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'أضف شيئًا لم نذكره' }));
    await userEvent.type(screen.getByTestId('edit-extra'), 'نص');
    await userEvent.click(screen.getByRole('button', { name: 'حفظ' }));
    await waitFor(() => expect(router.replace).toHaveBeenCalledWith('/crisis'));
  });

  it('asks for confirmation with the review time, then submits and opens the waiting screen (PB-33)', async () => {
    routeApi({ 'POST intake/in-1/submit': () => ({ status: 'pending', submittedAt: '2026-10-20T11:10:00Z', expectedBy: null, suggestionsReady: false }) });
    renderWithIntl(<SummaryReview initial={summary} reviewHours={24} />, 'ar');
    await userEvent.click(screen.getByRole('button', { name: 'إرسال إلى فريق الرعاية' }));
    const dialog = await screen.findByRole('dialog', { name: 'إرسال الملخص إلى فريق الرعاية؟' });
    expect(dialog).toHaveTextContent('خلال 24 ساعة');
    expect(api).not.toHaveBeenCalled();
    await userEvent.click(screen.getByRole('button', { name: 'أراجع مرة أخرى' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'إرسال إلى فريق الرعاية' }));
    await userEvent.click(await screen.findByRole('button', { name: 'نعم، أرسل' }));
    await waitFor(() => expect(router.push).toHaveBeenCalledWith('/waiting'));
  });

  it('limits a section to 2,000 characters, as the server does (English)', async () => {
    renderWithIntl(<SummaryReview initial={summary} reviewHours={24} />, 'en');
    await userEvent.click(screen.getAllByRole('button', { name: /^Edit/ })[0]!);
    const box = screen.getByTestId('edit-concern');
    expect(box).toHaveAttribute('maxLength', '2000');
    expect(screen.getByText(/of 2000 characters$/)).toBeInTheDocument();
  });

  it('explains how to write the summary when the AI was unavailable (English)', () => {
    const empty: IntakeSummary = {
      ...summary,
      generatedByAi: false,
      sections: Object.fromEntries(Object.keys(summary.sections).map((k) => [k, { text: '', editedByPatient: false }])) as IntakeSummary['sections'],
    };
    renderWithIntl(<SummaryReview initial={empty} reviewHours={1} />, 'en');
    expect(screen.getByText('Write your summary yourself')).toBeInTheDocument();
    expect(screen.getByTestId('edit-concern')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Write: What you hope for' })).toBeInTheDocument();
    expect(screen.getByText(/within 1 hour,/)).toBeInTheDocument();
  });
});
