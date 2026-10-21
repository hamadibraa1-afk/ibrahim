import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createTranslator } from 'next-intl';
import { messagesFor, renderWithIntl } from '../intl';
import { resetAll, router } from '../onboarding/helpers';

let locale: 'ar' | 'en' = 'ar';
vi.mock('next-intl/server', () => ({
  getTranslations: async (namespace: string) => createTranslator({ locale, messages: messagesFor(locale), namespace: namespace as never }),
}));
vi.mock('@/i18n/navigation', async () => (await import('../onboarding/helpers')).navigationMock());

import { cameFromThisApp, HistoryBackLink } from '@/components/public/HistoryBackLink';
import { SignupsPaused } from '@/components/public/SignupsPaused';
import { SiteFooter } from '@/components/public/SiteFooter';

describe('EC-26 sign-ups paused', () => {
  it('shows the pause message with the emergency numbers instead of a start button (Arabic)', async () => {
    locale = 'ar';
    render(await SignupsPaused({ emergencyNumber: '911', supportLine: '920033360' }));
    expect(screen.getByRole('heading', { name: 'التسجيل الجديد متوقف الآن' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /اتصل بالطوارئ/ })).toHaveAttribute('href', 'tel:911');
    expect(screen.getByRole('link', { name: /خط الدعم النفسي/ })).toHaveAttribute('href', 'tel:920033360');
    expect(screen.queryByRole('link', { name: 'لنبدأ معًا' })).not.toBeInTheDocument();
  });

  it('falls back to the crisis resources page when no number is configured (English)', async () => {
    locale = 'en';
    render(await SignupsPaused({ emergencyNumber: null, supportLine: null }));
    expect(screen.getByRole('heading', { name: 'New sign-ups are paused right now' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'In danger right now? Open emergency resources' })).toHaveAttribute('href', '/crisis-resources');
  });
});

describe('public footer', () => {
  it('links to how it works and both legal documents', async () => {
    locale = 'ar';
    render(await SiteFooter());
    expect(screen.getByRole('link', { name: 'كيف تعمل الخدمة' })).toHaveAttribute('href', '/how-it-works');
    expect(screen.getByRole('link', { name: 'سياسة الخصوصية' })).toHaveAttribute('href', '/legal?doc=privacy');
    expect(screen.getByRole('link', { name: 'شروط الاستخدام' })).toHaveAttribute('href', '/legal?doc=terms');
  });
});

describe('P13 back link', () => {
  const referrer = Object.getOwnPropertyDescriptor(Document.prototype, 'referrer')!;
  beforeEach(() => resetAll());
  afterEach(() => Object.defineProperty(document, 'referrer', referrer));

  it('goes back in history when the patient came from another screen of the app', async () => {
    Object.defineProperty(document, 'referrer', { configurable: true, get: () => `${window.location.origin}/ar/start/consents` });
    window.history.pushState({}, '', '/ar/legal');
    renderWithIntl(<HistoryBackLink fallback="/" />, 'ar');
    await userEvent.click(screen.getByRole('link', { name: 'رجوع' }));
    expect(router.back).toHaveBeenCalled();
  });

  it('follows the fallback when opened directly (English)', () => {
    Object.defineProperty(document, 'referrer', { configurable: true, get: () => '' });
    renderWithIntl(<HistoryBackLink fallback="/" />, 'en');
    expect(screen.getByRole('link', { name: 'Back' })).toHaveAttribute('href', '/');
    expect(cameFromThisApp()).toBe(false);
  });
});
