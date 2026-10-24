const { defineConfig } = require('@playwright/test');
module.exports = defineConfig({
  testDir: '.',
  testMatch: '**/*.spec.cjs',
  timeout: 30000,
  use: { baseURL: 'http://127.0.0.1:53922', browserName: 'chromium', headless: true },
  reporter: [['list'], ['html', { open: 'never', outputFolder: 'playwright-report' }]],
});
