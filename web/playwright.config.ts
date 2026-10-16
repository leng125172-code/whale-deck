import { defineConfig } from '@playwright/test'

const productionPreview = process.env.WHALEDECK_PRODUCTION_PREVIEW_URL
if (productionPreview && productionPreview !== 'http://127.0.0.1:5175')
  throw new Error(
    'Production fixture tests require the local 5175 preview, never a live workstation.',
  )

export default defineConfig({
  testDir: './e2e',
  fullyParallel: false,
  workers: 1,
  timeout: 60_000,
  use: {
    baseURL: 'http://127.0.0.1:5174',
    reducedMotion: 'reduce',
    screenshot: 'only-on-failure',
    launchOptions: { executablePath: process.env.PLAYWRIGHT_EXECUTABLE_PATH },
  },
  webServer: [
    {
      command: 'pnpm dev --host 127.0.0.1 --port 5174',
      url: 'http://127.0.0.1:5174',
      reuseExistingServer: !process.env.CI,
    },
    ...(productionPreview
      ? [
          {
            command: 'pnpm preview --host 127.0.0.1 --port 5175 --strictPort',
            url: productionPreview,
            reuseExistingServer: !process.env.CI,
          },
        ]
      : []),
  ],
})
