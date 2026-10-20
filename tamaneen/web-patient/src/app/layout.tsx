import type { ReactNode } from 'react';

// The real root layout is app/[locale]/layout.tsx, which owns <html lang dir>. This pass-through exists
// so the root not-found page has a layout.
export default function RootLayout({ children }: { children: ReactNode }) {
  return children;
}
