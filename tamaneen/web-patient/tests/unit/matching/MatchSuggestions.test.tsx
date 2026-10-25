import { screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { ApiError } from '@/lib/api/errors';
import { renderWithIntl } from '../intl';
import { khaled, noura, sara, suggestion } from './fixtures';

const mocks = vi.hoisted(() => ({ api: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api }));
vi.mock('@/i18n/navigation', () => ({
  Link: ({ href, children, ...rest }: { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { MatchSuggestions } from '@/components/matching/MatchSuggestions';

const items = [
  suggestion(sara, 1, { sharedSpecialties: ['work_stress', 'sleep'], speaksYourLanguage: true }),
  suggestion(khaled, 2, { sharedSpecialties: ['anxiety'], eveningSlots: true }),
  suggestion(noura, 3),
];

describe('MatchSuggestions (P07)', () => {
  afterEach(() => mocks.api.mockReset());

  it('shows three suggestions: best fit first, reasons, and the third one compact', () => {
    renderWithIntl(<MatchSuggestions initial={items} patientLanguage="ar" timeZone="Asia/Riyadh" />);
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('اخترنا لك ثلاثة أخصائيين');
    const cards = Array.from(screen.getByRole('list', { name: 'الأخصائيون المقترحون' }).children) as HTMLElement[];
    expect(cards).toHaveLength(3);
    expect(cards[0]).toHaveTextContent('الأنسب');
    expect(cards[0]).toHaveTextContent('أخصائية نفسية إكلينيكية · مرخصة');
    expect(cards[0]).toHaveTextContent('لماذا اقترحناها:');
    expect(cards[0]).toHaveTextContent('خبرة في الضغط المهني ومشكلات النوم، وتتحدث العربية.');
    expect(cards[1]).toHaveTextContent('لماذا اقترحناه:');
    expect(within(cards[0]!).getByRole('link', { name: 'عرض الملف والمواعيد' })).toHaveAttribute('href', '/therapists/t-sara');
    // Compact: a single "view" link and no dismiss button.
    expect(within(cards[2]!).getByRole('link', { name: 'عرض ملف أ. نورة ح.' })).toHaveTextContent('عرض');
    expect(within(cards[2]!).queryByRole('button')).toBeNull();
    expect(screen.getByRole('link', { name: 'تصفح جميع الأخصائيين' })).toHaveAttribute('href', '/therapists');
  });

  it('asks first in a sheet with an optional reason, then removes the therapist and shows the updated list (PB-36)', async () => {
    let resolve!: (v: unknown) => void;
    mocks.api.mockReturnValue(new Promise((r) => (resolve = r)));
    renderWithIntl(<MatchSuggestions initial={items} patientLanguage="ar" timeZone="Asia/Riyadh" />);

    await userEvent.click(screen.getByRole('button', { name: 'غير مناسب: أ. خالد ع.' }));
    const sheet = await screen.findByRole('dialog', { name: 'إزالة أ. خالد ع. من اقتراحاتك؟' });
    expect(mocks.api).not.toHaveBeenCalled();
    await userEvent.click(within(sheet).getByRole('radio', { name: 'المواعيد لا تناسبني' }));
    await userEvent.click(within(sheet).getByRole('button', { name: 'نعم، أزل الاقتراح' }));
    expect(mocks.api).toHaveBeenCalledWith('matches/t-khaled/dismiss', { method: 'POST', body: { reason: 'schedule' } });
    // Optimistic: gone before the server answers.
    expect(screen.queryByText('أ. خالد ع.')).toBeNull();
    const confirmation = screen.getByText('أزلنا أ. خالد ع. من اقتراحاتك، ولن نقترحه عليك مرة أخرى.');
    expect(confirmation.closest('[tabindex="-1"]')).toHaveFocus();

    resolve({ status: 'ready', items: [items[0], suggestion(noura, 2)] });
    await waitFor(() => expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('اخترنا لك أخصائيَّين'));
    // Noura moved up to a full card.
    expect(screen.getByRole('button', { name: 'غير مناسب: أ. نورة ح.' })).toBeInTheDocument();
  });

  it('puts the therapist back and shows the problem when dismissing fails', async () => {
    mocks.api.mockImplementation(async () => {
      throw new ApiError(500, { title: 'Server problem', status: 500 });
    });
    renderWithIntl(<MatchSuggestions initial={items} patientLanguage="en" timeZone="Asia/Riyadh" />, 'en');
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('We picked three therapists for you');

    await userEvent.click(screen.getByRole('button', { name: 'Not a good fit: أ. سارة م.' }));
    await userEvent.click(await screen.findByRole('button', { name: 'Yes, remove the suggestion' }));
    expect(mocks.api).toHaveBeenCalledWith('matches/t-sara/dismiss', { method: 'POST', body: {} });
    expect(await screen.findByRole('alert')).toHaveTextContent('Server problem');
    expect(screen.getByText('أ. سارة م.')).toBeInTheDocument();
    expect(screen.getByText('Why we suggested her:')).toBeInTheDocument();
  });

  it('keeps the suggestion when the sheet is cancelled', async () => {
    renderWithIntl(<MatchSuggestions initial={items} patientLanguage="en" timeZone="Asia/Riyadh" />, 'en');
    await userEvent.click(screen.getByRole('button', { name: 'Not a good fit: أ. خالد ع.' }));
    expect(await screen.findByRole('dialog')).toHaveTextContent('Why? (optional)');
    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByText('أ. خالد ع.')).toBeInTheDocument();
    expect(mocks.api).not.toHaveBeenCalled();
  });
});
