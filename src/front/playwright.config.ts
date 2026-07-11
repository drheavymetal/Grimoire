import { defineConfig, devices } from '@playwright/test';

// E2E suite (round 1). Drives a real Chromium against the real Vite front (:5173) and the
// real ASP.NET API (:5080) backed by live Postgres (:5433). The data is being populated by an
// ETL in the background, so the tests never assert exact counts — only "exists" / ">= 1" and
// stable text/roles.
export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 1,
  workers: 3,
  reporter: [['list']],
  timeout: 60_000,
  expect: { timeout: 15_000 },
  use: {
    baseURL: 'http://localhost:5173',
    trace: 'retain-on-failure',
    actionTimeout: 15_000,
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  // Both servers are reused if already running (the reviewer usually has them up); otherwise
  // Playwright starts them. The API build can be cold, hence the generous timeout.
  webServer: [
    {
      command:
        'ASPNETCORE_ENVIRONMENT=Development dotnet run --project ../web/server',
      url: 'http://localhost:5080/api/scenes',
      reuseExistingServer: true,
      timeout: 240_000,
      stdout: 'ignore',
      stderr: 'pipe',
    },
    {
      command: 'pnpm dev',
      url: 'http://localhost:5173',
      reuseExistingServer: true,
      timeout: 120_000,
      stdout: 'ignore',
      stderr: 'pipe',
    },
  ],
});
