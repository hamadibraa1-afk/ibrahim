import { act, render } from '@testing-library/react';

const mocks = vi.hoisted(() => ({ api: vi.fn(), replace: vi.fn(), refresh: vi.fn() }));
vi.mock('@/lib/api/client', () => ({ api: mocks.api }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => ({ replace: mocks.replace, push: vi.fn(), refresh: mocks.refresh }) }));

import { MatchReadyWatcher } from '@/components/intake/MatchReadyWatcher';

const pending = { status: 'pending', submittedAt: '2026-10-20T08:00:00Z', expectedBy: '2026-10-21T08:00:00Z', suggestionsReady: false };

describe('MatchReadyWatcher (P36)', () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => {
    vi.useRealTimers();
    mocks.api.mockReset();
    mocks.replace.mockReset();
  });

  it('polls the triage state and moves to the matches once suggestions are ready', async () => {
    mocks.api.mockResolvedValueOnce(pending).mockRejectedValueOnce(new TypeError('offline')).mockResolvedValueOnce({ ...pending, status: 'reviewed', suggestionsReady: true });
    render(<MatchReadyWatcher intervalMs={1000} />);
    expect(mocks.api).not.toHaveBeenCalled();

    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(mocks.api).toHaveBeenCalledWith('triage');
    expect(mocks.replace).not.toHaveBeenCalled();
    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(mocks.replace).not.toHaveBeenCalled();
    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(mocks.replace).toHaveBeenCalledWith('/matches');

    await act(() => vi.advanceTimersByTimeAsync(5000));
    expect(mocks.api).toHaveBeenCalledTimes(3);
  });

  it('does not poll while the tab is hidden, and checks again when it comes back', async () => {
    mocks.api.mockResolvedValue({ ...pending, suggestionsReady: true });
    const visibility = vi.spyOn(document, 'visibilityState', 'get').mockReturnValue('hidden');
    try {
      render(<MatchReadyWatcher intervalMs={1000} />);
      await act(() => vi.advanceTimersByTimeAsync(3000));
      expect(mocks.api).not.toHaveBeenCalled();

      visibility.mockReturnValue('visible');
      await act(async () => {
        document.dispatchEvent(new Event('visibilitychange'));
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(mocks.replace).toHaveBeenCalledWith('/matches');
    } finally {
      visibility.mockRestore();
    }
  });

  it.each(['closed', 'escalated'])('refreshes the page once the case is %s, then stops polling', async (status) => {
    mocks.refresh.mockReset();
    mocks.api.mockResolvedValue({ ...pending, status });
    render(<MatchReadyWatcher intervalMs={1000} />);
    await act(() => vi.advanceTimersByTimeAsync(1000));
    expect(mocks.refresh).toHaveBeenCalledTimes(1);
    expect(mocks.replace).not.toHaveBeenCalled();
    await act(() => vi.advanceTimersByTimeAsync(3000));
    expect(mocks.api).toHaveBeenCalledTimes(1);
  });
});
