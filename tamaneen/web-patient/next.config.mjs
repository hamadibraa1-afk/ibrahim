import createNextIntlPlugin from 'next-intl/plugin';

const withNextIntl = createNextIntlPlugin('./src/i18n/request.ts');

// Camera and microphone: this app's own pages only (the P41 device check and the P42 room; the browser still asks the
// patient), plus a real video provider's framed room when TAMANEEN_VIDEO_ORIGIN is set (e.g. https://video.example).
// Client-side navigation keeps the first document's policy, so this cannot be limited to the session routes.
const videoOrigin = /^https:\/\/[^/\s"]+$/.test(process.env.TAMANEEN_VIDEO_ORIGIN ?? '') ? ` "${process.env.TAMANEEN_VIDEO_ORIGIN}"` : '';

const securityHeaders = [
  { key: 'X-Content-Type-Options', value: 'nosniff' },
  { key: 'Referrer-Policy', value: 'no-referrer' },
  { key: 'X-Frame-Options', value: 'DENY' },
  { key: 'Permissions-Policy', value: `camera=(self${videoOrigin}), microphone=(self${videoOrigin}), geolocation=()` },
  { key: 'Strict-Transport-Security', value: 'max-age=63072000; includeSubDomains; preload' },
];

/** @type {import('next').NextConfig} */
const nextConfig = {
  reactStrictMode: true,
  poweredByHeader: false,
  output: 'standalone',
  async headers() {
    return [{ source: '/:path*', headers: securityHeaders }];
  },
};

export default withNextIntl(nextConfig);
