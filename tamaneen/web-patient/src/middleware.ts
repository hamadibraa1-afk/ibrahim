import createMiddleware from 'next-intl/middleware';
import { routing } from './i18n/routing';

// Picks the language from the URL, then the NEXT_LOCALE cookie, then Accept-Language, then Arabic.
export default createMiddleware(routing);

export const config = {
  matcher: ['/((?!api|_next|_vercel|.*\\..*).*)'],
};
