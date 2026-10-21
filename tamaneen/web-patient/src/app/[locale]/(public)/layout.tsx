import { setRequestLocale } from 'next-intl/server';
import type { ReactNode } from 'react';
import { SiteFooter } from '@/components/public/SiteFooter';

/** Public screens share the footer links to P12 and P13. */
export default async function PublicLayout({ children, params }: { children: ReactNode; params: Promise<{ locale: string }> }) {
  const { locale } = await params;
  setRequestLocale(locale);
  return (
    <>
      {children}
      <SiteFooter />
    </>
  );
}
