import { defineConfig, devices } from '@playwright/test';

/**
 * Two suites:
 *  - smoke: runs against the built UI with every API call mocked, so it needs no backend
 *           and no database. Good for CI and for checking a build in seconds.
 *  - full:  drives the real stack (API + SQL Server + demo data). Run it after
 *           `dotnet run -- --reset=true` so the seeded accounts exist.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 30_000,
  expect: { timeout: 7_000 },
  fullyParallel: false,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 1 : 0,
  workers: 1,
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'e2e-report' }]],
  use: {
    baseURL: process.env['E2E_BASE_URL'] ?? 'http://localhost:4200',
    locale: 'ar-AE',
    timezoneId: 'Asia/Dubai',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },
  projects: [
    { name: 'smoke', testMatch: /smoke\.spec\.ts/, use: { ...devices['Desktop Chrome'] } },
    { name: 'full', testMatch: /(hr|field|public|safeguards)\.spec\.ts/, use: { ...devices['Desktop Chrome'] } },
    {
      name: 'mobile',
      testMatch: /collector\.spec\.ts/,
      use: {
        ...devices['Pixel 7'],
        permissions: ['geolocation'],
        geolocation: { latitude: 25.3247, longitude: 55.3925 }, // City Centre Sharjah, from the demo data
      },
    },
  ],
});
