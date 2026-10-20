import type { Metadata, Viewport } from 'next';
import { IBM_Plex_Sans_Arabic } from 'next/font/google';
import { notFound } from 'next/navigation';
import { NextIntlClientProvider } from 'next-intl';
import { getMessages, getTranslations, setRequestLocale } from 'next-intl/server';
import type { ReactNode } from 'react';
import { AppHeader } from '@/components/AppHeader';
import { ServiceWorkerRegister } from '@/components/pwa/ServiceWorkerRegister';
import { directionOf, isLocale, locales } from '@/i18n/routing';
import '../globals.css';

const plex = IBM_Plex_Sans_Arabic({
  subsets: ['arabic', 'latin'],
  weight: ['400', '500', '600', '700'],
  display: 'swap',
  variable: '--font-plex-arabic',
});

export function generateStaticParams() {
  return locales.map((locale) => ({ locale }));
}

export async function generateMetadata({ params }: { params: Promise<{ locale: string }> }): Promise<Metadata> {
  const { locale } = await params;
  const t = await getTranslations({ locale, namespace: 'meta' });
  return {
    title: t('title'),
    description: t('description'),
    alternates: { languages: { ar: '/ar', en: '/en' } },
    robots: { index: false, follow: false },
    manifest: '/manifest.webmanifest',
    icons: { icon: '/icon.svg', apple: '/icon.svg' },
  };
}

export const viewport: Viewport = {
  width: 'device-width',
  initialScale: 1,
  themeColor: [
    { media: '(prefers-color-scheme: light)', color: '#FFF8F1' },
    { media: '(prefers-color-scheme: dark)', color: '#121A1C' },
  ],
};

export default async function LocaleLayout({ children, params }: { children: ReactNode; params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  if (!isLocale(locale)) notFound();
  setRequestLocale(locale);
  const [messages, t] = await Promise.all([getMessages(), getTranslations('common')]);

  return (
    <html lang={locale} dir={directionOf(locale)} className={plex.variable}>
      <body className="min-h-screen font-sans">
        <ServiceWorkerRegister />
        <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:start-4 focus:top-4 focus:rounded-full focus:bg-surface focus:px-4 focus:py-2">
          {t('skipToContent')}
        </a>
        <NextIntlClientProvider locale={locale} messages={messages}>
          <AppHeader />
          <main id="main" className="mx-auto w-full max-w-3xl px-4 pb-10">
            {children}
          </main>
        </NextIntlClientProvider>
      </body>
    </html>
  );
}
