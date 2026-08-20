import { defineConfig, devices } from '@playwright/test';

// ============================================================================
//  playwright.config.ts — Wave 12 slice 12.2.
//
//  Boots `ng serve` on port 4200 (the project's dev/prod port from
//  package.json:start) and runs every e2e/* spec against it. The
//  `webServer.reuseExistingServer: !process.env.CI` line lets local
//  developers hit their running dev server while CI always spawns a
//  fresh one — a Playwright standard pattern.
//
//  <para>
//  <b>Why chromium-only</b>: axe-core runs identically across browsers for
//  WCAG 2.1 AA rules; webkit + firefox would burn CI minutes without
//  catching more violations. `npm run test:e2e -- --project=webkit` is
//  available for ad-hoc cross-browser checks if needed.
//  </para>
//
//  <para>
//  <b>Why webServer waits on `http://localhost:4200`</b>: ng-serve prints
//  "Compiled successfully" before Angular hydration completes; hitting
//  `/` confirms the SPA actually responds with HTTP 200 (the canonical
//  Playwright wait-for-server pattern).
//  </para>
// ============================================================================

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  forbidOnly: !!process.env['CI'],
  retries: process.env['CI'] ? 2 : 0,
  workers: process.env['CI'] ? 1 : undefined,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL: 'http://localhost:4200',
    trace: 'on-first-retry',
    screenshot: 'only-on-failure',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: 'npm run start',
    url: 'http://localhost:4200',
    reuseExistingServer: !process.env['CI'],
    timeout: 120 * 1000,
    stdout: 'pipe',
    stderr: 'pipe',
  },
});
