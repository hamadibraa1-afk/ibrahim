import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { NextIntlClientProvider } from 'next-intl';
import { messagesFor } from './intl';

const replace = vi.fn();
vi.mock('@/i18n/navigation', () => ({
  useRouter: () => ({ replace }),
  usePathname: () => '/start/eligibility',
}));
vi.mock('next/navigation', () => ({
  useSearchParams: () => new URLSearchParams('step=2'),
}));

import { LanguageToggle } from '@/components/LanguageToggle';

function renderIn(locale: 'ar' | 'en') {
  return render(
    <NextIntlClientProvider locale={locale} messages={messagesFor(locale)}>
      <LanguageToggle />
    </NextIntlClientProvider>,
  );
}

describe('LanguageToggle', () => {
  beforeEach(() => replace.mockClear());

  it('in Arabic offers English, labelled in English, with an Arabic accessible name', () => {
    renderIn('ar');
    const button = screen.getByRole('button', { name: 'التبديل إلى الإنجليزية' });
    expect(button).toHaveTextContent('English');
    expect(button.querySelector('[lang="en"]')).not.toBeNull();
  });

  it('switches to English keeping the current page and query', async () => {
    renderIn('ar');
    await userEvent.click(screen.getByTestId('language-toggle'));
    expect(replace).toHaveBeenCalledWith('/start/eligibility?step=2', { locale: 'en' });
  });

  it('switches back to Arabic from English', async () => {
    renderIn('en');
    const button = screen.getByRole('button', { name: 'Switch to Arabic' });
    expect(button).toHaveTextContent('العربية');
    await userEvent.click(button);
    expect(replace).toHaveBeenCalledWith('/start/eligibility?step=2', { locale: 'ar' });
  });
});
