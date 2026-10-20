import type { Config } from 'tailwindcss';

// Colors are CSS variables (see globals.css) so light and dark themes share one set of class names.
const token = (name: string) => `rgb(var(--${name}) / <alpha-value>)`;

const config: Config = {
  content: ['./src/**/*.{ts,tsx}'],
  darkMode: 'media',
  theme: {
    extend: {
      colors: {
        bg: token('bg'),
        surface: token('surface'),
        ink: token('ink'),
        muted: token('muted'),
        primary: token('primary'),
        'primary-strong': token('primary-strong'),
        accent: token('accent'),
        'tint-teal': token('tint-teal'),
        'tint-sand': token('tint-sand'),
        help: token('help'),
        line: token('line'),
      },
      borderRadius: { card: '22px' },
      fontFamily: { sans: ['var(--font-plex-arabic)', 'system-ui', 'sans-serif'] },
      minHeight: { touch: '44px', cta: '52px' },
    },
  },
  plugins: [],
};

export default config;
