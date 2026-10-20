import { act, fireEvent, screen } from '@testing-library/react';
import { renderWithIntl } from '../intl';
import { Link, router } from '../intake/mocks';

vi.mock('@/lib/api/client', () => ({ api: vi.fn() }));
vi.mock('@/i18n/navigation', () => ({ useRouter: () => router, Link }));

import { BREATHING_TOTAL_SECONDS, BreathingPacer, breathPhaseAt } from '@/components/safety/BreathingPacer';

function mockReducedMotion(reduced: boolean) {
  Object.defineProperty(window, 'matchMedia', {
    configurable: true,
    writable: true,
    value: (query: string) => ({
      matches: reduced && query.includes('reduce'),
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {},
    }),
  });
}

afterEach(() => vi.useRealTimers());

describe('breathPhaseAt', () => {
  it('follows 4 s in, 4 s hold, 6 s out', () => {
    expect(breathPhaseAt(0)).toEqual({ phase: 'inhale', secondsLeft: 4 });
    expect(breathPhaseAt(3)).toEqual({ phase: 'inhale', secondsLeft: 1 });
    expect(breathPhaseAt(4)).toEqual({ phase: 'hold', secondsLeft: 4 });
    expect(breathPhaseAt(8)).toEqual({ phase: 'exhale', secondsLeft: 6 });
    expect(breathPhaseAt(13)).toEqual({ phase: 'exhale', secondsLeft: 1 });
    expect(breathPhaseAt(14)).toEqual({ phase: 'inhale', secondsLeft: 4 });
  });
});

describe('BreathingPacer', () => {
  it('starts, changes the instruction with each phase, and stops', async () => {
    mockReducedMotion(false);
    vi.useFakeTimers();
    renderWithIntl(<BreathingPacer />, 'ar');
    const instruction = screen.getByTestId('breathing-instruction');
    expect(instruction).toHaveAttribute('aria-live', 'polite');
    expect(instruction).toHaveTextContent('اجلس بارتياح، ثم اضغط ابدأ.');
    expect(screen.getByTestId('breathing-circle')).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: 'ابدأ' }));
    expect(instruction).toHaveTextContent('شهيق…');
    expect(screen.getByText('متبقٍّ 3:00')).toBeInTheDocument();
    await act(async () => vi.advanceTimersByTimeAsync(4000));
    expect(instruction).toHaveTextContent('احبس نفسك…');
    await act(async () => vi.advanceTimersByTimeAsync(4000));
    expect(instruction).toHaveTextContent('زفير…');

    fireEvent.click(screen.getByRole('button', { name: 'إيقاف' }));
    expect(instruction).toHaveTextContent('اجلس بارتياح، ثم اضغط ابدأ.');
    expect(screen.getByRole('button', { name: 'ابدأ' })).toBeInTheDocument();
  });

  it('finishes after three minutes (English)', async () => {
    mockReducedMotion(false);
    vi.useFakeTimers();
    renderWithIntl(<BreathingPacer />, 'en');
    fireEvent.click(screen.getByRole('button', { name: 'Start' }));
    await act(async () => vi.advanceTimersByTimeAsync(BREATHING_TOTAL_SECONDS * 1000));
    expect(screen.getByTestId('breathing-instruction')).toHaveTextContent('Well done.');
    expect(screen.getByRole('button', { name: 'Start again' })).toBeInTheDocument();
  });

  it('is text only when reduced motion is requested', () => {
    mockReducedMotion(true);
    renderWithIntl(<BreathingPacer />, 'en');
    expect(screen.queryByTestId('breathing-circle')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Start' }));
    expect(screen.getByTestId('breathing-instruction')).toHaveTextContent('Breathe in…');
  });
});
