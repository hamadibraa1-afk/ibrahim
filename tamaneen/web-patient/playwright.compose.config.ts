import { defineConfig, devices } from '@playwright/test';

/** Smoke test of the docker compose stack (docs/local-trial.md): `npx playwright test -c playwright.compose.config.ts`. */
export default defineConfig({
  testDir: './tests/compose',
  timeout: 90_000,
  workers: 1,
  use: { baseURL: process.env.COMPOSE_PATIENT_URL ?? 'http://localhost:3000', trace: 'retain-on-failure' },
  projects: [{ name: 'desktop', use: { ...devices['Desktop Chrome'] } }],
});
