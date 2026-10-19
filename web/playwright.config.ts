import process from 'node:process'
import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end flows run against a running app (API + web), e.g. `docker compose up` (http://localhost:8080)
 * or the dev servers (http://localhost:5173). Set E2E_BASE_URL to choose.
 */
export default defineConfig({
  testDir: './e2e',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 1,
  reporter: process.env.CI ? 'list' : 'html',
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
    trace: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
})
