import { render } from '@testing-library/react';

const replace = vi.fn();
let pathname = '/home';
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => ({ replace }),
  usePathname: () => pathname,
}));

import { CrisisLockGuard, isOpenWhileLocked } from '@/components/crisis/CrisisLockGuard';

describe('CrisisLockGuard', () => {
  afterEach(() => replace.mockReset());

  it('sends the patient to the crisis screen from any other screen while locked', () => {
    pathname = '/home';
    render(<CrisisLockGuard locked />);
    expect(replace).toHaveBeenCalledWith('/crisis');
  });

  it('keeps the crisis screen, the contact number and the safety plan open', () => {
    for (const p of ['/crisis', '/settings/safety', '/safety-plan']) {
      pathname = p;
      render(<CrisisLockGuard locked />);
    }
    expect(replace).not.toHaveBeenCalled();
    expect(isOpenWhileLocked('/settings')).toBe(false);
  });

  it('does nothing when the chat is open', () => {
    pathname = '/home';
    render(<CrisisLockGuard locked={false} />);
    expect(replace).not.toHaveBeenCalled();
  });
});
