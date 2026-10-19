import { expect, test } from '@playwright/test'
import { installTerminalFixture } from './fixtures/terminal.mjs'

// Browser layout fixture only. Real journald/administrator acceptance is separate.
test.use({ baseURL: process.env.WHALEDECK_PRODUCTION_PREVIEW_URL || 'http://127.0.0.1:5174' })
const longProcess = 'WhaleDeck.Agent.' + '一个很长的进程名称-without-breaking-'.repeat(8)
const entries = [
  { process: 'WhaleDeck.Agent', processId: 2710897 },
  { process: longProcess, processId: 2147483647 },
  { process: 'sudo', processId: 2806867 },
  { process: '', processId: 0 },
].map((entry, index) => ({
  ...entry,
  occurredAtUtc: new Date(Date.UTC(2026, 9, 19, 1, 0, index)).toISOString(),
  unit: 'whaledeck-agent.service',
  priority: '6',
  message: 'Synthetic layout fixture; no real host log contents.',
}))

for (const width of [320, 1045, 1440])
  for (const colorScheme of ['light', 'dark'] as const)
    test(`${width} ${colorScheme}: process name and PID are separate bounded lines`, async ({
      page,
    }) => {
      await page.setViewportSize({ width, height: 900 })
      await page.emulateMedia({ colorScheme })
      await installTerminalFixture(page)
      await page.route('**/api/v1/host/journal?*', (route) => route.fulfill({ json: entries }))
      await page.goto('/host/logs')
      const cells = page.locator('.journal-process')
      await expect(cells).toHaveCount(entries.length)
      await page.evaluate(() => document.fonts.ready.then(() => true))
      for (const [index, entry] of entries.entries()) {
        const name = cells.nth(index).locator('.journal-process__name')
        const pid = cells.nth(index).locator('.journal-process__pid')
        await expect(name).toHaveText(entry.process || '—')
        await expect(name).toHaveAttribute('title', entry.process || '—')
        await expect(pid).toHaveText(`PID ${entry.processId || '—'}`)
        const nameBox = await name.boundingBox(),
          pidBox = await pid.boundingBox()
        expect(pidBox!.y).toBeGreaterThanOrEqual(nameBox!.y + nameBox!.height)
        const layout = await name.evaluate((element) => {
          const style = getComputedStyle(element)
          return {
            whiteSpace: style.whiteSpace,
            overflow: style.overflowX,
            ellipsis: style.textOverflow,
            height: element.clientHeight,
            lineHeight: Number.parseFloat(style.lineHeight),
          }
        })
        expect(layout.whiteSpace).toBe('nowrap')
        expect(layout.overflow).toBe('hidden')
        expect(layout.ellipsis).toBe('ellipsis')
        expect(layout.height).toBeLessThanOrEqual(Math.ceil(layout.lineHeight))
        expect(await pid.evaluate((element) => getComputedStyle(element).fontSize)).toBe('12px')
        expect(await name.evaluate((element) => getComputedStyle(element).color)).not.toBe(
          await pid.evaluate((element) => getComputedStyle(element).color),
        )
      }
      const longName = cells.nth(1).locator('.journal-process__name')
      expect(await longName.evaluate((element) => element.scrollWidth > element.clientWidth)).toBe(
        true,
      )
      const heights = await cells.evaluateAll((items) =>
        items.map((item) => item.getBoundingClientRect().height),
      )
      expect(Math.max(...heights) - Math.min(...heights)).toBeLessThanOrEqual(1)
      expect(
        await page.evaluate(
          () => document.documentElement.scrollWidth <= document.documentElement.clientWidth,
        ),
      ).toBe(true)
      await page.screenshot({
        path: `test-results/host-logs-process-${width}-${colorScheme}.png`,
        fullPage: true,
      })
    })
