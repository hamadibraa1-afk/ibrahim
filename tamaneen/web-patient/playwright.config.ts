import { defineConfig, devices } from '@playwright/test';

const port = 3100;

export default defineConfig({
  testDir: 'tests/e2e',
  fullyParallel: true,
  retries: 0,
  reporter: [['list']],
  use: { baseURL: `http://localhost:${port}`, trace: 'retain-on-failure' },
  projects: [
    { name: 'mobile', use: { ...devices['Pixel 7'] } },
    { name: 'desktop', use: { ...devices['Desktop Chrome'] } },
  ],
  webServer: {
    command: `npx next start -p ${port}`,
    port,
    reuseExistingServer: false,
    timeout: 60_000,
    env: {
      TAMANEEN_EMERGENCY_NUMBER: '911',
      TAMANEEN_MATCH_WITHIN_HOURS: '72',
      TAMANEEN_API_URL: process.env.E2E_API_URL ?? '',
    },
  },
});
