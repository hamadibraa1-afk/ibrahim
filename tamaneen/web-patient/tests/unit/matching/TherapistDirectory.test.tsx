import { act, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';
import { emptyFilters, parseFilters } from '@/components/matching/directoryQuery';
import { renderWithIntl } from '../intl';
import { khaled, NOW, sara } from './fixtures';

const mocks = vi.hoisted(() => ({ api: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api }));
vi.mock('@/i18n/navigation', () => ({
  Link: ({ href, children, ...rest }: { href: string; children: ReactNode }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

import { SEARCH_DEBOUNCE_MS, TherapistDirectory } from '@/components/matching/TherapistDirectory';

describe('TherapistDirectory (P24)', () => {
  let replaceState: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    vi.setSystemTime(NOW);
    window.history.replaceState(null, '', '/ar/therapists');
    replaceState = vi.spyOn(window.history, 'replaceState');
  });
  afterEach(() => {
    replaceState.mockRestore();
    vi.useRealTimers();
    mocks.api.mockReset();
  });

  it('renders the server page with the count and next availability', () => {
    renderWithIntl(<TherapistDirectory initialFilters={emptyFilters} initialResults={[sara, khaled]} timeZone="Asia/Riyadh" />);
    expect(screen.getByRole('heading', { level: 1 })).toHaveTextContent('جميع الأخصائيين');
    expect(screen.getByRole('status')).toHaveTextContent('أخصائيان');
    expect(screen.getByRole('link', { name: /أ. خالد ع./ })).toHaveTextContent('غدًا');
    expect(screen.getByRole('link', { name: /أ. سارة م./ })).toHaveTextContent('القلق · مشكلات النوم · العلاج المعرفي السلوكي');
    expect(mocks.api).not.toHaveBeenCalled();
  });

  it('puts a chosen filter in the URL and fetches with it', async () => {
    mocks.api.mockResolvedValue([sara]);
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderWithIntl(<TherapistDirectory initialFilters={emptyFilters} initialResults={[sara, khaled]} timeZone="Asia/Riyadh" />);

    const anxiety = screen.getByRole('button', { name: 'القلق' });
    await user.click(anxiety);
    expect(anxiety).toHaveAttribute('aria-pressed', 'true');
    expect(replaceState).toHaveBeenLastCalledWith(null, '', '/ar/therapists?specialty=anxiety');
    expect(mocks.api).toHaveBeenLastCalledWith('therapists?specialty=anxiety', expect.objectContaining({ signal: expect.any(AbortSignal) }));
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('أخصائي واحد'));

    await user.click(screen.getByRole('button', { name: 'أخصائية' }));
    await user.click(screen.getByRole('button', { name: 'مسائي' }));
    await user.click(screen.getByRole('button', { name: 'العربية' }));
    expect(mocks.api).toHaveBeenLastCalledWith('therapists?specialty=anxiety&language=ar&gender=female&evening=true', expect.anything());

    // The two specialty chips are one choice.
    await user.click(screen.getByRole('button', { name: 'الاكتئاب' }));
    expect(anxiety).toHaveAttribute('aria-pressed', 'false');
    expect(mocks.api).toHaveBeenLastCalledWith('therapists?specialty=depression&language=ar&gender=female&evening=true', expect.anything());

    await user.click(screen.getByRole('button', { name: 'مسح البحث والتصفية' }));
    expect(replaceState).toHaveBeenLastCalledWith(null, '', '/ar/therapists');
    expect(mocks.api).toHaveBeenLastCalledWith('therapists', expect.anything());
  });

  it('waits for typing to settle before searching', async () => {
    mocks.api.mockResolvedValue([khaled]);
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    renderWithIntl(<TherapistDirectory initialFilters={emptyFilters} initialResults={[sara, khaled]} timeZone="Asia/Riyadh" />, 'en');

    await user.type(screen.getByRole('searchbox', { name: 'Search' }), 'sleep');
    expect(mocks.api).not.toHaveBeenCalled();
    await act(async () => {
      vi.advanceTimersByTime(SEARCH_DEBOUNCE_MS);
    });
    expect(mocks.api).toHaveBeenCalledTimes(1);
    expect(mocks.api).toHaveBeenCalledWith('therapists?q=sleep', expect.anything());
    expect(replaceState).toHaveBeenLastCalledWith(null, '', '/ar/therapists?q=sleep');
    await waitFor(() => expect(screen.getByRole('status')).toHaveTextContent('One therapist'));
  });

  it('starts from the filters in the URL', () => {
    const filters = parseFilters(new URLSearchParams('q=نوم&specialty=anxiety&gender=female&evening=true&language=xx'));
    expect(filters).toEqual({ q: 'نوم', specialty: 'anxiety', language: null, gender: 'female', evening: true });
    renderWithIntl(<TherapistDirectory initialFilters={filters} initialResults={[sara]} timeZone="Asia/Riyadh" />);
    expect(screen.getByRole('searchbox')).toHaveValue('نوم');
    expect(screen.getByRole('button', { name: 'القلق' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'أخصائية' })).toHaveAttribute('aria-pressed', 'true');
    expect(screen.getByRole('button', { name: 'العربية' })).toHaveAttribute('aria-pressed', 'false');
    expect(mocks.api).not.toHaveBeenCalled();
  });
});
